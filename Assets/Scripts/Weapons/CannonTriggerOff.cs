using UnityEngine;

public class CannonTriggerOff : MonoBehaviour
{
    public Shooting Cannon;

    private Shooting[] _allCannons;

    private void Awake()
    {
        if (Cannon != null)
            _allCannons = Cannon.GetComponentsInChildren<Shooting>(includeInactive: true);
    }

    /// <summary>
    /// Stops all cannons when the Statue enters this zone.
    /// </summary>
    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Statue")) return;
        if (_allCannons == null) return;
        foreach (Shooting s in _allCannons)
            s.StopShooting();
    }
}
