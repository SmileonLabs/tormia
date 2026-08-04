using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Tormia.Ontology.Core;
using UnityEditor;
using UnityEngine;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyPlayerProductionContractTests
    {
        private const string SettingsPath =
            "Assets/Data/Ontology/Networking/WorldAuthoritySettings.asset";
        private const string RuleDatabasePath =
            "Assets/Data/Ontology/RuleDatabase.asset";
        private const string PlayerProfilePath =
            "Assets/Data/Ontology/Actors/PlayerProfile.asset";

        [Test]
        public void AvatarTriplesDeclareCompleteGroundLocomotionMeaning()
        {
            var facts =
                OntologyWorldAuthorityAccountEntryFlow
                    .CreateAvatarSemanticFacts(
                        AssetDatabase.LoadAssetAtPath<
                            OntologyActorProfile>(
                            PlayerProfilePath),
                        AssetDatabase.LoadAssetAtPath<
                            OntologyWorldAuthoritySettings>(
                            SettingsPath).defaultAvatarMovementSpeed);

            Assert.That(facts, Has.Length.EqualTo(37));
            AssertCanonical(
                facts,
                OntologyPredicates.LocomotionAction,
                OntologyActions.MoveAvatar);
            AssertCanonical(
                facts,
                OntologyPredicates.GrantsCapability,
                "Locomotion");
            AssertCanonical(
                facts,
                OntologyPredicates.JumpAction,
                OntologyActions.JumpAvatar);
            AssertCanonical(
                facts,
                OntologyPredicates.GrantsCapability,
                "Jump");
            AssertCanonical(
                facts,
                OntologyPredicates.PhysicalProfile,
                OntologyObjects.LocalCharacterController);
            AssertCanonical(
                facts,
                OntologyPredicates.CollisionRole,
                OntologyObjects.ActorBody);
            AssertCanonical(
                facts,
                OntologyPredicates.CollisionProxyShape,
                OntologyObjects.Capsule);
            AssertCanonical(
                facts,
                OntologyPredicates.IdleAnimationIntent,
                OntologyAnimationIntentIds.Idle);
            AssertCanonical(
                facts,
                OntologyPredicates.MoveAnimationIntent,
                OntologyAnimationIntentIds.Locomotion);
            AssertCanonical(
                facts,
                OntologyPredicates.HitAnimationIntent,
                OntologyAnimationIntentIds.HitReaction);
            AssertCanonical(
                facts,
                OntologyPredicates.DeathAnimationIntent,
                OntologyAnimationIntentIds.Death);
            Assert.That(
                facts,
                Has.Some.Matches<OntologyAuthorityInitialFact>(value =>
                    value != null &&
                    value.predicateId ==
                    OntologyPredicates.MovementSpeed &&
                    value.objectKind == "number" &&
                    value.objectValueJson == "5"));
            Assert.That(
                facts,
                Has.Some.Matches<OntologyAuthorityInitialFact>(value =>
                    value != null &&
                    value.predicateId ==
                    OntologyPredicates.GravityAcceleration &&
                    value.objectKind == "number" &&
                    value.objectValueJson == "-20"));
            Assert.That(
                facts,
                Has.Some.Matches<OntologyAuthorityInitialFact>(value =>
                    value != null &&
                    value.predicateId ==
                    OntologyPredicates.JumpTakeoffSpeed &&
                    value.objectKind == "number" &&
                    value.objectValueJson == "7"));
            Assert.That(
                facts,
                Has.Some.Matches<OntologyAuthorityInitialFact>(value =>
                    value != null &&
                    value.predicateId ==
                    OntologyPredicates.GroundStickVelocity &&
                    value.objectKind == "number" &&
                    value.objectValueJson == "-1"));
            Assert.That(
                facts,
                Has.Some.Matches<OntologyAuthorityInitialFact>(value =>
                    value != null &&
                    value.predicateId ==
                    OntologyPredicates.MaximumStepHeight &&
                    value.objectKind == "number" &&
                    value.objectValueJson == "0.3"));
            Assert.That(
                facts,
                Has.Some.Matches<OntologyAuthorityInitialFact>(value =>
                    value != null &&
                    value.predicateId ==
                    OntologyPredicates.GroundClearance &&
                    value.objectKind == "number" &&
                    value.objectValueJson == "0.03"));
            Assert.That(
                facts,
                Has.Some.Matches<OntologyAuthorityInitialFact>(value =>
                    value != null &&
                    value.predicateId ==
                    OntologyPredicates.CollisionRadius &&
                    value.objectKind == "number" &&
                    value.objectValueJson == "0.45"));
            Assert.That(
                facts,
                Has.Some.Matches<OntologyAuthorityInitialFact>(value =>
                    value != null &&
                    value.predicateId ==
                    OntologyPredicates.CollisionHeight &&
                    value.objectKind == "number" &&
                    value.objectValueJson == "2"));

            var proxyFacts =
                OntologyWorldAuthorityAccountEntryFlow
                    .CreateAvatarCollisionProxySemanticFacts(
                        AssetDatabase.LoadAssetAtPath<
                            OntologyActorProfile>(
                            PlayerProfilePath));
            Assert.That(proxyFacts, Has.Length.EqualTo(7));

            var hitPresentationFacts =
                OntologyWorldAuthorityAccountEntryFlow
                    .CreateAvatarHitPresentationSemanticFacts(
                        AssetDatabase.LoadAssetAtPath<
                            OntologyActorProfile>(
                            PlayerProfilePath));
            Assert.That(hitPresentationFacts, Has.Length.EqualTo(1));
            AssertCanonical(
                hitPresentationFacts,
                OntologyPredicates.HitAnimationIntent,
                OntologyAnimationIntentIds.HitReaction);

            var deathPresentationFacts =
                OntologyWorldAuthorityAccountEntryFlow
                    .CreateAvatarDeathPresentationSemanticFacts(
                        AssetDatabase.LoadAssetAtPath<
                            OntologyActorProfile>(
                            PlayerProfilePath));
            Assert.That(deathPresentationFacts, Has.Length.EqualTo(1));
            AssertCanonical(
                deathPresentationFacts,
                OntologyPredicates.DeathAnimationIntent,
                OntologyAnimationIntentIds.Death);
            Assert.That(
                OntologySemanticContracts.PlayerAvatarVersion,
                Is.EqualTo(12));
        }

        [Test]
        public void DevelopmentPackagePublishesLocomotionAndJumpContracts()
        {
            var settings =
                AssetDatabase.LoadAssetAtPath<
                    OntologyWorldAuthoritySettings>(
                    SettingsPath);
            var rules =
                AssetDatabase.LoadAssetAtPath<
                    OntologyRuleDatabase>(
                    RuleDatabasePath);

            Assert.That(settings, Is.Not.Null);
            Assert.That(rules, Is.Not.Null);
            Assert.That(
                settings.developmentPackageVersion,
                Is.EqualTo("4.0.0"));
            Assert.That(
                settings.developmentRules,
                Has.Some.Matches<
                    OntologyAuthorityDevelopmentRule>(value =>
                    value != null &&
                    value.ruleId ==
                    OntologyRuleBlocks.MovePlayerFromIntent &&
                    value.definitionVersion == 3));

            var action = settings.developmentActions.Single(value =>
                value != null &&
                value.actionId == OntologyActions.MoveAvatar);
            Assert.That(action.requiresTool, Is.False);
            Assert.That(
                action.predicateId,
                Is.EqualTo(OntologyPredicates.LocomotionIntent));
            StringAssert.Contains(
                OntologyRuleBlocks.MovePlayerFromIntent,
                action.structuredDefinitionJson);

            var rule = rules.Definitions.Single(value =>
                value != null &&
                value.id ==
                OntologyRuleBlocks.MovePlayerFromIntent);
            Assert.That(rule.effects, Is.Empty);
            Assert.That(
                rule.runtimePresentation.actorAnimationIntent,
                Is.EqualTo(OntologyAnimationIntentIds.Locomotion));
            Assert.That(
                rule.conditions,
                Has.Some.Matches<OntologyCondition>(value =>
                    value.predicate ==
                    OntologyPredicates.PhysicalProfile &&
                    value.obj ==
                    OntologyObjects.LocalCharacterController));
            Assert.That(
                rule.conditions,
                Has.Some.Matches<OntologyCondition>(value =>
                    value.predicate ==
                    OntologyPredicates.HasRuleBlock &&
                    value.obj ==
                    OntologyRuleBlocks.MovePlayerFromIntent));

            Assert.That(
                settings.developmentActions,
                Has.Some.Matches<OntologyAuthorityDevelopmentAction>(value =>
                    value != null &&
                    value.actionId == OntologyActions.JumpAvatar &&
                    value.predicateId == OntologyPredicates.JumpIntent &&
                    value.structuredDefinitionJson.Contains(
                        "\"requiresGroundedObservation\":true")));
            Assert.That(
                settings.developmentRules,
                Has.Some.Matches<OntologyAuthorityDevelopmentRule>(value =>
                    value != null &&
                    value.ruleId ==
                    OntologyRuleBlocks.JumpPlayerFromIntent &&
                    value.definitionVersion == 4));
            Assert.That(
                settings.developmentActions,
                Has.Some.Matches<OntologyAuthorityDevelopmentAction>(value =>
                    value != null &&
                    value.actionId == OntologyActions.JumpAvatar &&
                    value.definitionVersion == 3));
            var jumpRule = rules.Definitions.Single(value =>
                value != null &&
                value.id ==
                OntologyRuleBlocks.JumpPlayerFromIntent);
            Assert.That(jumpRule.catalogVersion, Is.EqualTo(4));
            Assert.That(jumpRule.effects, Is.Empty);
            Assert.That(
                jumpRule.runtimePresentation.actorAnimationIntent,
                Is.EqualTo(OntologyAnimationIntentIds.JumpStart));
            Assert.That(
                jumpRule.conditions,
                Has.Some.Matches<OntologyCondition>(value =>
                    value.predicate ==
                    OntologyPredicates.HasRuleBlock &&
                    value.obj ==
                    OntologyRuleBlocks.JumpPlayerFromIntent));
        }

        [Test]
        public void RemovingLocomotionActionFactRemovesUnityIntentRoute()
        {
            var avatarId = Guid.NewGuid();
            var withAction = new OntologyAuthorityWorldProjection
            {
                facts = new[]
                {
                    new OntologyAuthorityFactProjection
                    {
                        subjectEntityId = avatarId.ToString("D"),
                        predicateId =
                            OntologyPredicates.LocomotionAction,
                        objectKind = "canonical",
                        objectCanonicalId =
                            OntologyActions.MoveAvatar
                    }
                }
            };

            Assert.That(
                OntologyWorldAuthorityPlayerIntentSender
                    .TryResolveLocomotionActionId(
                        withAction,
                        avatarId,
                        out var actionId),
                Is.True);
            Assert.That(
                actionId,
                Is.EqualTo(OntologyActions.MoveAvatar));

            withAction.facts =
                Array.Empty<OntologyAuthorityFactProjection>();
            Assert.That(
                OntologyWorldAuthorityPlayerIntentSender
                    .TryResolveLocomotionActionId(
                        withAction,
                        avatarId,
                        out _),
                Is.False);
        }

        [Test]
        public void UnrelatedMonsterRevisionPreservesApprovedLocomotionContract()
        {
            var avatarId = Guid.NewGuid();
            var monsterId = Guid.NewGuid();
            var before = CreateLocomotionProjection(
                avatarId,
                monsterId,
                "5",
                "10");
            var afterDamage = CreateLocomotionProjection(
                avatarId,
                monsterId,
                "5",
                "7");
            afterDamage.revision = before.revision + 1;

            Assert.That(
                OntologyWorldAuthorityPlayerIntentSender
                    .TryCreateLocomotionContractFingerprint(
                        before,
                        avatarId,
                        out var beforeFingerprint),
                Is.True);
            Assert.That(
                OntologyWorldAuthorityPlayerIntentSender
                    .TryCreateLocomotionContractFingerprint(
                        afterDamage,
                        avatarId,
                        out var afterFingerprint),
                Is.True);
            Assert.That(afterFingerprint, Is.EqualTo(beforeFingerprint));
            Assert.That(
                OntologyWorldAuthorityPlayerIntentSender
                    .ShouldInvalidateLocomotionApproval(
                        true,
                        beforeFingerprint,
                        true,
                        afterFingerprint),
                Is.False,
                "An unrelated monster damage revision must not interrupt " +
                "already-approved player locomotion.");
        }

        [Test]
        public void UnrelatedAvatarCombatStatePreservesApprovedLocomotionContract()
        {
            var avatarId = Guid.NewGuid();
            var monsterId = Guid.NewGuid();
            var before = CreateLocomotionProjection(
                avatarId,
                monsterId,
                "5",
                "10");
            var duringCombat = CreateLocomotionProjection(
                avatarId,
                monsterId,
                "5",
                "10");
            duringCombat.facts = duringCombat.facts
                .Concat(new[]
                {
                    new OntologyAuthorityFactProjection
                    {
                        subjectEntityId = avatarId.ToString("D"),
                        predicateId = OntologyPredicates.EquippedItem,
                        objectKind = "entity",
                        objectEntityId = Guid.NewGuid().ToString("D")
                    },
                    new OntologyAuthorityFactProjection
                    {
                        subjectEntityId = avatarId.ToString("D"),
                        predicateId =
                            OntologyPredicates.PrimaryAttackIntent,
                        objectKind = "entity",
                        objectEntityId = monsterId.ToString("D")
                    }
                })
                .ToArray();
            duringCombat.ruleBindings = duringCombat.ruleBindings
                .Concat(new[]
                {
                    new OntologyAuthorityRuleBindingProjection
                    {
                        bindingId = "player-respawn",
                        targetEntityId = avatarId.ToString("D"),
                        ruleId =
                            OntologyRuleBlocks.RespawnPlayerOnDeath,
                        ruleVersion = 2,
                        enabled = true,
                        parameterValuesJson = "{}"
                    }
                })
                .ToArray();

            Assert.That(
                OntologyWorldAuthorityPlayerIntentSender
                    .TryCreateLocomotionContractFingerprint(
                        before,
                        avatarId,
                        out var beforeFingerprint),
                Is.True);
            Assert.That(
                OntologyWorldAuthorityPlayerIntentSender
                    .TryCreateLocomotionContractFingerprint(
                        duringCombat,
                        avatarId,
                        out var combatFingerprint),
                Is.True);
            Assert.That(
                combatFingerprint,
                Is.EqualTo(beforeFingerprint),
                "Combat/equipment semantics must not revoke the independent " +
                "locomotion contract.");
        }

        [Test]
        public void AuthoredGravityContinuesSettlingWithoutNewMovementIntent()
        {
            Assert.That(
                OntologyInputSystemPlayerInput.IntegrateVerticalVelocity(
                    false,
                    0f,
                    -20f,
                    0.1f,
                    -1f),
                Is.EqualTo(-2f).Within(0.0001f));
            Assert.That(
                OntologyInputSystemPlayerInput.IntegrateVerticalVelocity(
                    true,
                    -5f,
                    -20f,
                    0.1f,
                    -1f),
                Is.EqualTo(-1f).Within(0.0001f),
                "Grounding continuity is Physical Meaning; it is not a new " +
                "locomotion intent.");
        }

        [Test]
        public void CollisionFlagsSettleVerticalVelocityWithoutSecondMove()
        {
            Assert.That(
                OntologyInputSystemPlayerInput
                    .ResolveVerticalVelocityAfterMove(
                        UnityEngine.CollisionFlags.Above,
                        6f,
                        -1f),
                Is.Zero);
            Assert.That(
                OntologyInputSystemPlayerInput
                    .ResolveVerticalVelocityAfterMove(
                        UnityEngine.CollisionFlags.Below,
                        -4f,
                        -1f),
                Is.EqualTo(-1f));
        }

        [Test]
        public void ApprovedControllerImpulseDecaysWithoutOwningSecondTransform()
        {
            var result =
                OntologyInputSystemPlayerInput
                    .DampExternalImpulseVelocity(
                        new UnityEngine.Vector3(5f, 0f, 0f),
                        10f,
                        0.1f);
            Assert.That(result.x, Is.EqualTo(4f).Within(0.0001f));
            Assert.That(result.y, Is.Zero);
            Assert.That(result.z, Is.Zero);
        }

        [Test]
        public void RemovingJumpActionFactRemovesUnityJumpRoute()
        {
            var avatarId = Guid.NewGuid();
            var projection = new OntologyAuthorityWorldProjection
            {
                facts = new[]
                {
                    new OntologyAuthorityFactProjection
                    {
                        subjectEntityId = avatarId.ToString("D"),
                        predicateId = OntologyPredicates.JumpAction,
                        objectKind = "canonical",
                        objectCanonicalId = OntologyActions.JumpAvatar
                    }
                }
            };

            Assert.That(
                OntologyWorldAuthorityPlayerIntentSender
                    .TryResolveCanonicalActionId(
                        projection,
                        avatarId,
                        OntologyPredicates.JumpAction,
                        out var actionId),
                Is.True);
            Assert.That(actionId, Is.EqualTo(OntologyActions.JumpAvatar));

            projection.facts =
                Array.Empty<OntologyAuthorityFactProjection>();
            Assert.That(
                OntologyWorldAuthorityPlayerIntentSender
                    .TryResolveCanonicalActionId(
                        projection,
                        avatarId,
                        OntologyPredicates.JumpAction,
                        out _),
                Is.False);
        }

        [Test]
        public void RuntimeRevalidationWaitsForInitialEntryPreparation()
        {
            Assert.That(
                OntologyWorldAuthorityPlayerIntentSender
                    .ShouldSubmitRuntimeIntent(
                        false,
                        false,
                        false,
                        false,
                        10f,
                        0f),
                Is.False,
                "Automatic lease recovery must not race the explicit " +
                "zero-motion world-entry handshake.");
            Assert.That(
                OntologyWorldAuthorityPlayerIntentSender
                    .ShouldSubmitRuntimeIntent(
                        true,
                        false,
                        false,
                        false,
                        10f,
                        0f),
                Is.True,
                "After initial entry preparation, a revoked lease should " +
                "revalidate without waiting for a key press.");
            Assert.That(
                OntologyWorldAuthorityPlayerIntentSender
                    .ShouldSubmitRuntimeIntent(
                        true,
                        false,
                        false,
                        false,
                        0f,
                        1f),
                Is.False,
                "Rejected revalidation remains rate-limited.");

            Assert.That(
                OntologyWorldAuthorityPlayerIntentSender
                    .ShouldSubmitRuntimeIntent(
                        true,
                        true,
                        false,
                        false,
                        1f,
                        1f),
                Is.True,
                "Standing players renew the ephemeral locomotion lease so " +
                "Authority contact uses a current position.");
        }

        [Test]
        public void IdenticalActiveHeartbeatDoesNotInvalidatePredictionCorrection()
        {
            Assert.That(
                OntologyWorldAuthorityPlayerIntentSender
                    .HasPredictionAffectingSampleChange(
                        true, true,
                        Vector2.right, 5f, true, new Vector2(4f, 8f), 0.1f,
                        Vector2.right, 5f, true, new Vector2(4f, 8f), 0.1f),
                Is.False,
                "An identical active lease heartbeat must not clear a pending " +
                "Authority reconciliation residual.");
            Assert.That(
                OntologyWorldAuthorityPlayerIntentSender
                    .HasPredictionAffectingSampleChange(
                        true, true,
                        Vector2.up, 4f, true, new Vector2(4f, 8f), 0.1f,
                        Vector2.right, 5f, true, new Vector2(4f, 8f), 0.1f),
                Is.False,
                "NavigateTo direction samples are presentation detail; the " +
                "unchanged canonical destination must keep one fence.");
            Assert.That(
                OntologyWorldAuthorityPlayerIntentSender
                    .HasPredictionAffectingSampleChange(
                        true, false,
                        Vector2.right, 5f, false, Vector2.zero, 0f,
                        Vector2.zero, 0f, false, Vector2.zero, 0f),
                Is.True,
                "Active movement changes the prediction fence.");
            Assert.That(
                OntologyWorldAuthorityPlayerIntentSender
                    .HasPredictionAffectingSampleChange(
                        false, true,
                        Vector2.zero, 0f, false, Vector2.zero, 0f,
                        Vector2.right, 5f, false, Vector2.zero, 0f),
                Is.True,
                "The first stop after movement changes the prediction fence.");
        }

        [Test]
        public void StopEdgeBypassesPeriodicIntentGate()
        {
            Assert.That(
                OntologyWorldAuthorityPlayerIntentSender
                    .ShouldSubmitRuntimeIntent(
                        true,
                        true,
                        false,
                        true,
                        0f,
                        10f),
                Is.True);
        }

        [Test]
        public void ChangedOrRemovedPlayerLocomotionContractRequiresReapproval()
        {
            var avatarId = Guid.NewGuid();
            var monsterId = Guid.NewGuid();
            var before = CreateLocomotionProjection(
                avatarId,
                monsterId,
                "5",
                "10");
            var changed = CreateLocomotionProjection(
                avatarId,
                monsterId,
                "3",
                "10");

            Assert.That(
                OntologyWorldAuthorityPlayerIntentSender
                    .TryCreateLocomotionContractFingerprint(
                        before,
                        avatarId,
                        out var beforeFingerprint),
                Is.True);
            Assert.That(
                OntologyWorldAuthorityPlayerIntentSender
                    .TryCreateLocomotionContractFingerprint(
                        changed,
                        avatarId,
                        out var changedFingerprint),
                Is.True);
            Assert.That(
                OntologyWorldAuthorityPlayerIntentSender
                    .ShouldInvalidateLocomotionApproval(
                        true,
                        beforeFingerprint,
                        true,
                        changedFingerprint),
                Is.True);

            changed.facts = changed.facts
                .Where(value =>
                    value.subjectEntityId != avatarId.ToString("D") ||
                    value.predicateId !=
                    OntologyPredicates.LocomotionAction)
                .ToArray();
            Assert.That(
                OntologyWorldAuthorityPlayerIntentSender
                    .TryCreateLocomotionContractFingerprint(
                        changed,
                        avatarId,
                        out var removedFingerprint),
                Is.False);
            Assert.That(
                OntologyWorldAuthorityPlayerIntentSender
                    .ShouldInvalidateLocomotionApproval(
                        true,
                        beforeFingerprint,
                        false,
                        removedFingerprint),
                Is.True,
                "Removing the authored locomotion route must stop predicted " +
                "movement until Authority accepts a complete contract again.");
        }

        [Test]
        public void RuntimeFoundationDoesNotReauthorRemovedPlayerSemantics()
        {
            var source = File.ReadAllText(
                "Assets/Scripts/Ontology/Unity/Networking/" +
                "OntologyWorldAuthorityAccountEntryFlow.cs");

            StringAssert.DoesNotContain(
                "foreach (var semanticFact in CreateAvatarSemanticFacts())",
                source);
            StringAssert.Contains(
                "EnsureAvatarSemanticContractRoutine",
                source);
        }

        [Test]
        public void AuthorityPlayerMotionSelectsLatestPublishedActionVersion()
        {
            var source = File.ReadAllText(
                "server/Tormia.WorldAuthority/Program.cs");

            StringAssert.Contains(
                "ORDER BY d.definition_version DESC\n" +
                "                LIMIT 1",
                source,
                "Historical immutable locomotion definitions must not make " +
                "the active player configuration ambiguous.");
        }

        [Test]
        public void SemanticContractMigrationUsesHighestAuthoredVersion()
        {
            var avatarId = Guid.NewGuid();
            var projection = new OntologyAuthorityWorldProjection
            {
                facts = new[]
                {
                    new OntologyAuthorityFactProjection
                    {
                        subjectEntityId = avatarId.ToString("D"),
                        predicateId =
                            OntologyPredicates.SemanticContractVersion,
                        objectKind = "number",
                        objectValueJson = "1"
                    },
                    new OntologyAuthorityFactProjection
                    {
                        subjectEntityId = avatarId.ToString("D"),
                        predicateId =
                            OntologyPredicates.SemanticContractVersion,
                        objectKind = "number",
                        objectValueJson = "2"
                    }
                }
            };

            Assert.That(
                OntologyWorldAuthorityAccountEntryFlow
                    .ResolveSemanticContractVersion(
                        projection,
                        avatarId),
                Is.EqualTo(2));
        }

        private static void AssertCanonical(
            OntologyAuthorityInitialFact[] facts,
            string predicate,
            string value)
        {
            Assert.That(
                facts,
                Has.Some.Matches<OntologyAuthorityInitialFact>(fact =>
                    fact != null &&
                    fact.predicateId == predicate &&
                    fact.objectKind == "canonical" &&
                    fact.objectCanonicalId == value));
        }

        private static OntologyAuthorityWorldProjection
            CreateLocomotionProjection(
                Guid avatarId,
                Guid monsterId,
                string movementSpeed,
                string monsterHealth)
        {
            return new OntologyAuthorityWorldProjection
            {
                worldId = "11111111-1111-1111-1111-111111111111",
                scopeZoneKey = "zone-main",
                revision = 12,
                facts = new[]
                {
                    new OntologyAuthorityFactProjection
                    {
                        subjectEntityId = avatarId.ToString("D"),
                        predicateId =
                            OntologyPredicates.LocomotionAction,
                        objectKind = "canonical",
                        objectCanonicalId =
                            OntologyActions.MoveAvatar
                    },
                    new OntologyAuthorityFactProjection
                    {
                        subjectEntityId = avatarId.ToString("D"),
                        predicateId =
                            OntologyPredicates.MovementSpeed,
                        objectKind = "number",
                        objectValueJson = movementSpeed
                    },
                    new OntologyAuthorityFactProjection
                    {
                        subjectEntityId = monsterId.ToString("D"),
                        predicateId =
                            OntologyPredicates.CurrentHealth,
                        objectKind = "number",
                        objectValueJson = monsterHealth
                    }
                },
                ruleBindings = new[]
                {
                    new OntologyAuthorityRuleBindingProjection
                    {
                        bindingId = "player-move",
                        targetEntityId = avatarId.ToString("D"),
                        ruleId =
                            OntologyRuleBlocks.MovePlayerFromIntent,
                        ruleVersion = 1,
                        enabled = true,
                        parameterValuesJson = "{}"
                    }
                },
                actions = new[]
                {
                    new OntologyAuthorityActionDefinitionProjection
                    {
                        packageId = "social_village",
                        packageVersion = "3.3.0",
                        actionId = OntologyActions.MoveAvatar,
                        definitionVersion = 1,
                        actorAnimationIntent =
                            OntologyAnimationIntentIds.Locomotion
                    }
                }
            };
        }
    }
}
