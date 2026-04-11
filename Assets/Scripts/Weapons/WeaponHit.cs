using Fusion;
using UnityEngine;

/// <summary>
/// Attach to any grabbable tool. Detects collisions with players and applies
/// a knockback impulse to the hit player's root Rigidbody.
/// Only the object's state authority processes and sends the hit.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class WeaponHit : NetworkBehaviour
{
    [Header("Hit settings")]
    [SerializeField] float knockbackForce       = 8f;
    [SerializeField] float minVelocityToHit     = 2f;   // tool must be moving this fast to register
    [SerializeField] float hitCooldown          = 0.5f; // seconds between hits on the same target

    Rigidbody _rigidbody;
    float _lastHitTime = -999f;

    public override void Spawned()
    {
        _rigidbody = GetComponent<Rigidbody>();
    }

    void OnCollisionEnter(Collision collision)
    {
        // Object is null when Fusion hasn't spawned this NetworkBehaviour yet or has
        // already despawned it. Physics callbacks can fire outside that window.
        if (Object == null || !Object.HasStateAuthority) return;
        if (_rigidbody == null) return;
        if (_rigidbody.linearVelocity.magnitude < minVelocityToHit) return;
        if (Time.time - _lastHitTime < hitCooldown) return;

        NetworkPlayer player = collision.gameObject.GetComponentInParent<NetworkPlayer>();
        if (player == null) return;
        if (player.RootRigidbody == null) return;

        Vector3 direction = (player.RootRigidbody.position - _rigidbody.position).normalized;
        direction.y = Mathf.Max(direction.y, 0.3f);

        player.RootRigidbody.AddForce(direction * knockbackForce, ForceMode.Impulse);
        _lastHitTime = Time.time;
    }
}
