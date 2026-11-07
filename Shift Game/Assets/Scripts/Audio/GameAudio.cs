using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Handle to a playing one-shot. Goes stale on its own once the sound ends (or its voice is reused).
/// </summary>
public struct SoundHandle
{
    internal GameAudio.Voice voice;
    internal int generation;

    public bool IsValid => voice != null && voice.generation == generation && voice.active;
    public bool IsPlaying => IsValid && voice.source.isPlaying;

    /// <summary>Stops the sound, fading out over <paramref name="fade"/> seconds.</summary>
    public void Stop(float fade = 0.1f)
    {
        if (IsValid) voice.FadeOut(fade);
    }
}

/// <summary>
/// The game's sound player, done the usual way for 3D games:
///  - a fixed pool of AudioSources (no AddComponent/Destroy per sound), with voice stealing by priority,
///  - random clip (never the same twice in a row), random pitch and volume on every play,
///  - 3D falloff that reaches silence at the cue's max distance, and distance culling before playing,
///  - per-cue voice limits and cooldowns so bursts and crowds don't turn into mush,
///  - occlusion: a wall between the sound and the listener muffles it (low-pass) and lowers it,
///  - mixer buses (Weapon, Player, Enemies, World, Voice, Ambience, Music), and ambience ducking under voice lines.
/// It creates itself when the game starts; nothing has to be placed in a scene.
/// Gameplay code calls <see cref="Play(SoundId, Vector3, float, float)"/>, <see cref="PlayAttached"/> or <see cref="Play2D"/>.
/// </summary>
[DefaultExecutionOrder(-500)]
public class GameAudio : MonoBehaviour
{
    private const int VoiceCount = 40;
    private const float OcclusionInterval = 0.15f;
    private const float OccludedCutoff = 1400f;
    private const float OccludedVolume = 0.55f;
    private const float OpenCutoff = 22000f;

    internal class Voice
    {
        public AudioSource source;
        public AudioLowPassFilter lowPass;
        public SoundCue cue;
        public Transform follow;
        public Vector3 offset;
        public bool active;
        public int generation;
        public float startTime;
        public float baseVolume;
        public float fade = 1f;
        public float fadeSpeed;      // > 0 while fading out
        public float occlusion;      // 0 open .. 1 blocked (smoothed)
        public float occlusionTarget;
        public float nextOcclusionCheck;

        public void FadeOut(float seconds)
        {
            if (!active) return;
            if (seconds <= 0f) { fade = 0f; fadeSpeed = 1000f; return; }
            fadeSpeed = Mathf.Max(fadeSpeed, 1f / seconds);
        }
    }

    private static GameAudio instance;
    private static bool quitting;

    private SoundBank bank;
    private readonly List<Voice> voices = new List<Voice>();
    private readonly Dictionary<SoundCue, float> lastPlayTime = new Dictionary<SoundCue, float>();
    private readonly Dictionary<SoundCue, int> lastClip = new Dictionary<SoundCue, int>();
    private static readonly Dictionary<Vector2, AnimationCurve> Curves = new Dictionary<Vector2, AnimationCurve>();
    private static readonly RaycastHit[] OcclusionHits = new RaycastHit[16];

    private AudioListener listener;
    private float duck = 1f;

    /// <summary>Ambience and machine loops are multiplied by this (it dips while a voice line plays).</summary>
    public static float AmbienceDuck => instance != null ? instance.duck : 1f;

    public static Transform Listener => instance != null && instance.listener != null ? instance.listener.transform : null;

