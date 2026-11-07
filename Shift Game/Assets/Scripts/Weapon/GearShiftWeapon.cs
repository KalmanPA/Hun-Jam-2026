using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

public enum Gear { Neutral, Reverse, First, Second, Third, Fourth }

[System.Serializable]
public class GearFireSettings
{
    public bool canFire = true;
    [Tooltip("Hold the trigger to keep firing. Off = one shot (or one burst) per click.")]
    public bool automatic;
    [Tooltip("Shots per second, or bursts per second for burst gears.")]
    public float fireRate = 2f;
    [Tooltip("Bullets per shot. 1 = a single bullet, more = a burst.")]
    public int burstCount = 1;
    [Tooltip("Bullets per second inside a burst.")]
    public float burstRate = 25f;
    [Tooltip("Ammo used per bullet (or per rocket jump).")]
    public int ammoCost = 1;
    public float bulletSpeed = 60f;
    [Tooltip("Metres the bullet flies before it is removed.")]
    public float range = 50f;
    public float damage = 10f;
    [Tooltip("Random cone in degrees.")]
    public float spread;
    public float bulletSize = 0.05f;
    [Tooltip("The bullet shrinks to nothing as it reaches its max range.")]
    public bool shrinkOverRange;
    public Color bulletColor = new Color(1f, 0.55f, 0.15f);
    [Tooltip("Degrees the gun tips back (nose up) on each shot. It settles back exactly when the next shot is ready.")]
    public float recoilTilt = 8f;

    [Header("Laser (instant beam instead of a bullet)")]
    public bool laser;
    public float laserWidth = 0.12f;
    [Tooltip("Seconds the beam stays visible.")]
    public float laserDuration = 0.25f;

    [Header("Explosion when the bullet disappears")]
    public bool explodeOnEnd;
    public float explosionSize = 0.6f;

    [Header("Rocket jump (pushes the player away from where the gun points)")]
    public bool rocketJump;
    [Tooltip("Push speed in m/s.")]
    public float rocketForce = 16f;
}

[System.Serializable]
public class GateSlot
{
    public Gear gear;
    [Tooltip("Column on the rail, in slot spacings. 0 = middle of the rail.")]
    public float column;
    [Tooltip("+1 = slot goes up from the rail, -1 = slot goes down.")]
    public int direction = 1;
}

[System.Serializable]
public class BarrelPiece
{
    public Gear gear;
    [Tooltip("How far the barrel slides out of the gun when its gear is engaged (m).")]
    public float length = 0.3f;
}

/// <summary>
/// The gear-shift weapon. Hold right mouse to tilt the gun towards the camera, then move the mouse to push the stick
/// along the gate. Left mouse fires using the current gear. Health and ammo bars live on the gun.
///
/// The gun lives in the GearWeapon prefab, split into pieces under "Model":
///   Model/Body/Visual, Model/Indicators/Visual (with HealthFill + AmmoFill), Model/Gate/Visual,
///   Model/Gate/Stick/Visual, Model/Barrels/Barrel_First/Visual ... Barrel_Reverse/Visual.
/// Swap the models inside each "Visual" object in the prefab. The piece objects themselves are moved by this script.
/// If there is no "Model" child, placeholder shapes are built when the game starts.
/// The gun is drawn by its own overlay camera so it never clips into walls.
/// </summary>
public class GearShiftWeapon : MonoBehaviour
{
    [Header("Gate layout (gate space, metres)")]
    [SerializeField] private List<GateSlot> slots = new List<GateSlot>
    {
        // Rail runs along the barrel. Column -2 is the muzzle end. +1 = right side of the gun, -1 = left side.
        new GateSlot { gear = Gear.First,   column = -2f, direction = 1 },
        new GateSlot { gear = Gear.Second,  column = -1f, direction = -1 },
        new GateSlot { gear = Gear.Third,   column = 0f,  direction = 1 },
        new GateSlot { gear = Gear.Fourth,  column = 1f,  direction = -1 },
        new GateSlot { gear = Gear.Reverse, column = 2f,  direction = 1 },
    };
    [SerializeField] private float slotSpacing = 0.032f;
    [SerializeField] private float slotLength = 0.032f;
    [Tooltip("How far the stick must be pushed into a slot (0-1) before the gear engages.")]
    [SerializeField, Range(0.3f, 1f)] private float engageThreshold = 0.75f;
    [Tooltip("How close (in slot spacings) the stick must be to a slot to enter it.")]
    [SerializeField, Range(0.05f, 0.5f)] private float slotSnapTolerance = 0.35f;
    [Tooltip("Metres of stick travel per unit of mouse movement.")]
    [SerializeField] private float stickSensitivity = 0.00035f;
    [SerializeField] private float gamepadStickSpeed = 0.12f;

