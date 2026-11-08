using UnityEngine;

/// <summary>
/// Plays a clank when a loose physics piece (robot wreckage, shield pieces) hits something hard enough.
/// Added at runtime by the scripts that break things apart.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class ImpactSound : MonoBehaviour
{
    public SoundId sound = SoundId.Debris;
    [Tooltip("Slower hits than this (m/s) make no sound.")]
    public float minSpeed = 1.5f;
    [Tooltip("Hit speed that plays at full volume.")]
    public float fullSpeed = 8f;
    public float minInterval = 0.15f;
    [Tooltip("After this many sounds the piece goes quiet (stops rattling forever).")]
    public int maxSounds = 4;

    private float nextTime;
    private int played;

    public static void AddTo(GameObject go, SoundId sound = SoundId.Debris)
    {
        if (go.GetComponent<Rigidbody>() == null || go.GetComponent<ImpactSound>() != null) return;
        go.AddComponent<ImpactSound>().sound = sound;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (played >= maxSounds || Time.time < nextTime) return;
        float speed = collision.relativeVelocity.magnitude;
        if (speed < minSpeed) return;
        nextTime = Time.time + minInterval;
        played++;
        float strength = Mathf.InverseLerp(minSpeed, fullSpeed, speed);
        Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
        GameAudio.Play(sound, point, Mathf.Lerp(0.25f, 1f, strength), Random.Range(0.9f, 1.25f));
    }
}
