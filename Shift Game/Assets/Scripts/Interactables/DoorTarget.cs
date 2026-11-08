using UnityEngine;

/// <summary>
/// Shootable target for a SlidingDoor. When its health runs out it pops in a small explosion and the
/// board disappears. A door with several targets opens once all of them are destroyed.
/// </summary>
public class DoorTarget : MonoBehaviour, IDamageable
{
    [SerializeField] private float health = 10f;
    [Tooltip("Hidden when the target is destroyed (the board). The rest (post, base) stays.")]
    [SerializeField] private GameObject[] hideWhenDestroyed;
    [SerializeField] private Material explosionMaterial;
    [SerializeField] private Color explosionColor = new Color(1f, 0.6f, 0.15f);
    [SerializeField] private float explosionSize = 1.2f;
    [Tooltip("The board kicks back by this many degrees when hit.")]
    [SerializeField] private float hitKick = 18f;

    private float current;
    private float kick;
    private Quaternion[] restRotations;

    public bool IsDestroyed { get; private set; }

    private void Awake()
    {
        current = health;
        restRotations = new Quaternion[hideWhenDestroyed != null ? hideWhenDestroyed.Length : 0];
        for (int i = 0; i < restRotations.Length; i++)
            if (hideWhenDestroyed[i] != null) restRotations[i] = hideWhenDestroyed[i].transform.localRotation;
    }

    public void TakeDamage(float amount)
    {
        if (IsDestroyed) return;
        current -= amount;
        kick = hitKick;
        if (current <= 0f) Break();
        else GameAudio.Play(SoundId.TargetHit, transform.position + Vector3.up, Mathf.Clamp(amount / 15f, 0.5f, 1f));
    }

    private void Update()
    {
        if (kick <= 0.01f || IsDestroyed) return;
        kick = Mathf.MoveTowards(kick, 0f, hitKick * 6f * Time.deltaTime);
        for (int i = 0; i < restRotations.Length; i++)
            if (hideWhenDestroyed[i] != null)
                hideWhenDestroyed[i].transform.localRotation = restRotations[i] * Quaternion.Euler(-kick, 0f, 0f);
    }

    /// <summary>Stands the target back up at full health (the boss raises its shield targets again).</summary>
    public void Restore()
    {
        IsDestroyed = false;
        current = health;
        kick = 0f;
        if (hideWhenDestroyed == null) return;
        for (int i = 0; i < hideWhenDestroyed.Length; i++)
        {
            if (hideWhenDestroyed[i] == null) continue;
            hideWhenDestroyed[i].SetActive(true);
            if (restRotations != null && i < restRotations.Length) hideWhenDestroyed[i].transform.localRotation = restRotations[i];
        }
    }

    /// <summary>Puts the target down quietly (no explosion), as if already destroyed.</summary>
    public void SetDown()
    {
        IsDestroyed = true;
        if (hideWhenDestroyed == null) return;
        foreach (GameObject go in hideWhenDestroyed)
            if (go != null) go.SetActive(false);
    }

    private void Break()
    {
        IsDestroyed = true;
        Vector3 center = transform.position + Vector3.up;
        if (hideWhenDestroyed != null)
        {
            foreach (GameObject go in hideWhenDestroyed)
            {
                if (go == null) continue;
                center = go.transform.position;
                go.SetActive(false);
            }
        }
        GearExplosion.Spawn(center, explosionSize, explosionMaterial, explosionColor);
        GameAudio.Play(SoundId.TargetBreak, center);
    }
}
