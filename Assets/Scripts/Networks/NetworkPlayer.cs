using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(NetworkRigidbody3D))]
[RequireComponent(typeof(NetworkGrabSync))]
public class NetworkPlayer : NetworkBehaviour, IPlayerLeft
{
    public static NetworkPlayer Local { get; private set; }

    /// <summary>The root physics Rigidbody of this player — used by WeaponHit to apply knockback.</summary>
    public Rigidbody RootRigidbody => rigidbody3D;

    [SerializeField] protected Rigidbody rigidbody3D;
    [SerializeField] protected NetworkRigidbody3D networkRigidbody3D;
    [SerializeField] protected ConfigurableJoint mainJoint;

    [SerializeField] ThirdPersonCamera thirdPersonCamera;
    [SerializeField] protected SphereCollider bodyCollider;
    [SerializeField] Animator animator;

    [Header("Grab")]
    [SerializeField] protected HandGrabber leftHandGrabber;
    [SerializeField] protected HandGrabber rightHandGrabber;

    [Header("Arm controllers")]
    [SerializeField] protected ArmController leftArmController;
    [SerializeField] protected ArmController rightArmController;

    [Header("Bend")]
    [SerializeField] PlayerBend playerBend;

    [Header("Ragdoll")]
    [SerializeField] RagdollController ragdollController;

    Vector2 moveInputVector  = Vector2.zero;
    bool isJumpButtonPressed = false;
    bool isBendPressed       = false;

    // Accumulated raw mouse delta between Update ticks, consumed in GetNetworkInput.
    Vector2 _accumulatedMouseDelta = Vector2.zero;

    // Last received input — used to keep movement alive on ticks where GetInput returns false.
    NetworkInputData _lastInput;

    // Previous physics velocity — unused, kept as placeholder.
    Vector3 _prevVelocity;

    const float defaultMaxSpeed        = 3f;
    const float backwardInputThreshold = 0.1f;

    protected bool isGrounded = false;
    Vector3 _groundNormal = Vector3.up;
    RaycastHit[] raycastHits = new RaycastHit[10];

    InputSystem_Actions inputActions;
    SyncPhysicsObject[] syncPhysicsObjects;
    NetworkGrabSync grabSync;

    protected Quaternion _jointStartRotation;
    protected Vector3 _lastPosition;
    protected float _lastCameraYaw;
    float _lastCameraPitch;

    // Smoothed body facing yaw.
    float _facingYaw;

    protected virtual float MaxMoveSpeed => defaultMaxSpeed;
    protected virtual float MoveForceMagnitude => 30f;

    void Awake()
    {
        syncPhysicsObjects = GetComponentsInChildren<SyncPhysicsObject>();
        grabSync = GetComponent<NetworkGrabSync>();

        if (networkRigidbody3D == null)
        {
            networkRigidbody3D = GetComponent<NetworkRigidbody3D>();
        }

        if (rigidbody3D == null && networkRigidbody3D != null)
        {
            rigidbody3D = networkRigidbody3D.Rigidbody;
        }

        if (mainJoint == null)
        {
            mainJoint = GetComponent<ConfigurableJoint>();
        }

        if (mainJoint != null)
        {
            _jointStartRotation = mainJoint.transform.localRotation;
        }

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (animator != null)
        {
            animator.applyRootMotion = false;
        }

        if (ragdollController == null)
            ragdollController = GetComponentInChildren<RagdollController>();
    }

    void OnEnable()
    {
        if (inputActions == null)
        {
            return;
        }

        inputActions.Player.Enable();
        inputActions.Player.Jump.performed += OnJump;
    }

    void OnDisable()
    {
        if (inputActions == null)
        {
            return;
        }

        inputActions.Player.Jump.performed -= OnJump;
        inputActions.Player.Disable();
    }

    void OnJump(InputAction.CallbackContext context)
    {
        isJumpButtonPressed = true;
    }

    void Update()
    {
        if (Object == null || !Object.HasInputAuthority || inputActions == null)
            return;

        if (!PauseMenuManager.IsPaused)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
        }

