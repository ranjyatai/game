using UnityEngine;
using UnityEngine.InputSystem;

namespace StylizedGrassDemo
{
    public class OrbitCamera : MonoBehaviour
    {
        [Space]
        public Transform pivot;

        [Space]
        public bool enableMouse = true;
        public float idleRotationSpeed = 0.05f;
        public float lookSmoothSpeed = 5;
        public float moveSmoothSpeed = 5;
        public float scrollSmoothSpeed = 5;

        private Transform cam;
        private float cameraRotSide;
        private float cameraRotUp;
        private float cameraRotSideCur;
        private float cameraRotUpCur;
        private float distance;

        private InputAction lookAction;
        private InputAction zoomAction;
        private InputAction clickAction;
        private InputAction rightClickAction;

        private void Awake()
        {
            // Define actions and bindings in code to avoid external assets
            lookAction = new InputAction("Look", binding: "<Mouse>/delta");
            zoomAction = new InputAction("Zoom", binding: "<Mouse>/scroll");
            clickAction = new InputAction("Click", binding: "<Mouse>/leftButton");
            rightClickAction = new InputAction("RightClick", binding: "<Mouse>/rightButton");
        }

        private void OnEnable()
        {
            lookAction.Enable();
            zoomAction.Enable();
            clickAction.Enable();
            rightClickAction.Enable();
        }

        private void OnDisable()
        {
            lookAction.Disable();
            zoomAction.Disable();
            clickAction.Disable();
            rightClickAction.Disable();
        }

        void Start()
        {
            cam = Camera.main.transform;

            cameraRotSide = transform.eulerAngles.y;
            cameraRotSideCur = transform.eulerAngles.y;
            cameraRotUp = transform.eulerAngles.x;
            cameraRotUpCur = transform.eulerAngles.x;
            distance = -cam.localPosition.z;
        }

        private void LateUpdate()
        {
            if (!enableMouse) return;

            bool isLeftClicking = clickAction.IsPressed();
            bool isRightClicking = rightClickAction.IsPressed();

            if (isLeftClicking)
            {
                Vector2 lookDelta = lookAction.ReadValue<Vector2>();
                cameraRotSide += lookDelta.x * 0.1f; // Adjusted sensitivity for New Input System delta
                cameraRotUp -= lookDelta.y * 0.1f;
                
                Cursor.visible = false;
            }
            else
            {
                cameraRotSide += idleRotationSpeed;
                Cursor.visible = true;
            }

            if (isRightClicking)
            {
                float mouseY = lookAction.ReadValue<Vector2>().y;
                distance *= (1 - 0.01f * mouseY);
            }

            Vector2 scrollDelta = zoomAction.ReadValue<Vector2>();
            if (scrollDelta.y != 0)
            {
                // New Input System scroll is usually ~120 units per notch
                distance *= (1 - 0.001f * scrollDelta.y);
            }
        }

        private void Apply()
        {
            cameraRotSideCur = Mathf.LerpAngle(cameraRotSideCur, cameraRotSide, 0.02f * lookSmoothSpeed);
            cameraRotUpCur = Mathf.Lerp(cameraRotUpCur, cameraRotUp, 0.02f * lookSmoothSpeed);
            
            transform.position = Vector3.Lerp(transform.position, pivot.position, 0.02f * moveSmoothSpeed);
            transform.rotation = Quaternion.Euler(cameraRotUpCur, cameraRotSideCur, 0);

            float dist = Mathf.Lerp(-cam.transform.localPosition.z, distance, 0.02f * scrollSmoothSpeed);
            cam.localPosition = -Vector3.forward * dist;
        }
    
        void FixedUpdate()
        {
            Apply();
        }
    }
}