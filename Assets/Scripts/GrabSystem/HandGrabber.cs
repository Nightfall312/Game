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
    [Tooltip("Fingertip transform — grab detection and spring anchor use this point instead of the hand bone center.")]
    [SerializeField] Transform handTip;

    [Header("Grab detection")]
    [SerializeField] float grabRadius = 0.12f;
    [SerializeField] LayerMask grabMask = ~0;

    [Header("Grab SpringJoint")]
    [SerializeField] float grabSpring    = 800f;
    [SerializeField] float grabDamper    = 80f;
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

    // Layer name whose objects should be ignored by the hand's own SphereCollider
    // to prevent the ragdoll hand from physically pushing tools around.
    const string GrabbableLayerName = "Grabbable";

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

        // Exclude the Grabbable layer from each hand collider individually so the hand
        // does not physically push tools around. Using Collider.excludeLayers (per-collider)
        // instead of Physics.IgnoreLayerCollision (global) avoids also muting collisions
        // between Grabbable objects and terrain/ground which share the Default layer.
        int grabbableLayer = LayerMask.NameToLayer(GrabbableLayerName);
        if (grabbableLayer >= 0 && handRigidbody != null)
        {
            Collider[] handColliders = handRigidbody.GetComponentsInChildren<Collider>();
            foreach (Collider handCol in handColliders)
                handCol.excludeLayers = handCol.excludeLayers | (1 << grabbableLayer);
        }
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

        // Guard against the grabbed object being despawned or destroyed between grab and release.
        if (_grabbedObject != null)
        {
            _grabbedObject.OnReleased();
            _grabbedObject = null;
        }

        _grabbedRigidbody = null;
        _isGrabbing       = false;
    }

    // ─── Internal ─────────────────────────────────────────────────────────────────

    void TryAttach()
    {
        if (handRigidbody == null) return;

        // Use the fingertip position for detection so the grab triggers at the tip of the
        // fingers, not the center of the hand bone. Falls back to hand center if not assigned.
        Vector3 tipPos = handTip != null ? handTip.position : handRigidbody.position;

        Collider[] hits = Physics.OverlapSphere(
            tipPos, grabRadius, grabMask,
            QueryTriggerInteraction.Ignore);

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

        _grabJoint = handRigidbody.gameObject.AddComponent<SpringJoint>();
        _grabJoint.autoConfigureConnectedAnchor = false;
        _grabJoint.spring          = grabSpring;
        _grabJoint.damper          = grabDamper;
        _grabJoint.tolerance       = grabTolerance;
        _grabJoint.minDistance     = 0f;
        _grabJoint.maxDistance     = 0f;
        _grabJoint.enableCollision = true;

        // Anchor the spring at the fingertip in the hand rigidbody's local space so the
        // grab force is applied at the tip of the fingers, not the palm center.
        _grabJoint.anchor = handTip != null
            ? handRigidbody.transform.InverseTransformPoint(handTip.position)
            : Vector3.zero;

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
            _grabbedRigidbody          = null;
            _grabJoint.connectedBody   = null;
            _grabJoint.connectedAnchor = worldPoint;
        }
    }

    void ApplyAssistForces()
    {
        if (_rootRigidbody == null || handRigidbody == null) return;

        // Track the grab point on dynamic objects for the pull-up height check.
        // Do NOT update connectedAnchor — it is the contact point on the grabbed body
        // and must stay fixed. The SpringJoint naturally drags the object as the hand moves.
        if (_grabbedRigidbody != null)
            _grabWorldPoint = _grabbedRigidbody.transform.TransformPoint(_grabLocalPoint);

        // Pull-up: only assist when climbing a static surface while airborne.
        bool grabAboveBody = _grabWorldPoint.y > _rootRigidbody.position.y + pullUpHeightOffset;
        if (!_isGrounded && grabAboveBody && _grabbedRigidbody == null)
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
