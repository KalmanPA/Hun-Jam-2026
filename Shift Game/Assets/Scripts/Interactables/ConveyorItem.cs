using UnityEngine;

/// <summary>
/// Something that rides a conveyor belt. Small items are normal physics bodies, so the player and bullets
/// can knock them off the belt. Immovable items (big crates as tall as the player) are kinematic: the belt
/// slides them along and nothing else can move them.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class ConveyorItem : MonoBehaviour
{
    [Tooltip("Big items: the player and bullets can't move them, the belt slides them along.")]
    [SerializeField] private bool immovable;
    [Tooltip("Seconds a belt-spawned item stays around after it was knocked off the belt. 0 = forever.")]
    [SerializeField] private float offBeltLifetime = 30f;

    private bool spawnedByBelt;
    private float lastCarriedTime;

    public bool Immovable => immovable;
    public Rigidbody Body { get; private set; }

    private void Awake()
    {
        Body = GetComponent<Rigidbody>();
        Body.interpolation = RigidbodyInterpolation.Interpolate;
        if (immovable) Body.isKinematic = true;
        lastCarriedTime = Time.time;
    }

    public void OnSpawnedByBelt()
    {
        spawnedByBelt = true;
        MarkCarried();
    }

    public void MarkCarried() => lastCarriedTime = Time.time;

    private void Update()
    {
        if (spawnedByBelt && offBeltLifetime > 0f && Time.time - lastCarriedTime > offBeltLifetime)
            Destroy(gameObject);
    }
}
