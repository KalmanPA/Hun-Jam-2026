using UnityEngine;

/// <summary>
/// The pixel-art display on the gun (Gun.fbx "GunDisplay", material slot 1).
///
/// Every frame it draws onto a small 67 x 100 texture (the UI art is drawn on a 20 px grid, so one texel = one
/// "pixel" of the art): the empty display (GunDisplay_Base.png), then
///  - two columns of 19 segments, filled from the bottom: ammo on the left (gold), health on the right (red);
///  - two analog needles on the gauges above them (left = ammo, right = health), sprung so they swing and settle.
/// Layout and colours match Assets/Art/Materials/GunUI 2.png (the finished reference).
/// </summary>
public class GunDisplayScreen : MonoBehaviour
{
    [Tooltip("The empty display art, 67 x 100, Read/Write enabled.")]
    [SerializeField] private Texture2D baseTexture;
    [SerializeField] private int materialIndex = 1;

    [Header("Segments")]
    [SerializeField] private int segmentCount = 19;
    [Tooltip("Row (from the top) of the topmost segment, in display pixels.")]
    [SerializeField] private int firstSegmentRow = 36;
    [SerializeField] private int segmentPitch = 3;
    [SerializeField] private Vector2Int ammoColumns = new Vector2Int(12, 22);
    [SerializeField] private Vector2Int healthColumns = new Vector2Int(44, 54);

    [Header("Needles (display pixels, from the top-left)")]
    [SerializeField] private Vector2 ammoPivot = new Vector2(17.5f, 17f);
    [SerializeField] private Vector2 healthPivot = new Vector2(49.5f, 17f);
    [SerializeField] private float needleStart = 1.2f;
    [SerializeField] private float needleLength = 8.3f;
    [Tooltip("Needle spring: higher = snappier.")]
    [SerializeField] private float needleStiffness = 70f;
    [Tooltip("Needle damping: lower = more wobble before it settles.")]
    [SerializeField] private float needleDamping = 9f;

    [Header("Colours (from the reference art)")]
    [SerializeField] private Color32 ammoLitTop = new Color32(240, 210, 122, 255);
    [SerializeField] private Color32 ammoLitBottom = new Color32(201, 162, 58, 255);
    [SerializeField] private Color32 ammoDim = new Color32(58, 46, 16, 255);
    [SerializeField] private Color32 healthLitTop = new Color32(255, 90, 60, 255);
    [SerializeField] private Color32 healthLitBottom = new Color32(224, 20, 15, 255);
    [SerializeField] private Color32 healthDim = new Color32(58, 10, 8, 255);
    [SerializeField] private Color32 needleColour = new Color32(255, 241, 200, 255);

    private const int W = 67, H = 100;

    private GearShiftWeapon weapon;
    private PlayerHealth health;
    private Renderer screen;
    private Texture2D tex;
    private Color32[] basePixels;
    private Color32[] pixels;

    private float ammoNeedle, ammoNeedleVel, healthNeedle, healthNeedleVel;
    private int lastAmmoSegs = -1, lastHealthSegs = -1;
    private Vector2Int lastAmmoTip = new Vector2Int(-99, -99), lastHealthTip = new Vector2Int(-99, -99);

    /// <summary>For tests / cutscenes: show these values instead of reading the weapon and player.</summary>
    public float? OverrideAmmo { get; set; }
    public float? OverrideHealth { get; set; }

    private void Awake()
    {
        weapon = GetComponentInParent<GearShiftWeapon>();
        health = GetComponentInParent<PlayerHealth>();
        Init(instanceMaterial: true);
        ammoNeedle = AmmoValue();
        healthNeedle = HealthValue();
        Redraw(true);
    }

    private void Init(bool instanceMaterial)
    {
        screen = GetComponent<Renderer>();
        tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "GunDisplay (runtime)" };
        basePixels = new Color32[W * H];
        if (baseTexture != null && baseTexture.isReadable && baseTexture.width == W && baseTexture.height == H)
            basePixels = baseTexture.GetPixels32();
        else
            Debug.LogWarning("GunDisplayScreen: base texture must be 67x100 with Read/Write enabled.", this);
        pixels = new Color32[W * H];

