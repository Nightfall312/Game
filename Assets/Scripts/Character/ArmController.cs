using UnityEngine;

public class ArmController : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────────

    [Header("Upper arm joint (mouse drives this)")]
    [SerializeField] ConfigurableJoint upperArmJoint;
    [SerializeField] SyncPhysicsObject upperArmSync;

    [Header("Forearm joint (driven to complete two-bone IK)")]
    [SerializeField] ConfigurableJoint forearmJoint;
    [SerializeField] SyncPhysicsObject forearmSync;

    [Header("Hand Rigidbody (for grab detection position)")]
    [SerializeField] Rigidbody handRb;

    [Header("Shoulder pivot (world-space origin for the arm sphere)")]
    [SerializeField] Transform shoulderPivot;

    [Header("Body root (used to clamp arm to frontal hemisphere)")]
    [Tooltip("Assign the character's root/body transform. The hand target will never go behind this transform's forward direction.")]
    [SerializeField] Transform bodyRoot;

    [Header("Arm reach radius (meters from shoulder to hand target)")]
    [SerializeField] float armReach = 0.35f;

    [Header("Mouse sensitivity (degrees per pixel)")]
    [SerializeField] float mouseSensitivity = 0.2f;
    [SerializeField] float minPitchDeg = -60f;
    [SerializeField] float maxPitchDeg = 80f;
    [Tooltip("Max yaw deviation (degrees) from the arm's initial side offset. Prevents arm from swinging behind the back.")]
    [SerializeField] float maxYawOffsetDeg = 60f;

    [Header("Resting pose (no mouse button held)")]
    [Tooltip("How quickly the arm blends toward the straight-down resting position (degrees per second).")]
    [SerializeField] float restBlendSpeed = 180f;

    // ─── State ────────────────────────────────────────────────────────────────────

    float _yawDeg;
    float _pitchDeg;
    float _initialYawOffset;
    float _initialPitchOffset;
    Vector3 _handTarget;
    Quaternion _upperArmStartLocalRot;
    Quaternion _forearmStartLocalRot;
    bool _initialized;
    bool _hasValidTarget;

    // ─── Public ───────────────────────────────────────────────────────────────────

    public Vector3   HandTarget      => _handTarget;
    public Rigidbody HandRigidbody   => handRb;
    /// <summary>True after the first ComputeHandTarget() call — confirms target is not world-zero default.</summary>
    public bool      HasValidTarget  => _hasValidTarget;

    /// <summary>
    /// Called once after spawning.
    /// yawOffsetDeg and pitchOffsetDeg are camera-relative offsets (e.g. -20 for left arm, +20 for right arm).
    /// </summary>
    public void Initialize(float yawOffsetDeg, float pitchOffsetDeg)
    {
        _initialYawOffset   = yawOffsetDeg;
        _initialPitchOffset = pitchOffsetDeg;
        _yawDeg             = yawOffsetDeg;
        _pitchDeg           = pitchOffsetDeg;

        if (upperArmJoint != null)
            _upperArmStartLocalRot = upperArmJoint.transform.localRotation;

        if (forearmJoint != null)
            _forearmStartLocalRot = forearmJoint.transform.localRotation;

        if (shoulderPivot != null)
            _handTarget = shoulderPivot.position + shoulderPivot.forward * armReach;

        _initialized = true;
    }

    /// <summary>Accumulates raw mouse pixel delta.</summary>
    public void AddMouseDelta(Vector2 pixelDelta)
    {
        _yawDeg   += pixelDelta.x * mouseSensitivity;
        _pitchDeg -= pixelDelta.y * mouseSensitivity;
        _pitchDeg  = Mathf.Clamp(_pitchDeg, minPitchDeg, maxPitchDeg);
        // Clamp yaw as an offset from the arm's initial side offset so the constraint
        // is symmetric for both arms and prevents the arm from going behind the back.
        _yawDeg    = Mathf.Clamp(_yawDeg,
                                  _initialYawOffset - maxYawOffsetDeg,
                                  _initialYawOffset + maxYawOffsetDeg);
    }

    /// <summary>Recomputes _handTarget. Call in GetNetworkInput().</summary>
    public void ComputeHandTarget(Quaternion cameraRotation)
    {
        if (!_initialized || shoulderPivot == null) return;

        Quaternion armRot = cameraRotation * Quaternion.Euler(_pitchDeg, _yawDeg, 0f);
        _handTarget = shoulderPivot.position + armRot * Vector3.forward * armReach;

        // Clamp target to the frontal hemisphere of the body so the arm can never
        // swing behind the character's back regardless of camera direction.
        if (bodyRoot != null)
        {
            Vector3 bodyFwd  = bodyRoot.forward;
            Vector3 toTarget = _handTarget - shoulderPivot.position;

            if (Vector3.Dot(toTarget.normalized, bodyFwd) < 0f)
            {
                // Project onto the plane whose normal is bodyFwd, then nudge slightly forward
                // so the arm sits at the side rather than clipping through the torso.
                Vector3 projected = Vector3.ProjectOnPlane(toTarget, bodyFwd);
                if (projected.sqrMagnitude < 0.001f) projected = shoulderPivot.right * 0.01f;
                toTarget    = (projected.normalized * 0.95f + bodyFwd * 0.05f).normalized * armReach;
                _handTarget = shoulderPivot.position + toTarget;
            }
        }

        _hasValidTarget = true;
    }

    /// <summary>Set target from network data (server-side).</summary>
    public void SetHandTarget(Vector3 worldTarget) => _handTarget = worldTarget;

    /// <summary>
    /// Drives the upper arm ConfigurableJoint toward _handTarget and keeps the forearm
    /// straight by locking it to its initial rest rotation. This produces a fully extended,
    /// unbent arm regardless of the target direction.
    /// </summary>
    public void UpdateArm()
    {
        if (!_initialized || upperArmJoint == null || shoulderPivot == null) return;

        upperArmSync?.SetSyncAnimation(false);
        forearmSync?.SetSyncAnimation(false);

        // Drive upper arm: align its Y-axis from shoulder toward the hand target.
        DriveJointToward(upperArmJoint, shoulderPivot.position, _handTarget,
                         shoulderPivot.forward, _upperArmStartLocalRot);

        // Keep forearm at its initial rest rotation so no elbow bend is ever visible.
        // Passing startLocalRot as both arguments drives the joint to zero relative rotation.
        if (forearmJoint != null)
        {
            ConfigurableJointExtensions.SetTargetRotationLocal(
                forearmJoint, _forearmStartLocalRot, _forearmStartLocalRot);
        }
    }

    /// <summary>
    /// Drives both joints back to their exact spawn-time local rotations — the safest way
    /// to return to a natural rest pose without any assumptions about bone axis directions.
    /// No world-space direction math so there is no NaN risk from degenerate cross products.
    /// </summary>
    public void DriveToRest()
    {
        if (!_initialized || upperArmJoint == null) return;

        upperArmSync?.SetSyncAnimation(false);
        forearmSync?.SetSyncAnimation(false);

        // Nudge accumulators back to neutral so the next grab starts cleanly.
        _yawDeg   = Mathf.MoveTowards(_yawDeg,   _initialYawOffset,   restBlendSpeed * Time.fixedDeltaTime);
        _pitchDeg = Mathf.MoveTowards(_pitchDeg, _initialPitchOffset, restBlendSpeed * Time.fixedDeltaTime);

        // Drive to spawn-time pose. Passing the same rotation as both target and start makes
        // SetTargetRotationLocal output the identity in joint space — the joint's natural rest.
        ConfigurableJointExtensions.SetTargetRotationLocal(
            upperArmJoint, _upperArmStartLocalRot, _upperArmStartLocalRot);

        if (forearmJoint != null)
            ConfigurableJointExtensions.SetTargetRotationLocal(
                forearmJoint, _forearmStartLocalRot, _forearmStartLocalRot);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Drives a ConfigurableJoint so its local Y-axis points from <paramref name="origin"/>
    /// toward <paramref name="target"/>. <paramref name="perpRef"/> is used to build a stable
    /// perpendicular so the bone roll is consistent.
    /// </summary>
    void DriveJointToward(ConfigurableJoint joint, Vector3 origin, Vector3 target,
                          Vector3 perpRef, Quaternion startLocalRot)
    {
        Vector3 toTarget = target - origin;
        if (toTarget.sqrMagnitude < 0.0001f) return;
        DriveJointTowardDir(joint, toTarget.normalized, perpRef, startLocalRot);
    }

    void DriveJointTowardDir(ConfigurableJoint joint, Vector3 dir,
                             Vector3 perpRef, Quaternion startLocalRot)
    {
        Vector3 armUp = dir;

        // Choose a stable perpendicular that is never parallel to armUp.
        // Using Vector3.up as a fallback fails when armUp itself is vertical (NaN cross product).
        if (Mathf.Abs(Vector3.Dot(armUp, perpRef)) > 0.98f)
            perpRef = Mathf.Abs(armUp.y) < 0.9f ? Vector3.up : Vector3.right;

        Vector3 armFwd = Vector3.Cross(armUp, perpRef);
        if (armFwd.sqrMagnitude < 0.0001f) return; // degenerate — skip this tick

        armFwd = armFwd.normalized;
        Quaternion targetWorldRot = Quaternion.LookRotation(armFwd, armUp);

        Transform  parent         = joint.transform.parent;
        Quaternion targetLocalRot = parent != null
            ? Quaternion.Inverse(parent.rotation) * targetWorldRot
            : targetWorldRot;

        ConfigurableJointExtensions.SetTargetRotationLocal(joint, targetLocalRot, startLocalRot);
    }
    /// <summary>Resets arm accumulators to neutral. Call when transitioning away from grab.</summary>
    public void RestoreAnimationSync()
    {
        upperArmSync?.SetSyncAnimation(false);
        forearmSync?.SetSyncAnimation(false);
        _yawDeg   = _initialYawOffset;
        _pitchDeg = _initialPitchOffset;
    }
}

