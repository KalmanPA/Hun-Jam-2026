using System.Collections;
using UnityEngine;

/// <summary>
/// A box (no collider needed) at the bottom of a pit or gap in the floor. Origin = centre of the box; set its size
/// in the inspector (orange box in the Scene view).
///
/// Respawn At Edge (default): the gap runs across the box's local Z. The zone remembers which side (local -Z or +Z)
/// the player last stood on, and when they fall in the screen fades and they are put back on that side's edge,
/// facing the gap, so they can try the jump again. Nothing in the level resets. Leave the side spawn points empty to
/// find the floor automatically, or drag in a transform to choose the spot yourself.
///
/// With Respawn At Edge off it kills the player instead, who then respawns at the last checkpoint like any other death.
/// </summary>
public class FallKillZone : MonoBehaviour
{
    [Tooltip("Size of the box (local space), centred on this object.")]
    [SerializeField] private Vector3 size = new Vector3(10f, 4f, 10f);

    [Header("Respawn at edge")]
    [Tooltip("On: falling in puts the player back on the edge they jumped from. Off: falling in kills the player.")]
    [SerializeField] private bool respawnAtEdge = true;
    [Tooltip("Optional fixed respawn spot on the local -Z side (blue arrow = facing). Empty = found automatically.")]
    [SerializeField] private Transform negativeSideSpawn;
    [Tooltip("Optional fixed respawn spot on the local +Z side (blue arrow = facing). Empty = found automatically.")]
    [SerializeField] private Transform positiveSideSpawn;
    [Tooltip("How far back from the edge of the gap the player is put (metres).")]
    [SerializeField] private float edgeDistance = 2f;
    [Tooltip("Keeps the automatic spot this far from the side walls (metres).")]
    [SerializeField] private float sideMargin = 1.5f;
    [Tooltip("The floor is searched for downwards from this far above the top of the box (metres).")]
    [SerializeField] private float floorSearchHeight = 8f;
    [Tooltip("Damage taken for falling in. 0 = none. If it would kill, the player dies as normal.")]
    [SerializeField] private float fallDamage = 0f;

    [Header("Fade")]
    [SerializeField] private float fadeOutDuration = 0.35f;
    [SerializeField] private float blackTime = 0.15f;
    [SerializeField] private float fadeInDuration = 0.45f;
    [SerializeField] private Color fadeColor = Color.black;

    private static PlayerHealth player;

    private int lastSide;          // -1 / +1 = side of the gap the player last stood on, 0 = not known yet
    private float lastLocalX;
    private bool respawning;
    private float fade;
    private Texture2D fadeTexture;

    private void OnDestroy()
    {
        if (fadeTexture != null) Destroy(fadeTexture);
    }

    private void Update()
    {
        if (player == null)
        {
            player = FindFirstObjectByType<PlayerHealth>();
            if (player == null) return;
        }
        if (player.IsDead || respawning) return;

        Vector3 local = transform.InverseTransformPoint(player.transform.position);
        Vector3 half = size * 0.5f;

        if (respawnAtEdge) TrackSide(local, half);

        if (Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z)
        {
            if (respawnAtEdge && lastSide != 0) StartCoroutine(RespawnAtEdge());
            else player.TakeDamage(player.Current + 1f);
        }
    }

    /// <summary>Remembers which side of the gap the player is standing on, whenever they are on the ground outside it.</summary>
    private void TrackSide(Vector3 local, Vector3 half)
    {
        CharacterController body = player.GetComponent<CharacterController>();
        if (body == null || !body.isGrounded) return;
        if (Mathf.Abs(local.z) <= half.z) return;              // above the gap itself (e.g. on a ledge inside it)
        if (Mathf.Abs(local.x) > half.x + 2f) return;          // somewhere else entirely
        lastSide = local.z < 0f ? -1 : 1;
        lastLocalX = local.x;
    }

    private IEnumerator RespawnAtEdge()
    {
        respawning = true;
        PlayerHealth target = player;
        FirstPersonController controller = target.GetComponent<FirstPersonController>();
        if (controller != null) controller.Frozen = true;

        yield return Fade(0f, 1f, fadeOutDuration);

        if (target != null && !target.IsDead)
        {
            GetSpawn(lastSide, out Vector3 position, out Quaternion rotation);
            if (controller != null) controller.Teleport(position, rotation);
            else target.transform.SetPositionAndRotation(position, rotation);
            if (fallDamage > 0f) target.TakeDamage(fallDamage);
        }

        yield return new WaitForSeconds(blackTime);
        if (controller != null && target != null && !target.IsDead) controller.Frozen = false;
        yield return Fade(1f, 0f, fadeInDuration);
        respawning = false;
    }

    private void GetSpawn(int side, out Vector3 position, out Quaternion rotation)
    {
        Transform fixedSpawn = side < 0 ? negativeSideSpawn : positiveSideSpawn;
        if (fixedSpawn != null)
        {
            position = fixedSpawn.position;
            rotation = fixedSpawn.rotation;
            return;
        }

        Vector3 half = size * 0.5f;
        float x = Mathf.Clamp(lastLocalX, -half.x + sideMargin, half.x - sideMargin);
        float z = side * (half.z + edgeDistance);
        Vector3 top = transform.TransformPoint(new Vector3(x, half.y, z));
        Vector3 from = top + Vector3.up * floorSearchHeight;

        position = top;
        RaycastHit[] hits = Physics.RaycastAll(from, Vector3.down, floorSearchHeight + size.y + 20f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.GetComponentInParent<PlayerHealth>() != null) continue;
            if (hit.distance < best) { best = hit.distance; position = hit.point; }
        }
        position += Vector3.up * 0.05f;

        // Face the gap.
        Vector3 facing = transform.TransformDirection(new Vector3(0f, 0f, -side));
        facing.y = 0f;
        rotation = facing.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(facing.normalized) : Quaternion.identity;
    }

    private IEnumerator Fade(float from, float to, float duration)
    {
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            fade = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / duration));
            yield return null;
        }
        fade = to;
    }

    private void OnGUI()
    {
        if (fade <= 0f) return;
        if (fadeTexture == null)
        {
            fadeTexture = new Texture2D(1, 1);
            fadeTexture.SetPixel(0, 0, Color.white);
            fadeTexture.Apply();
        }
        GUI.depth = -1000;
        Color c = fadeColor;
        c.a *= fade;
        GUI.color = c;
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), fadeTexture);
        GUI.color = Color.white;
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

    private void OnDrawGizmosSelected()
    {
        if (!respawnAtEdge) return;
        Gizmos.matrix = Matrix4x4.identity;
        Gizmos.color = new Color(0.2f, 0.5f, 1f);
        foreach (int side in new[] { -1, 1 })
        {
            Transform fixedSpawn = side < 0 ? negativeSideSpawn : positiveSideSpawn;
            Vector3 half = size * 0.5f;
            Vector3 p = fixedSpawn != null
                ? fixedSpawn.position
                : transform.TransformPoint(new Vector3(0f, half.y, side * (half.z + edgeDistance)));
            Gizmos.DrawWireSphere(p + Vector3.up * 0.4f, 0.4f);
            Gizmos.DrawLine(p, p + Vector3.up * 2f);
        }
    }
#endif
}