        if (screen == null) return;
        // Runtime: per-renderer material instance, so the shared asset keeps showing the empty display.
        Material[] mats = instanceMaterial ? screen.materials : CopyShared(screen.sharedMaterials);
        if (materialIndex < mats.Length && mats[materialIndex] != null)
        {
            if (mats[materialIndex].HasProperty("_BaseMap")) mats[materialIndex].SetTexture("_BaseMap", tex);
            if (mats[materialIndex].HasProperty("_MainTex")) mats[materialIndex].SetTexture("_MainTex", tex);
        }
        if (instanceMaterial) screen.materials = mats;
        else screen.sharedMaterials = mats;
    }

    private static Material[] CopyShared(Material[] shared)
    {
        var copy = new Material[shared.Length];
        for (int i = 0; i < shared.Length; i++) copy[i] = shared[i] != null ? new Material(shared[i]) : null;
        return copy;
    }

    /// <summary>Edit-mode preview (used by editor tools): draws the display for the given values on a copy of the
    /// materials of this renderer. Only call it on a temporary instance.</summary>
    public void BuildPreview(float ammo, float healthValue)
    {
        Init(instanceMaterial: false);
        OverrideAmmo = ammo;
        OverrideHealth = healthValue;
        ammoNeedle = Mathf.Clamp01(ammo);
        healthNeedle = Mathf.Clamp01(healthValue);
        Redraw(true);
    }

    private void OnDestroy()
    {
        if (tex != null) Destroy(tex);
    }

    private float AmmoValue()
    {
        if (OverrideAmmo.HasValue) return Mathf.Clamp01(OverrideAmmo.Value);
        return weapon != null && weapon.MaxAmmo > 0 ? Mathf.Clamp01((float)weapon.Ammo / weapon.MaxAmmo) : 1f;
    }

    private float HealthValue()
    {
        if (OverrideHealth.HasValue) return Mathf.Clamp01(OverrideHealth.Value);
        return health != null ? Mathf.Clamp01(health.Normalized) : 1f;
    }

    private void LateUpdate()
    {
        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        Spring(ref ammoNeedle, ref ammoNeedleVel, AmmoValue(), dt);
        Spring(ref healthNeedle, ref healthNeedleVel, HealthValue(), dt);
        Redraw(false);
    }

    private void Spring(ref float x, ref float v, float target, float dt)
    {
        v += (needleStiffness * (target - x) - needleDamping * v) * dt;
        x += v * dt;
        // The needle hits the end stops instead of swinging past them.
        if (x < 0f) { x = 0f; v = Mathf.Max(0f, v) * -0.3f; }
        if (x > 1f) { x = 1f; v = Mathf.Min(0f, v) * -0.3f; }
    }

    private int Segments(float value)
    {
        if (value <= 0f) return 0;
        return Mathf.Clamp(Mathf.CeilToInt(value * segmentCount - 0.001f), 1, segmentCount);
    }

    private void Redraw(bool force)
    {
        int ammoSegs = Segments(AmmoValue());
        int healthSegs = Segments(HealthValue());
        Vector2Int ammoTip = Tip(ammoPivot, ammoNeedle);
        Vector2Int healthTip = Tip(healthPivot, healthNeedle);
        if (!force && ammoSegs == lastAmmoSegs && healthSegs == lastHealthSegs && ammoTip == lastAmmoTip && healthTip == lastHealthTip)
            return; // nothing visible changed
        lastAmmoSegs = ammoSegs; lastHealthSegs = healthSegs; lastAmmoTip = ammoTip; lastHealthTip = healthTip;

        System.Array.Copy(basePixels, pixels, pixels.Length);
        DrawColumn(ammoColumns, ammoSegs, ammoLitTop, ammoLitBottom, ammoDim);
        DrawColumn(healthColumns, healthSegs, healthLitTop, healthLitBottom, healthDim);
        DrawNeedle(ammoPivot, ammoNeedle);
        DrawNeedle(healthPivot, healthNeedle);
        tex.SetPixels32(pixels);
        tex.Apply(false);
    }

    private void DrawColumn(Vector2Int cols, int lit, Color32 top, Color32 bottom, Color32 dim)
    {
        for (int i = 0; i < segmentCount; i++)
        {
            int row = firstSegmentRow + i * segmentPitch;           // top row of segment i (i = 0 is the topmost)
            bool on = i >= segmentCount - lit;                       // fills from the bottom
            for (int x = cols.x; x <= cols.y; x++)
            {
                Set(x, row, on ? top : dim);
                Set(x, row + 1, on ? bottom : dim);
            }
        }
    }

    private Vector2 Dir(float value)
    {
        float a = Mathf.PI * (1f - value); // 0 = pointing left (empty), 1 = pointing right (full)
        return new Vector2(Mathf.Cos(a), -Mathf.Sin(a)); // display rows grow downwards
    }

    private Vector2Int Tip(Vector2 pivot, float value)
    {
        Vector2 p = pivot + Dir(value) * needleLength;
        return new Vector2Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y));
    }

    private void DrawNeedle(Vector2 pivot, float value)
    {
        Vector2 d = Dir(value);
        for (float r = needleStart; r <= needleLength; r += 0.2f)
        {
            Vector2 p = pivot + d * r;
            int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y);
            // Leave the hub (3 x 2 pixels under the pivot) alone.
            if (y >= Mathf.FloorToInt(pivot.y) && Mathf.Abs(x + 0.5f - pivot.x) <= 1.5f) continue;
            Set(x, y, needleColour);
        }
    }

    private void Set(int x, int yFromTop, Color32 c)
    {
        if (x < 0 || x >= W || yFromTop < 0 || yFromTop >= H) return;
        pixels[(H - 1 - yFromTop) * W + x] = c; // texture rows start at the bottom
    }
}
