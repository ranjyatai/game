using UnityEngine;
#if INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

#if !INPUT_SYSTEM
#warning "Input System is not installed in your project. To use this script, please install the Input System package from the Package Manager."
#endif

namespace StylizedGrassDemo
{
    public class PlayerController : MonoBehaviour
    {
        #if INPUT_SYSTEM
        public float moveSpeed = 5f;
        public float jumpForce = 5f;
        
        private Rigidbody rb;
        private InputAction moveAction;
        private InputAction jumpAction;
        private Vector2 moveInput;
        
        public Camera cam;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();

            //Setup Move Action with WASD and Arrow Keys
            moveAction = new InputAction("Move", binding: "<Gamepad>/leftStick");
            moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");

            //Setup Jump Action
            jumpAction = new InputAction("Jump", binding: "<Keyboard>/space");
            jumpAction.AddBinding("<Gamepad>/buttonSouth");

            //Callback for Jump
            jumpAction.performed += OnJump;
            
            if (!cam) cam = Camera.main;
        }

        private void OnEnable()
        {
            moveAction.Enable();
            jumpAction.Enable();
        }

        private void OnDisable()
        {
            moveAction.Disable();
            jumpAction.Disable();
        }

        private void Update()
        {
            //Read movement vector every frame
            moveInput = moveAction.ReadValue<Vector2>();
        }

        private void FixedUpdate()
        {
            if (moveInput.sqrMagnitude > 0.01f)
            {
                // Get camera directions flattened on the Y axis
                Vector3 forward = cam.transform.forward;
                Vector3 right = cam.transform.right;
                forward.y = 0;
                right.y = 0;
                forward.Normalize();
                right.Normalize();

                // Create direction based on camera orientation
                Vector3 direction = forward * moveInput.y + right * moveInput.x;
                direction *= moveSpeed * Time.fixedDeltaTime;
        
                rb.MovePosition(transform.position + direction);
            }
        }

        private void OnJump(InputAction.CallbackContext context)
        {
            //Simple ground check could be added here
            rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
        }
        #endif
    }
}