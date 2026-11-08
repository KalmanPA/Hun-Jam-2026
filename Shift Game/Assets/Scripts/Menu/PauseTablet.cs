using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The pause menu: a rugged tablet held up in front of the camera (diegetic, no screen-space UI).
/// It rises from the bottom left into the middle of the view. While it's up the game is paused
/// (timeScale 0, audio paused, cursor free) and the mouse works on the tablet:
///  - volume slider (speaker icon) = master volume,
///  - sensitivity slider (mouse icon) = mouse look speed,
///  - the arrow button = start / resume.
/// It shows once when the game starts (before the intro cutscene) and again whenever Escape (or the gamepad
/// Start button) is pressed. Escape, Enter or Space also resume.
///
/// The tablet is drawn by its own overlay camera (last in the player camera's stack, depth cleared), so it never
/// clips into walls or the gun. Built by Final Shift > Pause Menu > Build Tablet Prefab + Add To Scenes.
/// </summary>
[DefaultExecutionOrder(1000)] // after the weapon, so the click that resumes can't also fire a shot
public class PauseTablet : MonoBehaviour
{
    [System.Serializable]
    public class Slider
    {
        public Transform track;
        public Transform fill;
        public Transform knob;
        public Renderer knobRenderer;
    }

    /// <summary>True while the tablet is up (or moving) and the game is paused.</summary>
    public static bool IsOpen { get; private set; }

    /// <summary>True when the scene has a pause tablet (it then owns Escape and the cursor lock).</summary>
    public static bool Exists => instance != null;

