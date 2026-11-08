using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Plays when the last intro robot is destroyed, before the blocking belt opens the level up:
///  1. The gun is lowered out of view.
///  2. The player pulls their glasses down off their face and holds them up in front of them.
///  3. A marker comes in and draws an X on the right lens, one stroke at a time.
///  4. The glasses go back on, with the red X still drawn on the lens. They sit so the X ends up exactly in the
///     middle of the view: the X on the worn glasses is the aim reticle (the guns shoot at the screen centre).
///     It's an object on the glasses, not UI; the frame itself is hidden once worn (too close to the eye to see).
///  5. The gun comes back up and the player gets control.
/// Plays once per play session; after a scene reload the reticle is simply there.
/// Started by IntroCutscene. The prefabs and materials are made by IntroCutsceneSetupTool.
/// Everything is drawn on the gun's layer (by the weapon camera), so it never clips into walls.
/// </summary>
public class GlassesCutscene : MonoBehaviour
{
    [Header("Art (set by the setup tool)")]
    [Tooltip("Glasses prefab: root in the middle of the frame, worn the way it is (lenses toward +Z), with a child named RightLens on the right lens.")]
    [SerializeField] private GameObject glassesPrefab;
    [Tooltip("Marker prefab: root at the tip, body along +Z.")]
    [SerializeField] private GameObject markerPrefab;
    [SerializeField] private Material strokeAMaterial;
    [SerializeField] private Material strokeBMaterial;
    [Tooltip("Colour of the marker X (also the aim reticle).")]
    [SerializeField] private Color markerColor = new Color(0.95f, 0.06f, 0.04f, 1f);

    [Header("Reticle")]
    [Tooltip("Width of the X drawn on the lens (metres).")]
    [SerializeField] private float lensReticleSize = 0.024f;
    [Tooltip("How big the X looks once the glasses are on, as 'this wide at this distance' (metres). " +
             "0.012 at 0.3 m is about 2.3 degrees.")]
    [SerializeField] private float hudDistance = 0.3f;
    [SerializeField] private float hudSize = 0.012f;

    [Header("Glasses poses (camera space)")]
    [SerializeField] private Vector3 wornPosition = new Vector3(0f, -0.005f, 0.022f);
    [SerializeField] private Vector3 nosePosition = new Vector3(0f, -0.1f, 0.07f);
    [SerializeField] private Vector3 noseEuler = new Vector3(12f, 0f, 0f);
    [SerializeField] private Vector3 heldPosition = new Vector3(0f, -0.03f, 0.23f);
    [SerializeField] private Vector3 heldEuler = new Vector3(-12f, 0f, -4f);

    [Header("Marker")]
    [Tooltip("Where the marker comes in from (camera space, the tip).")]
    [SerializeField] private Vector3 markerOffscreen = new Vector3(0.32f, -0.3f, 0.18f);
    [Tooltip("Which way the marker body points from the tip (camera space): toward the hand at the lower right.")]
    [SerializeField] private Vector3 markerBodyDirection = new Vector3(0.45f, -0.55f, -0.7f);

    [Header("Timing (seconds)")]
    [SerializeField] private float startDelay = 1.6f;
    [SerializeField] private float lowerGunTime = 0.45f;
    [SerializeField] private float pullDownTime = 0.55f;
    [SerializeField] private float holdUpTime = 0.7f;
    [SerializeField] private float markerInTime = 0.6f;
    [SerializeField] private float strokeTime = 0.45f;
    [SerializeField] private float betweenStrokesTime = 0.25f;
    [SerializeField] private float markerOutTime = 0.5f;
    [SerializeField] private float lookAtItTime = 0.5f;
    [SerializeField] private float putBackTime = 0.9f;
    [SerializeField] private float raiseGunTime = 0.6f;

    /// <summary>True once the glasses cutscene has played this play session (survives scene reloads).</summary>
    public static bool HasPlayed { get; private set; }
    public static bool IsPlaying { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        HasPlayed = false;
        IsPlaying = false;
    }

    private Light helperLight;

