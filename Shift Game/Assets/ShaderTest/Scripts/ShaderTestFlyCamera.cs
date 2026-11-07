using UnityEngine;
using UnityEngine.InputSystem;

namespace FinalShift.ShaderTest
{
    /// <summary>
    /// Simple fly camera for the shader test scene.
    /// Hold the right mouse button to look around, WASD to move, Q/E for down/up, Shift to go faster.
    /// </summary>
    public class ShaderTestFlyCamera : MonoBehaviour
    {
        public float moveSpeed = 3f;
        public float lookSensitivity = 0.15f;

        float _yaw;
        float _pitch;

        void Start()
        {
            Vector3 euler = transform.eulerAngles;
            _yaw = euler.y;
            _pitch = euler.x > 180f ? euler.x - 360f : euler.x;
        }

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (keyboard == null || mouse == null) return;

            if (mouse.rightButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue();
                _yaw += delta.x * lookSensitivity;
                _pitch = Mathf.Clamp(_pitch - delta.y * lookSensitivity, -85f, 85f);
                transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }

            Vector3 move = Vector3.zero;
            if (keyboard.wKey.isPressed) move += transform.forward;
            if (keyboard.sKey.isPressed) move -= transform.forward;
            if (keyboard.dKey.isPressed) move += transform.right;
            if (keyboard.aKey.isPressed) move -= transform.right;
            if (keyboard.eKey.isPressed) move += Vector3.up;
            if (keyboard.qKey.isPressed) move -= Vector3.up;

            float speed = keyboard.leftShiftKey.isPressed ? moveSpeed * 3f : moveSpeed;
            transform.position += move * speed * Time.deltaTime;
        }
    }
}
