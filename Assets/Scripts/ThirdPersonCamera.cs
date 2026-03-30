using UnityEngine;
using UnityEngine.InputSystem;

public class ThirdPersonCamera : MonoBehaviour
{
    [SerializeField] Transform target;

    [Header("Orbit Settings")]
    [SerializeField] float distance = 4f;
    [SerializeField] float mouseSensitivity = 0.15f;
    [SerializeField] float minVerticalAngle = -20f;
    [SerializeField] float maxVerticalAngle = 60f;
    [SerializeField] Vector3 targetOffset = new Vector3(0f, 0.8f, 0f);

    [Header("Zoom Settings")]
    [SerializeField] float zoomSpeed = 0.2f;
    [SerializeField] float minDistance = 1.5f;
    [SerializeField] float maxDistance = 5f;
    [SerializeField] float zoomSmoothTime = 0.15f;

    [Header("Collision")]
    [SerializeField] LayerMask collisionMask;
    [SerializeField] float collisionRadius = 0.2f;

    float yaw;
    float pitch = 20f;
    float targetDistance;
    float zoomVelocity;

    InputSystem_Actions inputActions;

    public void Initialize(InputSystem_Actions actions)
    {
        inputActions = actions;
        yaw = transform.eulerAngles.y;
        pitch = 20f;
        targetDistance = Mathf.Clamp(distance, minDistance, maxDistance);
    }

    void LateUpdate()
    {
        if (inputActions == null || target == null)
        {
            return;
        }

        Vector2 look = Vector2.zero;

        if (Cursor.lockState == CursorLockMode.Locked && Mouse.current != null)
        {
            look = Mouse.current.delta.ReadValue();
        }

        yaw += look.x * mouseSensitivity;
        pitch -= look.y * mouseSensitivity;
        pitch = Mathf.Clamp(pitch, minVerticalAngle, maxVerticalAngle);

        if (Mouse.current != null)
        {
            float scroll = Mouse.current.scroll.ReadValue().y;

            if (scroll != 0f)
            {
                targetDistance -= Mathf.Sign(scroll) * zoomSpeed;
                targetDistance = Mathf.Clamp(targetDistance, minDistance, maxDistance);
            }
        }

        distance = Mathf.SmoothDamp(distance, targetDistance, ref zoomVelocity, zoomSmoothTime);

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 pivot = target.position + targetOffset;
        Vector3 direction = rotation * Vector3.back;

        float actualDist = distance;

        if (Physics.SphereCast(pivot, collisionRadius, direction, out RaycastHit hit, distance, collisionMask))
        {
            actualDist = Mathf.Max(hit.distance - collisionRadius, 0.5f);
        }

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

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }
}
