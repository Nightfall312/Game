using UnityEngine;

public class CannonTrigger : MonoBehaviour
{
    public Shooting Cannon;

    private void Awake()
    {
        if (Cannon == null)
            Cannon = GetComponent<Shooting>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (Cannon == null) return;
        Cannon.StartShooting();
    }

    private void OnTriggerExit(Collider other)
    {
        if (Cannon == null) return;
        Cannon.StopShooting();
    }
}
