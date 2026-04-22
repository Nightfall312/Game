using Fusion;
using UnityEngine;

/// <summary>
/// Syncs per-player state that isn't captured by the root NetworkRigidbody3D:
///   - Hand grab points and grab flags
///   - Animator locomotion parameters (movementSpeed, isMovingBackward)
///   - Arm input state (hand targets, grab-held flags) for proxy arm pose
///   - Upper arm and forearm joint rotations — these are physics-driven ConfigurableJoints
///     with their own Rigidbodies. NetworkRigidbody3D only covers the root body, so arm
///     bone rotations are never transmitted unless we do it here explicitly.
///
/// State authority (host) writes every FixedUpdateNetwork tick.
/// Proxies read in NetworkPlayer.FixedUpdateNetwork and apply via Render().
/// </summary>
public class NetworkGrabSync : NetworkBehaviour
{
    // ── Grab state ────────────────────────────────────────────────────────────────
    [Networked] public NetworkBool IsLeftGrabbing  { get; set; }
    [Networked] public NetworkBool IsRightGrabbing { get; set; }
    [Networked] public Vector3     LeftGrabPoint   { get; set; }
    [Networked] public Vector3     RightGrabPoint  { get; set; }

    // ── Animator state ────────────────────────────────────────────────────────────
    /// <summary>Animator float "movementSpeed" — written by state authority, read by proxies.</summary>
    [Networked] public float       NetMovementSpeed    { get; set; }
    /// <summary>Animator bool "isMovingBackward" — written by state authority, read by proxies.</summary>
    [Networked] public NetworkBool NetIsMovingBackward { get; set; }

    // ── Arm input state ───────────────────────────────────────────────────────────
    /// <summary>World-space hand targets and button state — written by state authority so proxies
    /// can reproduce the correct arm direction without a local camera.</summary>
    [Networked] public Vector3     NetLeftHandTarget   { get; set; }
    [Networked] public Vector3     NetRightHandTarget  { get; set; }
    [Networked] public NetworkBool NetLeftTargetValid  { get; set; }
    [Networked] public NetworkBool NetRightTargetValid { get; set; }
    [Networked] public NetworkBool NetIsLeftGrabHeld   { get; set; }
    [Networked] public NetworkBool NetIsRightGrabHeld  { get; set; }

    // ── Arm bone rotations ────────────────────────────────────────────────────────
    // The arm joints are ConfigurableJoint-driven Rigidbodies. NetworkRigidbody3D only
    // covers the root body, so these rotations would never reach clients otherwise.
    // Clients set their arm bones to kinematic in NetworkRigidbody3D.FixedUpdateNetwork,
    // so they never simulate — we must push the authoritative rotations to them directly
    // by writing to Transform.localRotation in Render().
    [Networked] public Quaternion NetLeftUpperArmRot  { get; set; }
    [Networked] public Quaternion NetLeftForearmRot   { get; set; }
    [Networked] public Quaternion NetRightUpperArmRot { get; set; }
    [Networked] public Quaternion NetRightForearmRot  { get; set; }

    // ─── Internal references ──────────────────────────────────────────────────────
    HandGrabber _leftGrabber;
    HandGrabber _rightGrabber;

    // Arm bone transforms to read on state authority and write on proxies.
    Transform _leftUpperArm;
    Transform _leftForearm;
    Transform _rightUpperArm;
    Transform _rightForearm;

    // ─── Setup ────────────────────────────────────────────────────────────────────

    /// <summary>Called by NetworkPlayer once grabbers and arm controllers are set up.</summary>
    public void Initialize(HandGrabber left, HandGrabber right)
    {
        _leftGrabber  = left;
        _rightGrabber = right;
    }

