using System.Collections;
using UnityEngine;

public class Shooting : MonoBehaviour
{
    public GameObject BulletPrefab;
    public Transform FirePosition;
    public float BulletSpeed = 20f;
    public float ShootInterval = 2f;

    private Coroutine _shootRoutine;

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

        // Ignore collision between the ball and the cannon so it never gets stuck
        foreach (Collider cannonCol in GetComponents<Collider>())
            foreach (Collider ballCol in bullet.GetComponents<Collider>())
                Physics.IgnoreCollision(cannonCol, ballCol);
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
