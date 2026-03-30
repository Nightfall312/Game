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

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    public Rigidbody Rigidbody => _rb;

    public override void Spawned()
    {
        _rb.isKinematic = !Object.HasStateAuthority;

        if (Object.HasStateAuthority)
        {
            NetPosition = _rb.position;
            NetRotation = _rb.rotation;
            NetVelocity = Vector3.zero;
        }
        else
        {
            SnapToNetworkState();
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (_rb == null)
        {
            return;
        }

        if (Object.HasStateAuthority)
        {
            if (_rb.isKinematic)
            {
                _rb.isKinematic = false;
            }

            NetPosition = _rb.position;
            NetRotation = _rb.rotation;
            NetVelocity = _rb.linearVelocity;
        }
        else
        {
            if (!_rb.isKinematic)
            {
                _rb.isKinematic = true;
            }
        }
    }

    public override void Render()
    {
       
        if (Object.HasStateAuthority)
        {
            return;
        }

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
