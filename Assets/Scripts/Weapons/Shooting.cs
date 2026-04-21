using System.Collections;
using UnityEngine;

public class Shooting : MonoBehaviour
{
    public GameObject BulletPrefab;
    public Transform FirePosition;
    public float BulletSpeed = 20f;
    public float ShootInterval = 2f;
    public GameObject BushParent;

    private Collider[] _bushColliders;
    private Coroutine _shootRoutine;

    private void Awake()
    {
        if (BushParent != null)
            _bushColliders = BushParent.GetComponentsInChildren<Collider>(includeInactive: true);
    }

    /// <summary>
    /// Starts the automatic shooting loop.
    /// </summary>
    public void StartShooting()
    {
        if (_shootRoutine != null)
            return;

        _shootRoutine = StartCoroutine(ShootRoutine());
    }

    /// <summary>
    /// Stops the automatic shooting loop.
    /// </summary>
    public void StopShooting()
    {
        if (_shootRoutine == null)
            return;

        StopCoroutine(_shootRoutine);
        _shootRoutine = null;
    }

    private void Fire()
    {
        if (BulletPrefab == null || FirePosition == null)
        {
            Debug.LogWarning("[Shooting] BulletPrefab or FirePosition is not assigned.", this);
            return;
        }

        GameObject bullet = Instantiate(BulletPrefab, FirePosition.position, Quaternion.identity);
        Rigidbody rb = bullet.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.useGravity = true;
            rb.linearVelocity = -transform.forward * BulletSpeed;
        }

        Collider[] bulletColliders = bullet.GetComponents<Collider>();

        // Cannonball ignores the cannon's own colliders
        foreach (Collider cannonCol in GetComponents<Collider>())
            foreach (Collider ballCol in bulletColliders)
                Physics.IgnoreCollision(cannonCol, ballCol);

        // Cannonball passes through all bush colliders
        if (_bushColliders != null)
            foreach (Collider bushCol in _bushColliders)
                foreach (Collider ballCol in bulletColliders)
                    Physics.IgnoreCollision(bushCol, ballCol);
    }

    private IEnumerator ShootRoutine()
    {
        while (true)
        {
            Fire();
            yield return new WaitForSeconds(ShootInterval);
        }
    }
}
