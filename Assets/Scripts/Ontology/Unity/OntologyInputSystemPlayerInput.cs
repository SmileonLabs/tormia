using UnityEngine;
using UnityEngine.InputSystem;
using ithappy.Creative_Characters_FREE.Controller;

namespace Tormia.Ontology.Core
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class OntologyInputSystemPlayerInput : MonoBehaviour
    {
        [SerializeField] private OntologyWorldBootstrap bootstrap;
        [SerializeField] private string actorId = "Player";
        [SerializeField] private PlayerCamera playerCamera;
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private Animator visualAnimator;

        [Header("Input Bindings")]
        [SerializeField] private string moveUpBinding = "<Keyboard>/w";
        [SerializeField] private string moveDownBinding = "<Keyboard>/s";
        [SerializeField] private string moveLeftBinding = "<Keyboard>/a";
        [SerializeField] private string moveRightBinding = "<Keyboard>/d";
        [SerializeField] private string moveUpAltBinding = "<Keyboard>/upArrow";
        [SerializeField] private string moveDownAltBinding = "<Keyboard>/downArrow";
        [SerializeField] private string moveLeftAltBinding = "<Keyboard>/leftArrow";
        [SerializeField] private string moveRightAltBinding = "<Keyboard>/rightArrow";
        [SerializeField] private string runBinding = "<Keyboard>/leftShift";
        [SerializeField] private string runAltBinding = "<Keyboard>/rightShift";
        [SerializeField] private string jumpBinding = "<Keyboard>/space";
        [SerializeField] private string lookBinding = "<Mouse>/delta";
        [SerializeField] private string scrollBinding = "<Mouse>/scroll";
        [SerializeField] private string clickBinding = "<Mouse>/leftButton";
        [SerializeField] private string pointerPositionBinding = "<Pointer>/position";

        [Header("Movement")]
        [SerializeField] private bool directCharacterControllerFallback = true;
        [SerializeField] private float fallbackWalkSpeed = 1.5f;
        [SerializeField] private float fallbackRunSpeed = 4.0f;
        [SerializeField] private float slowedSpeedMultiplier = 0.5f;
        [SerializeField] private float fallbackGravity = -20f;
        [SerializeField] private bool clickToMove = true;
        [SerializeField] private float clickStopDistance = 0.1f;
        [SerializeField] private float clickRaycastDistance = 500f;
        [SerializeField] private float moveTargetDistance = 10f;
        [SerializeField] private float fallbackTurnSpeed = 360f;
        [SerializeField] private float mouseLookScale = 0.02f;
        [SerializeField] private float mouseScrollScale = 0.01f;

        [Header("Animator Parameters")]
        [SerializeField] private string horizontalParameter = "Hor";
        [SerializeField] private string verticalParameter = "Vert";
        [SerializeField] private string stateParameter = "State";
        [SerializeField] private string jumpParameter = "IsJump";
        [SerializeField] private float animatorDampTime = 0.08f;

        [Header("Debug")]
        [SerializeField] private Vector2 lastMoveAxis;
        [SerializeField] private Vector3 lastMoveTarget;
        [SerializeField] private bool lastKeyboardDetected;
        [SerializeField] private bool lastHadInput;
        [SerializeField] private bool hasClickTarget;
        [SerializeField] private Vector3 clickTarget;

        private CharacterController characterController;
        private InputAction moveAction;
        private InputAction lookAction;
        private InputAction scrollAction;
        private InputAction jumpAction;
        private InputAction runAction;
        private InputAction clickAction;
        private InputAction pointerPositionAction;
        private float verticalVelocity;

        private void Awake()
        {
            if (bootstrap == null)
            {
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            }

            characterController = GetComponent<CharacterController>();
            if (visualAnimator == null)
            {
                visualAnimator = FindBestVisualAnimator();
            }

            if (playerCamera == null && Camera.main != null)
            {
                playerCamera = Camera.main.GetComponent<PlayerCamera>();
            }

            if (cameraTransform == null && Camera.main != null)
            {
                cameraTransform = Camera.main.transform;
            }

            if (playerCamera != null)
            {
                playerCamera.SetPlayer(transform);
            }

            CreateInputActions();
        }

        private void OnEnable()
        {
            EnableInputActions();
        }

        private void OnDisable()
        {
            DisableInputActions();
        }

        private void Update()
        {
            EnsureCameraReferences();

            var axis = ReadMoveAxis();
            UpdateClickTarget();
            var useClickTarget = axis.sqrMagnitude <= 0.0001f && hasClickTarget;
            if (useClickTarget)
            {
                axis = GetClickMoveAxis();
            }

            var target = GetMoveTarget();
            var isRun = ReadRun();
            var isJump = ReadJump();

            lastMoveAxis = axis;
            lastMoveTarget = target;
            lastKeyboardDetected = Keyboard.current != null;
            lastHadInput = axis.sqrMagnitude > 0.0001f;

            if (directCharacterControllerFallback)
            {
                MoveCharacterController(axis, isRun, useClickTarget);
            }

            UpdateAnimator(axis, isRun, isJump);

            if (playerCamera != null)
            {
                playerCamera.SetInput(ReadMouseDelta(), ReadMouseScroll());
            }
        }

        private Animator FindBestVisualAnimator()
        {
            foreach (var candidate in GetComponentsInChildren<Animator>(true))
            {
                if (candidate != null && candidate.avatar != null && candidate.isHuman)
                {
                    return candidate;
                }
            }

            return null;
        }

        private void UpdateAnimator(Vector2 axis, bool isRun, bool isJump)
        {
            if (visualAnimator == null)
            {
                return;
            }

            var state = axis.sqrMagnitude > 0.0001f && isRun ? 1f : 0f;
            SetFloatIfExists(horizontalParameter, axis.x);
            SetFloatIfExists(verticalParameter, axis.y);
            SetFloatIfExists(stateParameter, state);
            SetBoolIfExists(jumpParameter, isJump);
        }

        private void SetFloatIfExists(string parameter, float value)
        {
            if (string.IsNullOrWhiteSpace(parameter) || !HasAnimatorParameter(parameter, AnimatorControllerParameterType.Float))
            {
                return;
            }

            visualAnimator.SetFloat(parameter, value, Mathf.Max(0f, animatorDampTime), Time.deltaTime);
        }

        private void SetBoolIfExists(string parameter, bool value)
        {
            if (string.IsNullOrWhiteSpace(parameter) || !HasAnimatorParameter(parameter, AnimatorControllerParameterType.Bool))
            {
                return;
            }

            visualAnimator.SetBool(parameter, value);
        }

        private bool HasAnimatorParameter(string parameter, AnimatorControllerParameterType type)
        {
            foreach (var animatorParameter in visualAnimator.parameters)
            {
                if (animatorParameter.name == parameter && animatorParameter.type == type)
                {
                    return true;
                }
            }

            return false;
        }

        private void CreateInputActions()
        {
            moveAction = new InputAction("OntologyMove", InputActionType.Value, expectedControlType: "Vector2");
            moveAction.AddCompositeBinding("2DVector")
                .With("Up", moveUpBinding)
                .With("Up", moveUpAltBinding)
                .With("Down", moveDownBinding)
                .With("Down", moveDownAltBinding)
                .With("Left", moveLeftBinding)
                .With("Left", moveLeftAltBinding)
                .With("Right", moveRightBinding)
                .With("Right", moveRightAltBinding);

            lookAction = new InputAction("OntologyLook", InputActionType.Value, lookBinding);
            scrollAction = new InputAction("OntologyScroll", InputActionType.Value, scrollBinding);
            jumpAction = new InputAction("OntologyJump", InputActionType.Button, jumpBinding);
            runAction = new InputAction("OntologyRun", InputActionType.Button);
            runAction.AddBinding(runBinding);
            runAction.AddBinding(runAltBinding);
            clickAction = new InputAction("OntologyClick", InputActionType.Button, clickBinding);
            pointerPositionAction = new InputAction("OntologyPointerPosition", InputActionType.Value, pointerPositionBinding);
        }

        private void EnableInputActions()
        {
            if (moveAction == null)
            {
                CreateInputActions();
            }

            moveAction.Enable();
            lookAction.Enable();
            scrollAction.Enable();
            jumpAction.Enable();
            runAction.Enable();
            clickAction.Enable();
            pointerPositionAction.Enable();
        }

        private void DisableInputActions()
        {
            if (moveAction == null)
            {
                return;
            }

            moveAction.Disable();
            lookAction.Disable();
            scrollAction.Disable();
            jumpAction.Disable();
            runAction.Disable();
            clickAction.Disable();
            pointerPositionAction.Disable();
        }

        private void EnsureCameraReferences()
        {
            var mainCamera = Camera.main;
            if (mainCamera == null)
            {
                return;
            }

            if (cameraTransform == null)
            {
                cameraTransform = mainCamera.transform;
            }

            if (playerCamera == null)
            {
                playerCamera = mainCamera.GetComponent<PlayerCamera>();
                if (playerCamera != null)
                {
                    playerCamera.SetPlayer(transform);
                }
            }
        }

        private Vector3 GetMoveTarget()
        {
            var forward = transform.forward;
            if (cameraTransform != null)
            {
                forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up);
                if (forward.sqrMagnitude < 0.0001f)
                {
                    forward = transform.forward;
                }
            }

            return transform.position + forward.normalized * Mathf.Max(0.1f, moveTargetDistance);
        }

        private void MoveCharacterController(Vector2 axis, bool isRun, bool useClickTarget)
        {
            if (characterController == null || axis.sqrMagnitude <= 0.0001f)
            {
                ApplyFallbackGravityOnly();
                return;
            }

            var move = useClickTarget ? GetClickMoveDirection() : GetCameraRelativeMove(axis);
            var speed = (isRun ? fallbackRunSpeed : fallbackWalkSpeed) * GetOntologySpeedMultiplier();
            UpdateFallbackGravity();
            characterController.Move((move * speed + Vector3.up * verticalVelocity) * Time.deltaTime);

            if (move.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    Quaternion.LookRotation(move, Vector3.up),
                    Mathf.Max(0f, fallbackTurnSpeed) * Time.deltaTime);
            }
        }

        private Vector3 GetCameraRelativeMove(Vector2 axis)
        {
            var forward = cameraTransform == null ? transform.forward : Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
            var right = cameraTransform == null ? transform.right : Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = transform.forward;
            }
            if (right.sqrMagnitude < 0.0001f)
            {
                right = transform.right;
            }

            return Vector3.ClampMagnitude(right * axis.x + forward * axis.y, 1f);
        }

        private Vector3 GetClickMoveDirection()
        {
            var toTarget = clickTarget - transform.position;
            toTarget.y = 0f;
            return toTarget.sqrMagnitude <= 0.0001f ? Vector3.zero : toTarget.normalized;
        }

        private float GetOntologySpeedMultiplier()
        {
            if (bootstrap == null)
            {
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            }

            if (bootstrap == null || bootstrap.World == null)
            {
                return 1f;
            }

            return bootstrap.World.HasFact(actorId, "movement_state", "Slowed") ? Mathf.Max(0f, slowedSpeedMultiplier) : 1f;
        }

        private void UpdateClickTarget()
        {
            if (!clickToMove || clickAction == null || !clickAction.WasPressedThisFrame())
            {
                return;
            }

            var camera = Camera.main;
            if (camera == null || pointerPositionAction == null)
            {
                return;
            }

            var pointerPosition = pointerPositionAction.ReadValue<Vector2>();
            if (OntologyUIPointerUtility.IsPointerOverUi(pointerPosition))
            {
                return;
            }

            var ray = camera.ScreenPointToRay(pointerPosition);
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, Mathf.Max(1f, clickRaycastDistance)))
            {
                clickTarget = hit.point;
                hasClickTarget = true;
            }
        }

        private Vector2 GetClickMoveAxis()
        {
            if (!clickToMove || !hasClickTarget)
            {
                return Vector2.zero;
            }

            var toTarget = clickTarget - transform.position;
            toTarget.y = 0f;
            if (toTarget.magnitude <= Mathf.Max(0.01f, clickStopDistance))
            {
                hasClickTarget = false;
                return Vector2.zero;
            }

            var forward = cameraTransform == null ? transform.forward : Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
            var right = cameraTransform == null ? transform.right : Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
            var direction = toTarget.normalized;
            return Vector2.ClampMagnitude(new Vector2(Vector3.Dot(direction, right), Vector3.Dot(direction, forward)), 1f);
        }

        private void ApplyFallbackGravityOnly()
        {
            if (characterController == null)
            {
                return;
            }

            UpdateFallbackGravity();
            characterController.Move(Vector3.up * verticalVelocity * Time.deltaTime);
        }

        private void UpdateFallbackGravity()
        {
            if (characterController != null && characterController.isGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = -1f;
                return;
            }

            verticalVelocity += fallbackGravity * Time.deltaTime;
        }

        private Vector2 ReadMoveAxis()
        {
            if (moveAction == null)
            {
                return Vector2.zero;
            }

            return Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);
        }

        private bool ReadRun()
        {
            return runAction != null && runAction.IsPressed();
        }

        private bool ReadJump()
        {
            return jumpAction != null && jumpAction.IsPressed();
        }

        private Vector2 ReadMouseDelta()
        {
            return lookAction == null ? Vector2.zero : lookAction.ReadValue<Vector2>() * mouseLookScale;
        }

        private float ReadMouseScroll()
        {
            return scrollAction == null ? 0f : scrollAction.ReadValue<Vector2>().y * mouseScrollScale;
        }
    }
}
