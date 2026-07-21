using System.Linq;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Reconciles semantic facts with Unity presentation adapters. UI code edits ontology
    /// data only; this service resolves profile ids and updates the physical presentation.
    /// </summary>
    public static class OntologySemanticAdapterSynchronizer
    {
        public static OntologyPhysicalProfile ResolvePhysicalProfile(
            OntologyObject ontology,
            OntologyPhysicalProfileDatabase database)
        {
            if (ontology == null || database == null) return null;
            var profileId = ontology.Facts
                .Where(value =>
                    value != null &&
                    value.predicate == OntologyPredicates.PhysicalProfile)
                .Select(value => value.obj)
                .LastOrDefault(value => !string.IsNullOrWhiteSpace(value));
            return database.Find(profileId);
        }

        public static OntologyAttachmentProfile ResolveAttachmentProfile(
            OntologyObject ontology,
            OntologyAttachmentProfileDatabase database)
        {
            if (ontology == null || database == null) return null;
            var profileId = ontology.Facts
                .Where(value =>
                    value != null &&
                    value.predicate == OntologyPredicates.AttachmentProfile)
                .Select(value => value.obj)
                .LastOrDefault(value => !string.IsNullOrWhiteSpace(value));
            return database.Find(profileId);
        }

        public static void SynchronizeAll(
            GameObject target,
            OntologyWorldBootstrap bootstrap,
            OntologyObject actor = null)
        {
            SynchronizePhysical(target, bootstrap);
            SynchronizePhysicalEffects(target, bootstrap);
            SynchronizeAttachment(target, bootstrap, actor);
        }

        public static void SynchronizePhysical(
            GameObject target,
            OntologyWorldBootstrap bootstrap)
        {
            if (target == null || bootstrap == null) return;
            var ontology = target.GetComponent<OntologyObject>();
            var profile = ResolvePhysicalProfile(
                ontology,
                bootstrap.PhysicalProfileDatabase);
            var existingPhysicalBody =
                target.GetComponent<OntologyPhysicalBodyAdapter>();
            var existingAdapter = target.GetComponent<OntologyBuoyancyAdapter>();
            var existingWaterSensor =
                target.GetComponent<OntologyWaterOccupancySensor>();
            var existingSupportSensor =
                target.GetComponent<OntologySupportObservationSensor>();

            if (profile == null)
            {
                if (existingAdapter != null)
                    existingAdapter.Configure(null, bootstrap);
                else if (existingPhysicalBody != null)
                    existingPhysicalBody.Configure(null);
                if (existingWaterSensor != null)
                    existingWaterSensor.enabled = false;
                if (existingSupportSensor != null)
                    existingSupportSensor.enabled = false;
                return;
            }

            var physicalBody = existingPhysicalBody ??
                               target.AddComponent<OntologyPhysicalBodyAdapter>();
            physicalBody.Configure(profile);

            var supportSensor = existingSupportSensor ??
                                target.AddComponent<OntologySupportObservationSensor>();
            supportSensor.enabled = true;
            supportSensor.Configure(bootstrap);

            if (!profile.supportsBuoyancy)
            {
                if (existingAdapter != null)
                {
                    existingAdapter.enabled = false;
                }
                if (existingWaterSensor != null)
                {
                    existingWaterSensor.enabled = false;
                }
                return;
            }

            var waterSensor = existingWaterSensor ??
                              target.AddComponent<OntologyWaterOccupancySensor>();
            waterSensor.enabled = true;
            waterSensor.Configure(bootstrap);
            var buoyancy = existingAdapter ??
                           target.AddComponent<OntologyBuoyancyAdapter>();
            buoyancy.enabled = true;
            buoyancy.Configure(bootstrap);
        }

        public static void SynchronizeAttachment(
            GameObject target,
            OntologyWorldBootstrap bootstrap,
            OntologyObject actor = null)
        {
            if (target == null || bootstrap == null) return;
            var ontology = target.GetComponent<OntologyObject>();
            var profile = ResolveAttachmentProfile(
                ontology,
                bootstrap.AttachmentProfileDatabase);
            var proximity =
                target.GetComponent<OntologyProximityObservationSensor>();
            var attachment = target.GetComponent<OntologyAttachmentAdapter>();

            if (profile == null)
            {
                if (proximity != null) proximity.enabled = false;
                if (attachment != null)
                {
                    attachment.Configure(null, bootstrap, actor);
                    attachment.enabled = false;
                }
                return;
            }

            if (actor == null)
            {
                bootstrap.EntityRegistry.TryGetSingleWithConcept(
                    OntologyConcepts.Actor,
                    out actor);
            }

            proximity ??= target.AddComponent<OntologyProximityObservationSensor>();
            proximity.enabled = true;
            proximity.Configure(
                bootstrap,
                actor,
                profile.autoEquipDistance,
                profile.proximityExitPadding,
                profile.proximityMeasurementMode,
                profile.proximityAnchorPath);

            attachment ??= target.AddComponent<OntologyAttachmentAdapter>();
            attachment.enabled = true;
            attachment.Configure(profile, bootstrap, actor);
        }

        public static void SynchronizePhysicalEffects(
            GameObject target,
            OntologyWorldBootstrap bootstrap)
        {
            if (target == null || bootstrap == null) return;
            var ontology = target.GetComponent<OntologyObject>();
            var existing = target.GetComponent<OntologyPhysicalEffectAdapter>();
            var hasAuthoredEffects = ontology != null &&
                                     ontology.Facts.Any(value =>
                                         value != null &&
                                         value.predicate ==
                                         OntologyPredicates.HasPhysicalEffect &&
                                         !string.IsNullOrWhiteSpace(value.obj));
            if (!hasAuthoredEffects)
            {
                if (existing != null) existing.enabled = false;
                return;
            }

            var adapter = existing ??
                          target.AddComponent<OntologyPhysicalEffectAdapter>();
            adapter.enabled = true;
            adapter.Configure(bootstrap);
        }
    }
}
