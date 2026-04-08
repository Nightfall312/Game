using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(NetworkRigidbody3D))]
[RequireComponent(typeof(NetworkGrabSync))]
public class NetworkPlayer : NetworkBehaviour, IPlayerLeft
{
    public static NetworkPlayer Local { get; private set; }

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

    Vector2 moveInputVector = Vector2.zero;
    bool isJumpButtonPressed = false;

    // Accumulated raw mouse delta between Update ticks, consumed in GetNetworkInput.
    Vector2 _accumulatedMouseDelta = Vector2.zero;

    const float defaultMaxSpeed        = 3f;
    const float backwardInputThreshold = 0.1f;

    protected bool isGrounded = false;
    RaycastHit[] raycastHits = new RaycastHit[10];

    InputSystem_Actions inputActions;
    SyncPhysicsObject[] syncPhysicsObjects;
    NetworkGrabSync grabSync;

    protected Quaternion _jointStartRotation;
    protected Vector3 _lastPosition;
    protected float _lastCameraYaw;
    float _lastCameraPitch;

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

        moveInputVector = inputActions.Player.Move.ReadValue<Vector2>();

        // Accumulate raw mouse delta every Update so no movement is lost between fixed ticks.
        if (Cursor.lockState == CursorLockMode.Locked && Mouse.current != null)
            _accumulatedMouseDelta += Mouse.current.delta.ReadValue();
    }


    public override void Spawned()
    {
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
                leftArmController?.UpdateArm();
                leftHandGrabber?.TryGrabOrHold();
            }
            else
            {
                leftHandGrabber?.Release();
                leftArmController?.RestoreAnimationSync();
            }

            // ── Right arm: drive joint and attempt grab while button is held ──
            if (networkInputData.isRightGrabHeld)
            {
                if (networkInputData.rightTargetValid)
                {
                    rightArmController?.SetHandTarget(networkInputData.rightHandTarget);
                }
                rightArmController?.UpdateArm();
                rightHandGrabber?.TryGrabOrHold();
            }
            else
            {
                rightHandGrabber?.Release();
                rightArmController?.RestoreAnimationSync();
            }


            Quaternion inputRotation = Quaternion.Euler(0f, _lastCameraYaw, 0f);
            Vector3 flatForward = inputRotation * Vector3.forward;
            Vector3 flatRight = inputRotation * Vector3.right;

            if (mainJoint != null)
            {
                ConfigurableJointExtensions.SetTargetRotationLocal(
                    mainJoint, inputRotation, _jointStartRotation);
            }

            Vector3 moveDir =
                (flatForward * networkInputData.movementInput.y +
                 flatRight * networkInputData.movementInput.x).normalized;

            float localForwardVelocity = Vector3.Dot(flatForward, rigidbody3D.linearVelocity);
            float inputMagnitude = networkInputData.movementInput.magnitude;

            if (inputMagnitude > 0f && localForwardVelocity < MaxMoveSpeed)
            {
                rigidbody3D.AddForce(moveDir * inputMagnitude * MoveForceMagnitude);
            }

            if (isGrounded && networkInputData.isJumpPressed)
            {
                rigidbody3D.AddForce(Vector3.up * 10f, ForceMode.Impulse);
            }

            UpdateAnimator(networkInputData.movementInput);
            UpdateSyncPhysicsObjects();
            return;
        }

        Vector3 localVelocity = transform.InverseTransformDirection(
            (transform.position - _lastPosition) / Mathf.Max(Time.fixedDeltaTime, 0.0001f));

        UpdateAnimator(new Vector2(localVelocity.x, localVelocity.z));
        UpdateSyncPhysicsObjects();
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
            break;
        }

        if (!isGrounded)
        {
            // Extra downward force while airborne to prevent SpringJoint tension
            // from floating the player when grabbing objects above them.
            rigidbody3D.AddForce(Vector3.down * 25f);
        }
        else
        {
            // Always pin the root to the ground with a small downward assist so
            // ragdoll joint tension from grabbed objects cannot lift the player up.
            rigidbody3D.AddForce(Vector3.down * 15f);
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
