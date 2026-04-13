using Fusion;
using UnityEngine;

/// <summary>Drunk character variant — lower speed cap, frozen rolling, heavy velocity damping.</summary>
public class DrunkNetworkPlayer : NetworkPlayer
{
    const float DrunkMaxSpeed     = 1.8f;
    const float LateralDampFactor = 0.6f;

    protected override float MaxMoveSpeed => DrunkMaxSpeed;

    public override void Spawned()
    {
        base.Spawned();
    }

    /// <summary>Overrides base physics settings with drunk-character specific values.</summary>
    protected override void ApplyBasePhysicsSettings()
    {
        if (rigidbody3D != null)
        {
            rigidbody3D.linearDamping  = 6f;
            rigidbody3D.angularDamping = 10f;
            rigidbody3D.constraints    = RigidbodyConstraints.FreezeRotationX
                                       | RigidbodyConstraints.FreezeRotationZ;
        }

        if (mainJoint != null)
        {
            JointDrive slerpDrive      = mainJoint.slerpDrive;
            slerpDrive.positionDamper  = 12f;
            mainJoint.slerpDrive       = slerpDrive;
        }
    }

    public override void FixedUpdateNetwork()
    {
        base.FixedUpdateNetwork();

        if (!Object.HasStateAuthority)
        {
            return;
        }

        DampLateralVelocity();
    }

    /// <summary>Kills residual lateral velocity every tick so the sphere cannot drift.</summary>
    void DampLateralVelocity()
    {
        Vector3 vel = rigidbody3D.linearVelocity;
        rigidbody3D.linearVelocity = new Vector3(
            vel.x * LateralDampFactor,
            vel.y,
            vel.z * LateralDampFactor
        );
    }
}
