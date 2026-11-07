using UnityEngine;

namespace FinalShift.ShaderTest
{
    /// <summary>Slowly spins an object so you can see the stepped lighting change.</summary>
    public class ShaderTestSpin : MonoBehaviour
    {
        public Vector3 degreesPerSecond = new Vector3(0f, 20f, 0f);

        void Update()
        {
            transform.Rotate(degreesPerSecond * Time.deltaTime, Space.World);
        }
    }
}
