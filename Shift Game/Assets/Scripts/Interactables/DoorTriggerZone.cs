using UnityEngine;

/// <summary>
/// Box that opens a SlidingDoor (set to "Player Enters Trigger") when the player walks into it.
/// Scale the object to size the box. It only checks the player, robots and bullets don't count.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class DoorTriggerZone : MonoBehaviour
{
    private static CharacterController player;
    private BoxCollider box;

    public bool PlayerInside { get; private set; }

    /// <summary>True if the point is inside the box, ignoring height (rooms are treated as tall columns).</summary>
    public bool ContainsXZ(Vector3 worldPoint)
    {
        BoxCollider b = box != null ? box : GetComponent<BoxCollider>();
        if (b == null) return false;
        Vector3 local = transform.InverseTransformPoint(worldPoint) - b.center;
        Vector3 half = b.size * 0.5f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.z) <= half.z;
    }

    private void Awake()
    {
        box = GetComponent<BoxCollider>();
        box.isTrigger = true;
    }

    private void Update()
    {
        if (player == null)
        {
            FirstPersonController fpc = FindFirstObjectByType<FirstPersonController>();
            if (fpc != null) player = fpc.GetComponent<CharacterController>();
        }
        if (player == null || !player.enabled) { PlayerInside = false; return; }

        // Use the transform (not collider bounds) so a teleported player is seen right away.
        Vector3 playerCenter = player.transform.TransformPoint(player.center);
        Vector3 local = transform.InverseTransformPoint(playerCenter) - box.center;
        Vector3 half = box.size * 0.5f;
        PlayerInside = Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        BoxCollider b = box != null ? box : GetComponent<BoxCollider>();
        if (b == null) return;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(1f, 0.9f, 0f, PlayerInside ? 0.35f : 0.12f);
        Gizmos.DrawCube(b.center, b.size);
        Gizmos.color = new Color(1f, 0.9f, 0f, 1f);
        Gizmos.DrawWireCube(b.center, b.size);
    }
#endif
}
