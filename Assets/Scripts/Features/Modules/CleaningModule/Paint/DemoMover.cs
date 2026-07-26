using UnityEngine;
using UnityEngine.InputSystem;

namespace Paint
{
    [RequireComponent(typeof(Rigidbody))]
    public class DemoMover : MonoBehaviour
    {
        [SerializeField] private float speed = 3f;
        [SerializeField] private float reverseSpeed = 2f;
        [SerializeField] private float rotationSpeed = 120f;

        private Rigidbody rb;
        private float moveInput;
        private float rotateInput;

        private void Awake() => rb = GetComponent<Rigidbody>();

        private void Update()
        {
            var k = Keyboard.current;
            if (k == null) { moveInput = rotateInput = 0f; return; }

            moveInput   = (k.wKey.isPressed ? 1f : 0f) - (k.sKey.isPressed ? 1f : 0f);
            rotateInput = (k.dKey.isPressed ? 1f : 0f) - (k.aKey.isPressed ? 1f : 0f);
        }

        private void FixedUpdate()
        {
            Quaternion delta = Quaternion.Euler(0f, rotateInput * rotationSpeed * Time.fixedDeltaTime, 0f);
            rb.MoveRotation(rb.rotation * delta);

            float movement = moveInput >= 0f ? speed : reverseSpeed;
            Vector3 forward = transform.forward * (moveInput * movement);    
            rb.linearVelocity = new Vector3(forward.x, rb.linearVelocity.y, forward.z);
        }
    }


}