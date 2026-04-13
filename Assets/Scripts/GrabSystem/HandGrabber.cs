using UnityEngine;

/// <summary>
/// HFF-style grab system.
///
/// The SpringJoint is attached to the ROOT Rigidbody (not the hand bone).
/// This means grab forces move the whole body as one rigid unit — no ragdoll
/// chain to collapse, no arm stretching. The arm bones are driven by ArmController
/// for visual IK only; they are NOT in the physics grab path.
///
/// The joint anchor is placed at the hand's world position expressed in root-local
/// space, so the body is pulled from the correct offset point (arm height)
/// rather than from its centre of mass.
/// </summary>
public class HandGrabber : MonoBehaviour
{
    [Header("References")]
    [SerializeField] Rigidbody handRigidbody;
    [Tooltip("Hand tip transform — used for grab detection overlap position only.")]
    [SerializeField] Transform handTip;

    [Header("Grab detection")]
    [SerializeField] float grabRadius = 0.18f;
    [SerializeField] LayerMask grabMask = ~0;

    [Header("Grab SpringJoint (applied to root body)")]
    [SerializeField] float grabSpring      = 1800f;
    [SerializeField] float grabDamper      = 120f;
    [SerializeField] float grabTolerance   = 0.01f;
    [SerializeField] float grabMaxDistance = 0f;

    [Header("Pull-up assist (airborne climbing)")]
    [SerializeField] float pullUpForce        = 25f;
    [SerializeField] float pullUpHeightOffset = 0.05f;

    [Header("Release fling")]
    [Tooltip("Fraction of obstacle surface velocity transferred to root on release.")]
    [SerializeField] float releaseImpulse = 0.25f;

    [Header("Arm reach guard")]
    [Tooltip("Shoulder transform — auto-releases if grab point moves beyond arm reach.")]
    [SerializeField] Transform shoulderPivot;
    [SerializeField] float maxArmReach = 0.8f;

    // ── Runtime ───────────────────────────────────────────────────────────────────

    Rigidbody _rootRigidbody;
    Transform _selfRoot;

    // Joint lives on the root body, not the hand.
    SpringJoint _grabJoint;
    bool        _isGrabbing;

    Rigidbody       _grabbedRigidbody;
    GrabbableObject _grabbedObject;
    Vector3         _grabLocalOnDynamic;

    Transform _anchorTransform;
    Vector3   _anchorLocalPoint;
    Vector3   _anchorPrevWorld;
    Vector3   _anchorVelocity;

    Vector3 _grabWorldPoint;

    const string GrabbableLayerName = "Grabbable";

    int  _activeHandCount = 1;
    bool _isGrounded      = true;

    // ── Public API ────────────────────────────────────────────────────────────────

    public bool    IsGrabbing => _isGrabbing;
    public Vector3 GrabPoint  => _grabWorldPoint;

    /// <summary>Called once by NetworkPlayer after spawning.</summary>
    public void Initialize(Rigidbody rootRb)
    {
        _rootRigidbody = rootRb;
        _selfRoot      = rootRb != null ? rootRb.transform : null;

        int grabbableLayer = LayerMask.NameToLayer(GrabbableLayerName);
        if (grabbableLayer >= 0 && handRigidbody != null)
        {
            foreach (Collider c in handRigidbody.GetComponentsInChildren<Collider>())
                c.excludeLayers = c.excludeLayers | (1 << grabbableLayer);
        }
    }

    /// <summary>Injects per-tick context from NetworkPlayer.</summary>
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

    /// <summary>Releases the grab and applies a momentum fling if the obstacle was moving.</summary>
    public void Release()
    {
        if (!_isGrabbing) return;

        if (_anchorTransform != null && _rootRigidbody != null && _anchorVelocity.sqrMagnitude > 0.1f)
            _rootRigidbody.AddForce(_anchorVelocity * releaseImpulse, ForceMode.VelocityChange);

        if (_grabJoint != null) { Destroy(_grabJoint); _grabJoint = null; }

        _grabbedObject?.OnReleased();
        _grabbedObject    = null;
        _grabbedRigidbody = null;
        _anchorTransform  = null;
        _anchorVelocity   = Vector3.zero;
        _isGrabbing       = false;
    }

    // ── Internal ──────────────────────────────────────────────────────────────────

    void TryAttach()
    {
        if (handRigidbody == null || _rootRigidbody == null) return;

        Vector3 tipPos = handTip != null ? handTip.position : handRigidbody.position;

        Collider[] hits = Physics.OverlapSphere(tipPos, grabRadius, grabMask, QueryTriggerInteraction.Ignore);

        float    bestSq  = float.MaxValue;
        Collider bestCol = null;
        Vector3  bestPt  = Vector3.zero;

        foreach (Collider col in hits)
        {
            if (IsOwnBody(col)) continue;
            Vector3 pt = col.ClosestPoint(tipPos);
            float   sq = (pt - tipPos).sqrMagnitude;
            if (sq < bestSq) { bestSq = sq; bestCol = col; bestPt = pt; }
        }

        if (bestCol != null) Attach(bestCol, bestPt);
    }

