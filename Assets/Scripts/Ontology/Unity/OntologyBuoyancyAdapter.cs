using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Presents an ontology-inferred Floating state as buoyancy forces.
    /// Rigidbody and Collider ownership belongs to OntologyPhysicalBodyAdapter.
    /// </summary>
    [RequireComponent(typeof(OntologyObject))]
    [RequireComponent(typeof(OntologyWaterOccupancySensor))]
    [RequireComponent(typeof(OntologyPhysicalBodyAdapter))]
    public sealed class OntologyBuoyancyAdapter : MonoBehaviour
    {
        [SerializeField] private OntologyWorldBootstrap bootstrap;
        [SerializeField] private string physicalStatePredicate =
            OntologyPredicates.PhysicalState;
        [SerializeField] private string floatingState = OntologyObjects.Floating;
        [SerializeField, Min(0f)] private float maximumVerticalAcceleration = 40f;

        private OntologyObject ontologyObject;
        private OntologyWaterOccupancySensor waterSensor;
        private OntologyPhysicalBodyAdapter physicalBody;
        private readonly List<Vector3> samplePoints = new();

        public OntologyPhysicalProfile PhysicalProfile =>
            physicalBody == null ? null : physicalBody.PhysicalProfile;
        public Rigidbody TargetBody =>
            physicalBody == null ? null : physicalBody.TargetBody;
        public bool IsBuoyancyActive { get; private set; }

        private void Awake()
        {
            ResolveDependencies();
        }

        private void FixedUpdate()
        {
            ResolveDependencies();
            IsBuoyancyActive = ShouldApplyBuoyancy();
            if (!IsBuoyancyActive)
            {
                return;
            }

            if (!waterSensor.TryGetActiveWaterConditions(
                    out var surfaceHeight,
                    out var flowVelocity))
            {
                IsBuoyancyActive = false;
                return;
            }

            ApplyBuoyancy(surfaceHeight, flowVelocity);
        }

        private void OnDisable()
        {
            IsBuoyancyActive = false;
        }

        public void Configure(
            OntologyPhysicalProfile profile,
            OntologyWorldBootstrap targetBootstrap)
        {
            bootstrap = targetBootstrap;
            ResolveDependencies();
            physicalBody.Configure(profile);
            if (profile == null || !profile.supportsBuoyancy)
            {
                IsBuoyancyActive = false;
            }
        }

        public void Configure(OntologyWorldBootstrap targetBootstrap)
        {
            bootstrap = targetBootstrap;
            ResolveDependencies();
        }

        private bool ShouldApplyBuoyancy()
        {
            var profile = PhysicalProfile;
            var targetBody = TargetBody;
            if (profile == null || !profile.supportsBuoyancy ||
                targetBody == null || targetBody.isKinematic ||
                ontologyObject == null || waterSensor == null)
            {
                return false;
            }

            if (bootstrap == null)
            {
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            }

            return bootstrap != null &&
                   bootstrap.World != null &&
                   !string.IsNullOrWhiteSpace(profile.profileId) &&
                   bootstrap.World.HasFact(
                       ontologyObject.EntityId,
                       OntologyPredicates.PhysicalProfile,
                       profile.profileId) &&
                   bootstrap.World.HasFact(
                       profile.profileId,
                       OntologyPredicates.SupportsBehavior,
                       OntologyObjects.Buoyancy) &&
                   bootstrap.World.HasFact(
                       ontologyObject.EntityId,
                       physicalStatePredicate,
                       floatingState);
        }

        private void ApplyBuoyancy(float surfaceHeight, Vector3 flowVelocity)
        {
            var profile = PhysicalProfile;
            var targetBody = TargetBody;
            var observedWaterline = surfaceHeight + profile.surfaceOffset;
            var currentWaterline = transform.position.y;
            if (physicalBody.TryGetPhysicalBounds(out var physicalBounds))
            {
                currentWaterline = Mathf.Lerp(
                    physicalBounds.min.y,
                    physicalBounds.max.y,
                    Mathf.Clamp01(profile.submergedFraction));
            }
            var verticalError = observedWaterline - currentWaterline;
            var gravityCompensation = targetBody.useGravity
                ? Mathf.Max(0f, -Vector3.Dot(Physics.gravity, Vector3.up))
                : 0f;
            var verticalAcceleration =
                gravityCompensation +
                verticalError * Mathf.Max(0f, profile.buoyancyStrength) -
                targetBody.linearVelocity.y *
                Mathf.Max(0f, profile.submergedDamping);
            verticalAcceleration = Mathf.Clamp(
                verticalAcceleration,
                -Mathf.Max(0f, maximumVerticalAcceleration),
                Mathf.Max(0f, maximumVerticalAcceleration));

            BuildSamplePoints();
            var accelerationPerSample =
                Vector3.up * (verticalAcceleration / samplePoints.Count);
            foreach (var point in samplePoints)
            {
                targetBody.AddForceAtPosition(
                    accelerationPerSample,
                    point,
                    ForceMode.Acceleration);
            }

            var horizontalVelocity =
                Vector3.ProjectOnPlane(targetBody.linearVelocity, Vector3.up);
            var horizontalCorrection = flowVelocity - horizontalVelocity;
            targetBody.AddForce(
                horizontalCorrection * Mathf.Max(0f, profile.submergedDamping),
                ForceMode.Acceleration);

            var uprightAxis = Vector3.Cross(transform.up, Vector3.up);
            var uprightAcceleration =
                uprightAxis * Mathf.Max(0f, profile.uprightStability) -
                targetBody.angularVelocity *
                Mathf.Max(0f, profile.angularDamping);
            targetBody.AddTorque(uprightAcceleration, ForceMode.Acceleration);
        }

        private void BuildSamplePoints()
        {
            samplePoints.Clear();
            var profile = PhysicalProfile;
            var targetBody = TargetBody;
            var count = Mathf.Max(1, profile.buoyancySampleCount);
            if (!physicalBody.TryGetPhysicalBounds(out var bounds) || count == 1)
            {
                samplePoints.Add(targetBody.worldCenterOfMass);
                return;
            }

            var radiusX = Mathf.Max(0.01f, bounds.extents.x * 0.65f);
            var radiusZ = Mathf.Max(0.01f, bounds.extents.z * 0.65f);
            var y = bounds.center.y;
            for (var index = 0; index < count; index++)
            {
                var angle = Mathf.PI * 2f * index / count;
                samplePoints.Add(new Vector3(
                    bounds.center.x + Mathf.Cos(angle) * radiusX,
                    y,
                    bounds.center.z + Mathf.Sin(angle) * radiusZ));
            }
        }

        private void ResolveDependencies()
        {
            if (ontologyObject == null)
                ontologyObject = GetComponent<OntologyObject>();
            if (waterSensor == null)
                waterSensor = GetComponent<OntologyWaterOccupancySensor>();
            if (physicalBody == null)
                physicalBody = GetComponent<OntologyPhysicalBodyAdapter>();
            if (bootstrap == null)
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
        }
    }
}
