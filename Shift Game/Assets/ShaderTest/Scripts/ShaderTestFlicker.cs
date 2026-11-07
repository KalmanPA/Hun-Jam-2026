using UnityEngine;

namespace FinalShift.ShaderTest
{
    /// <summary>Makes a light flicker like a faulty factory lamp or a fire.</summary>
    [RequireComponent(typeof(Light))]
    public class ShaderTestFlicker : MonoBehaviour
    {
        public float baseIntensity = 4f;
        [Range(0f, 1f)] public float flickerAmount = 0.25f;
        public float speed = 6f;

        Light _light;
        float _seed;

        void Awake()
        {
            _light = GetComponent<Light>();
            _seed = Random.value * 100f;
        }

        void Update()
        {
            float noise = Mathf.PerlinNoise(_seed, Time.time * speed);
            _light.intensity = baseIntensity * (1f - flickerAmount + noise * flickerAmount * 2f);
        }
    }
}