    /// <summary>Gives the player the reticle right away (no cutscene). Used after a reload or when the intro is skipped.</summary>
    public void ShowReticleNow()
    {
        Camera cam = PlayerCamera(out _);
        if (cam == null || cam.GetComponentInChildren<AimReticle>() != null) return;
        // The Player prefab carries one, switched off: use that.
        AimReticle own = AimReticle.FindUnder(cam.transform);
        if (own != null) { own.Show(); return; }
        AimReticle r = AimReticle.Create(strokeAMaterial, strokeBMaterial, markerColor, ViewLayer());
        PlaceOnHud(r.transform, cam.transform);
        r.Active = true;
    }

    private Transform SpawnGlasses(Transform camT, int layer, out Transform lens)
    {
        Transform glasses = Instantiate(glassesPrefab, camT, false).transform;
        glasses.name = "WornGlasses";
        SetLayer(glasses.gameObject, layer);
        foreach (Collider c in glasses.GetComponentsInChildren<Collider>()) Destroy(c);
        lens = glasses.Find("RightLens");
        if (lens == null) lens = glasses;
        return glasses;
    }

    /// <summary>The X, stuck to the inside of the lens (camera side), at its drawn size.</summary>
    private void StickToLens(Transform rt, Transform lens)
    {
        rt.SetParent(lens, false);
        rt.localPosition = Vector3.zero;
        rt.localRotation = Quaternion.identity;
        rt.localScale = Vector3.one * (lensReticleSize / Mathf.Max(1e-4f, lens.lossyScale.x));
    }

    /// <summary>
    /// Where the glasses sit when worn (camera space, no rotation) so the X on the lens is exactly in the middle
    /// of the view: the worn position, moved sideways and up/down by where the X sits on the frame.
    /// </summary>
    private Vector3 WornPoseFor(Transform glasses, Transform x)
    {
        Vector3 offset = Vector3.Scale(glasses.InverseTransformPoint(x.position), glasses.localScale);
        return new Vector3(-offset.x, -offset.y, wornPosition.z);
    }

    /// <summary>Worn glasses are too close to the eye to see: hide the frame and glass, keep the X.</summary>
    private static void HideFrame(Transform glasses, Transform x)
    {
        foreach (Renderer r in glasses.GetComponentsInChildren<Renderer>(true))
            if (!r.transform.IsChildOf(x)) r.enabled = false;
    }

    /// <summary>Plays the cutscene, then calls <paramref name="onDone"/> (the intro opens the level there).</summary>
    public void Play(Action onDone)
    {
        if (HasPlayed)
        {
            ShowReticleNow();
            onDone?.Invoke();
            return;
        }
        StartCoroutine(Sequence(onDone));
    }

    private Camera PlayerCamera(out FirstPersonController player)
    {
        player = FindFirstObjectByType<FirstPersonController>();
        Camera cam = null;
        if (player != null && player.CameraPivot != null) cam = player.CameraPivot.GetComponentInChildren<Camera>();
        if (cam == null && player != null) cam = player.GetComponentInChildren<Camera>();
        return cam != null ? cam : Camera.main;
    }

    private int ViewLayer()
    {
        GearShiftWeapon w = FindFirstObjectByType<GearShiftWeapon>(FindObjectsInactive.Include);
        return w != null ? w.gameObject.layer : 0;
    }

    private void PlaceOnHud(Transform reticle, Transform cam)
    {
        reticle.SetParent(cam, false);
        reticle.localPosition = new Vector3(0f, 0f, hudDistance);
        reticle.localRotation = Quaternion.identity;
        reticle.localScale = Vector3.one * hudSize;
    }

    // ---------------------------------------------------------------- the cutscene