    [Header("Barrels (one per slot)")]
    [SerializeField] private List<BarrelPiece> barrels = new List<BarrelPiece>
    {
        new BarrelPiece { gear = Gear.First,   length = 0.42f },
        new BarrelPiece { gear = Gear.Second,  length = 0.3f },
        new BarrelPiece { gear = Gear.Third,   length = 0.2f },
        new BarrelPiece { gear = Gear.Fourth,  length = 0.24f },
        new BarrelPiece { gear = Gear.Reverse, length = 0.1f },
    };

    [Header("Placeholder piece placement (only used when the pieces are built from scratch)")]
    [SerializeField] private Vector3 gatePosition = new Vector3(0f, 0.0615f, -0.03f);
    [SerializeField] private Vector3 indicatorsPosition = new Vector3(0f, 0.05f, 0.1f);
    [SerializeField] private Vector3 indicatorsEuler = new Vector3(-20f, 0f, 0f);
    [Tooltip("Front face of the body, where the barrels slide out.")]
    [SerializeField] private Vector3 barrelMount = new Vector3(0f, 0f, 0.245f);
    [Tooltip("How fast barrels slide in and out (fraction of their length per second).")]
    [SerializeField] private float barrelSlideSpeed = 5f;

    [Header("Weapon camera (stops the gun clipping into walls)")]
    [SerializeField] private bool useWeaponCamera = true;
    [Tooltip("Layer the gun is drawn on. The main camera stops drawing this layer and an overlay camera draws only it. " +
             "You can name it \"Weapon\" in Tags & Layers; the name isn't needed.")]
    [SerializeField, Range(8, 31)] private int weaponLayer = 30;

    [Header("Poses (camera space)")]
    [SerializeField] private Vector3 hipPosition = new Vector3(0.24f, -0.26f, 0.42f);
    [SerializeField] private Vector3 hipEuler = new Vector3(0f, -2f, 0f);
    [Tooltip("While shifting, the middle of the gear plate + bars is placed here (camera space). Raise Z to push the gun further away.")]
    [SerializeField] private Vector3 shiftFocusPosition = new Vector3(0.04f, -0.06f, 0.46f);
    [Tooltip("Where the barrel points while shifting (camera space). Default: up and to the left.")]
    [SerializeField] private Vector3 shiftBarrelDirection = new Vector3(-0.5f, 1f, 0.2f);
    [Tooltip("0 = the gear plate faces the camera head-on. Higher = the plate leans back like a desk.")]
    [SerializeField, Range(0f, 60f)] private float shiftPlateLean = 35f;
    [SerializeField] private float tiltSpeed = 6f;

    [Header("Ammo (no reload)")]
    [SerializeField] private int maxAmmo = 150;
    [SerializeField] private int startAmmo = 150;

    [Header("Gears")]
    [SerializeField] private GearFireSettings gear1 = new GearFireSettings
    {
        automatic = false, fireRate = 1.5f, burstCount = 1, bulletSpeed = 150f, range = 120f,
        damage = 50f, spread = 0f, bulletSize = 0.035f, bulletColor = new Color(1f, 0.25f, 0.1f), recoilTilt = 22f,
        laser = true, laserWidth = 0.12f, laserDuration = 0.25f
    };
    [SerializeField] private GearFireSettings gear2 = new GearFireSettings
    {
        automatic = true, fireRate = 4f, burstCount = 1, bulletSpeed = 45f, range = 30f,
        damage = 12f, spread = 1.5f, bulletSize = 0.05f, bulletColor = new Color(1f, 0.6f, 0.2f), recoilTilt = 7f,
        explodeOnEnd = true, explosionSize = 0.6f
    };
    [SerializeField] private GearFireSettings gear3 = new GearFireSettings
    {
        automatic = false, fireRate = 2.5f, burstCount = 8, burstRate = 30f, bulletSpeed = 30f, range = 8f,
        damage = 6f, spread = 6f, bulletSize = 0.07f, shrinkOverRange = true, recoilTilt = 14f, bulletColor = new Color(1f, 0.35f, 0.1f)
    };
    [SerializeField] private GearFireSettings gear4 = new GearFireSettings
    {
        automatic = false, fireRate = 0.8f, ammoCost = 5, recoilTilt = 30f, bulletColor = new Color(1f, 0.5f, 0.15f),
        rocketJump = true, rocketForce = 16f, explosionSize = 0.9f
    };
    [SerializeField] private GearFireSettings reverse = new GearFireSettings { canFire = false };

