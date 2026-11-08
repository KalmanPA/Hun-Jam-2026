using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The opening of the game, played once when the level starts:
///  1. Fade in. The player stands at the workbench looking down, working (what's on the bench is out of frame).
///  2. They look up: four gunless robots (1 HP each) stand across the belt in front of the bench.
///  3. One robot shouts a long berating line. When it's done the robots keep taking turns berating the player.
///  4. The player looks down again, lifts the weapon into view and shifts it into gear 2.
///  5. Control starts. Each robot keeps its smug face (face 1) while berating and switches to the angry face
///     (face 2) once the player looks at it. They stay where they are, still berating, until they're shot.
/// While any intro robot is alive the blocking belt (the second belt from the player, behind the robots) only
/// carries big crates, back to back, so the rest of the level can't be seen. When the last one dies the belt
/// goes back to its normal items.
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
    [Tooltip("Big crates the blocking belt carries while the intro robots are alive.")]
    [SerializeField] private GameObject[] blockingItems;

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
    [Tooltip("Pause after looking up, before the first robot starts talking.")]
    [SerializeField] private float beforeFirstLine = 0.6f;
    [Tooltip("Longest wait for the first line to finish (also used when the voice line has no clip).")]
    [SerializeField] private float firstLineMaxTime = 14f;
    [Tooltip("Pause after the first line before the player looks down again.")]
    [SerializeField] private float afterFirstLine = 0.8f;
    [SerializeField] private float lookDownTime = 0.9f;
    [Tooltip("Looking down at the bench before grabbing the weapon.")]
    [SerializeField] private float holdDownTime = 1f;
    [Tooltip("Lifting the weapon into the hands while the head comes back up.")]
    [SerializeField] private float raiseTime = 1.1f;

    [Header("Weapon")]
    [Tooltip("Where the weapon starts before it's lifted (camera space, added to the hip pose). Out of frame.")]
    [SerializeField] private Vector3 weaponLoweredOffset = new Vector3(-0.05f, -0.55f, 0.1f);
    [SerializeField] private Vector3 weaponLoweredEuler = new Vector3(40f, -25f, 30f);
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

    [Header("Bench work sounds")]
    [Tooltip("Small metal taps while the player works at the bench.")]
    [SerializeField] private bool workSounds = true;

    /// <summary>True once the intro has played (or been skipped) this play session. Survives scene reloads.</summary>
    public static bool HasPlayed { get; private set; }

    /// <summary>True while the cutscene has control of the player.</summary>
    public static bool IsPlaying { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        HasPlayed = false;
        IsPlaying = false;
    }

    private FirstPersonController player;
    private GearShiftWeapon weapon;
    private Transform cameraPivot;
    private bool introOver;
    private float fade;
    private Texture2D fadeTexture;
    private Coroutine berating;

    private void Awake()
    {
        // Keep the robots calm from their very first frame (they'd otherwise notice the player right away).
        foreach (RobotEnemy r in robots)
            if (r != null) r.CombatLocked = true;

        bool willPlay = playIntro && !HasPlayed;
        if (willPlay) fade = 1f; // black from the first frame, so the level never flashes before the fade in
    }

    private void Start()
    {
        player = FindFirstObjectByType<FirstPersonController>();
        weapon = player != null ? player.GetComponentInChildren<GearShiftWeapon>(true) : null;
        cameraPivot = player != null ? player.CameraPivot : null;

        if (blockingBelt != null && blockingItems != null && blockingItems.Length > 0 && AnyRobotAlive())
            blockingBelt.OverrideItems(blockingItems, true);
        RobotEnemy.AnyDied += OnRobotDied;

        if (!playIntro)
        {
            // Testing elsewhere: leave the player where it is, robots just stand there and fight.
            fade = 0f;
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
            StartFight();
            return;
        }

        StartCoroutine(Play());
    }

    private void OnDestroy()
    {
        RobotEnemy.AnyDied -= OnRobotDied;
        if (IsPlaying) ReleasePlayer();
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
        IsPlaying = true;
        HasPlayed = true;

        player.Frozen = true;
        SetPitch(lookDownPitch);
        Transform gun = null;
        Vector3 gunHip = Vector3.zero, gunLow = Vector3.zero;
        Quaternion gunHipRot = Quaternion.identity, gunLowRot = Quaternion.identity;
        if (weapon != null)
        {
            weapon.enabled = false; // no firing, shifting or posing until the player holds it
            gun = weapon.transform;
            gunHip = weapon.HipPosition;
            gunHipRot = weapon.HipRotation;
            gunLow = gunHip + weaponLoweredOffset;
            gunLowRot = gunHipRot * Quaternion.Euler(weaponLoweredEuler);
            gun.localPosition = gunLow;
            gun.localRotation = gunLowRot;
        }

        // 1. Fade in on the bench, working.
        StartCoroutine(WorkSounds());
        yield return FadeTo(0f, fadeInTime, true);
        yield return WorkAtBench(Mathf.Max(0f, workTime - fadeInTime));

        // 2. Look up at the robots.
        yield return TurnHead(lookDownPitch, lookUpPitch, lookUpTime);
        yield return new WaitForSeconds(beforeFirstLine);

        // 3. One of them gives the first long line.
        RobotEnemy speaker = RandomLivingRobot(null);
        if (speaker != null)
        {
            SoundHandle line = speaker.Say(SoundId.RobotBerate);
            yield return WaitForLine(line, firstLineMaxTime, speaker);
        }
        berating = StartCoroutine(BerateLoop(speaker));
        yield return new WaitForSeconds(afterFirstLine);

        // 4. Look down again, then lift the weapon while the head comes back up.
        yield return TurnHead(lookUpPitch, lookDownPitch, lookDownTime);
        yield return new WaitForSeconds(holdDownTime);

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

    /// <summary>The robots take turns berating the player, one line at a time, until they're all dead.</summary>
    private IEnumerator BerateLoop(RobotEnemy lastSpeaker)
    {
        while (AnyRobotAlive())
        {
            yield return new WaitForSeconds(Random.Range(berateGap.x, berateGap.y));
            RobotEnemy speaker = RandomLivingRobot(lastSpeaker);
            if (speaker == null) break;
            lastSpeaker = speaker;
            SoundHandle line = speaker.Say(SoundId.RobotBerate);
            yield return WaitForLine(line, lineMaxTime, speaker);
        }
        berating = null;
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

    /// <summary>When every intro robot is dead the blocking belt goes back to normal.</summary>
    private void CheckIntroOver()
    {
        if (introOver) return;
        foreach (RobotEnemy r in robots)
            if (r != null && !r.IsDead) return;
        introOver = true;
        if (blockingBelt != null) blockingBelt.ClearItemOverride();
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
        if (fade <= 0f) return;
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
