using UnityEngine;

/// <summary>The glass bubble over a boss hatch during the shield phase. Soaks up shots with a shield sound.</summary>
public class BossShieldBubble : MonoBehaviour, IDamageable
{
    private float nextSoundTime;

    public void TakeDamage(float amount)
    {
        if (Time.time < nextSoundTime) return;
        nextSoundTime = Time.time + 0.1f;
        GameAudio.Play(SoundId.ShieldHit, transform.position);
    }
}
