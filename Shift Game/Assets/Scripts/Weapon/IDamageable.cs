/// <summary>
/// Anything that can be hit by the gear weapon's bullets (robots, the player, props).
/// </summary>
public interface IDamageable
{
    void TakeDamage(float amount);
}