    // ---------------------------------------------------------------- setup

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        quitting = false;
        EnsureInstance();
    }

    private static GameAudio EnsureInstance()
    {
        if (instance != null || quitting || !Application.isPlaying) return instance;
        GameObject go = new GameObject("GameAudio");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<GameAudio>();
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;

        bank = Resources.Load<SoundBank>("SoundBank");
        if (bank == null)
            Debug.LogWarning("GameAudio: no SoundBank found. Run Final Shift > Audio > Rebuild Sound Bank.");

        for (int i = 0; i < VoiceCount; i++)
        {
            GameObject go = new GameObject("Voice " + i);
            go.transform.SetParent(transform, false);
            AudioSource src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.dopplerLevel = 0f;
            AudioLowPassFilter lp = go.AddComponent<AudioLowPassFilter>();
            lp.cutoffFrequency = OpenCutoff;
            voices.Add(new Voice { source = src, lowPass = lp });
        }
    }

    private void OnApplicationQuit() => quitting = true;

    // ---------------------------------------------------------------- public API

    /// <summary>Plays a sound at a world position.</summary>
    public static SoundHandle Play(SoundId id, Vector3 position, float volumeScale = 1f, float pitchScale = 1f)
    {
        GameAudio a = EnsureInstance();
        return a != null ? a.PlayInternal(id, position, null, volumeScale, pitchScale) : default;
    }

    /// <summary>Plays a sound that follows a transform (voice lines, things on moving robots).
    /// If the transform is destroyed the sound finishes where it was.</summary>
    public static SoundHandle PlayAttached(SoundId id, Transform follow, Vector3 localOffset = default, float volumeScale = 1f, float pitchScale = 1f)
    {
        GameAudio a = EnsureInstance();
        if (a == null) return default;
        Vector3 pos = follow != null ? follow.TransformPoint(localOffset) : localOffset;
        SoundHandle h = a.PlayInternal(id, pos, follow, volumeScale, pitchScale);
        if (h.IsValid) h.voice.offset = localOffset;
        return h;
    }

    /// <summary>Plays a first-person sound on the listener (the cue's spatial blend still applies).</summary>
    public static SoundHandle Play2D(SoundId id, float volumeScale = 1f, float pitchScale = 1f)
    {
        GameAudio a = EnsureInstance();
        if (a == null) return default;
        Transform l = Listener;
        return a.PlayInternal(id, l != null ? l.position : Vector3.zero, l, volumeScale, pitchScale);
    }

    /// <summary>The cue for an id (null when it has none). Used by loop emitters.</summary>
    public static SoundCue CueFor(SoundId id)
    {
        GameAudio a = EnsureInstance();
        return a != null && a.bank != null ? a.bank.Get(id) : null;
    }

    /// <summary>Sets up an AudioSource for a cue: mixer group, 3D falloff, priority. Used by loop emitters too.</summary>
    public static void Configure(AudioSource src, SoundCue cue)
    {
        GameAudio a = EnsureInstance();
        src.outputAudioMixerGroup = a != null ? a.GroupFor(cue.bus) : null;
        src.spatialBlend = cue.spatialBlend;
        src.dopplerLevel = 0f;
        src.priority = cue.priority;
        src.reverbZoneMix = cue.reverbMix;
        src.minDistance = Mathf.Max(0.01f, cue.minDistance);
        src.maxDistance = Mathf.Max(src.minDistance + 0.1f, cue.maxDistance);
        src.rolloffMode = AudioRolloffMode.Custom;
        src.SetCustomCurve(AudioSourceCurveType.CustomRolloff, Falloff(src.minDistance, src.maxDistance));
        src.spread = cue.spatialBlend < 1f ? 0f : 30f;
    }

    /// <summary>Picks a clip variation, avoiding the one played last time.</summary>
    public static AudioClip PickClip(SoundCue cue)
    {
        GameAudio a = EnsureInstance();
        if (cue == null || cue.clips == null || cue.clips.Length == 0) return null;
        int count = cue.clips.Length;
        int last = a != null && a.lastClip.TryGetValue(cue, out int l) ? l : -1;
        int index = Random.Range(0, count);
        if (count > 1 && index == last) index = (index + 1 + Random.Range(0, count - 1)) % count;
        for (int i = 0; i < count && cue.clips[index] == null; i++) index = (index + 1) % count;
        if (a != null) a.lastClip[cue] = index;
        return cue.clips[index];
    }

    /// <summary>Volume multiplier for a bus (for a future options menu).</summary>
    public static float BusVolume(SoundBus bus)
    {
        GameAudio a = EnsureInstance();
        SoundBank.BusGroup b = a != null && a.bank != null ? a.bank.Bus(bus) : null;
        return b != null ? b.volume : 1f;
    }

    /// <summary>
    /// How blocked the straight line from the listener to <paramref name="position"/> is: 0 = clear, 1 = behind a wall.
    /// Loose physics props, characters and the emitter's own colliders don't count.
    /// </summary>
    public static float Occlusion(Vector3 position, Transform ignore)
    {
        Transform l = Listener;
        if (l == null) return 0f;
        Vector3 from = l.position;
        Vector3 delta = position - from;
        float distance = delta.magnitude;
        if (distance < 1f) return 0f;

        int count = Physics.RaycastNonAlloc(from, delta / distance, OcclusionHits, distance, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            RaycastHit h = OcclusionHits[i];
            if (distance - h.distance < 0.6f) continue;                      // the surface the sound sits on
            if (h.distance < 0.4f) continue;                                  // the player's own body
            Collider c = h.collider;
            if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) continue;
            if (c is CharacterController) continue;
            if (ignore != null && c.transform.IsChildOf(ignore)) continue;
            if (c.GetComponentInParent<RobotEnemy>() != null) continue;
            return 1f;
        }
        return 0f;
    }

    // ---------------------------------------------------------------- playing

    private SoundHandle PlayInternal(SoundId id, Vector3 position, Transform follow, float volumeScale, float pitchScale)
    {
        if (bank == null || id == SoundId.None) return default;
        SoundCue cue = bank.Get(id);
        if (cue == null || !cue.HasClips) return default;

        float now = Time.unscaledTime;
        if (cue.cooldown > 0f && lastPlayTime.TryGetValue(cue, out float last) && now - last < cue.cooldown)
            return default;

        FindListener();
        bool positional = cue.spatialBlend > 0.5f && follow != (listener != null ? listener.transform : null);
        if (positional && listener != null && (position - listener.transform.position).sqrMagnitude > cue.maxDistance * cue.maxDistance)
            return default; // too far to hear: don't spend a voice on it

        // Per-cue voice limit.
        int playing = 0;
        Voice oldestOfCue = null;
        foreach (Voice v in voices)
        {
            if (!v.active || v.cue != cue) continue;
            playing++;
            if (oldestOfCue == null || v.startTime < oldestOfCue.startTime) oldestOfCue = v;
        }
        Voice voice = null;
        if (playing >= cue.maxVoices)
        {
            if (cue.limit == SoundCue.Limit.Reject) return default;
            voice = oldestOfCue;
        }

        if (voice == null) voice = FreeVoice(cue.priority);
        if (voice == null) return default;

        AudioClip clip = PickClip(cue);
        if (clip == null) return default;

        lastPlayTime[cue] = now;

        AudioSource src = voice.source;
        src.Stop();
        Configure(src, cue);
        src.clip = clip;
        src.loop = cue.loop;
        src.pitch = Mathf.Max(0.05f, cue.pitch * pitchScale * (1f + Random.Range(-cue.pitchVariance, cue.pitchVariance)));
        voice.baseVolume = cue.volume * volumeScale * (1f - Random.value * cue.volumeVariance) * BusVolume(cue.bus);
        voice.cue = cue;
        voice.follow = follow;
        voice.offset = Vector3.zero;
        voice.active = true;
        voice.generation++;
        voice.startTime = now;
        voice.fade = 1f;
        voice.fadeSpeed = 0f;
        voice.occlusionTarget = cue.occlusion && positional ? Occlusion(position, follow) : 0f;
        voice.occlusion = voice.occlusionTarget;
        voice.nextOcclusionCheck = now + OcclusionInterval;
        src.transform.position = position;
        ApplyLevel(voice);
        src.Play();

        return new SoundHandle { voice = voice, generation = voice.generation };
    }

    private Voice FreeVoice(int priority)
    {
        Voice steal = null;
        foreach (Voice v in voices)
        {
            if (!v.active) return v;
            // Candidate to steal: the least important one, and among equals the oldest.
            if (steal == null || v.cue.priority > steal.cue.priority
                || (v.cue.priority == steal.cue.priority && v.startTime < steal.startTime))
                steal = v;
        }
        return steal != null && steal.cue.priority >= priority ? steal : null;
    }

    private void ApplyLevel(Voice v)
    {
        float occ = v.occlusion;
        v.source.volume = v.baseVolume * v.fade * Mathf.Lerp(1f, OccludedVolume, occ);
        v.lowPass.cutoffFrequency = Mathf.Lerp(OpenCutoff, OccludedCutoff, occ);
        v.lowPass.enabled = occ > 0.01f;
    }

    private AudioMixerGroup GroupFor(SoundBus bus)
    {
        SoundBank.BusGroup b = bank != null ? bank.Bus(bus) : null;
        return b != null ? b.group : null;
    }

    private void FindListener()
    {
        if (listener != null && listener.isActiveAndEnabled) return;
        listener = FindFirstObjectByType<AudioListener>();
    }

    // ---------------------------------------------------------------- update

    private void LateUpdate()
    {
        FindListener();
        float now = Time.unscaledTime;
        float dt = Time.unscaledDeltaTime;
        bool voicePlaying = false;

        foreach (Voice v in voices)
        {
            if (!v.active) continue;
            AudioSource src = v.source;

            if (!src.isPlaying && !AudioListener.pause)
            {
                Release(v);
                continue;
            }

            if (v.follow != null) src.transform.position = v.follow.TransformPoint(v.offset);

            if (v.fadeSpeed > 0f)
            {
                v.fade -= v.fadeSpeed * dt;
                if (v.fade <= 0f) { Release(v); continue; }
            }

            if (v.cue.occlusion && v.cue.spatialBlend > 0.5f && now >= v.nextOcclusionCheck)
            {
                v.nextOcclusionCheck = now + OcclusionInterval;
                v.occlusionTarget = Occlusion(src.transform.position, v.follow);
            }
            v.occlusion = Mathf.MoveTowards(v.occlusion, v.occlusionTarget, dt * 4f);
            ApplyLevel(v);

            if (v.cue.bus == SoundBus.Voice) voicePlaying = true;
        }

        // Duck the ambience a little under voice lines so they read clearly.
        duck = Mathf.MoveTowards(duck, voicePlaying ? 0.55f : 1f, dt * (voicePlaying ? 4f : 1.5f));
    }

    private static void Release(Voice v)
    {
        v.source.Stop();
        v.source.clip = null;
        v.active = false;
        v.follow = null;
        v.cue = null;
        v.generation++;
    }

    // ---------------------------------------------------------------- falloff

    /// <summary>
    /// Natural-sounding falloff: full volume inside min distance, then roughly inverse distance,
    /// tapered so it reaches true silence exactly at max distance (Unity's built-in log rolloff never does).
    /// </summary>
    private static AnimationCurve Falloff(float min, float max)
    {
        Vector2 key = new Vector2(min, max);
        if (Curves.TryGetValue(key, out AnimationCurve cached)) return cached;

        const int samples = 16;
        Keyframe[] keys = new Keyframe[samples + 1];
        for (int i = 0; i <= samples; i++)
        {
            float t = i / (float)samples;           // 0..1 of max distance
            float d = t * max;
            float inverse = d <= min ? 1f : min / d;
            float taper = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 1f, t));
            keys[i] = new Keyframe(t, inverse * taper);
        }
        AnimationCurve curve = new AnimationCurve(keys);
        for (int i = 0; i < keys.Length; i++) curve.SmoothTangents(i, 0f);
        Curves[key] = curve;
        return curve;
    }
}
