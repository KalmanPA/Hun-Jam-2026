using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Maps every SoundId to a SoundCue, and every bus to a mixer group.
/// Lives at Assets/Audio/Resources/SoundBank.asset and is loaded by GameAudio at start.
/// </summary>
public class SoundBank : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public SoundId id;
        public SoundCue cue;
    }

    [System.Serializable]
    public class BusGroup
    {
        public SoundBus bus;
        public AudioMixerGroup group;
        [Range(0f, 1f)] public float volume = 1f;
    }

    public AudioMixer mixer;
    public List<BusGroup> buses = new List<BusGroup>();
    public List<Entry> entries = new List<Entry>();

    private Dictionary<SoundId, SoundCue> lookup;

    public SoundCue Get(SoundId id)
    {
        if (lookup == null)
        {
            lookup = new Dictionary<SoundId, SoundCue>();
            foreach (Entry e in entries)
                if (e != null && e.cue != null) lookup[e.id] = e.cue;
        }
        return lookup.TryGetValue(id, out SoundCue cue) ? cue : null;
    }

    public BusGroup Bus(SoundBus bus)
    {
        foreach (BusGroup b in buses)
            if (b != null && b.bus == bus) return b;
        return null;
    }

    private void OnValidate() => lookup = null;
}
