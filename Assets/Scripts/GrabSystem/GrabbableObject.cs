using Fusion;
using UnityEngine;

/// <summary>
/// Add to any physics object (with NetworkObject + NetworkRigidbody3D) that players can grab.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class GrabbableObject : NetworkBehaviour
{
    [Networked] public PlayerRef GrabbedBy { get; set; }

    int _grabCount;
    NetworkRigidbody3D _netRb;

    /// <summary>How many hands are currently holding this object.</summary>
    public int GrabCount => _grabCount;

    void Awake()
    {
        _netRb = GetComponent<NetworkRigidbody3D>();
    }

    /// <summary>Called when a hand attaches to this object.</summary>
    public void OnGrabbed()
    {
        _grabCount++;
        _netRb?.SetGrabbedLocally(true);

        if (Object != null && Object.HasStateAuthority)
            GrabbedBy = Runner.LocalPlayer;
    }

    /// <summary>Called when a hand releases this object.</summary>
    public void OnReleased()
    {
        _grabCount = Mathf.Max(0, _grabCount - 1);

        if (_grabCount == 0)
        {
            _netRb?.SetGrabbedLocally(false);

            if (Object != null && Object.HasStateAuthority)
                GrabbedBy = PlayerRef.None;
        }
    }
}
