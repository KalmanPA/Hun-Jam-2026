using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The end of the game, played when the boss reaches zero health:
///  - the fight stops (spawners close, robots and bullets vanish, the player can't move or shoot),
///  - three plain robots stand on the boss's front ledge showing their broken-screen face (face 3),
///  - a camera slowly arcs around the boss's screen head while small explosions pop all over the boss,
///  - the view slowly washes toward white (capped well below full white, so it isn't blinding),
///  - then it fades to black and stays there. That's the end of the game.
/// </summary>
public class BossEndingCutscene : MonoBehaviour
{
    [Header("Robots")]
    [SerializeField] private GameObject robotPrefab;
    [Tooltip("Where the three robots stand, in the boss's local space.")]
    [SerializeField] private Vector3[] robotSpots =
    {
        new Vector3(-2.6f, 5f, -4.4f),
        new Vector3(0f, 5f, -4.7f),
        new Vector3(2.6f, 5f, -4.4f),
    };

    [Header("Camera")]
    [Tooltip("What the camera circles around and looks at (the screen head).")]
    [SerializeField] private Transform focus;
    [SerializeField] private Vector3 lookOffset = new Vector3(0f, -3f, 0f);
    [SerializeField] private float radius = 11f;
    [SerializeField] private float heightOffset = -2f;
    [Tooltip("Arc in degrees around the front of the head (0 = straight in front).")]
    [SerializeField] private float startAngle = -65f;
    [SerializeField] private float endAngle = 65f;
    [SerializeField] private float fieldOfView = 60f;

    [Header("Timing (seconds)")]
    [SerializeField] private float orbitTime = 15f;
    [SerializeField] private float whiteStart = 2f;
    [SerializeField] private float whiteEnd = 11f;
    [Tooltip("How white the view gets. Kept well below 1 so it doesn't burn people's eyes.")]
    [SerializeField, Range(0f, 1f)] private float whiteMax = 0.6f;
    [SerializeField] private Color whiteColor = new Color(0.9f, 0.88f, 0.84f);
    [SerializeField] private float blackStart = 11.5f;
    [SerializeField] private float blackTime = 3.5f;

    [Header("Explosions")]
    [SerializeField] private Vector2 explosionInterval = new Vector2(0.15f, 0.45f);
    [SerializeField] private Vector2 explosionSize = new Vector2(0.7f, 1.8f);
    [SerializeField] private Material explosionMaterial;
    [SerializeField] private Color explosionColor = new Color(1f, 0.5f, 0.1f);
    [SerializeField] private bool playEndMusic = true;

    public bool IsPlaying { get; private set; }
    public bool Finished { get; private set; }

    private Color overlay = new Color(0f, 0f, 0f, 0f);

    public void Play(BossMachine boss)
    {
        if (IsPlaying) return;
        IsPlaying = true;
        StartCoroutine(Run(boss));
    }

    private IEnumerator Run(BossMachine boss)
    {
        StopTheFight();
        Camera cam = MakeCamera();
        List<RobotEnemy> robots = PlaceRobots(boss);
        Bounds bossBounds = BossBounds(boss);
        if (playEndMusic) GameAudio.Play2D(SoundId.MusicEnd);

        Transform f = focus != null ? focus : boss.transform;
        float total = Mathf.Max(orbitTime, blackStart + blackTime);
        float nextBoom = 0.3f;

        for (float t = 0f; t < total; t += Time.deltaTime)
        {
            // Slow arc around the face (eased at both ends).
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / orbitTime));
            float angle = Mathf.Lerp(startAngle, endAngle, k);
            Vector3 front = -f.forward; // the screen faces the boss's -Z
            Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * front;
            Vector3 look = f.position + lookOffset;
            cam.transform.position = f.position + dir * radius + Vector3.up * heightOffset;
            cam.transform.rotation = Quaternion.LookRotation(look - cam.transform.position);

            // Robots keep their broken face.
            foreach (RobotEnemy r in robots) if (r != null) r.ShowFace(r.DyingFaceOffset);

            // Small explosions on the boss (stop once it's dark).
            if (t >= nextBoom && t < blackStart + blackTime * 0.5f)
            {
                nextBoom = t + Random.Range(explosionInterval.x, explosionInterval.y);
                Vector3 p = RandomPointOn(bossBounds);
                GearExplosion.Spawn(p, Random.Range(explosionSize.x, explosionSize.y), explosionMaterial, explosionColor, 0.4f);
                GameAudio.Play(Random.value < 0.5f ? SoundId.RobotExplode : SoundId.Gear2Explosion, p, 0.7f);
            }