    /// <summary>
    /// Registers the arm bone transforms so this component can read/write their rotations.
    /// Pass the upper arm and forearm joint Transforms for each side.
    /// Called by NetworkPlayer after spawning (must be called before FixedUpdateNetwork).
    /// </summary>
    public void RegisterArmBones(Transform leftUpper, Transform leftFore,
                                 Transform rightUpper, Transform rightFore)
    {
        _leftUpperArm  = leftUpper;
        _leftForearm   = leftFore;
        _rightUpperArm = rightUpper;
        _rightForearm  = rightFore;
    }

    // ─── Write (state authority) ──────────────────────────────────────────────────

    /// <summary>Push current animator locomotion parameters into networked state.</summary>
    public void SyncAnimatorState(float movementSpeed, bool isMovingBackward)
    {
        if (!Object.HasStateAuthority) return;
        NetMovementSpeed    = movementSpeed;
        NetIsMovingBackward = isMovingBackward;
    }

    /// <summary>Push arm input state from the current input packet into networked state.</summary>
    public void SyncArmState(NetworkInputData input)
    {
        if (!Object.HasStateAuthority) return;
        NetLeftHandTarget   = input.leftHandTarget;
        NetRightHandTarget  = input.rightHandTarget;
        NetLeftTargetValid  = input.leftTargetValid;
        NetRightTargetValid = input.rightTargetValid;
        NetIsLeftGrabHeld   = input.isLeftGrabHeld;
        NetIsRightGrabHeld  = input.isRightGrabHeld;
    }

    // ─── FixedUpdateNetwork ───────────────────────────────────────────────────────

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority) return;

        // Grab points
        if (_leftGrabber != null)
        {
            IsLeftGrabbing = _leftGrabber.IsGrabbing;
            LeftGrabPoint  = _leftGrabber.IsGrabbing ? _leftGrabber.GrabPoint : Vector3.zero;
        }

        if (_rightGrabber != null)
        {
            IsRightGrabbing = _rightGrabber.IsGrabbing;
            RightGrabPoint  = _rightGrabber.IsGrabbing ? _rightGrabber.GrabPoint : Vector3.zero;
        }

        // Arm bone rotations — sample the physics-simulated joint transforms each tick.
        // These are world-simulated on the host; clients are kinematic and never simulate.
        if (_leftUpperArm  != null) NetLeftUpperArmRot  = _leftUpperArm.localRotation;
        if (_leftForearm   != null) NetLeftForearmRot   = _leftForearm.localRotation;
        if (_rightUpperArm != null) NetRightUpperArmRot = _rightUpperArm.localRotation;
        if (_rightForearm  != null) NetRightForearmRot  = _rightForearm.localRotation;
    }

    // ─── Render (write networked state to Fusion interpolation buffer) ────────────

    public override void Render()
    {
        // Render() in Fusion runs before the Animator's LateUpdate, so writing
        // localRotation here gets overwritten by the Animator every frame on clients.
        // Arm bone rotation application is handled in LateUpdate() below instead.
    }

    // ─── LateUpdate (apply arm bones AFTER Animator has run) ─────────────────────

    /// <summary>
    /// Applies networked arm bone rotations after the Animator has written its pose.
    /// Must run in LateUpdate so it overwrites the Animator output, not the other way around.
    /// Only runs on non-state-authority instances (clients): the host already has live physics.
    /// </summary>
    void LateUpdate()
    {
        // State authority (host) has live physics — no override needed.
        if (Object == null || Object.HasStateAuthority) return;

        // Write directly to Transform.localRotation AFTER the Animator has run.
        // This overwrites whatever the Animator placed on the arm bones, replacing it
        // with the authoritative rotation the host physics simulation produced.
        if (_leftUpperArm  != null) _leftUpperArm.localRotation  = NetLeftUpperArmRot;
        if (_leftForearm   != null) _leftForearm.localRotation   = NetLeftForearmRot;
        if (_rightUpperArm != null) _rightUpperArm.localRotation = NetRightUpperArmRot;
        if (_rightForearm  != null) _rightForearm.localRotation  = NetRightForearmRot;
    }
}
