using Fusion;
using UnityEngine;

/// <summary>
/// Syncs each hand's grab state across the network.
/// Add this to the same GameObject as NetworkPlayer and register it in the NetworkObject.
/// </summary>
public class NetworkGrabSync : NetworkBehaviour
{
    [Networked] public NetworkBool IsLeftGrabbing { get; set; }
    [Networked] public NetworkBool IsRightGrabbing { get; set; }
    [Networked] public Vector3 LeftGrabPoint { get; set; }
    [Networked] public Vector3 RightGrabPoint { get; set; }

    HandGrabber _leftGrabber;
    HandGrabber _rightGrabber;

    /// <summary>Called by NetworkPlayer once grabbers are set up.</summary>
    public void Initialize(HandGrabber left, HandGrabber right)
    {
        _leftGrabber = left;
        _rightGrabber = right;
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority || _leftGrabber == null || _rightGrabber == null)
        {
            return;
        }

        IsLeftGrabbing = _leftGrabber.IsGrabbing;
        LeftGrabPoint = _leftGrabber.IsGrabbing ? _leftGrabber.GrabPoint : Vector3.zero;

        IsRightGrabbing = _rightGrabber.IsGrabbing;
        RightGrabPoint = _rightGrabber.IsGrabbing ? _rightGrabber.GrabPoint : Vector3.zero;
    }
}
