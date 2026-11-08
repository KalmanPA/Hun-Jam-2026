using UnityEngine;

/// <summary>
/// A box (no collider needed) that kills the player when they fall into it, for pits and holes in the floor.
/// The player then respawns at the last checkpoint like any other death.
/// Origin = centre of the box; set its size in the inspector (orange box in the Scene view).
/// </summary>
public class FallKillZone : MonoBehaviour
{
    [Tooltip("Size of the kill box (local space), centred on this object.")]
    [SerializeField] private Vector3 size = new Vector3(10f, 4f, 10f);

    private static PlayerHealth player;

    private void Update()
    {
        if (player == null)
        {
            player = FindFirstObjectByType<PlayerHealth>();
            if (player == null) return;
        }
        if (player.IsDead) return;

        Vector3 local = transform.InverseTransformPoint(player.transform.position);
        Vector3 half = size * 0.5f;
        if (Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z)
            player.TakeDamage(player.Current + 1f);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(1f, 0.35f, 0f, 0.12f);
        Gizmos.DrawCube(Vector3.zero, size);
        Gizmos.color = new Color(1f, 0.35f, 0f, 1f);
        Gizmos.DrawWireCube(Vector3.zero, size);
    }
#endif
}
