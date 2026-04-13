using UnityEngine;

/// <summary>
/// Attach to any kinematic obstacle (DOTween-animated transforms) so HandGrabber
/// can read its velocity and transfer momentum to the player when grabbed.
/// Tracks both linear and angular velocity by diffing world position/rotation each FixedUpdate.
/// </summary>
public class KinematicObstacle : MonoBehaviour
{
    // How strongly the obstacle's linear velocity is transferred to the player root on grab.
    [SerializeField] float momentumTransferScale = 1.2f;

    // How strongly the obstacle's angular (tangential) velocity at the grab point is transferred.
    [SerializeField] float angularMomentumScale = 1.0f;

    // Max speed cap so a very fast obstacle can't fling the player infinitely.
    [SerializeField] float maxTransferSpeed = 12f;

    Vector3    _prevPosition;
    Quaternion _prevRotation;

    /// <summary>Linear velocity of the obstacle this fixed frame (world space, m/s).</summary>
    public Vector3 LinearVelocity  { get; private set; }

    /// <summary>Angular velocity of the obstacle this fixed frame (world space, rad/s).</summary>
    public Vector3 AngularVelocity { get; private set; }

    /// <summary>
    /// Returns the tangential velocity at a world-space point caused by this obstacle's rotation.
    /// Combines linear + tangential for the full transfer impulse.
    /// </summary>
    public Vector3 GetVelocityAtPoint(Vector3 worldPoint)
    {
        Vector3 tangential = Vector3.Cross(AngularVelocity, worldPoint - transform.position);
        Vector3 total      = LinearVelocity + tangential;

        // Apply scales and cap magnitude.
        Vector3 scaled = total * momentumTransferScale + tangential * (angularMomentumScale - 1f);
        if (scaled.sqrMagnitude > maxTransferSpeed * maxTransferSpeed)
            scaled = scaled.normalized * maxTransferSpeed;

        return scaled;
    }

    void Awake()
    {
        _prevPosition = transform.position;
        _prevRotation = transform.rotation;
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        if (dt <= 0f) return;

        LinearVelocity = (transform.position - _prevPosition) / dt;

        // Quaternion difference → angular velocity in world space.
        Quaternion deltaRot = transform.rotation * Quaternion.Inverse(_prevRotation);
        deltaRot.ToAngleAxis(out float angleDeg, out Vector3 axis);
        if (angleDeg > 180f) angleDeg -= 360f;
        AngularVelocity = axis * (angleDeg * Mathf.Deg2Rad / dt);

        _prevPosition = transform.position;
        _prevRotation = transform.rotation;
    }
}