    [Header("Feel")]
    [SerializeField] private float recoilKick = 0.03f;
    [SerializeField] private float recoilReturn = 12f;

    public Gear CurrentGear { get; private set; } = Gear.Neutral;
    public int Ammo { get; private set; }
    public int MaxAmmo => maxAmmo;
    public bool IsShifting => shiftHeld;

    private class BarrelRuntime
    {
        public BarrelPiece piece;
        public Transform pivot;
        public Transform visual;
        public Transform muzzle;
        public float extend;
    }

    // Pieces
    private Transform model;
    private Transform gate;
    private Transform stick;
    private Transform healthFill;
    private Transform ammoFill;
    private readonly List<BarrelRuntime> barrelRuntimes = new List<BarrelRuntime>();
    private readonly Dictionary<Color, Material> bulletMaterials = new Dictionary<Color, Material>();
    private Camera weaponCam;

    // State
    private FirstPersonController controller;
    private PlayerHealth health;
    private Transform owner;
    private Camera cam;
    private float tilt;
    private bool shiftHeld;

    private Vector2 stickPos;     // gate space: x along the rail, y into a slot
    private int activeSlot = -1;  // slot the stick is inside, -1 = on the rail
    private float railMin;
    private float railMax;

    private float nextFireTime;
    private int burstRemaining;
    private float nextBurstTime;
    private GearFireSettings burstSettings;
    private Vector3 recoilOffset;
    private float recoilShotTime;
    private float recoilReadyTime;
    private float recoilAngle;
    private float gearKick;

    // ---------------------------------------------------------------- setup

    private void Awake()
    {
        controller = GetComponentInParent<FirstPersonController>();
        health = GetComponentInParent<PlayerHealth>();
        owner = controller != null ? controller.transform : transform.root;
        cam = GetComponentInParent<Camera>();
        if (cam == null) cam = Camera.main;

        Ammo = Mathf.Clamp(startAmmo, 0, maxAmmo);

        ComputeRail();
        stickPos = new Vector2((railMin + railMax) * 0.5f, 0f);

        model = transform.Find("Model");
        if (model == null) BuildPlaceholderPieces();
        BindPieces();
        SetupWeaponCamera();

        transform.localPosition = hipPosition;
        transform.localRotation = Quaternion.Euler(hipEuler);
        UpdateStickVisual();
        UpdateBarrels(true);
        UpdateBars();
    }

    private void OnDisable()
    {
        if (controller != null) controller.LookLocked = false;
    }

    private void OnDestroy()
    {
        if (cam != null && weaponCam != null)
        {
            UniversalAdditionalCameraData baseData = cam.GetUniversalAdditionalCameraData();
            if (baseData != null) baseData.cameraStack.Remove(weaponCam);
        }
    }

    // ---------------------------------------------------------------- pieces

    private void ComputeRail()
    {
        railMin = float.MaxValue;
        railMax = float.MinValue;
        foreach (GateSlot s in slots)
        {
            railMin = Mathf.Min(railMin, s.column * slotSpacing);
            railMax = Mathf.Max(railMax, s.column * slotSpacing);
        }
        if (slots.Count == 0) { railMin = 0f; railMax = 0f; }
    }

