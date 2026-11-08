using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The opening of the game, played once when the level starts:
///  1. The moment the game starts one robot starts berating the player: it says the voice lines one after the
///     other, in order. Only that robot talks.
///  2. Fade in. The player stands at the workbench looking down, working on the weapon, which lies on the bench.
///  3. They look up: four gunless robots (1 HP each) stand across the belt in front of the bench.
///  4. After the first line the player looks down again, picks the weapon up off the bench and shifts into gear 2.
///  5. Control starts. Each robot keeps its smug face (face 1) while berating and switches to the angry face
///     (face 2) once the player looks at it. They stay where they are, still berating, until they're shot.
/// While any intro robot is alive the blocking belt (the second belt from the player, behind the robots) only
/// carries big crates, back to back, so the rest of the level can't be seen. When the last one dies the belt
/// goes back to its normal items, right after the glasses cutscene (GlassesCutscene: the marker aim reticle).
///
/// The intro plays once per play session: dying (respawn or scene reload) never plays it again.
/// Set up by Final Shift > Intro Cutscene > Build Intro In Game Scene, which places everything under
/// "== Intro Cutscene ==". Move the children around freely; the timings are below.
/// </summary>
[DefaultExecutionOrder(-100)]
public class IntroCutscene : MonoBehaviour
{
    [Tooltip("Turn off to skip the intro while testing other parts of the level (the player stays where it is placed).")]
    [SerializeField] private bool playIntro = true;

    [Header("Scene references (set by the setup tool)")]
    [Tooltip("Where the player stands at the workbench. The blue arrow is the way they face.")]
    [SerializeField] private Transform playerStart;
    [Tooltip("The intro robots. The intro is over for good once all of them are dead.")]
    [SerializeField] private List<RobotEnemy> robots = new List<RobotEnemy>();
    [Tooltip("The 'items come out' opening of the belt behind the robots.")]
    [SerializeField] private ConveyorOpening blockingBelt;
    [Tooltip("Belt Id of the belt that carries the wall of big crates until the whole intro is over (2 = the middle " +
             "belt). Used instead of Blocking Belt when it is above 0.")]
    [SerializeField] private int blockingBeltId = 2;
    [Tooltip("Big crates the blocking belt carries while the intro robots are alive.")]
    [SerializeField] private GameObject[] blockingItems;
    [Tooltip("Plays after the last intro robot is destroyed (glasses + marker reticle), before the belt opens the level.")]
    [SerializeField] private GlassesCutscene glasses;

    [Header("Camera")]
    [Tooltip("How far the player looks down at the workbench (degrees).")]
    [SerializeField] private float lookDownPitch = 42f;
    [Tooltip("Pitch while looking at the robots (negative = up). Their faces are a bit above eye height.")]
    [SerializeField] private float lookUpPitch = -4f;

    [Header("Timing (seconds)")]
    [SerializeField] private float fadeInTime = 1.5f;
    [Tooltip("Working at the bench before looking up.")]
    [SerializeField] private float workTime = 3f;
    [SerializeField] private float lookUpTime = 1.4f;
    [Tooltip("Longest wait for the first line to finish (also used when the voice line has no clip). The first line " +
             "starts the moment the game starts.")]
    [SerializeField] private float firstLineMaxTime = 14f;
    [Tooltip("Pause after the first line (and after looking up) before the player looks down again.")]
    [SerializeField] private float afterFirstLine = 0.8f;
    [SerializeField] private float lookDownTime = 0.9f;
    [Tooltip("Looking down at the bench before grabbing the weapon.")]
    [SerializeField] private float holdDownTime = 1f;
    [Tooltip("Lifting the weapon into the hands while the head comes back up.")]
    [SerializeField] private float raiseTime = 1.1f;

    [Header("Weapon")]
    [Tooltip("Where the weapon lies on the workbench before it's picked up, in the bench's local space " +
             "(the bench object is the 'Workbench' child; its origin is on the floor, middle of the bench).")]
    [SerializeField] private Vector3 gunOnBenchPosition = new Vector3(0.05f, 0.97f, -0.08f);
    [Tooltip("How the weapon lies on the bench (bench local rotation): barrel to the side, on its side.")]
    [SerializeField] private Vector3 gunOnBenchEuler = new Vector3(0f, 75f, 90f);
    [SerializeField] private Gear startGear = Gear.Second;

