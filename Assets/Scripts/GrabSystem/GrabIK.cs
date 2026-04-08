using UnityEngine;

/// <summary>
/// Drives the Animator's hand IK goals toward the ArmController hand target at all times,
/// and toward the actual grab contact point when grabbing.
/// Requires IK Pass enabled on the Base layer of the Animator Controller.
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

    void OnAnimatorIK(int layerIndex)
    {
        if (animator == null) return;

        UpdateHandIK(AvatarIKGoal.LeftHand,  leftHandGrabber,  leftArmController);
        UpdateHandIK(AvatarIKGoal.RightHand, rightHandGrabber, rightArmController);
    }

    /// <summary>
    /// When grabbing: IK targets the contact point so the hand locks onto the surface.
    /// When not grabbing: IK targets the arm controller's hand target so the arm follows the mouse.
    /// </summary>
    void UpdateHandIK(AvatarIKGoal goal, HandGrabber grabber, ArmController armController)
    {
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
