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
            SynchronizeCombatPresentation(target);
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
            var existingAuthorityActor =
                target.GetComponent<
                    OntologyAuthorityKinematicActorAdapter>();
            var existingCharacterImpact =
                target.GetComponent<OntologyCharacterImpactAdapter>();
            var existingCollisionRole =
                target.GetComponent<OntologyCollisionRoleAdapter>();
            var existingMotionDriver =
                target.GetComponent<OntologyMotionDriverAdapter>();
            var existingCharacterSupportProbe =
                target.GetComponent<OntologyCharacterSupportProbe>();
            var existingCharacterMotion =
                target.GetComponent<OntologyCharacterMotionCoordinator>();

            if (profile == null)
            {
                var disabledDynamicCheckpoint =
                    target.GetComponent<OntologyDynamicTransformCheckpointAdapter>();
                if (existingAdapter != null)
                    existingAdapter.Configure(null, bootstrap);
                else if (existingPhysicalBody != null)
                    existingPhysicalBody.Configure(null);
                if (disabledDynamicCheckpoint != null)
                    disabledDynamicCheckpoint.enabled = false;
                if (existingWaterSensor != null)
                    existingWaterSensor.enabled = false;
                if (existingSupportSensor != null)
                    existingSupportSensor.enabled = false;
                if (existingAuthorityActor != null)
                    existingAuthorityActor.enabled = false;
                if (existingCharacterImpact != null)
                    existingCharacterImpact.Configure(null);
                if (existingCollisionRole != null)
                {
                    existingCollisionRole.ClearConfiguration();
                    existingCollisionRole.enabled = false;
                }
                if (existingMotionDriver != null)
                    existingMotionDriver.enabled = false;
                if (existingCharacterSupportProbe != null)
                    existingCharacterSupportProbe.enabled = false;
                if (existingCharacterMotion != null)
                {
                    existingCharacterMotion.Configure(null);
                    existingCharacterMotion.enabled = false;
                }
                return;
            }

            var motionDriver = existingMotionDriver ??
                target.AddComponent<OntologyMotionDriverAdapter>();
            motionDriver.enabled = true;
            motionDriver.Configure(profile.motionDriver);

            var physicalBody = existingPhysicalBody ??
                               target.AddComponent<OntologyPhysicalBodyAdapter>();
            physicalBody.Configure(profile);
            var collisionRole = existingCollisionRole ??
                target.AddComponent<OntologyCollisionRoleAdapter>();
            collisionRole.enabled = true;
            collisionRole.Configure(
                profile.collisionRole,
                bootstrap.PhysicalProfileDatabase);
            var playerInput =
                target.GetComponent<OntologyInputSystemPlayerInput>();
            if (playerInput != null)
            {
                var characterImpact = existingCharacterImpact ??
                    target.AddComponent<OntologyCharacterImpactAdapter>();
                characterImpact.enabled = true;
                characterImpact.Configure(profile);
            }

            var dynamicCheckpoint =
                target.GetComponent<OntologyDynamicTransformCheckpointAdapter>();
            if (profile.mobilityMode == OntologyPhysicalMobilityMode.Dynamic)
            {
                dynamicCheckpoint ??=
                    target.AddComponent<OntologyDynamicTransformCheckpointAdapter>();
                dynamicCheckpoint.enabled = true;
            }
            else if (dynamicCheckpoint != null)
            {
                dynamicCheckpoint.enabled = false;
            }

            if (profile.motionDriver ==
                OntologyMotionDriver.AuthorityKinematic)
            {
                var authorityActor = existingAuthorityActor ??
                    target.AddComponent<
                        OntologyAuthorityKinematicActorAdapter>();
                authorityActor.enabled = true;
                authorityActor.Configure();
            }
            else if (existingAuthorityActor != null)
            {
                existingAuthorityActor.enabled = false;
            }

            if (profile.motionDriver ==
                OntologyMotionDriver.LocalCharacterController)
            {
                if (existingSupportSensor != null)
                    existingSupportSensor.enabled = false;
                var supportProbe =
                    target.GetComponent<OntologyCharacterSupportProbe>() ??
                    target.AddComponent<OntologyCharacterSupportProbe>();
                supportProbe.ConfigureProbeLayers(
                    bootstrap.PhysicalProfileDatabase.BuildCollisionMask(
                        OntologyCollisionRole.WalkableSupport));
                supportProbe.enabled = true;
                var coordinator =
                    target.GetComponent<
                        OntologyCharacterMotionCoordinator>() ??
                    target.AddComponent<
                        OntologyCharacterMotionCoordinator>();
                coordinator.Configure(profile);
                coordinator.enabled = true;
            }
            else
            {
                if (existingCharacterSupportProbe != null)
                    existingCharacterSupportProbe.enabled = false;
                if (existingCharacterMotion != null)
                {
                    existingCharacterMotion.Configure(null);
                    existingCharacterMotion.enabled = false;
                }
                var supportSensor = existingSupportSensor ??
                    target.AddComponent<OntologySupportObservationSensor>();
                supportSensor.enabled = true;
                supportSensor.Configure(bootstrap);
            }

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

            actor = ResolvePresentationActor(bootstrap, actor);

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

        /// <summary>
        /// Resolves the locally controlled presentation actor without assuming
        /// that Actor is unique in the world. NPCs and monsters may also be
        /// Actors; the account avatar or the authored player-input role is the
        /// presentation boundary for local proximity observations.
        /// </summary>
        public static OntologyObject ResolvePresentationActor(
            OntologyWorldBootstrap bootstrap,
            OntologyObject preferredActor = null)
        {
            if (preferredActor != null)
            {
                return preferredActor;
            }

            var entryFlow =
                Object.FindAnyObjectByType<OntologyWorldAuthorityAccountEntryFlow>(
                    FindObjectsInactive.Include);
            var avatarIdentity = entryFlow == null
                ? null
                : entryFlow.AvatarIdentity;
            var accountAvatar = avatarIdentity == null
                ? null
                : avatarIdentity.GetComponent<OntologyObject>();
            if (accountAvatar != null)
            {
                return accountAvatar;
            }

            var playerInputs =
                Object.FindObjectsByType<OntologyInputSystemPlayerInput>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
            var inputActors = playerInputs
                .Where(value => value != null)
                .Select(value => value.GetComponent<OntologyObject>())
                .Where(value => value != null)
                .Distinct()
                .ToArray();
            if (inputActors.Length == 1)
            {
                return inputActors[0];
            }

            if (bootstrap != null &&
                bootstrap.EntityRegistry.TryGetSingleWithConcept(
                    OntologyConcepts.Actor,
                    out var soleActor))
            {
                return soleActor;
            }

            return null;
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

        public static void SynchronizeCombatPresentation(
            GameObject target)
        {
            if (target == null) return;
            var ontology = target.GetComponent<OntologyObject>();
            var existing =
                target.GetComponent<OntologyCombatTargetPresenter>();
            var isDamageable = ontology != null &&
                               ontology.Concepts.Any(concept =>
                                   string.Equals(
                                       concept,
                                       OntologyConcepts.Damageable,
                                       System.StringComparison.Ordinal));
            if (!isDamageable)
            {
                if (existing != null) existing.enabled = false;
                return;
            }

            var identity =
                target.GetComponent<OntologyAuthorityEntityIdentity>();
            if (identity == null)
            {
                if (existing != null) existing.enabled = false;
                return;
            }

            var presenter = existing ??
                            target.AddComponent<
                                OntologyCombatTargetPresenter>();
            presenter.enabled = true;
            presenter.Configure(
                identity,
                null,
                ResolveCanonicalFact(
                    ontology,
                    OntologyPredicates.HitAnimationIntent),
                ResolveCanonicalFact(
                    ontology,
                    OntologyPredicates.DeathAnimationIntent),
                ResolveCanonicalFact(
                    ontology,
                    OntologyPredicates.HitVfxIntent),
                null,
                target.GetComponentInChildren<Collider>(true));
            var authorityClient =
                Object.FindAnyObjectByType<OntologyWorldAuthorityClient>();
            if (authorityClient?.CurrentProjection != null)
                presenter.ApplyProjection(
                    authorityClient.CurrentProjection);
        }

        private static string ResolveCanonicalFact(
            OntologyObject ontology,
            string predicateId)
        {
            if (ontology == null) return string.Empty;
            var matches = ontology.Facts
                .Where(value =>
                    value != null &&
                    string.Equals(
                        value.predicate,
                        predicateId,
                        System.StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(value.obj))
                .Select(value => value.obj.Trim())
                .Distinct(System.StringComparer.Ordinal)
                .ToArray();
            return matches.Length == 1
                ? matches[0]
                : string.Empty;
        }
    }
}
