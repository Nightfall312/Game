using UnityEngine;

/// <summary>
/// HFF-style grab. When the grab button is held and the hand touches a surface,
/// a SpringJoint is created anchored to the exact contact point. The ragdoll joint
/// chain transmits the tension upward so the whole character hangs naturally — just
/// like in the video. Releasing the button destroys the joint immediately.
/// </summary>
public class HandGrabber : MonoBehaviour
{
    [Header("References")]
    [SerializeField] Rigidbody handRigidbody;

    [Header("Grab detection")]
    [SerializeField] float grabRadius = 0.12f;
    [SerializeField] LayerMask grabMask = ~0;

    [Header("Grab SpringJoint")]
    [SerializeField] float grabSpring    = 6000f;
    [SerializeField] float grabDamper    = 150f;
    [SerializeField] float grabTolerance = 0.01f;

    [Header("Carry — dynamic Rigidbody objects")]
    [SerializeField] float maxCarryMass  = 40f;

    [Header("Pull-up assist")]
    [SerializeField] float pullUpForce        = 30f;
    [SerializeField] float pullUpHeightOffset = 0.05f;

    Rigidbody _rootRigidbody;
    Transform _selfRoot;

    SpringJoint     _grabJoint;
    Rigidbody       _grabbedRigidbody;
    Vector3         _grabWorldPoint;
    Vector3         _grabLocalPoint;
    GrabbableObject _grabbedObject;
    bool            _isGrabbing;

    public bool    IsGrabbing => _isGrabbing;
    public Vector3 GrabPoint  => _grabWorldPoint;

    // Injected by NetworkPlayer each tick — how many hands are simultaneously grabbing.
    // Used to split pull-up force evenly so two-hand grabs don't double the upward push.
    int  _activeHandCount = 1;
    bool _isGrounded      = true;

    // ─── Public API ───────────────────────────────────────────────────────────────

    /// <summary>Called once by NetworkPlayer after spawning.</summary>
    public void Initialize(Rigidbody rootRb)
    {
        _rootRigidbody = rootRb;
        _selfRoot      = rootRb != null ? rootRb.transform : null;
    }

    /// <summary>
    /// Must be called by NetworkPlayer before TryGrabOrHold each tick.
    /// Tells this grabber how many hands are actively grabbing so pull-up force is shared,
    /// and whether the player is grounded so pull-up only fires while climbing.
    /// </summary>
    public void SetFrameContext(int activeHandCount, bool isGrounded)
    {
        _activeHandCount = Mathf.Max(1, activeHandCount);
        _isGrounded      = isGrounded;
    }

    /// <summary>Called every physics tick while the grab button is held.</summary>
    public void TryGrabOrHold()
    {
        if (!_isGrabbing) TryAttach();
        if (_isGrabbing)  ApplyAssistForces();
    }

    /// <summary>Releases the grab and destroys the SpringJoint.</summary>
    public void Release()
    {
        if (!_isGrabbing) return;

        if (_grabJoint != null) { Destroy(_grabJoint); _grabJoint = null; }
        if (_grabbedObject != null) { _grabbedObject.OnReleased(); _grabbedObject = null; }
        _grabbedRigidbody = null;
        _isGrabbing       = false;
    }

    // ─── Internal ─────────────────────────────────────────────────────────────────

    void TryAttach()
    {
        if (handRigidbody == null) return;

        Collider[] hits = Physics.OverlapSphere(
            handRigidbody.position, grabRadius, grabMask,
            QueryTriggerInteraction.Ignore);

        float    bestSq  = float.MaxValue;
        Collider bestCol = null;
        Vector3  bestPt  = Vector3.zero;

        foreach (Collider col in hits)
        {
            if (IsOwnBody(col)) continue;
            Vector3 pt = col.ClosestPoint(handRigidbody.position);
            float   sq = (pt - handRigidbody.position).sqrMagnitude;
            if (sq < bestSq) { bestSq = sq; bestCol = col; bestPt = pt; }
        }

        if (bestCol != null) Attach(bestCol, bestPt);
    }

    void Attach(Collider col, Vector3 worldPoint)
    {
        _grabWorldPoint = worldPoint;
        _isGrabbing     = true;

        // Add SpringJoint to the hand Rigidbody anchored at the grab point.
        _grabJoint = handRigidbody.gameObject.AddComponent<SpringJoint>();
        _grabJoint.autoConfigureConnectedAnchor = false;
        _grabJoint.spring          = grabSpring;
        _grabJoint.damper          = grabDamper;
        _grabJoint.tolerance       = grabTolerance;
        _grabJoint.minDistance     = 0f;
        _grabJoint.maxDistance     = 0f;
        _grabJoint.enableCollision = true;
        _grabJoint.anchor          = Vector3.zero;

        Rigidbody hitRb = col.attachedRigidbody;
        if (hitRb != null && !hitRb.isKinematic)
        {
            _grabbedRigidbody          = hitRb;
            _grabLocalPoint            = hitRb.transform.InverseTransformPoint(worldPoint);
            _grabJoint.connectedBody   = hitRb;
            _grabJoint.connectedAnchor = _grabLocalPoint;

            _grabbedObject = col.GetComponentInParent<GrabbableObject>();
            _grabbedObject?.OnGrabbed();
        }
        else
        {
            // Static surface: anchor to world space.
            _grabbedRigidbody          = null;
            _grabJoint.connectedBody   = null;
            _grabJoint.connectedAnchor = worldPoint;
        }
    }

    void ApplyAssistForces()
    {
        if (_rootRigidbody == null || handRigidbody == null) return;

        // Track moving grab point on dynamic objects.
        if (_grabbedRigidbody != null)
            _grabWorldPoint = _grabbedRigidbody.transform.TransformPoint(_grabLocalPoint);

        // Pull-up: only assist when the player is airborne (i.e. genuinely climbing).
        // When grounded, the downward pin force in NetworkPlayer already resists floating,
        // so firing pull-up would just fight it and could still lift the player.
        bool grabAboveBody = _grabWorldPoint.y > _rootRigidbody.position.y + pullUpHeightOffset;
        if (!_isGrounded && grabAboveBody)
            _rootRigidbody.AddForce(Vector3.up * (pullUpForce / _activeHandCount));

        // Carry physics for dynamic objects below carry-mass limit.
        if (_grabbedRigidbody != null)
            ApplyCarryForces();
    }

    void ApplyCarryForces()
    {
        if (_grabbedRigidbody.mass > maxCarryMass) return;

        // Gravity compensation split across all hands currently holding this object.
        // Without this, gravity pulls the object away from the SpringJoint anchor.
        // With two hands, each compensates half so the total equals exactly one gravity.
        int grabCount = _grabbedObject != null ? Mathf.Max(1, _grabbedObject.GrabCount) : 1;
        float shareRatio = 1f / grabCount;
        Vector3 gravComp = Vector3.up * (-Physics.gravity.y * _grabbedRigidbody.mass * shareRatio);
        _grabbedRigidbody.AddForce(gravComp);
    }

    bool IsOwnBody(Collider col)
    {
        if (_selfRoot == null) return false;
        Transform t = col.transform;
        while (t != null) { if (t == _selfRoot) return true; t = t.parent; }
        return false;
    }
}
