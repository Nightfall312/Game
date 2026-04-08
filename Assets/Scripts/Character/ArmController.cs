using UnityEngine;

/// <summary>
/// HFF-style arm controller.
/// Mouse delta moves a world-space hand target on a sphere around the shoulder.
/// The UPPER ARM ConfigurableJoint targetRotation is set to aim the arm at that target —
/// exactly the same mechanism SyncPhysicsObject uses, so the joint spring does all the work
/// and the ragdoll never explodes.
/// The forearm and hand keep their animation sync untouched.
/// </summary>
public class ArmController : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────────

    [Header("Upper arm joint (mouse drives this)")]
    [SerializeField] ConfigurableJoint upperArmJoint;
    [SerializeField] SyncPhysicsObject upperArmSync;

    [Header("Hand Rigidbody (for grab detection position)")]
    [SerializeField] Rigidbody handRb;

    [Header("Shoulder pivot (world-space origin for the arm sphere)")]
    [SerializeField] Transform shoulderPivot;

    [Header("Arm reach radius (meters from shoulder to hand target)")]
    [SerializeField] float armReach = 0.35f;

    [Header("Mouse sensitivity (degrees per pixel)")]
    [SerializeField] float mouseSensitivity = 0.2f;
    [SerializeField] float minPitchDeg = -60f;
    [SerializeField] float maxPitchDeg = 80f;

    // ─── State ────────────────────────────────────────────────────────────────────

    float _yawDeg;
    float _pitchDeg;
    float _initialYawOffset;
    float _initialPitchOffset;
    Vector3 _handTarget;
    Quaternion _upperArmStartLocalRot;
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
    }

    /// <summary>Recomputes _handTarget. Call in GetNetworkInput().</summary>
    public void ComputeHandTarget(Quaternion cameraRotation)
    {
        if (!_initialized || shoulderPivot == null) return;
        Quaternion armRot = cameraRotation * Quaternion.Euler(_pitchDeg, _yawDeg, 0f);
        _handTarget     = shoulderPivot.position + armRot * Vector3.forward * armReach;
        _hasValidTarget = true;
    }

    /// <summary>Set target from network data (server-side).</summary>
    public void SetHandTarget(Vector3 worldTarget) => _handTarget = worldTarget;

    /// <summary>
    /// Drives the upper arm ConfigurableJoint targetRotation toward _handTarget.
    /// The arm bone's local Y-axis points from the shoulder toward the hand,
    /// so we build a world rotation that aligns local Y with the direction to the target.
    /// Uses SetTargetRotationLocal — NO AddForce, no ragdoll explosion.
    /// </summary>
    public void UpdateArm()
    {
        if (!_initialized || upperArmJoint == null || shoulderPivot == null) return;

        Vector3 toTarget = _handTarget - shoulderPivot.position;
        if (toTarget.sqrMagnitude < 0.0001f) return;

        // Suppress animation sync so our target rotation is authoritative on the upper arm.
        upperArmSync?.SetSyncAnimation(false);

        // The upper-arm bone's local Y-axis points toward the forearm/hand.
        // Build a world-space rotation where local Y aligns with toTarget.
        // LookRotation(forward, upwards) → Z = forward, Y ≈ upwards.
        // Use stable WORLD-SPACE references for perpendicular — never shoulder.right,
        // which flips when the character turns around and breaks the arm direction.
        Vector3 armUp   = toTarget.normalized;
        Vector3 perpRef = Vector3.forward;
        if (Mathf.Abs(Vector3.Dot(armUp, perpRef)) > 0.98f) perpRef = Vector3.right;
        Vector3    armFwd         = Vector3.Cross(armUp, perpRef).normalized;
        Quaternion targetWorldRot = Quaternion.LookRotation(armFwd, armUp);

        // Convert to local space of the joint's parent so SetTargetRotationLocal works correctly.
        Transform  parent         = upperArmJoint.transform.parent;
        Quaternion targetLocalRot = parent != null
            ? Quaternion.Inverse(parent.rotation) * targetWorldRot
            : targetWorldRot;

        ConfigurableJointExtensions.SetTargetRotationLocal(
            upperArmJoint, targetLocalRot, _upperArmStartLocalRot);
    }

    /// <summary>
    /// Restores animation sync and resets arm yaw/pitch to the neutral offset
    /// so the next grab always starts from a predictable resting position.
    /// </summary>
    public void RestoreAnimationSync()
    {
        upperArmSync?.SetSyncAnimation(true);
        _yawDeg   = _initialYawOffset;
        _pitchDeg = _initialPitchOffset;
    }
}
