using Fusion;
using UnityEngine;

/// <summary>
/// Tracks the statue's networked health. Applies damage on hard collisions and when thrown.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(GrabbableObject))]
public class StatueHealth : NetworkBehaviour
{
    [Header("Health")]
    [SerializeField] float maxHealth = 100f;

    [Header("Damage — collision")]
    [SerializeField] float collisionDamageThreshold = 20f;  // minimum impact speed (m/s) to register damage
    [SerializeField] float collisionDamageMultiplier = 2f;  // damage = (speed - threshold) * multiplier

    [Header("Damage — throw")]
    [SerializeField] float throwSpeedThreshold = 15f;       // speed at release that counts as a throw
    [SerializeField] float throwDamageMultiplier = 1.5f;

    [Header("Ignore")]
    [SerializeField] LayerMask damagingLayers = ~0;         // uncheck Player layer to prevent grab-touch damage

    [Networked, OnChangedRender(nameof(OnHealthChanged))]
    public float CurrentHealth { get; private set; }

    public float MaxHealth => maxHealth;
    public float HealthFraction => Mathf.Clamp01(CurrentHealth / maxHealth);

    public static event System.Action<float> OnHealthFractionChanged;

    Rigidbody _rigidbody;
    GrabbableObject _grabbable;

    int _previousGrabCount;
    Vector3 _velocityBeforeRelease;

    public override void Spawned()
    {
        _rigidbody = GetComponent<Rigidbody>();
        _grabbable = GetComponent<GrabbableObject>();

        if (Object.HasStateAuthority)
        {
            CurrentHealth = maxHealth;
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority) return;

        int currentGrabCount = _grabbable.GrabCount;

        // Detect throw: object just released with high velocity
        if (_previousGrabCount > 0 && currentGrabCount == 0)
        {
            float speed = _velocityBeforeRelease.magnitude;
            if (speed > throwSpeedThreshold)
            {
                float damage = (speed - throwSpeedThreshold) * throwDamageMultiplier;
                ApplyDamage(damage);
            }
        }

        _previousGrabCount = currentGrabCount;

        // Cache velocity each tick so we have it at the moment of release
        if (currentGrabCount > 0)
        {
            _velocityBeforeRelease = _rigidbody.linearVelocity;
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!Object.HasStateAuthority) return;

        // Ignore collisions from layers not in the damaging mask (e.g. player body)
        int colLayer = collision.gameObject.layer;
        if ((damagingLayers.value & (1 << colLayer)) == 0) return;

        float impactSpeed = collision.relativeVelocity.magnitude;
        if (impactSpeed > collisionDamageThreshold)
        {
            float damage = (impactSpeed - collisionDamageThreshold) * collisionDamageMultiplier;
            ApplyDamage(damage);
        }
    }

    /// <summary>Reduces health by the given amount, clamped to zero.</summary>
    void ApplyDamage(float damage)
    {
        CurrentHealth = Mathf.Max(0f, CurrentHealth - damage);
    }

    public override void Render()
    {
        OnHealthFractionChanged?.Invoke(HealthFraction);
    }

    void OnHealthChanged()
    {
        // Intentionally empty — UI is driven via Render() for all clients
    }
}