    [Header("Faces")]
    [Tooltip("After the intro a robot switches from its smug face to its angry face once it's this far inside the screen " +
             "(0 = anywhere on screen, 0.3 = the middle 40%).")]
    [SerializeField, Range(0f, 0.45f)] private float lookedAtMargin = 0.12f;
    [SerializeField] private float lookedAtDistance = 40f;

    [Header("Berating")]
    [Tooltip("Silence between two berating lines (random range).")]
    [SerializeField] private Vector2 berateGap = new Vector2(0.6f, 1.8f);
    [Tooltip("Longest a single line is waited on (in case a clip is missing).")]
    [SerializeField] private float lineMaxTime = 14f;

    [Header("Rest of the level")]
    [Tooltip("Seconds after the glasses cutscene (and the belt going back to normal) before the other robots can " +
             "notice the player and the enemy spawners can start.")]
    [SerializeField] private float levelStartDelay = 2f;

    [Header("Bench work sounds")]
    [Tooltip("Small metal taps while the player works at the bench.")]
    [SerializeField] private bool workSounds = true;

    /// <summary>True once the intro has played (or been skipped) this play session. Survives scene reloads.</summary>
    public static bool HasPlayed { get; private set; }

    /// <summary>True while the cutscene has control of the player.</summary>
    public static bool IsPlaying { get; private set; }

    /// <summary>
    /// True from the first frame until the whole intro is over (intro, killing the intro robots, glasses) plus
    /// <see cref="levelStartDelay"/>. Meanwhile every other robot stays calm and enemy spawners don't start.
    /// </summary>
    public static bool LevelHeld { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        HasPlayed = false;
        IsPlaying = false;
        LevelHeld = false;
    }

    private FirstPersonController player;
    private GearShiftWeapon weapon;
    private Transform cameraPivot;
    private bool introOver;
    private float fade;
    private Texture2D fadeTexture;
    private Coroutine berating;
    private bool skipGlasses;

    private void Awake()
    {
        // Keep the robots calm from their very first frame (they'd otherwise notice the player right away).
        foreach (RobotEnemy r in robots)
            if (r != null) { r.CombatLocked = true; r.IgnoreLevelHold = true; }

        bool willPlay = playIntro && !HasPlayed;
        // The rest of the level waits for the whole intro (only the first time; testing with Play Intro off doesn't wait).
        LevelHeld = willPlay;
        if (willPlay) fade = 1f; // black from the first frame, so the level never flashes before the fade in
    }

    private void Start()
    {
        player = FindFirstObjectByType<FirstPersonController>();
        weapon = player != null ? player.GetComponentInChildren<GearShiftWeapon>(true) : null;
        cameraPivot = player != null ? player.CameraPivot : null;

        if (blockingBeltId > 0)
        {
            ConveyorOpening byId = ConveyorOpening.FindBeltOwner(blockingBeltId, gameObject.scene);
            if (byId != null) blockingBelt = byId;
            else Debug.LogWarning($"[FinalShift] Intro: no belt with Belt Id {blockingBeltId} in this scene, using Blocking Belt instead.", this);
        }
        if (blockingBelt != null && blockingItems != null && blockingItems.Length > 0 && AnyRobotAlive())
            blockingBelt.OverrideItems(blockingItems, true);
        RobotEnemy.AnyDied += OnRobotDied;

        if (!playIntro)
        {
            // Testing elsewhere: leave the player where it is, robots just stand there and fight. Reticle right away.
            fade = 0f;
            skipGlasses = true;
            if (glasses != null) glasses.ShowReticleNow();
            StartFight();
            return;
        }

        PlacePlayerAtStart();

        if (HasPlayed || player == null)
        {
            // Died and the scene reloaded: no intro, straight into the fight.
            fade = 0f;
            HasPlayed = true;
            if (weapon != null) weapon.ForceGear(startGear);
            if (glasses != null && GlassesCutscene.HasPlayed) glasses.ShowReticleNow();
            StartFight();
            return;
        }

        StartCoroutine(Play());
    }

