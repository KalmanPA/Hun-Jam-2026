using UnityEngine;

/// <summary>
/// Health or ammo pickup dropped by robots. Spins and bobs; collected when the player walks into it.
/// It is only collected when the player actually needs it (not at full health / full ammo).
/// </summary>
public class Pickup : MonoBehaviour
{
    public enum Kind { Health, Ammo }

    [SerializeField] private Kind kind = Kind.Health;
    [SerializeField] private float amount = 25f;
    [SerializeField] private float pickupRadius = 1.1f;
    [SerializeField] private float spinSpeed = 90f;
    [SerializeField] private float bobHeight = 0.12f;
    [SerializeField] private float bobSpeed = 2.5f;
    [Tooltip("Seconds before it disappears. 0 = never.")]
    [SerializeField] private float lifetime = 0f;

    private static PlayerHealth playerHealth;
    private static GearShiftWeapon playerWeapon;

    private Vector3 basePosition;
    private float age;

    private void Start()
    {
        // Settle on the floor under the drop point.
        if (Physics.Raycast(transform.position + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 10f, ~0, QueryTriggerInteraction.Ignore))
            transform.position = hit.point + Vector3.up * 0.35f;
        basePosition = transform.position;
    }

    private void Update()
    {
        age += Time.deltaTime;
        transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
        transform.position = basePosition + Vector3.up * (Mathf.Sin(age * bobSpeed) * bobHeight);

        if (lifetime > 0f && age > lifetime) { Destroy(gameObject); return; }

        if (playerHealth == null) playerHealth = FindFirstObjectByType<PlayerHealth>();
        if (playerHealth == null) return;

        Vector3 playerCenter = playerHealth.transform.position + Vector3.up * 0.9f;
        if ((playerCenter - transform.position).sqrMagnitude > pickupRadius * pickupRadius) return;

        if (TryApply()) Destroy(gameObject);
    }

    private bool TryApply()
    {
        switch (kind)
        {
            case Kind.Health:
                if (playerHealth.IsDead || playerHealth.Current >= playerHealth.Max) return false;
                playerHealth.Heal(amount);
                return true;

            case Kind.Ammo:
                if (playerWeapon == null) playerWeapon = playerHealth.GetComponentInChildren<GearShiftWeapon>();
                if (playerWeapon == null || playerWeapon.Ammo >= playerWeapon.MaxAmmo) return false;
                playerWeapon.AddAmmo(Mathf.RoundToInt(amount));
                return true;
        }
        return false;
    }
}