    private IEnumerator Sequence(Action onDone)
    {
        HasPlayed = true;
        yield return new WaitForSeconds(startDelay);

        Camera cam = PlayerCamera(out FirstPersonController player);
        if (cam == null || glassesPrefab == null)
        {
            ShowReticleNow();
            onDone?.Invoke();
            yield break;
        }
        IsPlaying = true;
        Transform camT = cam.transform;
        int layer = ViewLayer();
        GearShiftWeapon weapon = player != null ? player.GetComponentInChildren<GearShiftWeapon>(true) : null;
        PlayerHealth health = player != null ? player.GetComponent<PlayerHealth>() : null;
        if (player != null) player.Frozen = true;

        // A small light near the face so the glasses read in the dark factory.
        helperLight = new GameObject("GlassesLight").AddComponent<Light>();
        helperLight.transform.SetParent(camT, false);
        helperLight.transform.localPosition = new Vector3(0.1f, 0.12f, 0.05f);
        helperLight.type = LightType.Point;
        helperLight.color = new Color(1f, 0.8f, 0.6f);
        helperLight.range = 0.9f;
        helperLight.intensity = 0f;
        helperLight.shadows = LightShadows.None;

        // 1. Lower the gun out of view.
        Transform gun = weapon != null ? weapon.transform : null;
        Vector3 gunFrom = Vector3.zero, gunLow = Vector3.zero;
        Quaternion gunFromRot = Quaternion.identity, gunLowRot = Quaternion.identity;
        if (weapon != null)
        {
            weapon.enabled = false;
            gunFrom = gun.localPosition;
            gunFromRot = gun.localRotation;
            gunLow = weapon.HipPosition + new Vector3(0.05f, -0.5f, 0.05f);
            gunLowRot = weapon.HipRotation * Quaternion.Euler(35f, -15f, 25f);
            yield return Animate(lowerGunTime, p =>
            {
                gun.localPosition = Vector3.Lerp(gunFrom, gunLow, p);
                gun.localRotation = Quaternion.Slerp(gunFromRot, gunLowRot, p);
                helperLight.intensity = p * 1.3f;
            });
        }

        // 2. Glasses down off the face and up in front of the eyes.
        Transform glasses = SpawnGlasses(camT, layer, out Transform lens);
        Quaternion worn = Quaternion.identity;
        Quaternion nose = Quaternion.Euler(noseEuler);
        Quaternion held = Quaternion.Euler(heldEuler);
        glasses.localPosition = wornPosition;
        glasses.localRotation = worn;

        yield return Animate(pullDownTime, p => Pose(glasses, wornPosition, worn, nosePosition, nose, p));
        yield return Animate(holdUpTime, p => Pose(glasses, nosePosition, nose, heldPosition, held, p));

        // The X, stuck to the inside of the right lens (camera side), not drawn yet.
        AimReticle reticle = AimReticle.Create(strokeAMaterial, strokeBMaterial, markerColor, layer);
        Transform rt = reticle.transform;
        StickToLens(rt, lens);
        reticle.SetReveal(0f, 0f);

        // 3. The marker draws the X.
        Transform marker = markerPrefab != null ? Instantiate(markerPrefab, camT, false).transform : null;
        if (marker != null)
        {
            SetLayer(marker.gameObject, layer);
            foreach (Collider c in marker.GetComponentsInChildren<Collider>()) Destroy(c);
        }
        Quaternion markerRot = Quaternion.LookRotation(markerBodyDirection.normalized, Vector3.up);
        Vector3 lift = new Vector3(0f, 0f, -0.012f); // pen off the glass (toward the camera)

        if (marker != null)
        {
            marker.localRotation = markerRot;
            Vector3 from = markerOffscreen;
            yield return Animate(markerInTime, p => marker.localPosition = Vector3.Lerp(from, TipOn(rt, camT, AimReticle.StrokeAStart) + lift * (1f - p), p));
        }

        yield return DrawStroke(reticle, rt, camT, marker, AimReticle.StrokeAStart, AimReticle.StrokeAEnd, false);
        if (marker != null)
        {
            Vector3 endA = marker.localPosition;
            yield return Animate(betweenStrokesTime, p =>
                marker.localPosition = Vector3.Lerp(endA, TipOn(rt, camT, AimReticle.StrokeBStart), p) + lift * Mathf.Sin(p * Mathf.PI));
        }
        yield return DrawStroke(reticle, rt, camT, marker, AimReticle.StrokeBStart, AimReticle.StrokeBEnd, true);

        if (marker != null)
        {
            Vector3 endB = marker.localPosition;
            Vector3 to = markerOffscreen;
            yield return Animate(markerOutTime, p => marker.localPosition = Vector3.Lerp(endB, to, p));
            Destroy(marker.gameObject);
        }
        yield return new WaitForSeconds(lookAtItTime);

        // 4. Glasses back on. The X stays drawn on the lens the whole way, and the glasses sit on the face so that the
        //    X ends exactly in the middle of the view: the X on the worn glasses IS the aim reticle (no UI).
        Vector3 gFrom = glasses.localPosition;
        Quaternion gFromRot = glasses.localRotation;
        Vector3 wornOnCentre = WornPoseFor(glasses, rt);
        Vector3 xScale = rt.localScale;
        float startAngle = lensReticleSize / Mathf.Max(0.01f, camT.InverseTransformPoint(rt.position).z);
        float endAngle = hudSize / hudDistance;
        yield return Animate(putBackTime, p =>
        {
            float first = Mathf.Clamp01(p * 2f);
            float second = Mathf.Clamp01(p * 2f - 1f);
            if (p < 0.5f) Pose(glasses, gFrom, gFromRot, nosePosition, nose, first);
            else Pose(glasses, nosePosition, nose, wornOnCentre, worn, second);
            // The X gets smaller as it comes to the eye, so on screen it shrinks smoothly to reticle size
            // (a full-size X a couple of centimetres from the eye would cover the whole view).
            float depth = Mathf.Max(0.005f, camT.InverseTransformPoint(rt.position).z);
            float angle = Mathf.Lerp(startAngle, endAngle, Mathf.SmoothStep(0f, 1f, p));
            rt.localScale = xScale * (angle * depth / lensReticleSize);
            helperLight.intensity = 1.3f * (1f - Mathf.SmoothStep(0f, 1f, p));
        }, smooth: false);

        // On the face now: the frame and glass are too close to the eye to be seen, only the X is. The X becomes the
        // aim reticle for good: a real object in a fixed plane in front of the eyes, dead centre, always visible.
        // Moved out along the view line with its size scaled to match, so on screen it doesn't move or change at all.
        HideFrame(glasses, rt);
        PlaceOnHud(rt, camT); // same spot and size on screen as on the lens (the X shrank to exactly this while coming to the eye)
        Destroy(glasses.gameObject);
        reticle.Active = true;

        // 5. Gun back up, control back.
        if (weapon != null)
        {
            Vector3 hip = weapon.HipPosition;
            Quaternion hipRot = weapon.HipRotation;
            yield return Animate(raiseGunTime, p =>
            {
                gun.localPosition = Vector3.LerpUnclamped(gunLow, hip, p);
                gun.localRotation = Quaternion.Slerp(gunLowRot, hipRot, p);
            });
            if (health == null || !health.IsDead) weapon.enabled = true;
        }
        if (player != null) player.Frozen = false;
        Destroy(helperLight.gameObject);
        IsPlaying = false;
        onDone?.Invoke();
    }