    /// <summary>
    /// Builds the piece hierarchy with placeholder shapes under a new "Model" child.
    /// Used by the editor tool that creates the GearWeapon prefab, and as a fallback at runtime.
    /// </summary>
    public void BuildPlaceholderPieces()
    {
        ComputeRail();
        model = new GameObject("Model").transform;
        model.SetParent(transform, false);

        Transform body = Piece("Body", model, Vector3.zero, Quaternion.identity);
        Visual(body, GearWeaponPlaceholders.BuildBody);

        Transform indicators = Piece("Indicators", model, indicatorsPosition, Quaternion.Euler(indicatorsEuler));
        Visual(indicators, GearWeaponPlaceholders.BuildIndicators);

        // Gate rotated 90 degrees so the rail runs along the barrel (gate +X = towards the stock, gate +Z = right side).
        Transform gateT = Piece("Gate", model, gatePosition, Quaternion.Euler(0f, 90f, 0f));
        Visual(gateT, t => GearWeaponPlaceholders.BuildGate(t, slots, slotSpacing, slotLength, railMin, railMax));

        Transform stickT = Piece("Stick", gateT, Vector3.zero, Quaternion.identity);
        Visual(stickT, GearWeaponPlaceholders.BuildStick);

        Transform barrelRoot = Piece("Barrels", model, barrelMount, Quaternion.identity);
        foreach (BarrelPiece b in barrels)
        {
            BarrelPiece piece = b;
            Transform pivot = Piece("Barrel_" + piece.gear, barrelRoot, Vector3.zero, Quaternion.identity);
            Transform visual = Visual(pivot, t => GearWeaponPlaceholders.BuildBarrel(t, piece.gear, piece.length));
            Transform muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(visual, false);
            muzzle.localPosition = new Vector3(0f, 0f, piece.length + 0.01f);
        }
    }

    /// <summary>Finds the pieces under "Model" by name.</summary>
    private void BindPieces()
    {
        Transform indicators = model.Find("Indicators");
        healthFill = FindDeep(indicators, "HealthFill");
        ammoFill = FindDeep(indicators, "AmmoFill");
        if (healthFill == null || ammoFill == null)
            Debug.LogWarning("GearShiftWeapon: Model/Indicators needs children named HealthFill and AmmoFill.", this);

        gate = model.Find("Gate");
        stick = gate != null ? gate.Find("Stick") : null;
        if (gate == null || stick == null)
            Debug.LogWarning("GearShiftWeapon: missing Model/Gate or Model/Gate/Stick.", this);

        Transform barrelRoot = model.Find("Barrels");
        foreach (BarrelPiece piece in barrels)
        {
            Transform pivot = barrelRoot != null ? barrelRoot.Find("Barrel_" + piece.gear) : null;
            if (pivot == null)
            {
                Debug.LogWarning("GearShiftWeapon: missing Model/Barrels/Barrel_" + piece.gear, this);
                continue;
            }
            Transform visual = pivot.Find("Visual");
            if (visual == null) visual = pivot;
            Transform muzzle = FindDeep(pivot, "Muzzle");
            if (muzzle == null)
            {
                muzzle = new GameObject("Muzzle").transform;
                muzzle.SetParent(pivot, false);
                muzzle.localPosition = new Vector3(0f, 0f, piece.length + 0.01f);
            }
            barrelRuntimes.Add(new BarrelRuntime { piece = piece, pivot = pivot, visual = visual, muzzle = muzzle });
        }
    }

    private static Transform Piece(string name, Transform parent, Vector3 pos, Quaternion rot)
    {
        Transform t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = pos;
        t.localRotation = rot;
        return t;
    }

    private static Transform Visual(Transform piece, System.Action<Transform> build)
    {
        Transform visual = new GameObject("Visual").transform;
        visual.SetParent(piece, false);
        build(visual);
        return visual;
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root == null) return null;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    private void SetupWeaponCamera()
    {
        if (!useWeaponCamera || cam == null) return;

        foreach (Transform t in GetComponentsInChildren<Transform>(true))
            t.gameObject.layer = weaponLayer;

        int bit = 1 << weaponLayer;
        cam.cullingMask &= ~bit;

        GameObject go = new GameObject("WeaponCamera");
        go.transform.SetParent(cam.transform, false);
        weaponCam = go.AddComponent<Camera>();
        weaponCam.cullingMask = bit;
        weaponCam.nearClipPlane = 0.01f;
        weaponCam.farClipPlane = 5f;
        weaponCam.fieldOfView = cam.fieldOfView;

        UniversalAdditionalCameraData data = weaponCam.GetUniversalAdditionalCameraData();
        data.renderType = CameraRenderType.Overlay;
        data.renderShadows = false;

        UniversalAdditionalCameraData baseData = cam.GetUniversalAdditionalCameraData();
        if (baseData.renderType != CameraRenderType.Base)
        {
            Debug.LogWarning("GearShiftWeapon: the player camera isn't a Base camera, so the weapon camera can't be stacked.", this);
            return;
        }
        baseData.cameraStack.Add(weaponCam);
    }

