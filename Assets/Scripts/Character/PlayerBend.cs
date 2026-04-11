using UnityEngine;

/// <summary>
/// Drives the spine ConfigurableJoint into a forward bend when activated.
/// Uses the same SetTargetRotationLocal pattern as ArmController/SyncPhysicsObject —
/// the animated body's local rotation is the base and the bend angle is added on top,
/// so the joint spring does all the work and the ragdoll never explodes.
/// Call UpdateBend() from NetworkPlayer after UpdateSyncPhysicsObjects().
/// </summary>
public class PlayerBend : MonoBehaviour
{
    [Header("Spine joint")]
    [SerializeField] ConfigurableJoint spineJoint;
    [SerializeField] SyncPhysicsObject spineSync;

    [Header("Bend settings")]
    [SerializeField] float bendAngleDegrees = 30f;  // extra forward lean on top of animation
    [SerializeField] float bendSmoothing    = 6f;

    float _currentBendAngle = 0f;

    /// <summary>
    /// Call every FixedUpdateNetwork tick, after UpdateSyncPhysicsObjects().
    /// </summary>
    public void UpdateBend(bool isBending)
    {
        if (spineJoint == null || spineSync == null) return;

        float targetAngle = isBending ? bendAngleDegrees : 0f;
        _currentBendAngle = Mathf.Lerp(_currentBendAngle, targetAngle, bendSmoothing * Time.fixedDeltaTime);

        if (!isBending && _currentBendAngle < 0.5f)
        {
            // Fully returned — hand back to animation sync
            spineSync.SetSyncAnimation(true);
            return;
        }

        // Take authority from SyncPhysicsObject, same as ArmController does
        spineSync.SetSyncAnimation(false);

        // Base = what the animator currently drives the spine to,
        // plus a small forward lean on top — identical to SyncPhysicsObject.UpdateJointFromAnimation
        // but with an extra Euler offset
        Quaternion baseLocalRot = spineSync.AnimatedLocalRotation;
        Quaternion targetLocalRot = baseLocalRot * Quaternion.Euler(_currentBendAngle, 0f, 0f);

        ConfigurableJointExtensions.SetTargetRotationLocal(
            spineJoint, targetLocalRot, spineSync.StartLocalRotation);
    }
}

