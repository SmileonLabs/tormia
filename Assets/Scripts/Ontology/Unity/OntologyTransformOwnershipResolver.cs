using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// The exclusive presentation owner of a Unity Transform. This state is
    /// ephemeral and is derived from ontology-selected adapters; it is never
    /// persisted as a world Fact.
    /// </summary>
    public enum OntologyTransformOwner
    {
        DurableProjection,
        RuntimePhysics,
        LocalCharacterController,
        AuthorityActorRuntime,
        Attachment,
        WorldEditor
    }

    /// <summary>
    /// Central transform-ownership policy. Gameplay meaning comes from the
    /// selected profiles and relations; this resolver only prevents two Unity
    /// presentation adapters from writing the same Transform.
    /// </summary>
    public static class OntologyTransformOwnershipResolver
    {
        public static OntologyTransformOwner Resolve(
            OntologyAuthorityEntityIdentity identity,
            OntologyAuthorityEntityIdentity localIdentity,
            bool worldEditorOwnsTransform = false)
        {
            if (worldEditorOwnsTransform)
            {
                return OntologyTransformOwner.WorldEditor;
            }

            if (identity == null)
            {
                return OntologyTransformOwner.DurableProjection;
            }

            var attachment = identity.GetComponent<OntologyAttachmentAdapter>();
            if (attachment != null && attachment.OwnsWorldTransform)
            {
                return OntologyTransformOwner.Attachment;
            }

            var impactReaction =
                identity.GetComponent<OntologyCharacterImpactAdapter>();
            if (impactReaction != null &&
                impactReaction.OwnsWorldTransform)
            {
                return OntologyTransformOwner.RuntimePhysics;
            }

            // A local avatar keeps ownership of its visible runtime pose even
            // while its ontology-selected motion lease is being rebound or has
            // been removed. The missing lease stops CharacterController.Move;
            // it must not hand the Transform to an older durable projection and
            // snap the player back to a checkpoint. Entry and respawn adapters
            // apply durable poses explicitly at their lifecycle boundaries.
            if (IsLocallyControlled(identity, localIdentity))
            {
                return OntologyTransformOwner.LocalCharacterController;
            }

            var motionDriver =
                identity.GetComponent<OntologyMotionDriverAdapter>();
            var authorityActor =
                identity.GetComponent<
                    OntologyAuthorityKinematicActorAdapter>();
            if (authorityActor != null &&
                motionDriver != null &&
                motionDriver.Allows(
                    OntologyMotionDriver.AuthorityKinematic) &&
                authorityActor.OwnsWorldTransform)
            {
                return OntologyTransformOwner.AuthorityActorRuntime;
            }

            var physicalBody = identity.GetComponent<OntologyPhysicalBodyAdapter>();
            if (physicalBody != null &&
                motionDriver != null &&
                motionDriver.Allows(OntologyMotionDriver.Rigidbody) &&
                physicalBody.PhysicalProfile != null &&
                physicalBody.PhysicalProfile.mobilityMode ==
                OntologyPhysicalMobilityMode.Dynamic &&
                physicalBody.TargetBody != null &&
                !physicalBody.TargetBody.isKinematic)
            {
                return OntologyTransformOwner.RuntimePhysics;
            }

            return OntologyTransformOwner.DurableProjection;
        }

        public static bool IsLocallyControlled(
            OntologyAuthorityEntityIdentity identity,
            OntologyAuthorityEntityIdentity localIdentity)
        {
            if (identity == null)
            {
                return false;
            }

            if (identity == localIdentity ||
                identity.GetComponent<OntologyInputSystemPlayerInput>() != null)
            {
                return true;
            }

            return localIdentity != null &&
                   identity.TryGetGuid(out var identityId) &&
                   localIdentity.TryGetGuid(out var localId) &&
                   identityId == localId;
        }

        public static bool ShouldApplyDurableProjection(
            OntologyTransformOwner owner) =>
            owner == OntologyTransformOwner.DurableProjection;

        public static bool ShouldSeedDynamicProjection(
            OntologyTransformOwner owner,
            bool hasAlreadySeeded) =>
            owner == OntologyTransformOwner.RuntimePhysics &&
            !hasAlreadySeeded;
    }
}