    private void LateUpdate()
    {
        if (weaponCam != null && cam != null) weaponCam.fieldOfView = cam.fieldOfView;
    }

    // ---------------------------------------------------------------- update

    private void Update()
    {
        bool locked = Cursor.lockState == CursorLockMode.Locked;
        Mouse mouse = Mouse.current;
        Gamepad pad = Gamepad.current;

        bool rightHeld = (mouse != null && mouse.rightButton.isPressed) || (pad != null && pad.leftTrigger.isPressed);
        shiftHeld = locked && rightHeld;
        if (controller != null) controller.LookLocked = shiftHeld;

        tilt = Mathf.MoveTowards(tilt, shiftHeld ? 1f : 0f, tiltSpeed * Time.deltaTime);

        if (shiftHeld && tilt > 0.6f)
        {
            Vector2 delta = Vector2.zero;
            if (mouse != null) delta += mouse.delta.ReadValue() * stickSensitivity;
            if (pad != null) delta += pad.rightStick.ReadValue() * gamepadStickSpeed * Time.deltaTime;
            MoveStick(ScreenToGate(delta));
        }
        else if (!shiftHeld)
        {
            SettleStick();
        }

        UpdateGear();
        UpdateStickVisual();
        UpdateBarrels(false);
        HandleFiring(locked, mouse, pad);
        UpdatePose();
        UpdateBars();
    }

    // ---------------------------------------------------------------- gate

    /// <summary>
    /// Turns a screen-space mouse movement into gate-space movement, so pushing the mouse
    /// towards a slot on screen moves the stick towards it, whatever angle the gun is at.
    /// </summary>
    private Vector2 ScreenToGate(Vector2 d)
    {
        if (cam == null || gate == null) return d;
        Vector3 u3 = cam.transform.InverseTransformDirection(gate.right);
        Vector3 v3 = cam.transform.InverseTransformDirection(gate.forward);
        Vector2 u = new Vector2(u3.x, u3.y);
        Vector2 v = new Vector2(v3.x, v3.y);
        if (u.sqrMagnitude < 1e-4f || v.sqrMagnitude < 1e-4f) return d;
        return new Vector2(Vector2.Dot(d, u.normalized), Vector2.Dot(d, v.normalized));
    }

    private void MoveStick(Vector2 d)
    {
        if (activeSlot < 0)
        {
            // On the rail: moves along it slide the stick, moves across it try to enter a slot.
            if (Mathf.Abs(d.x) >= Mathf.Abs(d.y))
            {
                stickPos.x = Mathf.Clamp(stickPos.x + d.x, railMin, railMax);
                return;
            }

            int dir = d.y > 0f ? 1 : -1;
            int best = NearestSlot(stickPos.x, dir, out float distance);
            if (best < 0) return;

            float column = slots[best].column * slotSpacing;
            if (distance <= slotSnapTolerance * slotSpacing)
            {
                stickPos.x = column;
                activeSlot = best;
                MoveInSlot(d.y);
            }
            else
            {
                // Guide the stick towards the nearest slot it can go into.
                stickPos.x = Mathf.MoveTowards(stickPos.x, column, Mathf.Abs(d.y));
            }
        }
        else
        {
            MoveInSlot(d.y);
        }
    }

    private void MoveInSlot(float dy)
    {
        GateSlot s = slots[activeSlot];
        float end = s.direction >= 0 ? slotLength : -slotLength;
        stickPos.y = s.direction >= 0
            ? Mathf.Clamp(stickPos.y + dy, 0f, end)
            : Mathf.Clamp(stickPos.y + dy, end, 0f);

        if (Mathf.Approximately(stickPos.y, 0f))
            activeSlot = -1; // back on the rail
    }

    private int NearestSlot(float x, int dir, out float distance)
    {
        int best = -1;
        distance = float.MaxValue;
        for (int i = 0; i < slots.Count; i++)
        {
            if ((slots[i].direction >= 0 ? 1 : -1) != dir) continue;
            float dd = Mathf.Abs(slots[i].column * slotSpacing - x);
            if (dd < distance) { distance = dd; best = i; }
        }
        return best;
    }

