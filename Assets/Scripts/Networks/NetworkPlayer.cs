using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(NetworkRigidbody3D))]
public class NetworkPlayer : NetworkBehaviour, IPlayerLeft
{
    public static NetworkPlayer Local { get; private set; }

    [SerializeField] Rigidbody rigidbody3D;
    [SerializeField] NetworkRigidbody3D networkRigidbody3D;
    [SerializeField] ConfigurableJoint mainJoint;
    [SerializeField] ThirdPersonCamera thirdPersonCamera;
    [SerializeField] SphereCollider bodyCollider;
    [SerializeField] Animator animator;

    Vector2 moveInputVector = Vector2.zero;
    bool isJumpButtonPressed = false;

    const float maxSpeed = 3f;
    const float backwardInputThreshold = 0.1f;

    bool isGrounded = false;
    RaycastHit[] raycastHits = new RaycastHit[10];

    InputSystem_Actions inputActions;
    SyncPhysicsObject[] syncPhysicsObjects;

    Quaternion _jointStartRotation;
    Vector3 _lastPosition;

    void Awake()
    {
        syncPhysicsObjects = GetComponentsInChildren<SyncPhysicsObject>();

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
        {
            return;
        }

        moveInputVector = inputActions.Player.Move.ReadValue<Vector2>();

        if (!PauseMenuManager.IsPaused &&
            Mouse.current != null &&
            Mouse.current.leftButton.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
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
            Quaternion inputRotation = Quaternion.Euler(0f, networkInputData.cameraYaw, 0f);

            Vector3 flatForward = inputRotation * Vector3.forward;
            Vector3 flatRight = inputRotation * Vector3.right;

            if (mainJoint != null)
            {
                ConfigurableJointExtensions.SetTargetRotationLocal(
                    mainJoint,
                    inputRotation,
                    _jointStartRotation
                );
            }

            Vector3 moveDir =
                (flatForward * networkInputData.movementInput.y +
                 flatRight * networkInputData.movementInput.x).normalized;

            float localForwardVelocity = Vector3.Dot(flatForward, rigidbody3D.linearVelocity);
            float inputMagnitude = networkInputData.movementInput.magnitude;

            if (inputMagnitude > 0f && localForwardVelocity < maxSpeed)
            {
                rigidbody3D.AddForce(moveDir * inputMagnitude * 30f);
            }

            if (isGrounded && networkInputData.isJumpPressed)
            {
                rigidbody3D.AddForce(Vector3.up * 20f, ForceMode.Impulse);
            }

            UpdateAnimator(networkInputData.movementInput);
            UpdateSyncPhysicsObjects();
            return;
        }

        Vector3 localVelocity = transform.InverseTransformDirection(
            (transform.position - _lastPosition) / Mathf.Max(Time.fixedDeltaTime, 0.0001f)
        );

        UpdateAnimator(new Vector2(localVelocity.x, localVelocity.z));
        UpdateSyncPhysicsObjects();
    }

    public override void Render()
    {
        _lastPosition = transform.position;
    }

    public NetworkInputData GetNetworkInput()
    {
        NetworkInputData data = new NetworkInputData
        {
            movementInput = moveInputVector,
            isJumpPressed = isJumpButtonPressed,
            cameraYaw = thirdPersonCamera != null ? thirdPersonCamera.CameraYaw : 0f
        };

        isJumpButtonPressed = false;
        return data;
    }

    void UpdateGrounding()
    {
        isGrounded = false;

        Vector3 castOrigin = rigidbody3D.position + bodyCollider.center;
        float radius = bodyCollider.radius;

        int hitCount = Physics.SphereCastNonAlloc(
            castOrigin,
            radius,
            Vector3.down,
            raycastHits,
            radius + 0.06f
        );

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
            rigidbody3D.AddForce(Vector3.down * 10f);
        }
    }

    void UpdateAnimator(Vector2 movementInput)
    {
        if (animator == null)
        {
            return;
        }

        animator.SetFloat("movementSpeed", movementInput.magnitude);
        animator.SetBool("isMovingBackward", movementInput.y < -backwardInputThreshold);
    }

    void UpdateSyncPhysicsObjects()
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

    // Clears the local player reference on disconnect.
    public static void ClearLocal() => Local = null;
}
