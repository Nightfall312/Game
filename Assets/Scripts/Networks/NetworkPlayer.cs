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

    Vector2 moveInputVector = Vector2.zero;
    bool isJumpButtonPressed = false;
    bool isBendPressed = false;

    // Accumulated raw mouse delta between Update ticks, consumed in GetNetworkInput.
    Vector2 _accumulatedMouseDelta = Vector2.zero;

    // Last received input — used to keep movement alive on ticks where GetInput returns false
    // (e.g. during Fusion resimulation steps triggered by grab/drop physics events).
    NetworkInputData _lastInput;

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

    // Smoothed body facing yaw — body turns slightly behind the camera for HFF body-lag feel.
    float _facingYaw;
    float _facingYawVelocity;

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

        // Keep the cursor locked every frame so the game view always has focus.
        // Without this, Unity's editor only delivers keyboard input while the mouse is clicked inside the view.
        // Re-lock unconditionally after any grab/drop action that may have released the cursor.
        if (!PauseMenuManager.IsPaused)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
        }

        moveInputVector = inputActions.Player.Move.ReadValue<Vector2>();
        isBendPressed   = Keyboard.current != null && Keyboard.current.leftCtrlKey.isPressed;

        // Accumulate raw mouse delta every Update. Read unconditionally while not paused
        // so a momentary cursor state change after a grab/drop never freezes the camera.
        if (!PauseMenuManager.IsPaused && Mouse.current != null)
            _accumulatedMouseDelta += Mouse.current.delta.ReadValue();
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
    /// Sets Rigidbody drag and joint damper for HFF-style weighted movement.
    /// Override in subclasses (e.g. DrunkNetworkPlayer) to apply character-specific values.
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

        if (mainJoint != null)
        {
            // Slerp drive spring of 1200 is strong enough to keep the body upright
            // while the angular axes are Free (no hard Locked constraint). This is the
            // HFF approach: soft spring resists tipping rather than a locked axis that
            // fights the SpringJoint and causes the ragdoll to collapse.
            JointDrive slerpDrive = mainJoint.slerpDrive;
            slerpDrive.positionSpring = 1200f;
            slerpDrive.positionDamper = 40f;
            mainJoint.slerpDrive = slerpDrive;
        }
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
            // Not state authority — run animator from estimated velocity, no forces.
            Vector3 estimatedVelocity = transform.InverseTransformDirection(
                (transform.position - _lastPosition) / Mathf.Max(Time.fixedDeltaTime, 0.0001f));

            UpdateAnimator(new Vector2(estimatedVelocity.x, estimatedVelocity.z));
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
                {
                    leftArmController?.SetHandTarget(networkInputData.leftHandTarget);
                }

                // If the hand is already grabbing, override the arm target to the actual
                // grab contact point so the arm drives TOWARD the grab instead of fighting it.
                if (leftHandGrabber != null && leftHandGrabber.IsGrabbing)
                    leftArmController?.OverrideHandTargetToGrabPoint(leftHandGrabber.GrabPoint);

                leftArmController?.UpdateArm();
                leftHandGrabber?.TryGrabOrHold();
            }
            else
            {
                leftHandGrabber?.Release();
                leftArmController?.DriveToRest();
            }

            // ── Right arm: drive joint and attempt grab while button is held ──
            if (networkInputData.isRightGrabHeld)
            {
                if (networkInputData.rightTargetValid)
                {
                    rightArmController?.SetHandTarget(networkInputData.rightHandTarget);
                }

                // If the hand is already grabbing, override the arm target to the actual
                // grab contact point so the arm drives TOWARD the grab instead of fighting it.
                if (rightHandGrabber != null && rightHandGrabber.IsGrabbing)
                    rightArmController?.OverrideHandTargetToGrabPoint(rightHandGrabber.GrabPoint);

                rightArmController?.UpdateArm();
                rightHandGrabber?.TryGrabOrHold();
            }
            else
            {
                rightHandGrabber?.Release();
                rightArmController?.DriveToRest();
            }

            // ── Upright stabilization while grabbing ──────────────────────────────────
            // Since angular axes are now Free (not Locked), the slerp drive and angular
            // damping must do all the stabilisation work. While grabbing, aggressively
            // bleed pitch/roll angular velocity every tick so the body self-rights even
            // when the SpringJoint is pulling hard — matching HFF's feel.
            bool anyGrabbing = (leftHandGrabber  != null && leftHandGrabber.IsGrabbing)
                            || (rightHandGrabber != null && rightHandGrabber.IsGrabbing);

            if (anyGrabbing && rigidbody3D != null)
            {
                Vector3 av = rigidbody3D.angularVelocity;
                // Damp pitch (X) and roll (Z) very aggressively while hanging/grabbing.
                // Leave yaw (Y) relatively free so the player can still swing/turn.
                rigidbody3D.angularVelocity = new Vector3(av.x * 0.3f, av.y * 0.85f, av.z * 0.3f);

                // Additionally apply a corrective torque toward world-upright to counteract
                // the grab spring pulling the root sideways. This is the equivalent of
                // HFF's pelvis upright constraint — a torque, not a hard lock.
                Vector3 bodyUp   = rigidbody3D.transform.up;
                Vector3 corrAxis = Vector3.Cross(bodyUp, Vector3.up);
                if (corrAxis.sqrMagnitude > 0.0001f)
                    rigidbody3D.AddTorque(corrAxis * 120f, ForceMode.Acceleration);
            }
            else if (!anyGrabbing && rigidbody3D != null)
            {
                rigidbody3D.constraints = RigidbodyConstraints.None;
            }


            // Smoothly rotate the body to face the camera — gives HFF-style body lag on turns.
            _facingYaw = Mathf.SmoothDampAngle(
                _facingYaw, _lastCameraYaw, ref _facingYawVelocity, 0.1f);

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

        Vector3 localVelocity = transform.InverseTransformDirection(
            (transform.position - _lastPosition) / Mathf.Max(Time.fixedDeltaTime, 0.0001f));

        UpdateAnimator(new Vector2(localVelocity.x, localVelocity.z));
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

        if (!isGrounded)
        {
            // Heavier fall gravity — HFF characters drop fast and feel weighty in the air.
            _groundNormal = Vector3.up;
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
