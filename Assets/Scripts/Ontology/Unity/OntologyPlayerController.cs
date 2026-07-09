using ithappy.Creative_Characters_FREE.Controller;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    [RequireComponent(typeof(OntologyObject))]
    [RequireComponent(typeof(CharacterMover))]
    public sealed class OntologyPlayerController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private OntologyWorldBootstrap bootstrap;

        [Header("Movement Settings")]
        [SerializeField] private float defaultWalkSpeed = 1.5f;
        [SerializeField] private float defaultRunSpeed = 4.0f;
        [SerializeField] private float slowedWalkSpeed = 0.75f;
        [SerializeField] private float slowedRunSpeed = 2.0f;

        [Header("Ontology")]
        [SerializeField] private string actorId = "Player";

        private CharacterMover characterMover;
        private bool lastSlowed;
        private bool appliedInitialSpeed;

        private void Awake()
        {
            characterMover = GetComponent<CharacterMover>();
            if (bootstrap == null)
            {
                bootstrap = FindFirstObjectByType<OntologyWorldBootstrap>();
            }
        }

        private void Update()
        {
            if (bootstrap == null || bootstrap.World == null || characterMover == null)
            {
                return;
            }

            var isSlowed = bootstrap.World.HasFact(actorId, "movement_state", "Slowed");
            if (!appliedInitialSpeed || isSlowed != lastSlowed)
            {
                ApplyMoveSpeed(isSlowed);
                lastSlowed = isSlowed;
                appliedInitialSpeed = true;
            }
        }

        private void ApplyMoveSpeed(bool isSlowed)
        {
            var walkSpeed = isSlowed ? slowedWalkSpeed : defaultWalkSpeed;
            var runSpeed = isSlowed ? slowedRunSpeed : defaultRunSpeed;
            characterMover.SetMoveSpeeds(walkSpeed, runSpeed);
        }
    }
}
