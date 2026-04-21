using UnityEngine;
using UnityEngine.InputSystem;

public class ThirdPersonCamera : MonoBehaviour
{
    [SerializeField] Transform target;

    [Header("Orbit Settings")]
    [SerializeField] float distance = 4f;
    [SerializeField] float mouseSensitivity = 0.25f;   // increased from 0.15 — feels correct in builds
    [SerializeField] float minVerticalAngle = -20f;
    [SerializeField] float maxVerticalAngle = 60f;
    [SerializeField] Vector3 targetOffset = new Vector3(0f, 0.8f, 0f);

    [Header("Zoom Settings")]
    [SerializeField] float zoomSpeed = 0.2f;
    [SerializeField] float minDistance = 1.5f;
    [SerializeField] float maxDistance = 3f;
    [SerializeField] float zoomSmoothTime = 0.15f;

    [Header("Collision")]
    [SerializeField] LayerMask collisionMask;
    [SerializeField] float collisionRadius = 0.2f;

    float yaw;
    float pitch = 20f;
    float targetDistance;
    float zoomVelocity;

    // Smoothed pitch — gives a subtle weight to vertical camera movement without lagging yaw.
    float _smoothedPitch = 20f;
    float _pitchSmoothVel;
    const float PitchSmoothTime = 0.05f;

    InputSystem_Actions inputActions;

    public void Initialize(InputSystem_Actions actions)
    {
        inputActions = actions;
        yaw = transform.eulerAngles.y;
        pitch = 20f;
        targetDistance = Mathf.Clamp(distance, minDistance, maxDistance);
    }

    // Mouse delta accumulated by NetworkPlayer.Update() and handed to the camera each frame.
    // This ensures only one consumer reads Mouse.current.delta — no more double-drain.
    Vector2 _pendingDelta;

    /// <summary>Called by NetworkPlayer.Update() to feed this frame's mouse delta.</summary>
    public void FeedMouseDelta(Vector2 delta) => _pendingDelta += delta;

    void LateUpdate()
    {
        if (inputActions == null || target == null)
            return;

        // Use the delta fed by NetworkPlayer (already read from Mouse.current.delta this frame).
        // Do NOT call Mouse.current.delta.ReadValue() here — NetworkPlayer already consumed it.
        Vector2 look = PauseMenuManager.IsPaused ? Vector2.zero : _pendingDelta;
        _pendingDelta = Vector2.zero;

        yaw   += look.x * mouseSensitivity;
        pitch -= look.y * mouseSensitivity;
        pitch  = Mathf.Clamp(pitch, minVerticalAngle, maxVerticalAngle);

        _smoothedPitch = Mathf.SmoothDamp(_smoothedPitch, pitch, ref _pitchSmoothVel, PitchSmoothTime);

        if (Mouse.current != null)
        {
            float scroll = Mouse.current.scroll.ReadValue().y;
            if (scroll != 0f)
            {
                targetDistance -= Mathf.Sign(scroll) * zoomSpeed;
                targetDistance  = Mathf.Clamp(targetDistance, minDistance, maxDistance);
            }
        }

        distance = Mathf.SmoothDamp(distance, targetDistance, ref zoomVelocity, zoomSmoothTime);

        Quaternion rotation = Quaternion.Euler(_smoothedPitch, yaw, 0f);
        Vector3 pivot       = target.position + targetOffset;
        Vector3 direction   = rotation * Vector3.back;

        float actualDist = distance;
        if (Physics.SphereCast(pivot, collisionRadius, direction, out RaycastHit hit, distance, collisionMask))
            actualDist = Mathf.Max(hit.distance - collisionRadius, 0.5f);

        transform.position = pivot + direction * actualDist;
        transform.rotation = rotation;
    }


    public Vector3 FlatForward
    {
        get
        {
            Vector3 f = transform.forward;
            f.y = 0f;
            return f.normalized;
        }
    }

    public Vector3 FlatRight
    {
        get
        {
            Vector3 r = transform.right;
            r.y = 0f;
            return r.normalized;
        }
    }

    public float CameraYaw => yaw;
    public float CameraPitch => pitch;

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }
}