        moveInputVector = inputActions.Player.Move.ReadValue<Vector2>();
        isBendPressed   = Keyboard.current != null && Keyboard.current.leftCtrlKey.isPressed;

        // Read mouse delta once per frame here. Feed it to the camera AND store it for
        // the arm controller. This is the single point of consumption — ThirdPersonCamera
        // no longer reads Mouse.current.delta itself, avoiding the double-drain bug that
        // made both camera and arms feel sluggish on host and in builds.
        if (!PauseMenuManager.IsPaused && Mouse.current != null)
        {
            Vector2 delta = Mouse.current.delta.ReadValue();
            _accumulatedMouseDelta += delta;
            thirdPersonCamera?.FeedMouseDelta(delta);
        }
    }


    public override void Spawned()
    {
        // Apply physics settings on every instance so both state authority and
        // simulated proxies share the same drag/damping values for consistent simulation.
        ApplyBasePhysicsSettings();

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (animator != null)
        {
            animator.applyRootMotion = false;
        }

        if (!Object.HasInputAuthority)
        {
            return;
        }

        Application.focusChanged += OnApplicationFocusChanged;
        Local = this;

        if (thirdPersonCamera == null)
        {
            thirdPersonCamera = FindFirstObjectByType<ThirdPersonCamera>();
        }

        inputActions = new InputSystem_Actions();

        if (thirdPersonCamera != null)
        {
            thirdPersonCamera.SetTarget(transform);
            thirdPersonCamera.Initialize(inputActions);
        }

        inputActions.Player.Enable();
        inputActions.Player.Jump.performed += OnJump;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        InitializeGrabbers();
    }

    /// <summary>
    /// Sets Rigidbody drag and the initial mainJoint noodle-balance drive.
    /// RagdollController.ApplyUprightDrive() will overwrite the mainJoint drive at Awake,
    /// so NetworkPlayer only needs to set Rigidbody damping here.
    /// </summary>
    protected virtual void ApplyBasePhysicsSettings()
    {
        if (rigidbody3D != null)
        {
            rigidbody3D.linearDamping  = 2f;
            // High angular damping resists rotational forces from grabs/collisions
            // without hard constraints that cause physics launches.
            rigidbody3D.angularDamping = 12f;
        }
        // mainJoint drive is now fully owned by RagdollController (uprightSpring / uprightDamper).
        // NetworkPlayer writes GetScaledMainDrive(MuscleStrength) every fixed tick instead.
    }

    /// <summary>Wires up arm controllers, grabbers, and the network sync component after spawning.</summary>
    void InitializeGrabbers()
    {
        // Pass camera-relative yaw offsets only; ComputeHandTarget multiplies by camera rotation.
        leftArmController?.Initialize(-20f, 0f);
        rightArmController?.Initialize(20f, 0f);

        leftHandGrabber?.Initialize(rigidbody3D);
        rightHandGrabber?.Initialize(rigidbody3D);

        grabSync?.Initialize(leftHandGrabber, rightHandGrabber);
    }

    public override void FixedUpdateNetwork()
    {
        if (rigidbody3D == null || bodyCollider == null)
        {
            return;
        }

        UpdateGrounding();

        if (Object.HasStateAuthority && GetInput(out NetworkInputData networkInputData))
        {
            _lastInput = networkInputData;
        }
        else if (Object.HasStateAuthority)
        {
            // GetInput returned false this tick (Fusion resimulation or dropped packet).
            // Re-use the last confirmed input so movement force is never silently skipped.
            // Clear one-shot flags so jump does not fire repeatedly.
            networkInputData = _lastInput;
            networkInputData.isJumpPressed = false;
        }
        else
        {
            // Not state authority (simulated proxy on client) — drive animator from the
            // networked velocity that the server syncs every tick. Using transform.position
            // delta is wrong here because the client position is a visual Lerp and gives
            // near-zero delta every fixed tick, making the animator always show idle.
            Vector3 netVel = networkRigidbody3D != null
                ? transform.InverseTransformDirection(networkRigidbody3D.NetVelocity)
                : Vector3.zero;

            UpdateAnimator(new Vector2(netVel.x, netVel.z));
            UpdateSyncPhysicsObjects();
            playerBend?.UpdateBend(false);
            return;
        }

        if (Object.HasStateAuthority)
        {
            _lastCameraYaw   = networkInputData.cameraYaw;
            _lastCameraPitch = networkInputData.cameraPitch;

            Quaternion camRot = Quaternion.Euler(_lastCameraPitch, _lastCameraYaw, 0f);

            // Count how many hands are (or will be) grabbing this tick so each can split
            // the pull-up force. A hand counts as active only when its button is held.
            int activeHandCount = (networkInputData.isLeftGrabHeld  ? 1 : 0)
                                + (networkInputData.isRightGrabHeld ? 1 : 0);
            leftHandGrabber?.SetFrameContext(activeHandCount, isGrounded);
            rightHandGrabber?.SetFrameContext(activeHandCount, isGrounded);

            // ── Left arm: drive joint and attempt grab while button is held ──
            if (networkInputData.isLeftGrabHeld)
            {
                if (networkInputData.leftTargetValid)
                    leftArmController?.SetHandTarget(networkInputData.leftHandTarget);

                // While holding a dynamic object the arm drives toward the mouse-controlled
                // target (soft spring). The PD controller in HandGrabber pulls the object
                // toward the hand — NOT the other way around. Overriding the arm target to
                // the grab point creates a feedback loop: arm chases object, object chases
                // arm, they oscillate. SetGrabMode softens the joint so it yields naturally.
                leftArmController?.SetGrabMode(leftHandGrabber != null && leftHandGrabber.IsGrabbing);
                leftArmController?.UpdateArm();
                leftHandGrabber?.TryGrabOrHold();
            }
            else
            {
                leftHandGrabber?.Release();
                leftArmController?.SetGrabMode(false);
                leftArmController?.DriveToRest();
            }

            // ── Right arm: drive joint and attempt grab while button is held ──
            if (networkInputData.isRightGrabHeld)
            {
                if (networkInputData.rightTargetValid)
                    rightArmController?.SetHandTarget(networkInputData.rightHandTarget);

                rightArmController?.SetGrabMode(rightHandGrabber != null && rightHandGrabber.IsGrabbing);
                rightArmController?.UpdateArm();
                rightHandGrabber?.TryGrabOrHold();
            }
            else
            {
                rightHandGrabber?.Release();
                rightArmController?.SetGrabMode(false);
                rightArmController?.DriveToRest();
            }

            // ── Upright stabilization while grabbing ──────────────────────────────────
            bool anyGrabbing = (leftHandGrabber  != null && leftHandGrabber.IsGrabbing)
                            || (rightHandGrabber != null && rightHandGrabber.IsGrabbing);

            if (anyGrabbing && rigidbody3D != null)
            {
                // Only damp spin (roll/pitch angular velocity) to stop the body tumbling.
                // Do NOT apply a corrective upright torque here — the body should be free
                // to tilt and swing with the obstacle, not fight toward world-up.
                Vector3 av = rigidbody3D.angularVelocity;
                rigidbody3D.angularVelocity = new Vector3(av.x * 0.4f, av.y * 0.85f, av.z * 0.4f);
            }
            else if (!anyGrabbing && rigidbody3D != null)
            {
                rigidbody3D.constraints = RigidbodyConstraints.None;
            }


            // Body yaw tracks the camera directly — the mainJoint slerp drive spring
            // is the sole source of lag, giving the HFF body-lag feel without double-smoothing.
            _facingYaw = _lastCameraYaw;

            // Lean body into the movement direction: forward when walking, back when reversing.
            float forwardLean   = -networkInputData.movementInput.y * 12f;
            float sideLean      = -networkInputData.movementInput.x * 5f;
            Quaternion bodyRot  = Quaternion.Euler(forwardLean, _facingYaw, sideLean);

            // Use raw camera yaw for movement direction so the player moves exactly where
            // the camera faces regardless of how far the body has turned yet.
            Quaternion camYawRot = Quaternion.Euler(0f, _lastCameraYaw, 0f);
            Vector3 flatForward  = camYawRot * Vector3.forward;
            Vector3 flatRight    = camYawRot * Vector3.right;

            if (mainJoint != null)
            {
                // ── Pillar 1+2: Noodle balance + dynamic strength scaling ─────────────
                // Write the scaled slerp drive so the upright torque weakens as the body
                // moves faster — at high speed the character goes partially limp and
                // inertia takes over, just like HFF. RagdollController owns the base
                // spring values; MuscleStrength is the per-tick scale factor.
                float muscleStrength = ragdollController != null
                    ? ragdollController.MuscleStrength
                    : 1f;
                mainJoint.slerpDrive = ragdollController != null
                    ? ragdollController.GetScaledMainDrive(muscleStrength)
                    : mainJoint.slerpDrive;

                ConfigurableJointExtensions.SetTargetRotationLocal(
                    mainJoint, bodyRot, _jointStartRotation);
            }

            Vector3 moveDir =
                (flatForward * networkInputData.movementInput.y +
                 flatRight   * networkInputData.movementInput.x).normalized;

            // Project current velocity onto move direction to know if we're already fast enough.
            Vector3 flatVelocity = new Vector3(rigidbody3D.linearVelocity.x, 0f, rigidbody3D.linearVelocity.z);
            float moveDirectionSpeed = moveDir.sqrMagnitude > 0f
                ? Vector3.Dot(flatVelocity, moveDir)
                : 0f;
            float inputMagnitude = networkInputData.movementInput.magnitude;

            if (inputMagnitude > 0f && moveDirectionSpeed < MaxMoveSpeed)
            {
                rigidbody3D.AddForce(moveDir * inputMagnitude * MoveForceMagnitude);
            }

            // Coast to a halt with momentum when no key is held — HFF characters don't stop instantly.
            if (inputMagnitude < 0.1f && isGrounded)
            {
                Vector3 vel = rigidbody3D.linearVelocity;
                rigidbody3D.linearVelocity = new Vector3(vel.x * 0.80f, vel.y, vel.z * 0.80f);
            }

            if (isGrounded && networkInputData.isJumpPressed)
            {
                rigidbody3D.AddForce(Vector3.up * 15f, ForceMode.Impulse);
            }

            UpdateAnimator(networkInputData.movementInput);
            UpdateSyncPhysicsObjects();
            playerBend?.UpdateBend(networkInputData.isBendPressed);
            return;
        }

        Vector3 netVelBottom = networkRigidbody3D != null
            ? transform.InverseTransformDirection(networkRigidbody3D.NetVelocity)
            : Vector3.zero;

        UpdateAnimator(new Vector2(netVelBottom.x, netVelBottom.z));
        UpdateSyncPhysicsObjects();
        playerBend?.UpdateBend(false);
    }

    public override void Render()
    {
        _lastPosition = transform.position;
    }

    public NetworkInputData GetNetworkInput()
    {
        float yaw   = thirdPersonCamera != null ? thirdPersonCamera.CameraYaw   : 0f;
        float pitch = thirdPersonCamera != null ? thirdPersonCamera.CameraPitch : 0f;

        Quaternion camRot = Quaternion.Euler(pitch, yaw, 0f);

        bool mouseMoved = _accumulatedMouseDelta.sqrMagnitude > 0.5f;

        // Always feed mouse delta and recompute targets so they are ready the moment a grab button is pressed.
        // Both arms receive the same raw delta; their independent yaw/pitch accumulate camera-relative offsets.
        // Only accumulate delta while the button is held — when released the arm resets to rest.
        bool leftHeld  = Mouse.current != null && Mouse.current.leftButton.isPressed;
        bool rightHeld = Mouse.current != null && Mouse.current.rightButton.isPressed;

        if (leftArmController != null)
        {
            if (leftHeld) leftArmController.AddMouseDelta(_accumulatedMouseDelta);
            leftArmController.ComputeHandTarget(camRot);
        }

        if (rightArmController != null)
        {
            if (rightHeld) rightArmController.AddMouseDelta(_accumulatedMouseDelta);
            rightArmController.ComputeHandTarget(camRot);
        }

        _accumulatedMouseDelta = Vector2.zero;

        NetworkInputData data = new NetworkInputData
        {
            movementInput    = moveInputVector,
            isJumpPressed    = isJumpButtonPressed,
            isBendPressed    = isBendPressed,
            isLeftGrabHeld   = leftHeld,
            isRightGrabHeld  = rightHeld,
            cameraYaw        = yaw,
            cameraPitch      = pitch,
            leftHandTarget   = leftArmController  != null ? leftArmController.HandTarget  : Vector3.zero,
            rightHandTarget  = rightArmController != null ? rightArmController.HandTarget : Vector3.zero,
            // Valid once Initialize() has run — _initialized becomes true after the first ComputeHandTarget call.
            leftTargetValid  = leftArmController  != null && leftArmController.HasValidTarget,
            rightTargetValid = rightArmController != null && rightArmController.HasValidTarget,
        };

        isJumpButtonPressed = false;
        return data;
    }


    protected void UpdateGrounding()
    {
        isGrounded = false;

        Vector3 castOrigin = rigidbody3D.position + bodyCollider.center;
        float radius = bodyCollider.radius;

        int hitCount = Physics.SphereCastNonAlloc(
            castOrigin, radius, Vector3.down, raycastHits, radius + 0.06f);

        for (int i = 0; i < hitCount; i++)
        {
            if (raycastHits[i].transform == null)
            {
                continue;
            }

            if (raycastHits[i].transform.root == transform)
            {
                continue;
            }

            isGrounded = true;
            _groundNormal = raycastHits[i].normal;
            break;
        }

        bool anyGrabbing = (leftHandGrabber  != null && leftHandGrabber.IsGrabbing)
                        || (rightHandGrabber != null && rightHandGrabber.IsGrabbing);

        if (!isGrounded)
        {
            _groundNormal = Vector3.up;

            // While grabbed, suppress the extra downward gravity so the body can be
            // freely carried by the moving obstacle's velocity injection and the
            // SpringJoint. Without this the 35 N/kg downforce fights every upward or
            // sideward pull and the body barely moves with the obstacle.
            if (!anyGrabbing)
                rigidbody3D.AddForce(Vector3.down * 35f);
        }
        else
        {
            rigidbody3D.AddForce(-_groundNormal * 18f);
        }
    }

    protected void UpdateAnimator(Vector2 movementInput)
    {
        if (animator == null)
        {
            return;
        }

        animator.SetFloat("movementSpeed", movementInput.magnitude);
        animator.SetBool("isMovingBackward", movementInput.y < -backwardInputThreshold);
    }

    protected void UpdateSyncPhysicsObjects()
    {
        if (syncPhysicsObjects == null)
        {
            return;
        }

        for (int i = 0; i < syncPhysicsObjects.Length; i++)
        {
            if (syncPhysicsObjects[i] != null)
            {
                syncPhysicsObjects[i].UpdateJointFromAnimation();
            }
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        // Impact detection retained for future use — ragdoll triggering removed.
    }

    /// <summary>
    /// Called by WeaponHit when a tool strikes this player.
    /// </summary>
    public void TriggerRagdollFromHit()
    {
        // Ragdoll on hit disabled.
    }

    public void PlayerLeft(PlayerRef player)
    {
        if (Object == null)
        {
            return;
        }

        if (Object.InputAuthority == player)
        {
            if (inputActions != null)
            {
                inputActions.Player.Jump.performed -= OnJump;
                inputActions.Player.Disable();
                inputActions = null;
            }

            if (Local == this)
            {
                Local = null;
            }
        }
    }

    void OnApplicationFocusChanged(bool hasFocus)
    {
        if (!hasFocus || PauseMenuManager.IsPaused)
        {
            return;
        }

        if (inputActions != null)
        {
            inputActions.Player.Enable();
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        Application.focusChanged -= OnApplicationFocusChanged;
    }

    public static void ClearLocal() => Local = null;
}
