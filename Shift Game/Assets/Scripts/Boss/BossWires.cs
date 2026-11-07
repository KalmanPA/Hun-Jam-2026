using UnityEngine;

/// <summary>The shootable wire bundle inside a boss hatch. Forwards hits to its <see cref="BossWeakPoint"/>.</summary>
public class BossWires : MonoBehaviour, IDamageable
{
    private BossWeakPoint weakPoint;

    private void Awake() => weakPoint = GetComponentInParent<BossWeakPoint>();

    public void TakeDamage(float amount)
    {
        if (weakPoint != null) weakPoint.HitWires(amount);
    }
}
