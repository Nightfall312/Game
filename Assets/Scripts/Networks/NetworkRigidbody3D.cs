using Fusion;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class NetworkRigidbody3D : NetworkBehaviour
{
    [SerializeField, Tooltip("Visual interpolation speed toward the networked position.")]
    float positionLerpSpeed = 15f;

    [SerializeField, Tooltip("Visual interpolation speed toward the networked rotation.")]
    float rotationLerpSpeed = 15f;

    [Networked] public Vector3 NetPosition { get; set; }
    [Networked] public Quaternion NetRotation { get; set; }
    [Networked] public Vector3 NetVelocity { get; set; }

    Rigidbody _rb;
    bool _hasReceivedFirstState;
    bool _isGrabbedLocally;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    public Rigidbody Rigidbody => _rb;

    /// <summary>
    /// Suspends or restores network-driven kinematic override while this object is held locally.
    /// Call with true on grab and false on release.
    /// </summary>
    public void SetGrabbedLocally(bool grabbed)
    {
        _isGrabbedLocally = grabbed;

        if (_rb == null) return;

        if (grabbed)
        {
            _rb.isKinematic = false;
        }
        else if (!Object.HasStateAuthority)
        {
            _rb.isKinematic = true;
        }
    }

    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            _rb.isKinematic = false;
            NetPosition = _rb.position;
            NetRotation = _rb.rotation;
            NetVelocity = Vector3.zero;
        }
        else
        {
            _rb.isKinematic = true;

            if (NetPosition != Vector3.zero)
            {
                SnapToNetworkState();
                _hasReceivedFirstState = true;
            }
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (_rb == null) return;

        if (Object.HasStateAuthority)
        {
            if (_rb.isKinematic)
                _rb.isKinematic = false;

            NetPosition = _rb.position;
            NetRotation = _rb.rotation;
            NetVelocity = _rb.linearVelocity;
        }
        else
        {
            if (!_isGrabbedLocally)
            {
                if (!_rb.isKinematic)
                    _rb.isKinematic = true;

                if (!_hasReceivedFirstState && NetPosition != Vector3.zero)
                {
                    SnapToNetworkState();
                    _hasReceivedFirstState = true;
                }
            }
        }
    }

    public override void Render()
    {
        if (Object.HasStateAuthority) return;
        if (!_hasReceivedFirstState) return;
        if (_isGrabbedLocally) return;

        float posT = 1f - Mathf.Exp(-positionLerpSpeed * Time.deltaTime);
        float rotT = 1f - Mathf.Exp(-rotationLerpSpeed * Time.deltaTime);

        transform.position = Vector3.Lerp(transform.position, NetPosition, posT);
        transform.rotation = Quaternion.Slerp(transform.rotation, NetRotation, rotT);
    }

    public void SnapToNetworkState()
    {
        transform.position = NetPosition;
        transform.rotation = NetRotation;

        if (_rb != null)
        {
            _rb.position = NetPosition;
            _rb.rotation = NetRotation;
        }
    }
}
