using Fusion;
using UnityEngine;

/// <summary>
/// Drives the Animator's hand IK goals toward the ArmController hand target at all times,
/// and toward the actual grab contact point when grabbing.
/// Requires IK Pass enabled on the Base layer of the Animator Controller.
///
/// On simulated proxies (non-local players) IK is disabled entirely because:
///   - ArmController never calls ComputeHandTarget() without a local camera, so HandTarget
///     stays at shoulderPivot.forward * reach which raises the arm into a T-pose sideways.
///   - The actual arm pose is driven by the physics joints whose state is synced by Fusion's
///     NetworkRigidbody3D, so the IK would only fight the correct joint-driven pose.
/// </summary>
public class GrabIK : MonoBehaviour
{
    [SerializeField] Animator animator;

    [SerializeField] HandGrabber leftHandGrabber;
    [SerializeField] HandGrabber rightHandGrabber;

    [SerializeField] ArmController leftArmController;
    [SerializeField] ArmController rightArmController;

    [Range(0f, 1f)]
    [SerializeField] float ikWeight = 1f;

    /// <summary>
    /// Set to false by NetworkPlayer on non-local (proxy) instances so IK does not
    /// override the joint-driven arm pose with a stale uncomputed hand target.
    /// </summary>
    public bool EnableIK { get; set; } = true;

    void OnAnimatorIK(int layerIndex)
    {
        if (animator == null) return;

        if (!EnableIK)
        {
            // Clear IK weights so the Animator uses the raw animation/joint-driven pose.
            animator.SetIKPositionWeight(AvatarIKGoal.LeftHand,  0f);
            animator.SetIKRotationWeight(AvatarIKGoal.LeftHand,  0f);
            animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
            animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
            return;
        }

        UpdateHandIK(AvatarIKGoal.LeftHand,  leftHandGrabber,  leftArmController);
        UpdateHandIK(AvatarIKGoal.RightHand, rightHandGrabber, rightArmController);
    }

    /// <summary>
    /// When grabbing: IK targets the contact point so the hand locks onto the surface.
    /// When not grabbing: IK targets the arm controller's hand target so the arm follows the mouse.
    /// </summary>
    void UpdateHandIK(AvatarIKGoal goal, HandGrabber grabber, ArmController armController)
    {
        // If the arm controller has never computed a valid target (no local camera), suppress
        // IK so the default uninitialized HandTarget does not yank the arm to the wrong place.
        if (armController != null && !armController.HasValidTarget)
        {
            animator.SetIKPositionWeight(goal, 0f);
            animator.SetIKRotationWeight(goal, 0f);
            return;
        }

        Vector3 targetPos;

        if (grabber != null && grabber.IsGrabbing)
        {
            targetPos = grabber.GrabPoint;
        }
        else if (armController != null)
        {
            targetPos = armController.HandTarget;
        }
        else
        {
            animator.SetIKPositionWeight(goal, 0f);
            animator.SetIKRotationWeight(goal, 0f);
            return;
        }

        animator.SetIKPositionWeight(goal, ikWeight);
        animator.SetIKRotationWeight(goal, 0f);
        animator.SetIKPosition(goal, targetPos);
    }
}
