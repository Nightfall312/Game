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
    [SerializeField] GrabIK grabIK;

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

        if (grabIK == null)
            grabIK = GetComponentInChildren<GrabIK>();
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
            // Grabbers still need to be initialized on the state-authority (host) side so that
            // when the host simulates a remote client's player, TryGrabOrHold / Release work
            // correctly. Without this, _rootRigidbody is null on the host for client avatars
            // and every grab attempt silently aborts at the null-guard in HandGrabber.
            InitializeGrabbers();

            // Disable hand IK on proxies — ArmController.ComputeHandTarget() is never called
            // without a local camera, so HandTarget stays at shoulderPivot.forward * reach.
            // Applying IK at full weight to that stale default raises both arms into a raised
            // pose. The physics joints (synced by NetworkRigidbody3D) already reproduce the
            // correct arm pose, so IK must step aside on non-local instances.
            if (grabIK != null) grabIK.EnableIK = false;
            return;
        }

        // Local player — IK is fully active.
        if (grabIK != null) grabIK.EnableIK = true;

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

        // Register arm bone transforms so NetworkGrabSync can read physics-driven rotations
        // from the host each tick and push them to clients in Render().
        // Without this, arm bone Rigidbodies are kinematic on clients and never simulated,
        // so the arms stay frozen at their spawn-time rotation on all non-host machines.
        grabSync?.RegisterArmBones(
            leftArmController?.UpperArmTransform,
            leftArmController?.ForearmTransform,
            rightArmController?.UpperArmTransform,
            rightArmController?.ForearmTransform
        );
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
            // Proxy path — apply networked animator parameters so walk/idle animations play.
            // Arm bone rotations are applied in NetworkGrabSync.Render() by writing directly
            // to Transform.localRotation, which bypasses the kinematic Rigidbody correctly.
            if (grabSync != null)
            {
                ApplyNetworkedAnimatorState(grabSync.NetMovementSpeed, grabSync.NetIsMovingBackward);
            }

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

                // Only drive the arm and attempt a grab when we have a valid target.
                // Calling UpdateArm() with the stale default target (shoulderPivot.forward)
                // drives the joint to the wrong pose on the first tick before the client
                // has sent a valid leftHandTarget, raising the arm into a T-pose that then
                // gets synced to all clients via the physics joint state.
                if (networkInputData.leftTargetValid || (leftHandGrabber != null && leftHandGrabber.IsGrabbing))
                {
                    leftArmController?.SetGrabMode(leftHandGrabber != null && leftHandGrabber.IsGrabbing);
                    leftArmController?.UpdateArm();
                    leftHandGrabber?.TryGrabOrHold();
                }
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

                if (networkInputData.rightTargetValid || (rightHandGrabber != null && rightHandGrabber.IsGrabbing))
                {
                    rightArmController?.SetGrabMode(rightHandGrabber != null && rightHandGrabber.IsGrabbing);
                    rightArmController?.UpdateArm();
                    rightHandGrabber?.TryGrabOrHold();
                }
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
            grabSync?.SyncAnimatorState(networkInputData.movementInput.magnitude,
                                        networkInputData.movementInput.y < -backwardInputThreshold);
            grabSync?.SyncArmState(networkInputData);
            UpdateSyncPhysicsObjects();
            playerBend?.UpdateBend(networkInputData.isBendPressed);
            return;
        }

        Vector3 netVelBottom = networkRigidbody3D != null
            ? transform.InverseTransformDirection(networkRigidbody3D.NetVelocity)
            : Vector3.zero;

        // This path is only reached by simulated proxies that fell through without HasStateAuthority.
        // Arm bones are handled by NetworkGrabSync.Render() — no ArmController calls needed here.
        if (grabSync != null)
        {
            ApplyNetworkedAnimatorState(grabSync.NetMovementSpeed, grabSync.NetIsMovingBackward);
        }
        else
        {
            UpdateAnimator(new Vector2(netVelBottom.x, netVelBottom.z));
        }

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

    /// <summary>
    /// Applies pre-synced animator parameters from the server to the local Animator.
    /// Used by simulated proxies so the host's animation state is replicated faithfully.
    /// </summary>
    protected void ApplyNetworkedAnimatorState(float speed, bool isMovingBackward)
    {
        if (animator == null) return;

        animator.SetFloat("movementSpeed", speed);
        animator.SetBool("isMovingBackward", isMovingBackward);
    }

    /// <summary>
    /// Drives the arm controllers to the networked hand targets on proxy instances.
    /// This reproduces the correct arm direction (matching the server's simulation) without
    /// needing a local camera. ArmController.UpdateArm() is also called so the physics joint
    /// target is updated and the arm doesn't stay locked in the rest pose.
    /// </summary>
    protected void ApplyNetworkedArmState(NetworkGrabSync sync)
    {
        if (sync.NetLeftTargetValid)
        {
            leftArmController?.SetHandTarget(sync.NetLeftHandTarget);
            if (sync.NetIsLeftGrabHeld)
            {
                leftArmController?.SetGrabMode(sync.IsLeftGrabbing);
                leftArmController?.UpdateArm();
            }
            else
            {
                leftArmController?.SetGrabMode(false);
                leftArmController?.DriveToRest();
            }
        }
        else
        {
            leftArmController?.SetGrabMode(false);
            leftArmController?.DriveToRest();
        }

        if (sync.NetRightTargetValid)
        {
            rightArmController?.SetHandTarget(sync.NetRightHandTarget);
            if (sync.NetIsRightGrabHeld)
            {
                rightArmController?.SetGrabMode(sync.IsRightGrabbing);
                rightArmController?.UpdateArm();
            }
            else
            {
                rightArmController?.SetGrabMode(false);
                rightArmController?.DriveToRest();
            }
        }
        else
        {
            rightArmController?.SetGrabMode(false);
            rightArmController?.DriveToRest();
        }
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