    private static PauseTablet instance;
    private static bool shownThisSession;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        IsOpen = false;
        shownThisSession = false;
        instance = null;
    }

    [Header("Behaviour")]
    [Tooltip("Show the tablet once when the game starts (only the first scene of a play session, not after a death).")]
    [SerializeField] private bool showAtStart = true;
    [Tooltip("Layer only the tablet camera draws. Must be free (the gun uses 30).")]
    [SerializeField, Range(8, 31)] private int menuLayer = 29;

    [Header("Placement")]
    [Tooltip("How far in front of the camera the tablet sits (it's scaled to fit, so this only affects perspective).")]
    [SerializeField] private float distance = 0.5f;
    [Tooltip("Tablet height as a fraction of the screen height.")]
    [SerializeField, Range(0.3f, 0.95f)] private float screenHeight = 0.66f;
    [Tooltip("Size of the tablet body in its own units (used to fit it on screen).")]
    [SerializeField] private Vector2 tabletSize = new Vector2(1.6f, 1.05f);
    [Tooltip("Tilt while it's down at the bottom left (degrees).")]
    [SerializeField] private Vector3 hiddenTilt = new Vector3(40f, 15f, 35f);

    [Header("Timing (real seconds)")]
    [SerializeField] private float riseTime = 0.45f;
    [SerializeField] private float dropTime = 0.3f;
    [Tooltip("How dark the game gets behind the tablet when paused mid-game (at game start it's fully black).")]
    [SerializeField, Range(0f, 1f)] private float pauseDim = 0.55f;

    [Header("Parts (set by the setup tool)")]
    [SerializeField] private Transform tablet;
    [SerializeField] private Renderer backdrop;
    [SerializeField] private Slider volumeSlider = new Slider();
    [SerializeField] private Slider sensitivitySlider = new Slider();
    [SerializeField] private Transform startButton;
    [SerializeField] private Renderer startButtonRenderer;
    [Tooltip("The arrow on the button (moves in with it when pressed).")]
    [SerializeField] private Transform startArrow;
    [SerializeField] private Material buttonNormal;
    [SerializeField] private Material buttonHover;
    [SerializeField] private Material knobNormal;
    [SerializeField] private Material knobHover;

    private Camera baseCam;
    private Camera menuCam;
    private FirstPersonController player;
    private Material backdropMat;
    private AudioSource clickSource;

    private float shown;          // 0 = down out of view, 1 = in place
    private bool wantOpen;
    private bool atGameStart;
    private float savedTimeScale = 1f;
    private CursorLockMode savedLock = CursorLockMode.Locked;
    private bool savedVisible;
    private bool wasLocked;

    private Slider dragging;
    private bool pressingButton;
    private Vector3 buttonRestPos;
    private Vector3 arrowRestPos;
    private int lastVolumeStep = -1;

    // ------------------------------------------------------------------ lifecycle

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;

        SetLayer(transform, menuLayer);
        if (backdrop != null)
        {
            backdropMat = backdrop.material; // own copy, its alpha is animated
            backdrop.gameObject.SetActive(false);
        }
        if (tablet != null) tablet.gameObject.SetActive(false);
        if (startButton != null) buttonRestPos = startButton.localPosition;
        if (startArrow != null) arrowRestPos = startArrow.localPosition;

        clickSource = gameObject.AddComponent<AudioSource>();
        clickSource.playOnAwake = false;
        clickSource.spatialBlend = 0f;
        clickSource.ignoreListenerPause = true; // the game's audio is paused, the tablet's clicks aren't

        if (showAtStart && !shownThisSession)
        {
            shownThisSession = true;
            atGameStart = true;
            Open();
            // Awake runs before the player locks the cursor; when the tablet closes it should lock.
            savedLock = CursorLockMode.Locked;
            savedVisible = false;
        }
    }

    private void OnDestroy()
    {
        if (instance != this) return;
        instance = null;
        if (IsOpen)
        {
            IsOpen = false;
            Time.timeScale = savedTimeScale;
            AudioListener.pause = false;
        }
    }

    // ------------------------------------------------------------------ open / close

    private void Open()
    {
        if (!IsOpen)
        {
            savedTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
            savedLock = Cursor.lockState;
            savedVisible = Cursor.visible;
        }
        IsOpen = true;
        wantOpen = true;
        dragging = null;
        pressingButton = false;
        Time.timeScale = 0f;
        AudioListener.pause = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        RefreshSliders();
    }

    private void Close()
    {
        if (!wantOpen) return;
        wantOpen = false;
        dragging = null;
        pressingButton = false;
        PlayClick(1f);
    }

    /// <summary>The tablet is fully down: unpause.</summary>
    private void FinishClose()
    {
        IsOpen = false;
        atGameStart = false;
        if (tablet != null) tablet.gameObject.SetActive(false);
        if (backdrop != null) backdrop.gameObject.SetActive(false);
        Time.timeScale = savedTimeScale;
        AudioListener.pause = false;
        Cursor.lockState = savedLock;
        Cursor.visible = savedVisible;
        GameSettings.Save();
    }

    private bool CanPause()
    {
        if (player == null) player = FindFirstObjectByType<FirstPersonController>();
        return player != null && player.isActiveAndEnabled && FindBaseCamera() != null;
    }

    // ------------------------------------------------------------------ update

    private void Update()
    {
        Keyboard kb = Keyboard.current;
        Gamepad pad = Gamepad.current;
        bool toggle = (kb != null && kb.escapeKey.wasPressedThisFrame) || (pad != null && pad.startButton.wasPressedThisFrame);

        bool locked = Cursor.lockState == CursorLockMode.Locked;
        if (!IsOpen)
        {
            // Escape, or the browser dropping the pointer lock (WebGL eats the first Escape itself).
            if ((toggle || (wasLocked && !locked)) && CanPause())
            {
                atGameStart = false;
                Open();
            }
            wasLocked = Cursor.lockState == CursorLockMode.Locked;
            return;
        }
        wasLocked = false;

        if (wantOpen)
        {
            // The player controller locks the cursor in OnEnable, which can run after Awake: keep it free.
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            bool resumeKey = toggle
                || (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
                || (pad != null && pad.buttonSouth.wasPressedThisFrame);
            if (resumeKey && shown > 0.5f) Close();
            else HandleMouse();
        }
    }

    private void LateUpdate()
    {
        if (!IsOpen) return;

        if (!SetUpCamera())
        {
            // No camera to show it on: just pause/unpause without the tablet.
            shown = wantOpen ? 1f : 0f;
            if (!wantOpen) FinishClose();
            return;
        }

        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        shown = Mathf.MoveTowards(shown, wantOpen ? 1f : 0f, dt / Mathf.Max(0.01f, wantOpen ? riseTime : dropTime));
        if (!wantOpen && shown <= 0f)
        {
            FinishClose();
            return;
        }
        Place();
    }

    // ------------------------------------------------------------------ camera + placement

    private Camera FindBaseCamera()
    {
        if (baseCam != null && baseCam.isActiveAndEnabled) return baseCam;
        baseCam = null;
        if (player == null) player = FindFirstObjectByType<FirstPersonController>();
        if (player != null && player.CameraPivot != null)
        {
            baseCam = player.CameraPivot.GetComponent<Camera>();
            if (baseCam == null) baseCam = player.CameraPivot.GetComponentInChildren<Camera>();
        }
        if (baseCam == null) baseCam = Camera.main;
        if (baseCam != null && !baseCam.isActiveAndEnabled) baseCam = null;
        return baseCam;
    }

    private bool SetUpCamera()
    {
        Camera cam = FindBaseCamera();
        if (cam == null || tablet == null) return false;

        if (transform.parent != cam.transform)
        {
            transform.SetParent(cam.transform, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
        }

        if (menuCam == null)
        {
            GameObject go = new GameObject("PauseTabletCamera");
            go.layer = menuLayer;
            go.transform.SetParent(transform, false);
            menuCam = go.AddComponent<Camera>();
            menuCam.cullingMask = 1 << menuLayer;
            UniversalAdditionalCameraData d = menuCam.GetUniversalAdditionalCameraData();
            d.renderType = CameraRenderType.Overlay;
            d.renderShadows = false; // overlay cameras clear depth by default, so nothing clips into the tablet
        }
        menuCam.fieldOfView = cam.fieldOfView;
        menuCam.nearClipPlane = 0.01f;
        menuCam.farClipPlane = distance * 6f;

        // Last in the stack, after the gun's camera (which may be added after this one).
        UniversalAdditionalCameraData baseData = cam.GetUniversalAdditionalCameraData();
        if (baseData.renderType == CameraRenderType.Base)
        {
            var stack = baseData.cameraStack;
            if (stack.Count == 0 || stack[stack.Count - 1] != menuCam)
            {
                stack.Remove(menuCam);
                stack.Add(menuCam);
            }
        }

        // No other camera draws the menu layer.
        int bit = 1 << menuLayer;
        foreach (Camera c in Camera.allCameras)
            if (c != menuCam) c.cullingMask &= ~bit;

        if (!tablet.gameObject.activeSelf) tablet.gameObject.SetActive(true);
        if (backdrop != null && !backdrop.gameObject.activeSelf) backdrop.gameObject.SetActive(true);
        return true;
    }

    private void Place()
    {
        float tan = Mathf.Tan(menuCam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float aspect = Mathf.Max(0.1f, menuCam.aspect);
        float halfH = distance * tan;
        float halfW = halfH * aspect;

        // Fit: the chosen fraction of the screen height, but never wider than 92% of the screen.
        float scale = Mathf.Min(2f * halfH * screenHeight / tabletSize.y, 2f * halfW * 0.92f / tabletSize.x);
        tablet.localScale = Vector3.one * scale;

        float e = EaseOutBack(shown);
        Vector3 inPlace = new Vector3(0f, 0f, distance);
        Vector3 down = new Vector3(-halfW * 0.95f, -halfH - tabletSize.y * scale * 0.75f, distance);
        tablet.localPosition = Vector3.LerpUnclamped(down, inPlace, e);
        tablet.localRotation = Quaternion.SlerpUnclamped(Quaternion.Euler(hiddenTilt), Quaternion.identity, e);

        if (backdrop != null)
        {
            float z = distance * 4f;
            backdrop.transform.localPosition = new Vector3(0f, 0f, z);
            backdrop.transform.localRotation = Quaternion.identity;
            backdrop.transform.localScale = new Vector3(z * tan * aspect * 2.4f, z * tan * 2.4f, 1f);
            if (backdropMat != null)
            {
                float a = atGameStart ? 1f : pauseDim * Mathf.Clamp01(shown);
                Color c = Color.black;
                c.a = a;
                if (backdropMat.HasProperty("_BaseColor")) backdropMat.SetColor("_BaseColor", c);
                else backdropMat.color = c;
            }
        }
    }

    private static float EaseOutBack(float t)
    {
        t = Mathf.Clamp01(t);
        const float c1 = 1.4f, c3 = c1 + 1f;
        float u = t - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }

    // ------------------------------------------------------------------ mouse

    private void HandleMouse()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || menuCam == null || tablet == null || !tablet.gameObject.activeInHierarchy) return;

        Ray ray = menuCam.ScreenPointToRay(mouse.position.ReadValue());
        Plane plane = new Plane(tablet.forward, tablet.position);
        Vector3 local = new Vector3(999f, 999f, 0f);
        if (plane.Raycast(ray, out float hit)) local = tablet.InverseTransformPoint(ray.GetPoint(hit));

        bool ready = shown > 0.9f;
        Slider over = !ready ? null : OverSlider(volumeSlider, local) ? volumeSlider : OverSlider(sensitivitySlider, local) ? sensitivitySlider : null;
        bool overButton = ready && startButton != null && Inside(startButton, local, 0.03f);

        if (mouse.leftButton.wasPressedThisFrame)
        {
            if (over != null) { dragging = over; PlayClick(0.6f); }
            else if (overButton) { pressingButton = true; PlayClick(0.8f); }
        }

        if (dragging != null)
        {
            if (mouse.leftButton.isPressed) SetSliderFrom(dragging, local.x);
            else dragging = null;
        }

        if (pressingButton && !mouse.leftButton.isPressed)
        {
            pressingButton = false;
            if (overButton) { Close(); return; } // resume on release (also lets WebGL grab the pointer lock)
        }

        // Feedback
        if (startButtonRenderer != null && buttonNormal != null && buttonHover != null)
            startButtonRenderer.sharedMaterial = overButton || pressingButton ? buttonHover : buttonNormal;
        Vector3 push = pressingButton && overButton ? new Vector3(0f, 0f, 0.012f) : Vector3.zero;
        if (startButton != null) startButton.localPosition = buttonRestPos + push;
        if (startArrow != null) startArrow.localPosition = arrowRestPos + push;
        SetKnobLook(volumeSlider, dragging == volumeSlider || (dragging == null && over == volumeSlider));
        SetKnobLook(sensitivitySlider, dragging == sensitivitySlider || (dragging == null && over == sensitivitySlider));
    }

    private void SetKnobLook(Slider s, bool hot)
    {
        if (s == null || s.knobRenderer == null || knobNormal == null || knobHover == null) return;
        s.knobRenderer.sharedMaterial = hot ? knobHover : knobNormal;
        if (s.knob != null)
        {
            Vector3 p = s.knob.localPosition;
            p.z = s.track != null ? s.track.localPosition.z - (hot ? 0.035f : 0.02f) : p.z;
            s.knob.localPosition = p;
        }
    }

    private static bool Inside(Transform box, Vector3 local, float pad)
    {
        Vector3 c = box.localPosition, s = box.localScale;
        return Mathf.Abs(local.x - c.x) <= s.x * 0.5f + pad && Mathf.Abs(local.y - c.y) <= s.y * 0.5f + pad;
    }

    private static bool OverSlider(Slider s, Vector3 local)
    {
        if (s == null || s.track == null) return false;
        Vector3 c = s.track.localPosition, size = s.track.localScale;
        return Mathf.Abs(local.x - c.x) <= size.x * 0.5f + 0.05f && Mathf.Abs(local.y - c.y) <= Mathf.Max(size.y * 0.5f, 0.04f) + 0.03f;
    }

    // ------------------------------------------------------------------ sliders

    private void SetSliderFrom(Slider s, float localX)
    {
        float left = s.track.localPosition.x - s.track.localScale.x * 0.5f;
        float t = Mathf.Clamp01((localX - left) / Mathf.Max(0.0001f, s.track.localScale.x));
        if (s == volumeSlider)
        {
            GameSettings.Volume = t;
            int step = Mathf.RoundToInt(t * 10f);
            if (step != lastVolumeStep) { lastVolumeStep = step; PlayClick(0.7f); } // hear the new volume
        }
        else
        {
            GameSettings.MouseSensitivity = SensitivityFromSlider(t);
        }
        ShowSlider(s, t);
    }

    private void RefreshSliders()
    {
        lastVolumeStep = Mathf.RoundToInt(GameSettings.Volume * 10f);
        ShowSlider(volumeSlider, GameSettings.Volume);
        ShowSlider(sensitivitySlider, SliderFromSensitivity(GameSettings.MouseSensitivity));
    }

    private static void ShowSlider(Slider s, float t)
    {
        if (s == null || s.track == null) return;
        float w = s.track.localScale.x;
        float left = s.track.localPosition.x - w * 0.5f;
        if (s.fill != null)
        {
            Vector3 sc = s.fill.localScale;
            sc.x = Mathf.Max(0.0001f, w * t);
            s.fill.localScale = sc;
            Vector3 p = s.fill.localPosition;
            p.x = left + w * t * 0.5f;
            s.fill.localPosition = p;
        }
        if (s.knob != null)
        {
            Vector3 p = s.knob.localPosition;
            p.x = left + w * t;
            s.knob.localPosition = p;
        }
    }

    // Sensitivity is on a log scale so 1x sits a bit past the middle and both ends have room.
    private static float SensitivityFromSlider(float t) =>
        Mathf.Exp(Mathf.Lerp(Mathf.Log(GameSettings.MinSensitivity), Mathf.Log(GameSettings.MaxSensitivity), t));

    private static float SliderFromSensitivity(float s) =>
        Mathf.InverseLerp(Mathf.Log(GameSettings.MinSensitivity), Mathf.Log(GameSettings.MaxSensitivity), Mathf.Log(s));

    // ------------------------------------------------------------------ misc

    private void PlayClick(float volume)
    {
        if (clickSource == null) return;
        SoundCue cue = GameAudio.CueFor(SoundId.ButtonPress);
        AudioClip clip = cue != null ? GameAudio.PickClip(cue) : null;
        if (clip == null) return;
        clickSource.pitch = Random.Range(1.1f, 1.25f);
        clickSource.PlayOneShot(clip, cue.volume * volume * 0.6f);
    }

    private static void SetLayer(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        foreach (Transform c in t) SetLayer(c, layer);
    }
}
