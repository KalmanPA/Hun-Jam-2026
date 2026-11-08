using UnityEngine;

/// <summary>
/// A soft light that travels with the player so nearby walls and floors are never pitch black, plus a small
/// light right above the gun's gear-shift stick so the stick is always readable, even in the darkest corner.
/// Added automatically by FirstPersonController; tweak the numbers on the component in play mode.
/// </summary>
public class PlayerGlowLight : MonoBehaviour
{
    [Header("Surroundings")]
    [SerializeField] private Color glowColour = new Color(1f, 0.9f, 0.78f);
    [SerializeField] private float glowIntensity = 1.6f;
    [SerializeField] private float glowRange = 7f;
    [Tooltip("Offset from the camera (local space). A bit above and behind so the light falls softly.")]
    [SerializeField] private Vector3 glowOffset = new Vector3(0f, 0.4f, -0.3f);

    [Header("Gear-shift stick")]
    [SerializeField] private Color stickColour = new Color(1f, 0.95f, 0.85f);
    [SerializeField] private float stickIntensity = 1.2f;
    [SerializeField] private float stickRange = 0.9f;
    [Tooltip("How far above the stick (camera up) the stick light sits.")]
    [SerializeField] private float stickLightHeight = 0.3f;

    private Light glow;
    private Light stickLight;
    private Transform stick;
    private Transform cam;

    private void Start()
    {
        Camera c = GetComponentInChildren<Camera>();
        if (c == null) c = Camera.main;
        cam = c != null ? c.transform : transform;

        glow = MakeLight("Player Glow Light", cam, glowColour, glowIntensity, glowRange);
        glow.transform.localPosition = glowOffset;

        GearShiftWeapon weapon = GetComponentInChildren<GearShiftWeapon>(true);
        if (weapon != null)
        {
            stick = weapon.transform.Find("Model/Gate/Stick");
            stickLight = MakeLight("Stick Light", weapon.transform, stickColour, stickIntensity, stickRange);
        }
    }

    private void LateUpdate()
    {
        if (stickLight == null) return;
        // Follow the stick (it moves as gears change and the gun tilts); fall back to the gun's centre.
        Vector3 target = stick != null ? stick.position : stickLight.transform.parent.position;
        stickLight.transform.position = target + cam.up * stickLightHeight - cam.forward * 0.05f;
    }

    private static Light MakeLight(string name, Transform parent, Color colour, float intensity, float range)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Light l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = colour;
        l.intensity = intensity;
        l.range = range;
        l.shadows = LightShadows.None;
        l.renderMode = LightRenderMode.ForcePixel;
        return l;
    }
}
