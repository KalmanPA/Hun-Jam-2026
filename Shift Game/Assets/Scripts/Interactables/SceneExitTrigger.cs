using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Box that loads the next scene in Build Settings when the player walks into it.
/// Scale the object to size the box. Only the player counts; robots and bullets don't.
/// The screen fades to black first, then the next scene loads.
/// If this is the last scene in the build, it loads "Scene After Last" (-1 = do nothing).
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class SceneExitTrigger : MonoBehaviour
{
    [Tooltip("Optional: load this scene (by name, must be in Build Settings) instead of the next one.")]
    [SerializeField] private string overrideSceneName = "";
    [Tooltip("Build index to load when this is the last scene in the build. -1 = do nothing.")]
    [SerializeField] private int sceneAfterLast = -1;

    [Header("Fade")]
    [SerializeField] private float fadeOutDuration = 0.8f;
    [SerializeField] private Color fadeColor = Color.black;
    [Tooltip("Stop the player moving and looking while the screen fades out.")]
    [SerializeField] private bool freezePlayer = true;

    private static CharacterController player;
    private BoxCollider box;
    private bool triggered;
    private float fade;
    private Texture2D fadeTexture;

    private void Awake()
    {
        box = GetComponent<BoxCollider>();
        box.isTrigger = true;
    }

    private void Update()
    {
        if (triggered) return;

        if (player == null)
        {
            FirstPersonController fpc = FindFirstObjectByType<FirstPersonController>();
            if (fpc != null) player = fpc.GetComponent<CharacterController>();
        }
        if (player == null || !player.enabled) return;

        PlayerHealth health = player.GetComponent<PlayerHealth>();
        if (health != null && health.IsDead) return;

        // Same check as DoorTriggerZone: use the transform so a teleported player is seen right away.
        Vector3 playerCenter = player.transform.TransformPoint(player.center);
        Vector3 local = transform.InverseTransformPoint(playerCenter) - box.center;
        Vector3 half = box.size * 0.5f;
        if (Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z)
            StartCoroutine(Exit());
    }

    private System.Collections.IEnumerator Exit()
    {
        triggered = true;

        int target = NextSceneIndex();
        if (target < 0)
        {
            Debug.LogWarning("SceneExitTrigger: no next scene to load. Add the scenes to File > Build Profiles (Scene List) in order.", this);
            yield break;
        }

        if (freezePlayer)
        {
            FirstPersonController fpc = player != null ? player.GetComponent<FirstPersonController>() : null;
            if (fpc != null) { fpc.Frozen = true; fpc.LookLocked = true; }
        }

        fadeTexture = new Texture2D(1, 1);
        fadeTexture.SetPixel(0, 0, Color.white);
        fadeTexture.Apply();

        AsyncOperation load = SceneManager.LoadSceneAsync(target);
        load.allowSceneActivation = false;

        float t = 0f;
        while (t < fadeOutDuration)
        {
            t += Time.unscaledDeltaTime;
            fade = fadeOutDuration > 0f ? Mathf.Clamp01(t / fadeOutDuration) : 1f;
            yield return null;
        }
        fade = 1f;
        load.allowSceneActivation = true;
    }

    private int NextSceneIndex()
    {
        if (!string.IsNullOrEmpty(overrideSceneName))
        {
            int byName = SceneUtility.GetBuildIndexByScenePath(overrideSceneName);
            if (byName < 0)
            {
                for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
                {
                    string path = SceneUtility.GetScenePathByBuildIndex(i);
                    if (System.IO.Path.GetFileNameWithoutExtension(path) == overrideSceneName) { byName = i; break; }
                }
            }
            if (byName < 0) Debug.LogWarning("SceneExitTrigger: scene '" + overrideSceneName + "' isn't in the build, loading the next scene instead.", this);
            else return byName;
        }

        int current = SceneManager.GetActiveScene().buildIndex;
        if (current < 0) return -1; // this scene isn't in the build list
        int next = current + 1;
        if (next < SceneManager.sceneCountInBuildSettings) return next;
        return sceneAfterLast < SceneManager.sceneCountInBuildSettings ? sceneAfterLast : -1;
    }

    private void OnGUI()
    {
        if (fade <= 0f || fadeTexture == null) return;
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
        BoxCollider b = box != null ? box : GetComponent<BoxCollider>();
        if (b == null) return;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.15f);
        Gizmos.DrawCube(b.center, b.size);
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 1f);
        Gizmos.DrawWireCube(b.center, b.size);
    }
#endif
}
