using System.Collections;
using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// HFF / Gang Beasts style grab, lift, and climb system.
///
/// LIFTING (dynamic objects) — PD controller, no SpringJoint:
///   Every FixedUpdate a spring-damper force is applied directly to the grabbed object
///   at the contact point, driving that point toward the hand tip. Gravity is cancelled
///   so mass never prevents lifting. Applied at the contact point (not CoM) so the object
///   also rotates naturally — tools swing, axes tilt, exactly like HFF.
///
///   Why not SpringJoint connected to handRigidbody?
///   Newton's 3rd law: the spring pushes BOTH bodies equally. The hand Rigidbody is 0.1 kg,
///   the axe is 1.5 kg — the hand accelerates 15x faster and shoots toward the object
///   instead of the object moving to the hand. Mass ratio destroys the effect.
///   Direct AddForceAtPosition bypasses this entirely.
///
/// CLIMBING / HANGING (static or kinematic surfaces):
///   SpringJoint on the root, connected to the surface. Pull-up assist adds upward force
///   while airborne so the player can climb ledges.
///
/// THROW on release:
///   impulse = v_contact_point * (m_obj / (m_obj + m_player)) * releaseImpulseScale
/// </summary>
public class HandGrabber : MonoBehaviour
{
    [Header("References")]
    [SerializeField] Rigidbody handRigidbody;
    [Tooltip("Fingertip transform — the world position the grab targets. Child of the hand bone.")]
    [SerializeField] Transform handTip;

    [Header("Grab detection")]
    [Tooltip("Sphere radius around the hand tip used to detect grabbable colliders.")]
    [SerializeField] float     grabRadius = 0.2f;
    [SerializeField] LayerMask grabMask   = ~0;

    [Header("Object holding — PD controller")]
    [Tooltip("Spring strength of the PD controller driving the object to the hand tip. " +
             "Higher = stiffer hold. 300–600 works well for most props.")]
    [SerializeField] float holdSpring = 400f;
    [Tooltip("Damper of the PD controller. Must be high enough to kill oscillation. " +
             "Rule of thumb: holdDamper >= 2 * sqrt(holdSpring * objectMass).")]
    [SerializeField] float holdDamper = 80f;
    [Header("Climbing (SpringJoint on root for static/kinematic surfaces)")]
    [SerializeField] float climbSpring      = 1800f;
    [SerializeField] float climbDamper      = 120f;
    [SerializeField] float climbTolerance   = 0.01f;
    [SerializeField] float climbMaxDistance = 0f;

    [Header("Pull-up assist (airborne climbing)")]
    [SerializeField] float pullUpForce        = 28f;
    [SerializeField] float pullUpHeightOffset = 0.05f;

    [Header("Momentum drag — pulled by moving objects while holding")]
    [SerializeField] [Range(0f, 1f)] float continuousDragFraction = 0.18f;
    [SerializeField] float maxDragSpeed = 8f;

    [Header("Release throw")]
    [SerializeField] float releaseImpulseScale   = 1.4f;
    [SerializeField] float ragdollFlingThreshold = 4.5f;

    [Header("Arm reach guard")]
    [SerializeField] Transform shoulderPivot;
    [SerializeField] float     maxArmReach = 0.9f;

    // ── Runtime ───────────────────────────────────────────────────────────────────

    Rigidbody _rootRigidbody;
    Transform _selfRoot;

    // Spring joint on the grabbed dynamic object, connected to world (no reaction on the arm).
    SpringJoint _holdJoint;

    // Root-side spring joint for climbing static/kinematic surfaces.
    SpringJoint _climbJoint;

    // Collider pairs whose collision was suppressed at grab time — restored on release.
    readonly List<(Collider, Collider)> _ignoredPairs = new List<(Collider, Collider)>();

    bool _isGrabbing;