            // Wash to (soft) white, then to black.
            float white = Mathf.SmoothStep(0f, whiteMax, Mathf.InverseLerp(whiteStart, whiteEnd, t));
            float black = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(blackStart, blackStart + blackTime, t));
            Color c = Color.Lerp(whiteColor, Color.black, black);
            c.a = Mathf.Lerp(white, 1f, black);
            overlay = c;
            yield return null;
        }

        overlay = Color.black;
        Finished = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void StopTheFight()
    {
        foreach (EnemySpawner s in EnemySpawner.All.ToArray()) if (s != null) s.StopSpawning();
        // Includes robots still dropping out of a chute (their scripts are paused, so they aren't in RobotEnemy.Alive).
        foreach (RobotEnemy r in FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None)) if (r != null) Destroy(r.gameObject);
        foreach (EnemyBullet b in FindObjectsByType<EnemyBullet>(FindObjectsSortMode.None)) Destroy(b.gameObject);

        PlayerHealth player = FindFirstObjectByType<PlayerHealth>();
        if (player == null) return;
        player.enabled = false;
        foreach (FirstPersonController c in player.GetComponentsInChildren<FirstPersonController>()) c.enabled = false;
        foreach (GearShiftWeapon w in player.GetComponentsInChildren<GearShiftWeapon>()) w.enabled = false;
        foreach (Camera c in player.GetComponentsInChildren<Camera>()) c.enabled = false;
        foreach (AudioListener l in player.GetComponentsInChildren<AudioListener>()) l.enabled = false;
    }

    private Camera MakeCamera()
    {
        GameObject go = new GameObject("Ending Camera");
        Camera cam = go.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = RenderSettings.fog ? RenderSettings.fogColor : Color.black;
        cam.fieldOfView = fieldOfView;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 300f;
        cam.depth = 50f;
        go.AddComponent<AudioListener>();
        return cam;
    }

    private List<RobotEnemy> PlaceRobots(BossMachine boss)
    {
        var list = new List<RobotEnemy>();
        if (robotPrefab == null) return list;
        Vector3 outward = -boss.transform.forward;
        foreach (Vector3 spot in robotSpots)
        {
            Vector3 p = boss.transform.TransformPoint(spot);
            // Turn each one a little toward the middle so the three read as a group.
            Vector3 face = outward + (boss.transform.position - p).normalized * 0.15f;
            face.y = 0f;
            GameObject go = Instantiate(robotPrefab, p, Quaternion.LookRotation(face));
            go.name = "Ending Robot";
            if (go.TryGetComponent(out UnityEngine.AI.NavMeshAgent agent)) agent.enabled = false; // stay on the ledge
            RobotEnemy r = go.GetComponent<RobotEnemy>();
            if (r != null)
            {
                r.CombatLocked = true;
                r.ShowFace(r.DyingFaceOffset);
                list.Add(r);
            }
        }
        return list;
    }

    private static Bounds BossBounds(BossMachine boss)
    {
        Renderer[] rs = boss.GetComponentsInChildren<Renderer>();
        Bounds b = new Bounds(boss.transform.position + Vector3.up * 6f, new Vector3(8f, 12f, 8f));
        bool first = true;
        foreach (Renderer r in rs)
        {
            if (r is LineRenderer || r is ParticleSystemRenderer) continue;
            if (first) { b = r.bounds; first = false; }
            else b.Encapsulate(r.bounds);
        }
        return b;
    }

    private static Vector3 RandomPointOn(Bounds b)
    {
        // A random point on the bounds' outer skin, biased to the upper half where the camera looks.
        Vector3 p = new Vector3(Random.Range(b.min.x, b.max.x), Mathf.Lerp(b.center.y, b.max.y, Random.value), Random.Range(b.min.z, b.max.z));
        int axis = Random.Range(0, 3);
        if (axis == 0) p.x = Random.value < 0.5f ? b.min.x : b.max.x;
        else if (axis == 2) p.z = Random.value < 0.5f ? b.min.z : b.max.z;
        else p.y = b.max.y;
        return Vector3.Lerp(b.center, p, 0.85f);
    }

    private void OnGUI()
    {
        if (!IsPlaying || overlay.a <= 0.001f) return;
        GUI.depth = -1000;
        Color old = GUI.color;
        GUI.color = overlay;
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = old;
    }
}