    private void OnDestroy()
    {
        RobotEnemy.AnyDied -= OnRobotDied;
        if (IsPlaying) ReleasePlayer();
        LevelHeld = false;
        if (fadeTexture != null) Destroy(fadeTexture);
    }

    private void PlacePlayerAtStart()
    {
        if (player == null || playerStart == null) return;
        player.Teleport(playerStart.position, playerStart.rotation);
        PlayerDeathSequence death = player.GetComponent<PlayerDeathSequence>();
        if (death != null) death.SetStartPoint(playerStart.position, Quaternion.Euler(0f, playerStart.eulerAngles.y, 0f));
    }

    // ---------------------------------------------------------------- the cutscene

    private IEnumerator Play()
    {
        // The pause tablet is up at game start: the intro (and the first berating line) waits for it.
        while (PauseTablet.IsOpen) yield return null;

        IsPlaying = true;
        HasPlayed = true;

        player.Frozen = true;
        SetPitch(lookDownPitch);
        Transform gun = null, gunParent = null;
        Vector3 gunHip = Vector3.zero, gunLow = Vector3.zero;
        Quaternion gunHipRot = Quaternion.identity, gunLowRot = Quaternion.identity;
        Transform bench = transform.Find("Workbench");
        if (weapon != null)
        {
            weapon.enabled = false; // no firing, shifting or posing until the player holds it
            gun = weapon.transform;
            gunParent = gun.parent;
            gunHip = weapon.HipPosition;
            gunHipRot = weapon.HipRotation;
            if (bench != null)
            {
                // The weapon lies on the workbench: it's what the player is working on.
                gun.SetParent(transform, true);
                gun.SetPositionAndRotation(bench.TransformPoint(gunOnBenchPosition), bench.rotation * Quaternion.Euler(gunOnBenchEuler));
            }
            else
            {
                gun.localPosition = gunHip + new Vector3(-0.05f, -0.55f, 0.1f); // no bench: out of frame below
                gun.localRotation = gunHipRot * Quaternion.Euler(40f, -25f, 30f);
            }
        }

        // 1. The game starts: one robot starts berating right away, over the fade in. Only that robot talks.
        RobotEnemy speaker = RandomLivingRobot(null);
        bool firstLineDone = speaker == null;
        if (speaker != null)
        {
            SoundHandle firstLine = speaker.Say(SoundId.RobotBerate);
            berating = StartCoroutine(FirstLineThenBerate(firstLine, speaker, () => firstLineDone = true));
        }

        // 2. Fade in on the bench, working on the weapon.
        StartCoroutine(WorkSounds());
        yield return FadeTo(0f, fadeInTime, true);
        yield return WorkAtBench(Mathf.Max(0f, workTime - fadeInTime));

        // 3. Look up at the robots, and listen until the first line is over.
        yield return TurnHead(lookDownPitch, lookUpPitch, lookUpTime);
        while (!firstLineDone) yield return null;
        yield return new WaitForSeconds(afterFirstLine);

        // 4. Look down again, pick the weapon up off the bench and lift it while the head comes back up.
        yield return TurnHead(lookUpPitch, lookDownPitch, lookDownTime);
        yield return new WaitForSeconds(holdDownTime);
        if (gun != null)
        {
            if (gun.parent != gunParent) gun.SetParent(gunParent, true);
            gunLow = gun.localPosition;
            gunLowRot = gun.localRotation;
        }

        bool shifted = false;
        for (float t = 0f; t < raiseTime; t += Time.deltaTime)
        {
            float p = Mathf.SmoothStep(0f, 1f, t / raiseTime);
            SetPitch(Mathf.Lerp(lookDownPitch, 0f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t / raiseTime - 0.15f) / 0.85f))));
            if (gun != null)
            {
                // Comes up from below the frame with a little overshoot at the end, like it's swung into the hands.
                float lift = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / raiseTime), 3f);
                gun.localPosition = Vector3.LerpUnclamped(gunLow, gunHip, lift);
                gun.localRotation = Quaternion.Slerp(gunLowRot, gunHipRot, p);
            }
            if (!shifted && t >= raiseTime * 0.7f && weapon != null)
            {
                shifted = true;
                weapon.ForceGear(startGear); // clunk into gear 2 as it settles
            }
            yield return null;
        }
        SetPitch(0f);
        if (gun != null) { gun.localPosition = gunHip; gun.localRotation = gunHipRot; }
        if (!shifted && weapon != null) weapon.ForceGear(startGear);

        // 5. The game starts.
        ReleasePlayer();
        StartFight();
    }

    private IEnumerator WorkAtBench(float duration)
    {
        // Small head movements, like concentrating on something on the bench.
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float nod = Mathf.Sin(t * 2.1f) * 1.2f + Mathf.Sin(t * 5.3f) * 0.4f;
            SetPitch(lookDownPitch + nod * Mathf.Clamp01(t));
            yield return null;
        }
        SetPitch(lookDownPitch);
    }

    private IEnumerator WorkSounds()
    {
        if (!workSounds) yield break;
        float end = Time.time + workTime;
        yield return new WaitForSeconds(0.4f);
        while (Time.time < end)
        {
            GameAudio.Play2D(SoundId.Debris, Random.Range(0.6f, 1f), Random.Range(0.9f, 1.2f));
            yield return new WaitForSeconds(Random.Range(0.35f, 0.9f));
        }
    }

    private IEnumerator TurnHead(float from, float to, float duration)
    {
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            SetPitch(Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / duration)));
            yield return null;
        }
        SetPitch(to);
    }

    private void SetPitch(float pitch)
    {
        if (cameraPivot != null) cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    private void ReleasePlayer()
    {
        IsPlaying = false;
        // The head ends level (pitch 0), which is also where Teleport left the controller's own pitch.
        if (player != null) player.Frozen = false;
        if (weapon != null) weapon.enabled = true;
        fade = 0f;
    }

    private void StartFight()
    {
        HasPlayed = true;
        StartCoroutine(UnlockWhenSeen());
        if (berating == null && AnyRobotAlive()) berating = StartCoroutine(BerateLoop(null));
        CheckIntroOver();
    }

    /// <summary>
    /// Once the player has control, each robot keeps its smug face (face 1) until the player looks at it,
    /// then switches to its angry face (face 2) and joins the fight.
    /// </summary>
    private IEnumerator UnlockWhenSeen()
    {
        Camera cam = cameraPivot != null ? cameraPivot.GetComponentInChildren<Camera>() : null;
        if (cam == null) cam = Camera.main;
        while (true)
        {
            bool anyLocked = false;
            foreach (RobotEnemy r in robots)
            {
                if (r == null || r.IsDead || !r.CombatLocked) continue;
                anyLocked = true;
                if (cam == null || IsLookedAt(cam, r)) r.CombatLocked = false;
            }
            if (!anyLocked) yield break;
            yield return null;
        }
    }

    private bool IsLookedAt(Camera cam, RobotEnemy robot)
    {
        Vector3 face = robot.transform.position + Vector3.up * 1.6f;
        Vector3 v = cam.WorldToViewportPoint(face);
        return v.z > 0f && v.z < lookedAtDistance && v.x > lookedAtMargin && v.x < 1f - lookedAtMargin && v.y > 0f && v.y < 1f;
    }

    // ---------------------------------------------------------------- berating

    /// <summary>
    /// One robot berates the player, saying the lines one after the other (in order), until they're all dead.
    /// Only that robot talks; if it's destroyed, another living one takes over where it left off.
    /// </summary>
    private IEnumerator BerateLoop(RobotEnemy speaker)
    {
        while (AnyRobotAlive())
        {
            yield return new WaitForSeconds(Random.Range(berateGap.x, berateGap.y));
            if (speaker == null || speaker.IsDead) speaker = RandomLivingRobot(null);
            if (speaker == null) break;
            SoundHandle line = speaker.Say(SoundId.RobotBerate);
            yield return WaitForLine(line, lineMaxTime, speaker);
        }
        berating = null;
    }

    private IEnumerator FirstLineThenBerate(SoundHandle firstLine, RobotEnemy speaker, System.Action onFirstLineDone)
    {
        yield return WaitForLine(firstLine, firstLineMaxTime, speaker);
        onFirstLineDone();
        yield return BerateLoop(speaker);
    }

    private static IEnumerator WaitForLine(SoundHandle line, float maxTime, RobotEnemy speaker)
    {
        if (!line.IsValid)
        {
            // No clip (sound bank not rebuilt yet): wait a bit so the timing still feels right.
            yield return new WaitForSeconds(Mathf.Min(3f, maxTime));
            yield break;
        }
        // A short wait first, the voice can take a frame to start.
        yield return null;
        float end = Time.time + maxTime;
        while (line.IsPlaying && Time.time < end && speaker != null && !speaker.IsDead)
            yield return null;
    }

    private RobotEnemy RandomLivingRobot(RobotEnemy notThisOne)
    {
        List<RobotEnemy> alive = new List<RobotEnemy>();
        foreach (RobotEnemy r in robots)
            if (r != null && !r.IsDead && r != notThisOne) alive.Add(r);
        if (alive.Count == 0 && notThisOne != null && !notThisOne.IsDead) return notThisOne;
        return alive.Count > 0 ? alive[Random.Range(0, alive.Count)] : null;
    }

    private bool AnyRobotAlive()
    {
        foreach (RobotEnemy r in robots)
            if (r != null && !r.IsDead) return true;
        return false;
    }

    // ---------------------------------------------------------------- end of the intro

    private void OnRobotDied(RobotEnemy robot)
    {
        if (robots.Contains(robot)) CheckIntroOver();
    }

    /// <summary>When every intro robot is dead the glasses cutscene plays, then the blocking belt goes back to normal.</summary>
    private void CheckIntroOver()
    {
        if (introOver) return;
        foreach (RobotEnemy r in robots)
            if (r != null && !r.IsDead) return;
        introOver = true;
        if (glasses != null && !skipGlasses) glasses.Play(OpenLevel);
        else OpenLevel();
    }

    private void OpenLevel()
    {
        if (blockingBelt != null) blockingBelt.ClearItemOverride();
        if (LevelHeld) StartCoroutine(ReleaseLevelAfterDelay());
    }

    private IEnumerator ReleaseLevelAfterDelay()
    {
        yield return new WaitForSeconds(levelStartDelay);
        LevelHeld = false;
    }

    // ---------------------------------------------------------------- fade

    private IEnumerator FadeTo(float target, float duration, bool smooth)
    {
        float from = fade;
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float p = t / duration;
            fade = Mathf.Lerp(from, target, smooth ? Mathf.SmoothStep(0f, 1f, p) : p);
            yield return null;
        }
        fade = target;
    }

    private void OnGUI()
    {
        if (fade <= 0f || PauseTablet.IsOpen) return; // the tablet draws its own black backdrop
        if (fadeTexture == null)
        {
            fadeTexture = new Texture2D(1, 1);
            fadeTexture.SetPixel(0, 0, Color.white);
            fadeTexture.Apply();
        }
        GUI.depth = -900;
        GUI.color = new Color(0f, 0f, 0f, fade);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), fadeTexture);
        GUI.color = Color.white;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (playerStart == null) return;
        Vector3 p = playerStart.position;
        Gizmos.color = new Color(1f, 0.8f, 0.2f);
        Gizmos.DrawWireSphere(p + Vector3.up * 0.4f, 0.35f);
        Gizmos.DrawWireSphere(p + Vector3.up * 1.4f, 0.35f);
        Vector3 f = Vector3.ProjectOnPlane(playerStart.forward, Vector3.up).normalized;
        Vector3 eye = p + Vector3.up * 1.6f;
        Gizmos.DrawLine(eye, eye + f * 1.2f);
        UnityEditor.Handles.Label(p + Vector3.up * 2.2f, "Intro: player start");
    }
#endif
}
