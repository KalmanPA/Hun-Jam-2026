using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Plays when PlayerHealth dies: the camera drops to the floor and tips over, the gun falls out of the hands,
/// the screen fades to black, the player respawns at the active Checkpoint (its segment is rebuilt), then the
/// screen fades back in. With no checkpoint reached yet the scene reloads (or, if it isn't in the build
/// settings, the player goes back to where they started).
/// Added to the player automatically by PlayerHealth.
/// </summary>
[RequireComponent(typeof(PlayerHealth))]
public class PlayerDeathSequence : MonoBehaviour
{
    [Header("Camera fall")]
    [SerializeField] private float fallDuration = 0.6f;
    [Tooltip("Camera height above the player's feet once it hits the floor.")]
    [SerializeField] private float floorCameraHeight = 0.25f;
    [Tooltip("How far the head tips sideways on the floor (degrees).")]
    [SerializeField] private float fallRoll = 78f;
    [SerializeField] private float fallYaw = 12f;

    [Header("Gun drop")]
    [Tooltip("How far the gun slides (camera space) as it falls out of the hands.")]
    [SerializeField] private Vector3 gunDropOffset = new Vector3(0.15f, -1.1f, 0.35f);
    [SerializeField] private Vector3 gunDropSpin = new Vector3(70f, -30f, 110f);
    [SerializeField] private float gunDropDuration = 0.5f;

    [Header("Fade")]
    [SerializeField] private float lieOnFloorTime = 0.7f;
    [SerializeField] private float fadeOutDuration = 0.9f;
    [SerializeField] private float blackTime = 0.4f;
    [SerializeField] private float fadeInDuration = 0.8f;
    [SerializeField] private Color fadeColor = Color.black;

    private PlayerHealth health;
    private FirstPersonController controller;
    private CharacterController body;
    private GearShiftWeapon weapon;
    private Transform cameraPivot;
    private Vector3 cameraRestPosition;
    private Vector3 startPosition;
    private Quaternion startRotation;
    private float fade;
    private Texture2D fadeTexture;
    private bool running;

    public bool IsRunning => running;

    private void Awake()
    {
        health = GetComponent<PlayerHealth>();
        controller = GetComponent<FirstPersonController>();
        body = GetComponent<CharacterController>();
        weapon = GetComponentInChildren<GearShiftWeapon>(true);

        cameraPivot = controller != null ? controller.CameraPivot : null;
        if (cameraPivot == null)
        {
            Camera cam = GetComponentInChildren<Camera>();
            if (cam != null) cameraPivot = cam.transform;
        }
        if (cameraPivot != null) cameraRestPosition = cameraPivot.localPosition;

        startPosition = transform.position;
        startRotation = transform.rotation;

        fadeTexture = new Texture2D(1, 1);
        fadeTexture.SetPixel(0, 0, Color.white);
        fadeTexture.Apply();
    }

    private void OnEnable() => health.Died += OnDied;
    private void OnDisable() => health.Died -= OnDied;

    private void OnDestroy()
    {
        if (fadeTexture != null) Destroy(fadeTexture);
    }

    private void OnDied()
    {
        if (!running) StartCoroutine(Sequence());
    }

    private IEnumerator Sequence()
    {
        running = true;
        if (controller != null) controller.Frozen = true;

        // Gun falls out of the hands. Turning the weapon script off stops firing and stops it posing the gun.
        Transform gun = null;
        Vector3 gunFrom = Vector3.zero;
        Quaternion gunFromRot = Quaternion.identity;
        if (weapon != null)
        {
            weapon.enabled = false;
            gun = weapon.transform;
            gunFrom = gun.localPosition;
            gunFromRot = gun.localRotation;
        }

        Vector3 camFrom = cameraPivot != null ? cameraPivot.localPosition : Vector3.zero;
        Quaternion camFromRot = cameraPivot != null ? cameraPivot.localRotation : Quaternion.identity;
        Vector3 camTo = new Vector3(camFrom.x, FeetHeight() + floorCameraHeight, camFrom.z);
        // Tip over sideways, looking a little down at the floor.
        Quaternion camToRot = Quaternion.Euler(12f, fallYaw, fallRoll);

        float duration = Mathf.Max(fallDuration, gunDropDuration);
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            if (cameraPivot != null)
            {
                float p = Mathf.Clamp01(t / fallDuration);
                float drop = p * p;                              // speeds up like falling
                float bounce = Mathf.Sin(Mathf.Clamp01((t - fallDuration * 0.85f) / 0.15f) * Mathf.PI) * 0.04f;
                cameraPivot.localPosition = Vector3.LerpUnclamped(camFrom, camTo, drop) + Vector3.up * bounce;
                cameraPivot.localRotation = Quaternion.Slerp(camFromRot, camToRot, Mathf.SmoothStep(0f, 1f, p));
            }
            if (gun != null)
            {
                float g = Mathf.Clamp01(t / gunDropDuration);
                gun.localPosition = gunFrom + gunDropOffset * (g * g);
                gun.localRotation = gunFromRot * Quaternion.Euler(gunDropSpin * g);
            }
            yield return null;
        }
        if (cameraPivot != null) { cameraPivot.localPosition = camTo; cameraPivot.localRotation = camToRot; }

        yield return new WaitForSeconds(lieOnFloorTime);
        yield return Fade(0f, 1f, fadeOutDuration);

        if (!Respawn()) yield break; // scene is reloading

        yield return new WaitForSeconds(blackTime);
        if (controller != null) controller.Frozen = false;
        yield return Fade(1f, 0f, fadeInDuration);
        running = false;
    }

    /// <summary>Puts the player back at the active checkpoint. Returns false if the scene is being reloaded instead.</summary>
    private bool Respawn()
    {
        Checkpoint checkpoint = RespawnManager.Active;
        bool refillAmmo = checkpoint == null || checkpoint.RefillAmmo;
        int ammoAtCheckpoint = checkpoint != null ? checkpoint.AmmoWhenReached : -1;

        if (!RespawnManager.ResetToActiveCheckpoint(out Vector3 position, out Quaternion rotation))
        {
            if (RespawnManager.CanReloadScene)
            {
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                return false;
            }
            position = startPosition;
            rotation = startRotation;
        }

        if (controller != null) controller.Teleport(position, rotation);
        else transform.SetPositionAndRotation(position, rotation);

        if (cameraPivot != null)
        {
            cameraPivot.localPosition = cameraRestPosition;
            cameraPivot.localRotation = Quaternion.identity;
        }

        health.Revive();

        if (weapon != null)
        {
            weapon.enabled = true; // it puts the gun back in the hip pose on its next Update
            if (refillAmmo || ammoAtCheckpoint < 0) weapon.AddAmmo(weapon.MaxAmmo);
            else weapon.AddAmmo(ammoAtCheckpoint - weapon.Ammo);
        }
        return true;
    }

    private float FeetHeight()
    {
        if (body == null) return 0f;
        return body.center.y - body.height * 0.5f;
    }

    private IEnumerator Fade(float from, float to, float duration)
    {
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            fade = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / duration));
            yield return null;
        }
        fade = to;
    }

    private void OnGUI()
    {
        if (fade <= 0f) return;
        GUI.depth = -1000;
        Color c = fadeColor;
        c.a *= fade;
        GUI.color = c;
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), fadeTexture);
        GUI.color = Color.white;
    }
}
