using UnityEngine;

/// <summary>
/// A looping 3D sound (machine hum, conveyor, elevator motor). Gameplay scripts add it at runtime,
/// and it can also be placed by hand in a scene for ambience (pick a Sound or drag in a Cue).
///  - Fades in and out when switched with <see cref="Playing"/>.
///  - Starts at a random point in the clip so several machines never pulse in sync.
///  - Line emitter: with a Line Length the sound comes from the nearest point along the object's
///    forward axis (a long conveyor sounds like a conveyor, not a dot in its middle).
///  - Pauses itself when the listener is out of range (costs nothing), and muffles behind walls.
/// </summary>
public class SoundLoopEmitter : MonoBehaviour
{
    [SerializeField] private SoundId sound = SoundId.None;
    [Tooltip("Optional: plays this cue instead of the Sound id.")]
    [SerializeField] private SoundCue cueOverride;
    [SerializeField] private bool playOnStart = true;
    [Range(0f, 2f)] [SerializeField] private float volume = 1f;
    [SerializeField] private float fadeIn = 0.6f;
    [SerializeField] private float fadeOut = 0.8f;
    [Tooltip("0 = point. Otherwise the sound slides along this many metres of the local Z axis (centred) to stay nearest the listener.")]
    [SerializeField] private float lineLength;
    [Tooltip("Ambience bus loops dip under voice lines.")]
    [SerializeField] private bool duckUnderVoice = true;

    private AudioSource source;
    private AudioLowPassFilter lowPass;
    private SoundCue cue;
    private float level;        // 0..1 fade
    private float occlusion;
    private float occlusionTarget;
    private float nextCheck;
    private bool playing;

    /// <summary>Fades the loop in (true) or out (false).</summary>
    public bool Playing
    {
        get => playing;
        set => playing = value;
    }

    public float Volume { get => volume; set => volume = value; }

    /// <summary>Adds (or reuses) an emitter on <paramref name="host"/> for <paramref name="id"/>.</summary>
    public static SoundLoopEmitter Attach(GameObject host, SoundId id, bool startPlaying, float lineLength = 0f, float volume = 1f)
    {
        GameObject go = new GameObject("Loop " + id);
        go.transform.SetParent(host.transform, false);
        SoundLoopEmitter e = go.AddComponent<SoundLoopEmitter>();
        e.sound = id;
        e.playOnStart = startPlaying;
        e.lineLength = lineLength;
        e.volume = volume;
        return e;
    }

    private void Start()
    {
        cue = cueOverride != null ? cueOverride : GameAudio.CueFor(sound);
        if (cue == null || !cue.HasClips) { enabled = false; return; }

        source = gameObject.AddComponent<AudioSource>();
        lowPass = gameObject.AddComponent<AudioLowPassFilter>();
        lowPass.cutoffFrequency = 22000f;
        lowPass.enabled = false;
        GameAudio.Configure(source, cue);
        source.playOnAwake = false;
        source.loop = true;
        source.clip = GameAudio.PickClip(cue);
        source.pitch = cue.pitch * (1f + Random.Range(-cue.pitchVariance, cue.pitchVariance));
        source.volume = 0f;
        if (source.clip != null) source.time = Random.Range(0f, source.clip.length * 0.9f);
        playing = playOnStart;
    }

    private void LateUpdate()
    {
        if (source == null) return;

        float dt = Time.deltaTime;
        float target = playing ? 1f : 0f;
        float speed = playing ? 1f / Mathf.Max(0.01f, fadeIn) : 1f / Mathf.Max(0.01f, fadeOut);
        level = Mathf.MoveTowards(level, target, speed * dt);

        Transform listener = GameAudio.Listener;
        Vector3 pos = transform.parent != null && lineLength > 0f ? NearestOnLine(listener) : transform.position;
        if (lineLength > 0f) source.transform.position = pos;

        bool inRange = listener == null || cue.spatialBlend < 0.5f
            || (listener.position - pos).sqrMagnitude <= (cue.maxDistance + 2f) * (cue.maxDistance + 2f);
        bool audible = level > 0.001f && inRange;

        if (!audible)
        {
            if (source.isPlaying) source.Pause();
            return;
        }
        if (!source.isPlaying) source.UnPause();
        if (!source.isPlaying) source.Play();

        if (cue.occlusion && cue.spatialBlend > 0.5f && Time.time >= nextCheck)
        {
            nextCheck = Time.time + 0.25f;
            occlusionTarget = GameAudio.Occlusion(pos, transform.parent != null ? transform.parent : transform);
        }
        occlusion = Mathf.MoveTowards(occlusion, occlusionTarget, dt * 3f);
        lowPass.enabled = occlusion > 0.01f;
        lowPass.cutoffFrequency = Mathf.Lerp(22000f, 1400f, occlusion);

        float duck = duckUnderVoice && (cue.bus == SoundBus.Ambience || cue.bus == SoundBus.World) ? GameAudio.AmbienceDuck : 1f;
        source.volume = cue.volume * volume * level * duck * Mathf.Lerp(1f, 0.55f, occlusion) * GameAudio.BusVolume(cue.bus);
    }

    /// <summary>For line emitters: the point on the line (this object's local Z) closest to the listener.
    /// The emitter is a child, so the line is measured on the parent's axes.</summary>
    private Vector3 NearestOnLine(Transform listener)
    {
        Transform axis = transform.parent;
        if (listener == null) return axis.position;
        Vector3 local = axis.InverseTransformPoint(listener.position);
        float half = lineLength * 0.5f;
        return axis.TransformPoint(new Vector3(0f, transform.localPosition.y, Mathf.Clamp(local.z, -half, half)));
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 0.8f, 1f);
        if (lineLength > 0f && transform.parent != null)
        {
            Transform axis = transform.parent;
            float half = lineLength * 0.5f;
            Gizmos.DrawLine(axis.TransformPoint(new Vector3(0f, transform.localPosition.y, -half)),
                            axis.TransformPoint(new Vector3(0f, transform.localPosition.y, half)));
        }
        SoundCue c = cueOverride;
        float range = c != null ? c.maxDistance : 15f;
        Gizmos.DrawWireSphere(transform.position, range);
    }
#endif
}
