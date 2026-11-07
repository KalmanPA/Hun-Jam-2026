using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

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

/// <summary>
/// The gear-shift weapon. Builds a placeholder model out of primitives at runtime.
/// Hold right mouse to tilt the gun towards the camera, then move the mouse to push the stick
/// along the gate. Left mouse fires using the current gear. Health and ammo bars live on the gun.
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
    [SerializeField] private GearFireSettings gear4 = new GearFireSettings { canFire = false };
    [SerializeField] private GearFireSettings reverse = new GearFireSettings { canFire = false };

    [Header("Feel")]
    [SerializeField] private float recoilKick = 0.03f;
    [SerializeField] private float recoilReturn = 12f;

    public Gear CurrentGear { get; private set; } = Gear.Neutral;
    public int Ammo { get; private set; }
    public int MaxAmmo => maxAmmo;
    public bool IsShifting => shiftHeld;

    // Model parts
    private Transform gate;
    private Transform stick;
    private Transform muzzle;
    private Transform healthFill;
    private Transform ammoFill;
    private float barLength;
    private readonly Dictionary<Color, Material> bulletMaterials = new Dictionary<Color, Material>();

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

        railMin = float.MaxValue;
        railMax = float.MinValue;
        foreach (GateSlot s in slots)
        {
            railMin = Mathf.Min(railMin, s.column * slotSpacing);
            railMax = Mathf.Max(railMax, s.column * slotSpacing);
        }
        if (slots.Count == 0) { railMin = 0f; railMax = 0f; }
        stickPos = new Vector2((railMin + railMax) * 0.5f, 0f);

        BuildModel();

        transform.localPosition = hipPosition;
        transform.localRotation = Quaternion.Euler(hipEuler);
        UpdateStickVisual();
        UpdateBars();
    }

    private void OnDisable()
    {
        if (controller != null) controller.LookLocked = false;
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
            // On the rail: horizontal moves slide along it, vertical moves try to enter a slot.
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
            CurrentGear = g;
            burstRemaining = 0;
            if (g != Gear.Neutral) gearKick = 1f;
        }
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

        bool pressed = (mouse != null && mouse.leftButton.wasPressedThisFrame) || (pad != null && pad.rightTrigger.wasPressedThisFrame);
        bool held = (mouse != null && mouse.leftButton.isPressed) || (pad != null && pad.rightTrigger.isPressed);
        if (!(s.automatic ? held : pressed)) return;
        if (burstRemaining > 0 || Time.time < nextFireTime) return;

        nextFireTime = Time.time + 1f / Mathf.Max(0.01f, s.fireRate);
        if (Ammo <= 0) return; // empty: no reload in this game

        // Tip the gun back; it returns to rest exactly when the next shot (or burst) is ready.
        recoilShotTime = Time.time;
        recoilReadyTime = nextFireTime;
        recoilAngle = s.recoilTilt;

        burstSettings = s;
        burstRemaining = Mathf.Max(1, s.burstCount) - 1;
        nextBurstTime = Time.time + 1f / Mathf.Max(1f, s.burstRate);
        FireBullet(s);
    }

    private bool FireBullet(GearFireSettings s)
    {
        if (Ammo <= 0 || s == null) return false;
        Ammo--;

        Vector3 origin = muzzle.position;
        Vector3 dir = muzzle.forward;
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

        Material mat = BulletMaterial(s.bulletColor);
        if (s.laser)
            GearLaser.Fire(origin, dir, s, owner, mat, mat);
        else
            GearBullet.Spawn(origin, dir, s, owner, mat);
        recoilOffset += new Vector3(0f, 0.004f, -recoilKick);
        return true;
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

    private void SetFill(Transform fill, float amount)
    {
        if (fill == null) return;
        Vector3 s = fill.localScale;
        s.z = Mathf.Max(0.0001f, barLength * Mathf.Clamp01(amount));
        fill.localScale = s;
        fill.gameObject.SetActive(amount > 0.001f);
    }

    // ---------------------------------------------------------------- placeholder model

    private void BuildModel()
    {
        Material rust = MakeMaterial(new Color(0.36f, 0.2f, 0.12f), false);
        Material steel = MakeMaterial(new Color(0.3f, 0.3f, 0.31f), false);
        Material darkSteel = MakeMaterial(new Color(0.12f, 0.12f, 0.13f), false);
        Material groove = MakeMaterial(new Color(0.02f, 0.02f, 0.02f), false);
        Material reverseGroove = MakeMaterial(new Color(0.12f, 0.02f, 0.02f), false);
        Material wood = MakeMaterial(new Color(0.28f, 0.17f, 0.09f), false);
        Material knob = MakeMaterial(new Color(0.04f, 0.04f, 0.04f), false);
        Material hazard = MakeMaterial(new Color(0.7f, 0.55f, 0.1f), false);
        Material glow = MakeMaterial(new Color(1f, 0.45f, 0.1f), true);
        Material healthMat = MakeMaterial(new Color(0.8f, 0.1f, 0.06f), true);
        Material ammoMat = MakeMaterial(new Color(1f, 0.6f, 0.15f), true);

        Transform model = new GameObject("Model").transform;
        model.SetParent(transform, false);

        // Body
        Box("Receiver", model, new Vector3(0f, 0f, 0.02f), new Vector3(0.1f, 0.1f, 0.45f), rust);
        Box("GatePlate", model, new Vector3(0f, 0.055f, -0.03f), new Vector3(0.11f, 0.012f, 0.2f), steel);
        Box("Hazard", model, new Vector3(0.051f, 0f, 0.19f), new Vector3(0.003f, 0.1f, 0.04f), hazard);
        Box("HazardL", model, new Vector3(-0.051f, 0f, 0.19f), new Vector3(0.003f, 0.1f, 0.04f), hazard);

        // Barrel
        Cylinder("Barrel", model, new Vector3(0f, 0.015f, 0.435f), 0.024f, 0.38f, steel);
        Cylinder("BarrelBand1", model, new Vector3(0f, 0.015f, 0.32f), 0.03f, 0.015f, darkSteel);
        Cylinder("BarrelBand2", model, new Vector3(0f, 0.015f, 0.5f), 0.03f, 0.015f, darkSteel);
        Cylinder("MuzzleClamp", model, new Vector3(0f, 0.015f, 0.6f), 0.036f, 0.06f, darkSteel);
        muzzle = new GameObject("Muzzle").transform;
        muzzle.SetParent(model, false);
        muzzle.localPosition = new Vector3(0f, 0.015f, 0.64f);

        // Side tank + pressure gauge (the orange accent)
        Cylinder("Tank", model, new Vector3(-0.072f, -0.015f, 0.05f), 0.028f, 0.22f, steel);
        Transform gauge = Part("Gauge", PrimitiveType.Cylinder, model, new Vector3(-0.072f, 0.018f, -0.03f), new Vector3(0.035f, 0.006f, 0.035f), Vector3.zero, darkSteel);
        Part("GaugeFace", PrimitiveType.Cylinder, gauge, new Vector3(0f, 1.1f, 0f), new Vector3(0.8f, 0.3f, 0.8f), Vector3.zero, glow);

        // Stock and grips
        Box("Stock", model, new Vector3(0f, -0.025f, -0.32f), new Vector3(0.06f, 0.09f, 0.24f), wood);
        Box("ButtPlate", model, new Vector3(0f, -0.025f, -0.445f), new Vector3(0.07f, 0.11f, 0.015f), darkSteel);
        Part("Grip", PrimitiveType.Cube, model, new Vector3(0f, -0.1f, -0.1f), new Vector3(0.045f, 0.11f, 0.05f), new Vector3(15f, 0f, 0f), wood);
        Box("Foregrip", model, new Vector3(0f, -0.085f, 0.3f), new Vector3(0.045f, 0.09f, 0.05f), wood);

        // Diegetic health + ammo bars: a raised panel in front of the gate, tipped up towards the player.
        Transform barPanel = new GameObject("BarPanel").transform;
        barPanel.SetParent(model, false);
        barPanel.localPosition = new Vector3(0f, 0.05f, 0.1f);
        barPanel.localRotation = Quaternion.Euler(-20f, 0f, 0f);
        Box("BarRiser", model, new Vector3(0f, 0.065f, 0.19f), new Vector3(0.09f, 0.03f, 0.07f), darkSteel);
        Box("PanelPlate", barPanel, new Vector3(0f, 0.006f, 0.065f), new Vector3(0.1f, 0.012f, 0.13f), darkSteel);
        barLength = 0.11f;
        healthFill = Bar("HealthBar", barPanel, new Vector3(-0.025f, 0.0145f, 0.01f), healthMat, groove);
        ammoFill = Bar("AmmoBar", barPanel, new Vector3(0.025f, 0.0145f, 0.01f), ammoMat, groove);

        // Gate. Rotated 90 degrees so the rail runs along the barrel and the slots go out to the sides
        // (gate +X = towards the stock, gate +Z = right side of the gun).
        gate = new GameObject("Gate").transform;
        gate.SetParent(model, false);
        gate.localPosition = new Vector3(0f, 0.0615f, -0.03f);
        gate.localRotation = Quaternion.Euler(0f, 90f, 0f);

        const float grooveWidth = 0.012f;
        float railLength = (railMax - railMin) + grooveWidth;
        Box("Rail", gate, new Vector3((railMin + railMax) * 0.5f, 0.0005f, 0f), new Vector3(railLength, 0.002f, grooveWidth), groove);
        foreach (GateSlot s in slots)
        {
            float dir = s.direction >= 0 ? 1f : -1f;
            Box("Slot_" + s.gear, gate, new Vector3(s.column * slotSpacing, 0.0005f, dir * slotLength * 0.5f),
                new Vector3(grooveWidth, 0.002f, slotLength + grooveWidth), s.gear == Gear.Reverse ? reverseGroove : groove);
        }

        // Stick: plain rod with a ball on top, no boot.
        stick = new GameObject("Stick").transform;
        stick.SetParent(gate, false);
        Box("Collar", stick, new Vector3(0f, 0.004f, 0f), new Vector3(0.022f, 0.008f, 0.022f), darkSteel);
        Cylinder("Rod", stick, new Vector3(0f, 0.07f, 0f), 0.006f, 0.14f, steel, upright: true);
        Part("Knob", PrimitiveType.Sphere, stick, new Vector3(0f, 0.14f, 0f), Vector3.one * 0.04f, Vector3.zero, knob);
    }

    private Transform Bar(string name, Transform parent, Vector3 start, Material fillMat, Material backMat)
    {
        Box(name + "Back", parent, start + new Vector3(0f, -0.0015f, barLength * 0.5f), new Vector3(0.038f, 0.002f, barLength + 0.008f), backMat);
        Transform fill = new GameObject(name + "Fill").transform;
        fill.SetParent(parent, false);
        fill.localPosition = start;
        fill.localScale = new Vector3(0.03f, 0.004f, barLength);
        Part("Fill", PrimitiveType.Cube, fill, new Vector3(0f, 0f, 0.5f), Vector3.one, Vector3.zero, fillMat);
        return fill;
    }

    private Transform Box(string name, Transform parent, Vector3 pos, Vector3 size, Material mat)
    {
        return Part(name, PrimitiveType.Cube, parent, pos, size, Vector3.zero, mat);
    }

    /// <summary>Cylinder along the weapon's forward axis (or upright).</summary>
    private Transform Cylinder(string name, Transform parent, Vector3 pos, float radius, float length, Material mat, bool upright = false)
    {
        return Part(name, PrimitiveType.Cylinder, parent, pos, new Vector3(radius * 2f, length * 0.5f, radius * 2f),
            upright ? Vector3.zero : new Vector3(90f, 0f, 0f), mat);
    }

    private static Transform Part(string name, PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Vector3 euler, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        DestroyImmediate(go.GetComponent<Collider>());
        Transform t = go.transform;
        t.SetParent(parent, false);
        t.localPosition = pos;
        t.localRotation = Quaternion.Euler(euler);
        t.localScale = scale;
        Renderer r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return t;
    }

    private static Material MakeMaterial(Color color, bool unlit)
    {
        Shader shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        Material m = new Material(shader);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
        return m;
    }

    private Material BulletMaterial(Color color)
    {
        if (!bulletMaterials.TryGetValue(color, out Material m))
        {
            m = MakeMaterial(color, true);
            bulletMaterials[color] = m;
        }
        return m;
    }

    // ---------------------------------------------------------------- editor preview

    private void OnDrawGizmos()
    {
        if (Application.isPlaying) return;
        // Rough outline so the weapon can be placed in the editor before Play builds the model.
        Gizmos.color = new Color(0.9f, 0.5f, 0.2f, 0.8f);
        Gizmos.matrix = transform.parent != null
            ? transform.parent.localToWorldMatrix * Matrix4x4.TRS(hipPosition, Quaternion.Euler(hipEuler), Vector3.one)
            : transform.localToWorldMatrix;
        Gizmos.DrawWireCube(new Vector3(0f, 0f, 0.02f), new Vector3(0.1f, 0.1f, 0.45f));
        Gizmos.DrawWireCube(new Vector3(0f, 0.015f, 0.435f), new Vector3(0.048f, 0.048f, 0.38f));
        Gizmos.DrawWireCube(new Vector3(0f, -0.025f, -0.32f), new Vector3(0.06f, 0.09f, 0.24f));
        Gizmos.DrawLine(new Vector3(0f, 0.06f, -0.03f), new Vector3(0f, 0.2f, -0.03f));
        Gizmos.DrawWireSphere(new Vector3(0f, 0.2f, -0.03f), 0.02f);
    }
}
