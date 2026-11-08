using System;
using UnityEngine;

/// <summary>
/// Player health. The gear weapon reads it to fill the health bar on the gun (diegetic UI).
/// </summary>
public class PlayerHealth : MonoBehaviour, IDamageable
{
    [SerializeField] private float maxHealth = 100f;

    public float Max => maxHealth;
    public float Current { get; private set; }
    public float Normalized => maxHealth > 0f ? Current / maxHealth : 0f;
    public bool IsDead => Current <= 0f;

    public event Action Died;
    public event Action Revived;

    private void Awake()
    {
        Current = maxHealth;
        // Death: camera falls, gun drops, fade to black, respawn at the active checkpoint.
        if (GetComponent<PlayerDeathSequence>() == null) gameObject.AddComponent<PlayerDeathSequence>();
    }

    /// <summary>Brings the player back to full health (called on respawn).</summary>
    public void Revive()
    {
        Current = maxHealth;
        Revived?.Invoke();
    }

    public void TakeDamage(float amount)
    {
        if (IsDead || amount <= 0f) return;
        Current = Mathf.Max(0f, Current - amount);
        GameAudio.Play2D(SoundId.PlayerHurt, Mathf.Clamp(amount / 20f, 0.5f, 1f));
        if (IsDead) Died?.Invoke();
    }

    public void Heal(float amount)
    {
        if (IsDead || amount <= 0f) return;
        Current = Mathf.Min(maxHealth, Current + amount);
    }
}
