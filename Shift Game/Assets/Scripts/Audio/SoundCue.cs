using UnityEngine;

/// <summary>
/// One sound event: a pool of clip variations plus how they play (volume and pitch randomisation,
/// 3D falloff, how many can overlap, cooldown). Made by Final Shift > Audio > Rebuild Sound Bank,
/// and safe to tweak by hand afterwards (a rebuild only refreshes the clip list while Auto Assign Clips is on).
/// </summary>
[CreateAssetMenu(menuName = "Final Shift/Sound Cue", fileName = "SoundCue")]
public class SoundCue : ScriptableObject
{
    public enum Limit { StealOldest, Reject }

    [Tooltip("Variations. A random one plays each time, never the same one twice in a row.")]
    public AudioClip[] clips = new AudioClip[0];
    [Tooltip("Play the clips one after the other in list order (then from the top again) instead of at random.")]
    public bool playInOrder;
    [Tooltip("Off: Rebuild Sound Bank leaves the clip list alone.")]
    public bool autoAssignClips = true;
    public SoundBus bus = SoundBus.World;

    [Header("Level")]
    [Range(0f, 1f)] public float volume = 1f;
    [Tooltip("Each play is up to this much quieter (0.1 = up to 10%).")]
    [Range(0f, 1f)] public float volumeVariance = 0.1f;
    [Range(0.1f, 3f)] public float pitch = 1f;
    [Tooltip("Random pitch +/- this amount.")]
    [Range(0f, 1f)] public float pitchVariance = 0.06f;

    [Header("3D")]
    [Tooltip("0 = flat 2D (first person sounds), 1 = fully positioned in the world.")]
    [Range(0f, 1f)] public float spatialBlend = 1f;
    [Tooltip("Full volume inside this distance.")]
    public float minDistance = 2f;
    [Tooltip("Silent beyond this distance (and not played at all if it starts further away).")]
    public float maxDistance = 30f;
    [Tooltip("Muffle and lower the sound when a wall is between it and the listener.")]
    public bool occlusion = true;
    [Range(0f, 1.1f)] public float reverbMix = 1f;

    [Header("Voices")]
    [Tooltip("How many of this cue can play at once.")]
    [Min(1)] public int maxVoices = 4;
    [Tooltip("What happens when Max Voices are already playing.")]
    public Limit limit = Limit.StealOldest;
    [Tooltip("Minimum seconds between two plays of this cue (stops machine-gun stacking).")]
    [Min(0f)] public float cooldown = 0.03f;
    [Tooltip("0 = most important, 256 = least. Unimportant sounds are cut first when all voices are busy.")]
    [Range(0, 256)] public int priority = 128;

    [Header("Loop")]
    public bool loop;

    public bool HasClips
    {
        get
        {
            if (clips == null) return false;
            foreach (AudioClip c in clips) if (c != null) return true;
            return false;
        }
    }
}
