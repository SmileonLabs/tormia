using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using Tormia.Ontology.Core;

namespace Tormia.Characters
{
    public class WorldPlayerController : MonoBehaviour
    {
        [SerializeField] private Camera worldCamera;
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField] private float moveSpeed = 2.8f;
        [SerializeField] private float rotationSpeed = 12f;
        [SerializeField] private float stoppingDistance = 0.08f;
        [SerializeField] private Animator animator;
        [SerializeField] private string idleStateName = "Idle_Relaxed";
        [SerializeField] private string walkStateName = "Walk_Forward";

        private Vector3 destination;
        private bool hasDestination;
        private bool isWalking;
        private string currentAnimationState;

        private void Awake()
        {
            if (worldCamera == null)
            {
                worldCamera = Camera.main;
            }

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            destination = transform.position;
        }

        private void OnEnable()
        {
            hasDestination = false;
            isWalking = false;
            ForceAnimationState(idleStateName);
        }

        private void Start()
        {
            ForceAnimationState(idleStateName);
        }

        private void Update()
        {
            HandleInput();
            MoveToDestination();
        }

        private void HandleInput()
        {
            if (worldCamera == null || IsPointerOverUi())
            {
                return;
            }

            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                TrySetDestination(Mouse.current.position.ReadValue());
                return;
            }

            if (Touchscreen.current != null)
            {
                var touch = Touchscreen.current.primaryTouch;
                if (touch.press.wasPressedThisFrame)
                {
                    TrySetDestination(touch.position.ReadValue());
                }
            }
        }

        private bool IsPointerOverUi()
        {
            return OntologyUIPointerUtility.IsPointerOverUi();
        }

        private void TrySetDestination(Vector2 screenPosition)
        {
            var ray = worldCamera.ScreenPointToRay(screenPosition);
            if (!Physics.Raycast(ray, out var hit, 500f, groundMask, QueryTriggerInteraction.Ignore))
            {
                return;
            }

            destination = hit.point;
            hasDestination = true;
            SetWalking(true, true);
        }

        private void MoveToDestination()
        {
            if (!hasDestination)
            {
                SetWalking(false);
                return;
            }

            var current = transform.position;
            var target = destination;
            var toTarget = target - current;
            if (toTarget.magnitude <= stoppingDistance)
            {
                transform.position = target;
                hasDestination = false;
                SetWalking(false, true);
                return;
            }

            var direction = toTarget.normalized;
            transform.position = Vector3.MoveTowards(current, target, moveSpeed * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), rotationSpeed * Time.deltaTime);
        }

        private void SetWalking(bool walking, bool force = false)
        {
            if (!force && isWalking == walking)
            {
                return;
            }

            isWalking = walking;
            PlayAnimationState(walking ? walkStateName : idleStateName, 0.12f);
        }

        private void PlayAnimationState(string stateName, float transitionDuration)
        {
            if (animator == null || string.IsNullOrEmpty(stateName) || currentAnimationState == stateName)
            {
                return;
            }

            currentAnimationState = stateName;
            if (transitionDuration <= 0f)
            {
                animator.Play(stateName, 0, 0f);
                animator.Update(0f);
                return;
            }

            animator.CrossFade(stateName, transitionDuration);
        }

        private void ForceAnimationState(string stateName)
        {
            if (animator == null || string.IsNullOrEmpty(stateName))
            {
                return;
            }

            currentAnimationState = stateName;
            animator.Play(stateName, 0, 0f);
            animator.Update(0f);
        }
    }
}