    private IEnumerator DrawStroke(AimReticle reticle, Transform rt, Transform cam, Transform marker, Vector2 from, Vector2 to, bool rightToLeft)
    {
        for (float t = 0f; t <= strokeTime; t += Time.deltaTime)
        {
            float p = Mathf.Clamp01(t / strokeTime);
            float eased = p * p * (3f - 2f * p) * 0.85f + p * 0.15f;
            Vector2 uv = Vector2.Lerp(from, to, eased);
            float wipe = rightToLeft ? 1f - uv.x : uv.x;
            if (rightToLeft) reticle.SetReveal(1f, wipe);
            else reticle.SetReveal(wipe, 0f);
            if (marker != null) marker.localPosition = TipOn(rt, cam, uv) + Wobble(t);
            yield return null;
        }
        if (rightToLeft) reticle.SetReveal(1f, 1f);
        else reticle.SetReveal(1f, 0f);
    }

    private static Vector3 Wobble(float t) => new Vector3(Mathf.Sin(t * 41f), Mathf.Sin(t * 33f + 1f), 0f) * 0.0006f;

    /// <summary>A point of the X (texture UV) in camera space.</summary>
    private static Vector3 TipOn(Transform reticle, Transform cam, Vector2 uv)
        => cam.InverseTransformPoint(reticle.TransformPoint(AimReticle.LocalPoint(uv)));

    private static void Pose(Transform t, Vector3 a, Quaternion ar, Vector3 b, Quaternion br, float p)
    {
        t.localPosition = Vector3.Lerp(a, b, p);
        t.localRotation = Quaternion.Slerp(ar, br, p);
    }

    private static IEnumerator Animate(float duration, Action<float> step, bool smooth = true)
    {
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float p = t / duration;
            step(smooth ? Mathf.SmoothStep(0f, 1f, p) : p);
            yield return null;
        }
        step(1f);
    }

    private static void SetLayer(GameObject go, int layer)
    {
        foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
    }

    private void OnDestroy()
    {
        if (helperLight != null) Destroy(helperLight.gameObject);
    }
}