    /// <summary>When the player lets go, the stick drops fully into its slot or back to the rail.</summary>
    private void SettleStick()
    {
        if (activeSlot < 0) return;
        GateSlot s = slots[activeSlot];
        float depth = Mathf.Abs(stickPos.y) / slotLength;
        float target = depth >= 0.5f ? (s.direction >= 0 ? slotLength : -slotLength) : 0f;
        stickPos.y = Mathf.MoveTowards(stickPos.y, target, slotLength * 8f * Time.deltaTime);
        if (Mathf.Approximately(stickPos.y, 0f)) activeSlot = -1;
    }

    private void UpdateGear()
    {
        Gear g = Gear.Neutral;
        if (activeSlot >= 0 && Mathf.Abs(stickPos.y) >= slotLength * engageThreshold)
            g = slots[activeSlot].gear;

        if (g != CurrentGear)
        {
            bool wasInGear = CurrentGear != Gear.Neutral;
            CurrentGear = g;
            burstRemaining = 0;
            if (g != Gear.Neutral)
            {
                gearKick = 1f;
                // Heavier clunk into the low gears, lighter into the high ones.
                float pitch = g == Gear.Reverse ? 0.85f : 1.05f - 0.05f * ((int)g - (int)Gear.First);
                GameAudio.Play2D(SoundId.WeaponGearEngage, 1f, pitch);
            }
            else if (wasInGear)
            {
                GameAudio.Play2D(SoundId.WeaponGearRelease);
            }
        }
    }

    // ---------------------------------------------------------------- barrels

    /// <summary>
    /// Each slot has its own barrel. A barrel slides out as far as the stick is pushed into its slot,
    /// and slides back into the body when the stick leaves the slot.
    /// </summary>
    private void UpdateBarrels(bool instant)
    {
        Gear slotGear = activeSlot >= 0 ? slots[activeSlot].gear : Gear.Neutral;
        float depth = activeSlot >= 0 ? Mathf.Clamp01(Mathf.Abs(stickPos.y) / slotLength) : 0f;

        foreach (BarrelRuntime b in barrelRuntimes)
        {
            float target = b.piece.gear == slotGear ? depth : 0f;
            b.extend = instant ? target : Mathf.MoveTowards(b.extend, target, barrelSlideSpeed * Time.deltaTime);

            if (b.visual == null || b.pivot == null) continue;
            float eased = Mathf.SmoothStep(0f, 1f, b.extend);
            b.pivot.localPosition = new Vector3(0f, 0f, -b.piece.length * (1f - eased));
            bool visible = b.extend > 0.001f;
            if (b.visual.gameObject.activeSelf != visible) b.visual.gameObject.SetActive(visible);
        }
    }

    private BarrelRuntime ActiveBarrel()
    {
        foreach (BarrelRuntime b in barrelRuntimes)
            if (b.piece.gear == CurrentGear) return b;
        return null;
    }

    // ---------------------------------------------------------------- firing

    private GearFireSettings SettingsFor(Gear g)
    {
        switch (g)
        {
            case Gear.First: return gear1;
            case Gear.Second: return gear2;
            case Gear.Third: return gear3;
            case Gear.Fourth: return gear4;
            case Gear.Reverse: return reverse;
            default: return null;
        }
    }

    private void HandleFiring(bool locked, Mouse mouse, Gamepad pad)
    {
        // Finish a running burst.
        if (burstRemaining > 0 && Time.time >= nextBurstTime)
        {
            if (FireBullet(burstSettings))
            {
                burstRemaining--;
                nextBurstTime = Time.time + 1f / Mathf.Max(1f, burstSettings.burstRate);
            }
            else burstRemaining = 0;
        }

        // No shooting while the gun is tilted for shifting.
        if (!locked || tilt > 0.15f) return;

        GearFireSettings s = SettingsFor(CurrentGear);
        if (s == null || !s.canFire) return;

        // The barrel has to be fully out.
        BarrelRuntime barrel = ActiveBarrel();
        if (barrel != null && barrel.extend < 0.95f) return;

        bool pressed = (mouse != null && mouse.leftButton.wasPressedThisFrame) || (pad != null && pad.rightTrigger.wasPressedThisFrame);
        bool held = (mouse != null && mouse.leftButton.isPressed) || (pad != null && pad.rightTrigger.isPressed);
        if (!(s.automatic ? held : pressed)) return;
        if (burstRemaining > 0 || Time.time < nextFireTime) return;

        nextFireTime = Time.time + 1f / Mathf.Max(0.01f, s.fireRate);
        if (Ammo < Mathf.Max(1, s.ammoCost))
        {
            GameAudio.Play2D(SoundId.WeaponEmpty); // empty: no reload in this game
            return;
        }

        // Tip the gun back; it returns to rest exactly when the next shot (or burst) is ready.
        recoilShotTime = Time.time;
        recoilReadyTime = nextFireTime;
        recoilAngle = s.recoilTilt;

        if (s.rocketJump)
        {
            RocketJump(s);
            return;
        }

        burstSettings = s;
        burstRemaining = Mathf.Max(1, s.burstCount) - 1;
        nextBurstTime = Time.time + 1f / Mathf.Max(1f, s.burstRate);
        FireBullet(s);
    }

