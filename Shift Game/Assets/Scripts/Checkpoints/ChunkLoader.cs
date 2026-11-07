using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// When the player walks into the trigger, every object in "Turn Off" is deactivated and every object in
/// "Turn On" is activated. Use it to load the next segment and unload the one behind the player.
/// References to segments that a checkpoint resets keep working after a respawn.
/// </summary>
[DefaultExecutionOrder(-1000)]
public class ChunkLoader : MonoBehaviour
{
    [Tooltip("Objects turned OFF when the player enters.")]
    [SerializeField] private List<GameObject> turnOff = new List<GameObject>();
    [Tooltip("Objects turned ON when the player enters.")]
    [SerializeField] private List<GameObject> turnOn = new List<GameObject>();
    [Tooltip("Only fire the first time. Off = fires every time the player enters (safe, since it only sets on/off).")]
    [SerializeField] private bool onlyOnce;

    private bool fired;

    private void Reset()
    {
        Collider c = GetComponent<Collider>();
        if (c != null) c.isTrigger = true;
    }

    private void Awake()
    {
        RespawnManager.EnsureScanned();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (fired && onlyOnce) return;
        PlayerHealth player = other.GetComponentInParent<PlayerHealth>();
        if (player == null || player.IsDead) return;
        fired = true;
        Apply();
    }

    /// <summary>Applies the lists right now (also callable from buttons or UnityEvents).</summary>
    public void Apply()
    {
        foreach (GameObject go in turnOff) if (!ReferenceEquals(go, null)) RespawnManager.SetChunkActive(go, false);
        foreach (GameObject go in turnOn) if (!ReferenceEquals(go, null)) RespawnManager.SetChunkActive(go, true);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.8f);
        Checkpoint.DrawCollider(GetComponent<Collider>());
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 from = transform.position + Vector3.up;
        Gizmos.color = Color.green;
        foreach (GameObject go in turnOn) if (go != null) Gizmos.DrawLine(from, go.transform.position);
        Gizmos.color = Color.red;
        foreach (GameObject go in turnOff) if (go != null) Gizmos.DrawLine(from, go.transform.position);
    }
#endif
}