    void Attach(Collider col, Vector3 worldPoint)
    {
        _grabWorldPoint = worldPoint;
        _isGrabbing     = true;

        // Kill spin immediately so the upright drive starts from a stable state.
        Vector3 av = _rootRigidbody.angularVelocity;
        _rootRigidbody.angularVelocity = new Vector3(0f, av.y * 0.3f, 0f);

        // ── Create SpringJoint on ROOT Rigidbody ──────────────────────────────────
        // The whole body moves as one unit. No force travels through the ragdoll arm
        // chain, so there is nothing to collapse or stretch.
        _grabJoint = _rootRigidbody.gameObject.AddComponent<SpringJoint>();
        _grabJoint.autoConfigureConnectedAnchor = false;
        _grabJoint.spring          = grabSpring;
        _grabJoint.damper          = grabDamper;
        _grabJoint.tolerance       = grabTolerance;
        _grabJoint.minDistance     = 0f;
        _grabJoint.maxDistance     = grabMaxDistance;
        _grabJoint.enableCollision = true;

        // Place the root anchor at the hand's current world position expressed in
        // root-local space. Body is pulled from arm height, not from the CoM.
        Vector3 handWorld = handTip != null ? handTip.position : handRigidbody.position;
        _grabJoint.anchor = _rootRigidbody.transform.InverseTransformPoint(handWorld);

        Rigidbody hitRb = col.attachedRigidbody;

        if (hitRb != null && !hitRb.isKinematic)
        {
            // Dynamic rigidbody: connect to it directly, Unity tracks the anchor.
            _grabbedRigidbody          = hitRb;
            _grabLocalOnDynamic        = hitRb.transform.InverseTransformPoint(worldPoint);
            _grabJoint.connectedBody   = hitRb;
            _grabJoint.connectedAnchor = _grabLocalOnDynamic;

            _grabbedObject = col.GetComponentInParent<GrabbableObject>();
            _grabbedObject?.OnGrabbed();
        }
        else if (hitRb != null)
        {
            // Kinematic rigidbody (DOTween etc.): connect to it; Unity resolves local space.
            _grabJoint.connectedBody   = hitRb;
            _grabJoint.connectedAnchor = hitRb.transform.InverseTransformPoint(worldPoint);
            _anchorTransform           = hitRb.transform;
            _anchorLocalPoint          = hitRb.transform.InverseTransformPoint(worldPoint);
            _anchorPrevWorld           = worldPoint;
            _anchorVelocity            = Vector3.zero;
        }
        else
        {
            // Pure transform-animated obstacle (no Rigidbody at all).
            // connectedAnchor is world-space when connectedBody is null; update each tick.
            _grabJoint.connectedBody   = null;
            _grabJoint.connectedAnchor = worldPoint;
            _anchorTransform           = col.transform;
            _anchorLocalPoint          = col.transform.InverseTransformPoint(worldPoint);
            _anchorPrevWorld           = worldPoint;
            _anchorVelocity            = Vector3.zero;
        }
    }

    void ApplyAssistForces()
    {
        if (_rootRigidbody == null) return;

        // ── Track moving / kinematic obstacle ─────────────────────────────────────
        if (_anchorTransform != null)
        {
            Vector3 currentWorld = _anchorTransform.TransformPoint(_anchorLocalPoint);

            if (shoulderPivot != null && Vector3.Distance(shoulderPivot.position, currentWorld) > maxArmReach)
            {
                Release();
                return;
            }

            _anchorVelocity  = (currentWorld - _anchorPrevWorld) / Time.fixedDeltaTime;
            _anchorPrevWorld = currentWorld;
            _grabWorldPoint  = currentWorld;

            if (_grabJoint != null && _grabJoint.connectedBody == null)
                _grabJoint.connectedAnchor = currentWorld;

            // Re-sync root anchor to current hand position each tick so the pull
            // point stays accurate even as the body swings below the bar.
            if (_grabJoint != null && handTip != null)
                _grabJoint.anchor = _rootRigidbody.transform.InverseTransformPoint(handTip.position);
        }

        // ── Dynamic object: sync grab world point ─────────────────────────────────
        if (_grabbedRigidbody != null)
            _grabWorldPoint = _grabbedRigidbody.transform.TransformPoint(_grabLocalOnDynamic);

        // ── Pull-up assist ─────────────────────────────────────────────────────────
        bool grabAboveBody = _grabWorldPoint.y > _rootRigidbody.position.y + pullUpHeightOffset;
        if (!_isGrounded && grabAboveBody)
            _rootRigidbody.AddForce(Vector3.up * (pullUpForce / _activeHandCount));
    }

    bool IsOwnBody(Collider col)
    {
        if (_selfRoot == null) return false;
        Transform t = col.transform;
        while (t != null) { if (t == _selfRoot) return true; t = t.parent; }
        return false;
    }
}