    private Transform Muzzle()
    {
        BarrelRuntime b = ActiveBarrel();
        if (b != null) return b.muzzle;
        return barrelRuntimes.Count > 0 ? barrelRuntimes[0].muzzle : transform;
    }

    /// <summary>Gear 4: a blast at the muzzle pushes the player away from where they are aiming.</summary>
    private void RocketJump(GearFireSettings s)
    {
        Ammo -= Mathf.Max(1, s.ammoCost);
        GameAudio.Play2D(SoundId.WeaponRocketJump);
        Vector3 aim = cam != null ? cam.transform.forward : transform.forward;
        if (controller != null) controller.AddImpulse(-aim * s.rocketForce);

        Transform m = Muzzle();
        GearExplosion.Spawn(m.position + aim * 0.15f, s.explosionSize, BulletMaterial(s.bulletColor), s.bulletColor);
        recoilOffset += new Vector3(0f, 0.01f, -recoilKick * 2f);
    }

    private bool FireBullet(GearFireSettings s)
    {
        if (s == null) return false;
        int cost = Mathf.Max(1, s.ammoCost);
        if (Ammo < cost) return false;
        Ammo -= cost;

        Transform m = Muzzle();
        Vector3 origin = m.position;
        Vector3 dir = m.forward;
        if (cam != null)
        {
            // Aim at whatever is under the screen centre.
            Ray ray = new Ray(cam.transform.position, cam.transform.forward);
            Vector3 target = ray.GetPoint(s.range);
            if (Physics.Raycast(ray, out RaycastHit hit, s.range, ~0, QueryTriggerInteraction.Ignore) && !hit.transform.IsChildOf(owner))
                target = hit.point;
            Vector3 toTarget = target - origin;
            dir = Vector3.Dot(toTarget, cam.transform.forward) > 0.2f ? toTarget.normalized : cam.transform.forward;

            if (s.spread > 0f)
            {
                Vector2 r = Random.insideUnitCircle * (s.spread * 0.5f);
                dir = Quaternion.AngleAxis(r.x, cam.transform.up) * Quaternion.AngleAxis(r.y, cam.transform.right) * dir;
            }
        }

        GameAudio.Play2D(ShotSound(s));

        Material mat = BulletMaterial(s.bulletColor);
        if (s.laser)
            GearLaser.Fire(origin, dir, s, owner, mat, mat);
        else
            GearBullet.Spawn(origin, dir, s, owner, mat);
        recoilOffset += new Vector3(0f, 0.004f, -recoilKick);
        return true;
    }

    private SoundId ShotSound(GearFireSettings s)
    {
        if (s.laser) return SoundId.WeaponLaser;
        if (s == gear3 || s.burstCount > 1) return SoundId.WeaponGear3Shot;
        return SoundId.WeaponGear2Shot;
    }

    public void AddAmmo(int amount)
    {
        Ammo = Mathf.Clamp(Ammo + amount, 0, maxAmmo);
    }

    // ---------------------------------------------------------------- visuals

