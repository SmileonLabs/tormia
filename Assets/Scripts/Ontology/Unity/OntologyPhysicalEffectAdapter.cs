using System.Linq;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Expresses inferred active_physical_effect facts through Rigidbody forces.
    /// It never decides compatibility and never activates an effect by itself.
    /// </summary>
    [RequireComponent(typeof(OntologyObject))]
    [RequireComponent(typeof(OntologyPhysicalBodyAdapter))]
    public sealed class OntologyPhysicalEffectAdapter : MonoBehaviour
    {
        [SerializeField] private OntologyWorldBootstrap bootstrap;

        private OntologyObject ontologyObject;
        private OntologyPhysicalBodyAdapter physicalBody;

        public void Configure(OntologyWorldBootstrap targetBootstrap)
        {
            bootstrap = targetBootstrap;
            ResolveDependencies();
        }

        private void Awake()
        {
            ResolveDependencies();
        }

        private void FixedUpdate()
        {
            ResolveDependencies();
            var body = physicalBody == null ? null : physicalBody.TargetBody;
            var database = bootstrap == null
                ? null
                : bootstrap.PhysicalEffectDatabase;
            if (body == null ||
                body.isKinematic ||
                database == null ||
                ontologyObject == null ||
                bootstrap.World == null)
            {
                return;
            }

            foreach (var effect in database.Effects.Where(value =>
                         value != null &&
                         !string.IsNullOrWhiteSpace(value.effectId) &&
                         bootstrap.World.HasFact(
                             ontologyObject.EntityId,
                             OntologyPredicates.ActivePhysicalEffect,
                             value.effectId)))
            {
                ApplyEffect(body, effect);
            }
        }

        private static void ApplyEffect(
            Rigidbody body,
            OntologyPhysicalEffectProfile effect)
        {
            switch (effect.presentation)
            {
                case OntologyPhysicalEffectPresentation.WindDrift:
                    var direction = effect.direction.sqrMagnitude > 0.0001f
                        ? effect.direction.normalized
                        : Vector3.right;
                    body.AddForce(
                        direction * Mathf.Max(0f, effect.strength),
                        ForceMode.Acceleration);
                    break;

                case OntologyPhysicalEffectPresentation.WaveRocking:
                    var phase =
                        Time.time * Mathf.Max(0f, effect.frequency) * Mathf.PI * 2f;
                    var rockingAxis = effect.direction.sqrMagnitude > 0.0001f
                        ? effect.direction.normalized
                        : Vector3.forward;
                    body.AddTorque(
                        rockingAxis *
                        (Mathf.Sin(phase) * Mathf.Max(0f, effect.strength)),
                        ForceMode.Acceleration);
                    break;
            }
        }

        private void ResolveDependencies()
        {
            ontologyObject ??= GetComponent<OntologyObject>();
            physicalBody ??= GetComponent<OntologyPhysicalBodyAdapter>();
            bootstrap ??= FindAnyObjectByType<OntologyWorldBootstrap>();
        }
    }
}
