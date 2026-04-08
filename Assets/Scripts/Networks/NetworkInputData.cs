using Fusion;
using UnityEngine;

public struct NetworkInputData : INetworkInput
{
    public Vector2 movementInput;
    public NetworkBool isJumpPressed;
    public NetworkBool isLeftGrabHeld;
    public NetworkBool isRightGrabHeld;
    public float cameraYaw;
    public float cameraPitch;

    // World-space positions each hand is aiming at (only valid when grab held).
    public Vector3 leftHandTarget;
    public Vector3 rightHandTarget;

    // Flags to tell the server the targets are valid (not just default Vector3.zero).
    public NetworkBool leftTargetValid;
    public NetworkBool rightTargetValid;
}