    private void UpdatePose()
    {
        recoilOffset = Vector3.Lerp(recoilOffset, Vector3.zero, recoilReturn * Time.deltaTime);
        gearKick = Mathf.MoveTowards(gearKick, 0f, 6f * Time.deltaTime);

        float t = Mathf.SmoothStep(0f, 1f, tilt);
        Quaternion hipRot = Quaternion.Euler(hipEuler);
        Quaternion kick = Quaternion.Euler(-gearKick * 4f, 0f, gearKick * 2f);

        float recoil = 0f;
        if (Time.time < recoilReadyTime && recoilReadyTime > recoilShotTime)
        {
            float p = Mathf.InverseLerp(recoilShotTime, recoilReadyTime, Time.time);
            float rise = Mathf.Clamp01((Time.time - recoilShotTime) / 0.03f); // snap up over 30 ms
            recoil = recoilAngle * rise * (1f - Mathf.SmoothStep(0f, 1f, p));
        }
        kick *= Quaternion.Euler(-recoil, 0f, 0f);

        Quaternion shiftRot = ShiftRotation();
        Vector3 shiftPos = shiftFocusPosition - shiftRot * ShiftFocusLocal;

        transform.localPosition = Vector3.Lerp(hipPosition, shiftPos, t) + recoilOffset;
        transform.localRotation = Quaternion.Slerp(hipRot, shiftRot, t) * kick;
    }

    /// <summary>Point on the gun (between the gear plate and the bars) that is centred while shifting.</summary>
    private static readonly Vector3 ShiftFocusLocal = new Vector3(0f, 0.06f, 0.04f);

    /// <summary>Gun rotation while shifting: the top of the gun (the gear plate) faces the player.</summary>
    private Quaternion ShiftRotation()
    {
        Vector3 up = Quaternion.AngleAxis(shiftPlateLean, Vector3.right) * Vector3.back; // towards the camera
        Vector3 forward = Vector3.ProjectOnPlane(shiftBarrelDirection, up);
        if (forward.sqrMagnitude < 1e-4f) forward = Vector3.up;
        return Quaternion.LookRotation(forward.normalized, up);
    }

    private void UpdateStickVisual()
    {
        if (stick == null) return;
        stick.localPosition = new Vector3(stickPos.x, 0f, stickPos.y);
        float halfRail = Mathf.Max(0.001f, (railMax - railMin) * 0.5f);
        float railMid = (railMin + railMax) * 0.5f;
        float leanForward = stickPos.y / slotLength * 10f;
        float leanSide = -(stickPos.x - railMid) / halfRail * 8f;
        stick.localRotation = Quaternion.Euler(leanForward, 0f, leanSide);
    }

    private void UpdateBars()
    {
        float h = health != null ? health.Normalized : 1f;
        float a = maxAmmo > 0 ? (float)Ammo / maxAmmo : 0f;
        SetFill(healthFill, h);
        SetFill(ammoFill, a);
    }

    private static void SetFill(Transform fill, float amount)
    {
        if (fill == null) return;
        Vector3 s = fill.localScale;
        s.z = Mathf.Max(0.0001f, Mathf.Clamp01(amount));
        fill.localScale = s;
        bool visible = amount > 0.001f;
        if (fill.gameObject.activeSelf != visible) fill.gameObject.SetActive(visible);
    }

    private Material BulletMaterial(Color color)
    {
        if (!bulletMaterials.TryGetValue(color, out Material m))
        {
            m = GearWeaponPlaceholders.MakeMaterial(color, true);
            bulletMaterials[color] = m;
        }
        return m;
    }

    // ---------------------------------------------------------------- editor preview

    private void OnDrawGizmos()
    {
        if (Application.isPlaying || transform.Find("Model") != null) return;
        // Rough outline so the weapon can be placed in the editor before Play builds the model.
        Gizmos.color = new Color(0.9f, 0.5f, 0.2f, 0.8f);
        Gizmos.matrix = transform.parent != null
            ? transform.parent.localToWorldMatrix * Matrix4x4.TRS(hipPosition, Quaternion.Euler(hipEuler), Vector3.one)
            : transform.localToWorldMatrix;
        Gizmos.DrawWireCube(new Vector3(0f, 0f, 0.02f), new Vector3(0.1f, 0.1f, 0.45f));
        Gizmos.DrawWireCube(new Vector3(0f, -0.025f, -0.32f), new Vector3(0.06f, 0.09f, 0.24f));
        Gizmos.DrawLine(new Vector3(0f, 0.06f, -0.03f), new Vector3(0f, 0.2f, -0.03f));
        Gizmos.DrawWireSphere(new Vector3(0f, 0.2f, -0.03f), 0.02f);
    }
}
