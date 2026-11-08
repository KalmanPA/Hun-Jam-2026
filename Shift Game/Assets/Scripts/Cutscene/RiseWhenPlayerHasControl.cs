using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Hides an object under the floor when the level starts and raises it back to where it was placed in the editor
/// the moment the player first gets control (the end of the intro cutscene). Used for the "controlButtons" board
/// next to the workbench, so it pops up when the player can actually use the controls.
///
/// Place the object where it should end up; it is pushed down by <see cref="sinkDepth"/> at runtime.
/// After a scene reload (dying) or with the intro turned off it is simply already up.
/// Objects named "controlButtons" get this component automatically, so it doesn't have to be added by hand.
/// </summary>
public class RiseWhenPlayerHasControl : MonoBehaviour
{
    private const string AutoAttachName = "controlButtons";

    [Tooltip("How far below its editor position it waits (metres).")]
    [SerializeField] private float sinkDepth = 2.5f;
    [Tooltip("How long the rise takes (seconds).")]
    [SerializeField] private float riseTime = 1.2f;
    [Tooltip("Extra wait after the player gets control (seconds).")]
    [SerializeField] private float delay = 0.3f;
    [Tooltip("Wait for the glasses cutscene (after the intro robots are dead) instead of the end of the intro.")]
    [SerializeField] private bool waitForGlasses;
    [SerializeField] private bool playSound = true;

    private Vector3 upPosition;
    private Vector3 downPosition;
    private bool firstCheck = true;
    private bool rising;
    private bool done;
    private float timer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        // The first scene is already loaded by the time this runs.
        AttachIn(SceneManager.GetActiveScene());
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => AttachIn(scene);

    private static void AttachIn(Scene scene)
    {
        if (!scene.isLoaded) return;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == AutoAttachName && t.GetComponent<RiseWhenPlayerHasControl>() == null)
                    t.gameObject.AddComponent<RiseWhenPlayerHasControl>();
    }

    private void Awake()
    {
        upPosition = transform.position;
        downPosition = upPosition + Vector3.down * sinkDepth;
        transform.position = downPosition;
    }

    private bool PlayerHasControl()
    {
        // No intro in this scene: the player has control from the start.
        if (FindFirstObjectByType<IntroCutscene>() == null) return true;
        if (!IntroCutscene.HasPlayed || IntroCutscene.IsPlaying) return false;
        if (waitForGlasses && (!GlassesCutscene.HasPlayed || GlassesCutscene.IsPlaying)) return false;
        return true;
    }

    private void Update()
    {
        if (done) return;

        if (!rising)
        {
            bool ready = PlayerHasControl();
            if (ready && firstCheck)
            {
                // Already in control on the first frame (scene reload, intro off): just be there.
                transform.position = upPosition;
                done = true;
                return;
            }
            firstCheck = false;
            if (!ready) return;
            rising = true;
            timer = -delay;
            if (playSound) GameAudio.Play(SoundId.ElevatorStart, upPosition, 0.8f, 1.15f);
            return;
        }

        timer += Time.deltaTime;
        if (timer < 0f) return;
        float p = Mathf.Clamp01(timer / Mathf.Max(0.01f, riseTime));
        // Ease out with a small overshoot, so it lands with a little bump.
        float eased = 1f + 2.2f * Mathf.Pow(p - 1f, 3f) + 1.2f * Mathf.Pow(p - 1f, 2f);
        transform.position = Vector3.LerpUnclamped(downPosition, upPosition, eased);
        if (p >= 1f)
        {
            transform.position = upPosition;
            done = true;
            if (playSound) GameAudio.Play(SoundId.ElevatorStop, upPosition, 0.8f, 1.2f);
        }
    }
}
