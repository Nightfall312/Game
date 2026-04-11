using UnityEngine;

public class SyncPhysicsObject : MonoBehaviour
{
    Rigidbody rigidbody3D;
    ConfigurableJoint joint;

    [SerializeField] Rigidbody animatedRigidbody3D;
    [SerializeField] bool syncAnimation = false;

    Quaternion startLocalRotation;

    void Awake()
    {
        rigidbody3D = GetComponent<Rigidbody>();
        joint = GetComponent<ConfigurableJoint>();
        startLocalRotation = transform.localRotation;
    }

    /// <summary>Enables or disables animation sync at runtime (used by ArmController).</summary>
    public void SetSyncAnimation(bool value) => syncAnimation = value;

    /// <summary>The bone's local rotation at Awake — used as the reference frame for SetTargetRotationLocal.</summary>
    public Quaternion StartLocalRotation => startLocalRotation;

    /// <summary>The current local rotation of the animated reference body — use as base for procedural offsets.</summary>
    public Quaternion AnimatedLocalRotation => animatedRigidbody3D != null
        ? animatedRigidbody3D.transform.localRotation
        : startLocalRotation;

    public void UpdateJointFromAnimation()
    {
        if (!syncAnimation)
        {
            return;
        }

        ConfigurableJointExtensions.SetTargetRotationLocal(
            joint,
            animatedRigidbody3D.transform.localRotation,
            startLocalRotation
        );
    }
}