    // Dynamic path — PD controller state
    Rigidbody       _grabbedRigidbody;
    GrabbableObject _grabbedObject;
    Vector3         _grabLocalOnDynamic;    // contact point offset from CoM in grabbed-body local space

    // Kinematic / static path
    Transform         _anchorTransform;
    Vector3           _anchorLocalPoint;
    Vector3           _anchorPrevWorld;
    Vector3           _anchorVelocity;
    KinematicObstacle _anchorKinematic;

    Vector3 _grabWorldPoint;

    System.Action<float> _onReleaseFling;

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
    }

    /// <summary>Registers a callback fired on release when fling speed exceeds threshold.</summary>
    public void SetFlingCallback(System.Action<float> callback) => _onReleaseFling = callback;

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
        if (_isGrabbing)  ApplyHoldForces();
    }

    /// <summary>
    /// Releases the grab and applies a momentum-conserving throw impulse to the root.
    /// </summary>
    public void Release()
    {
        if (!_isGrabbing) return;

        // ── Compute release velocity at the grab contact point ────────────────────
        Vector3 releaseVelocity = Vector3.zero;

        if (_grabbedRigidbody != null)
        {
            Vector3 grabWorld = _grabbedRigidbody.worldCenterOfMass
                              + _grabbedRigidbody.transform.TransformVector(_grabLocalOnDynamic);
            Vector3 r         = grabWorld - _grabbedRigidbody.worldCenterOfMass;
            releaseVelocity   = _grabbedRigidbody.linearVelocity
                              + Vector3.Cross(_grabbedRigidbody.angularVelocity, r);
        }
        else if (_anchorVelocity.sqrMagnitude > 0.01f)
        {
            releaseVelocity = _anchorVelocity;
        }

        // ── Mass-ratio impulse on the player root ──────────────────────────────────
        if (_rootRigidbody != null && releaseVelocity.sqrMagnitude > 0.1f)
        {
            float mPlayer = _rootRigidbody.mass;
            float mObj    = _grabbedRigidbody != null ? _grabbedRigidbody.mass : mPlayer;
            float ratio   = mObj / (mObj + mPlayer);

            Vector3 impulse = releaseVelocity * (ratio * releaseImpulseScale);
            _rootRigidbody.AddForce(impulse, ForceMode.VelocityChange);

            if (impulse.magnitude >= ragdollFlingThreshold)
                _onReleaseFling?.Invoke(impulse.magnitude);
        }

        // ── Teardown ──────────────────────────────────────────────────────────────
        if (_holdJoint  != null) { Destroy(_holdJoint);  _holdJoint  = null; }
        if (_climbJoint != null) { Destroy(_climbJoint); _climbJoint = null; }

        // Restore collision between the grabbed object and the player body.
        foreach ((Collider a, Collider b) in _ignoredPairs)
            if (a != null && b != null)
                Physics.IgnoreCollision(a, b, false);
        _ignoredPairs.Clear();

        _grabbedObject?.OnReleased();
        _grabbedObject    = null;
        _grabbedRigidbody = null;
        _anchorTransform  = null;
        _anchorKinematic  = null;
        _anchorVelocity   = Vector3.zero;
        _isGrabbing       = false;
    }

    // ── Internal ──────────────────────────────────────────────────────────────────

    void TryAttach()
    {
        if (_rootRigidbody == null) return;

        Vector3 tipPos = HandTipWorld();

        Collider[] hits = Physics.OverlapSphere(tipPos, grabRadius, grabMask, QueryTriggerInteraction.Ignore);

        float    bestSq  = float.MaxValue;
        Collider bestCol = null;
        Vector3  bestPt  = Vector3.zero;

        foreach (Collider col in hits)
        {
            if (IsOwnBody(col)) continue;
            Vector3 pt = ClosestPointSafe(col, tipPos);
            float   sq = (pt - tipPos).sqrMagnitude;
            if (sq < bestSq) { bestSq = sq; bestCol = col; bestPt = pt; }
        }

        if (bestCol != null) Attach(bestCol, bestPt);
    }

    void Attach(Collider col, Vector3 worldPoint)
    {
        _grabWorldPoint = worldPoint;
        _isGrabbing     = true;

        Rigidbody hitRb = col.attachedRigidbody;

        if (hitRb != null && !hitRb.isKinematic)
            AttachDynamic(hitRb, col, worldPoint);
        else if (hitRb != null)
            AttachKinematic(hitRb, col, worldPoint);
        else
            AttachStatic(col, worldPoint);
    }

    /// <summary>
    /// Dynamic body — creates a SpringJoint on the grabbed object connected to world (null body).
    /// World has infinite mass so there are zero reaction forces on the arm — the object is
    /// pulled toward the hand tip without pushing the hand away. The joint solver handles the
    /// constraint implicitly, so there is no discrete-time oscillation from manual AddForce.
    /// Collision between the grabbed object and the player body is suppressed for the grab
    /// duration to prevent a physics fight between the joint pull and the body pushing back.
    /// </summary>
    void AttachDynamic(Rigidbody hitRb, Collider col, Vector3 worldPoint)
    {
        _grabbedRigidbody = hitRb;

        Vector3 comOffset   = worldPoint - hitRb.worldCenterOfMass;
        _grabLocalOnDynamic = hitRb.transform.InverseTransformVector(comOffset);

        // Disable collision between the grabbed object and every player collider.
        // Without this the object physically pushes the player while the spring
        // simultaneously pulls it toward the hand — they fight and the object shakes.
        _ignoredPairs.Clear();
        if (_selfRoot != null)
        {
            Collider[] playerCols = _selfRoot.GetComponentsInChildren<Collider>(true);
            Collider[] objCols    = hitRb.GetComponentsInChildren<Collider>(true);
            foreach (Collider pc in playerCols)
                foreach (Collider oc in objCols)
                    if (pc != null && oc != null)
                    {
                        Physics.IgnoreCollision(pc, oc, true);
                        _ignoredPairs.Add((pc, oc));
                    }
        }

        // SpringJoint ON the grabbed object, connected to world (connectedBody = null).
        // connectedAnchor is updated every FixedUpdate to follow HandTipWorld().
        _holdJoint = hitRb.gameObject.AddComponent<SpringJoint>();
        _holdJoint.autoConfigureConnectedAnchor = false;
        _holdJoint.connectedBody   = null;
        _holdJoint.anchor          = hitRb.transform.InverseTransformPoint(worldPoint);
        _holdJoint.connectedAnchor = HandTipWorld();
        _holdJoint.spring          = holdSpring;
        _holdJoint.damper          = holdDamper;
        _holdJoint.minDistance     = 0f;
        _holdJoint.maxDistance     = 0f;
        _holdJoint.tolerance       = 0.001f;
        _holdJoint.enableCollision = false;

        _grabbedObject = col.GetComponentInParent<GrabbableObject>();
        _grabbedObject?.OnGrabbed();
    }

    /// <summary>
    /// Kinematic rigidbody — SpringJoint on the root, player is pulled toward the surface.
    /// </summary>
    void AttachKinematic(Rigidbody hitRb, Collider col, Vector3 worldPoint)
    {
        _anchorTransform  = hitRb.transform;
        _anchorLocalPoint = hitRb.transform.InverseTransformPoint(worldPoint);
        _anchorPrevWorld  = worldPoint;
        _anchorVelocity   = Vector3.zero;
        _anchorKinematic  = col.GetComponentInParent<KinematicObstacle>();

        _climbJoint = _rootRigidbody.gameObject.AddComponent<SpringJoint>();
        _climbJoint.autoConfigureConnectedAnchor = false;
        _climbJoint.connectedBody   = hitRb;
        _climbJoint.spring          = climbSpring;
        _climbJoint.damper          = climbDamper;
        _climbJoint.tolerance       = climbTolerance;
        _climbJoint.minDistance     = 0f;
        _climbJoint.maxDistance     = climbMaxDistance;
        _climbJoint.enableCollision = true;

        _climbJoint.anchor          = _rootRigidbody.transform.InverseTransformPoint(HandTipWorld());
        _climbJoint.connectedAnchor = hitRb.transform.InverseTransformPoint(worldPoint);

        Vector3 av = _rootRigidbody.angularVelocity;
        _rootRigidbody.angularVelocity = new Vector3(av.x * 0.3f, av.y * 0.5f, av.z * 0.3f);
    }

    /// <summary>
    /// Pure static collider — SpringJoint on root, world-space connectedAnchor updated each tick.
    /// </summary>
    void AttachStatic(Collider col, Vector3 worldPoint)
    {
        _anchorTransform  = col.transform;
        _anchorLocalPoint = col.transform.InverseTransformPoint(worldPoint);
        _anchorPrevWorld  = worldPoint;
        _anchorVelocity   = Vector3.zero;
        _anchorKinematic  = col.GetComponentInParent<KinematicObstacle>();

        _climbJoint = _rootRigidbody.gameObject.AddComponent<SpringJoint>();
        _climbJoint.autoConfigureConnectedAnchor = false;
        _climbJoint.connectedBody   = null;
        _climbJoint.connectedAnchor = worldPoint;
        _climbJoint.spring          = climbSpring;
        _climbJoint.damper          = climbDamper;
        _climbJoint.tolerance       = climbTolerance;
        _climbJoint.minDistance     = 0f;
        _climbJoint.maxDistance     = climbMaxDistance;
        _climbJoint.enableCollision = true;

        _climbJoint.anchor = _rootRigidbody.transform.InverseTransformPoint(HandTipWorld());

        Vector3 av = _rootRigidbody.angularVelocity;
        _rootRigidbody.angularVelocity = new Vector3(av.x * 0.3f, av.y * 0.5f, av.z * 0.3f);
    }

    void ApplyHoldForces()
    {
        if (_rootRigidbody == null) return;

        // ── Dynamic object: SpringJoint drives the grab point toward the hand tip ───
        // The joint is connected to world (null body) so there are no reaction forces
        // on the arm — only the grabbed object accelerates. connectedAnchor is updated
        // each tick to follow the hand tip, making the world anchor move with the hand.
        if (_grabbedRigidbody != null)
        {
            Vector3 grabWorld = _grabbedRigidbody.worldCenterOfMass
                              + _grabbedRigidbody.transform.TransformVector(_grabLocalOnDynamic);
            _grabWorldPoint = grabWorld;

            if (shoulderPivot != null && Vector3.Distance(shoulderPivot.position, grabWorld) > maxArmReach)
            {
                Release();
                return;
            }

            // Move the world-space spring anchor to the current hand tip every tick.
            if (_holdJoint != null)
                _holdJoint.connectedAnchor = HandTipWorld();

            // Gravity compensation — offset default spring sag (mg/k downward at equilibrium).
            _grabbedRigidbody.AddForce(-Physics.gravity * _grabbedRigidbody.mass, ForceMode.Force);

            // Bleed angular velocity so spin asymmetries don't accumulate into oscillation.
            _grabbedRigidbody.angularVelocity *= 0.85f;

            Vector3 r        = grabWorld - _grabbedRigidbody.worldCenterOfMass;
            Vector3 pointVel = _grabbedRigidbody.linearVelocity
                             + Vector3.Cross(_grabbedRigidbody.angularVelocity, r);
            InjectDragVelocity(pointVel);
        }

        // ── Static / kinematic: update world connectedAnchor and sync root anchor ──
        if (_anchorTransform != null && _climbJoint != null)
        {
            Vector3 currentWorld = _anchorTransform.TransformPoint(_anchorLocalPoint);

            if (shoulderPivot != null && Vector3.Distance(shoulderPivot.position, currentWorld) > maxArmReach)
            {
                Release();
                return;
            }

            _anchorVelocity = _anchorKinematic != null
                ? _anchorKinematic.GetVelocityAtPoint(currentWorld)
                : (currentWorld - _anchorPrevWorld) / Time.fixedDeltaTime;

            _anchorPrevWorld = currentWorld;
            _grabWorldPoint  = currentWorld;

            _climbJoint.anchor = _rootRigidbody.transform.InverseTransformPoint(HandTipWorld());

            if (_climbJoint.connectedBody == null)
                _climbJoint.connectedAnchor = currentWorld;

            InjectDragVelocity(_anchorVelocity);
        }

        // ── Pull-up assist — upward force while airborne, climbing only (not holding dynamics) ──
        bool grabAboveBody = _grabWorldPoint.y > _rootRigidbody.position.y + pullUpHeightOffset;
        bool isHoldingDynamic = _grabbedRigidbody != null;
        if (!_isGrounded && grabAboveBody && !isHoldingDynamic)
            _rootRigidbody.AddForce(Vector3.up * (pullUpForce / _activeHandCount));
    }

    /// <summary>
    /// Injects a fraction of <paramref name="sourceVelocity"/> into the root per tick
    /// so a swinging/spinning object drags the player along.
    /// Vertical drag is capped independently so a lifted dynamic object can't launch the player upward.
    /// </summary>
    void InjectDragVelocity(Vector3 sourceVelocity)
    {
        if (_rootRigidbody == null || sourceVelocity.sqrMagnitude < 0.04f) return;

        Vector3 target = sourceVelocity * continuousDragFraction;

        // Horizontal drag: respect maxDragSpeed.
        Vector3 hTarget = new Vector3(target.x, 0f, target.z);
        if (hTarget.magnitude > maxDragSpeed)
            hTarget = hTarget.normalized * maxDragSpeed;

        // Vertical drag: clamp tightly — dynamic objects should not be able to carry
        // the full player mass upward. Only allow downward velocity transfer freely.
        const float maxUpDrag = 1.5f;
        float vTarget = Mathf.Clamp(target.y, -maxDragSpeed, maxUpDrag);

        ApplyDragTo(_rootRigidbody, new Vector3(hTarget.x, vTarget, hTarget.z));
    }

    void ApplyDragTo(Rigidbody rb, Vector3 target)
    {
        if (rb == null) return;
        // Apply only the velocity delta needed to nudge toward target, never overshooting.
        // We treat each axis independently so horizontal drag and vertical drag cap separately.
        Vector3 current = rb.linearVelocity;
        Vector3 delta   = target - current;

        // Only inject if the delta meaningfully closes the gap (avoids jitter at rest).
        if (delta.sqrMagnitude > 0.001f)
            rb.AddForce(delta, ForceMode.VelocityChange);
    }

    /// <summary>
    /// Returns the closest point on <paramref name="col"/> to <paramref name="point"/>.
    /// Falls back to clamping the point to the collider's AABB for non-convex MeshColliders,
    /// which do not support Physics.ClosestPoint and would otherwise throw every frame.
    /// </summary>
    static Vector3 ClosestPointSafe(Collider col, Vector3 point)
    {
        bool isConcaveMesh = col is MeshCollider mc && !mc.convex;
        if (isConcaveMesh)
            return col.bounds.ClosestPoint(point);

        return col.ClosestPoint(point);
    }

    Vector3 HandTipWorld() =>
        handTip        != null ? handTip.position :
        handRigidbody  != null ? handRigidbody.position :
        _rootRigidbody.position;

    bool IsOwnBody(Collider col)
    {
        if (_selfRoot == null) return false;
        Transform t = col.transform;
        while (t != null) { if (t == _selfRoot) return true; t = t.parent; }
        return false;
    }
}
