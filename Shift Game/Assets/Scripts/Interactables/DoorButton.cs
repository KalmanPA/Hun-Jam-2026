using UnityEngine;

/// <summary>
/// Shootable wall button for a SlidingDoor. Glows red when up and green when pressed.
/// When shot it stays pressed for a few seconds, then pops back up (it blinks just before).
/// A door with several buttons opens only when all of them are pressed at the same time,
/// and once the door opens its buttons lock down green.
/// </summary>
public class DoorButton : MonoBehaviour, IDamageable
{
    [Tooltip("Seconds the button stays pressed before it pops back up.")]
    [SerializeField] private float pressedTime = 4f;

    [Header("Parts")]
    [SerializeField] private Transform cap;
    [Tooltip("How far the cap sinks in (local -Z) when pressed.")]
    [SerializeField] private float capPushDepth = 0.05f;
    [SerializeField] private Renderer[] glowRenderers;
    [SerializeField] private Material unpressedMaterial;
    [SerializeField] private Material pressedMaterial;
    [SerializeField] private Light glowLight;
    [SerializeField] private Color unpressedLightColor = new Color(1f, 0.1f, 0.05f);
    [SerializeField] private Color pressedLightColor = new Color(0.15f, 1f, 0.2f);

    private float timer;
    private bool locked;
    private Vector3 capRest;
    private bool shownPressed;
    private bool shownOnce;

    public bool IsPressed { get; private set; }
    public bool IsLocked => locked;

    private void Awake()
    {
        if (cap != null) capRest = cap.localPosition;
        Show(false);
    }

    public void TakeDamage(float amount) => Press();

    public void Press()
    {
        if (locked) return;
        IsPressed = true;
        timer = pressedTime;
    }

    /// <summary>Keeps the button pressed for good (called by the door when it opens).</summary>
    public void Lock()
    {
        locked = true;
        IsPressed = true;
    }

    private void Update()
    {
        if (IsPressed && !locked)
        {
            timer -= Time.deltaTime;
            if (timer <= 0f) IsPressed = false;
        }

        // Blink red in the last second before popping back up.
        bool blink = IsPressed && !locked && timer < 1f && Mathf.Repeat(timer, 0.25f) < 0.12f;
        Show(IsPressed && !blink);

        if (cap != null)
            cap.localPosition = capRest - Vector3.forward * (IsPressed ? capPushDepth : 0f);
    }

    private void Show(bool pressed)
    {
        if (shownOnce && pressed == shownPressed) return;
        shownOnce = true;
        shownPressed = pressed;
        Material m = pressed ? pressedMaterial : unpressedMaterial;
        if (m != null && glowRenderers != null)
            foreach (Renderer r in glowRenderers)
                if (r != null) r.sharedMaterial = m;
        if (glowLight != null) glowLight.color = pressed ? pressedLightColor : unpressedLightColor;
    }
}
