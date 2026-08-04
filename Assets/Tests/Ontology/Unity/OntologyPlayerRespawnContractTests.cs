using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;

namespace Tormia.Ontology.Tests.Unity
{
    public sealed class OntologyPlayerRespawnContractTests
    {
        private const string SettingsPath =
            "Assets/Data/Ontology/Networking/WorldAuthoritySettings.asset";
        private const string RuleDatabasePath =
            "Assets/Data/Ontology/RuleDatabase.asset";
        private const string PlayerProfilePath =
            "Assets/Data/Ontology/Actors/PlayerProfile.asset";

        [Test]
        public void AvatarSemanticFactsDeclareDataOwnedRespawnAction()
        {
            var facts =
                Tormia.Ontology.Core
                    .OntologyWorldAuthorityAccountEntryFlow
                    .CreateAvatarSemanticFacts(
                        AssetDatabase.LoadAssetAtPath<
                            Tormia.Ontology.Core.OntologyActorProfile>(
                            PlayerProfilePath),
                        AssetDatabase.LoadAssetAtPath<
                            Tormia.Ontology.Core
                                .OntologyWorldAuthoritySettings>(
                            SettingsPath).defaultAvatarMovementSpeed);

            Assert.That(facts, Has.Length.EqualTo(23));
            Assert.That(
                Array.Exists(
                    facts,
                    value =>
                        value.predicateId ==
                        Tormia.Ontology.Core
                            .OntologyPredicates.RespawnAction &&
                        value.objectKind == "canonical" &&
                        value.objectCanonicalId ==
                        Tormia.Ontology.Core
                            .OntologyActions.RespawnAvatar),
                Is.True);
        }

        [Test]
        public void DevelopmentPackagePublishesRespawnActionAndRule()
        {
            var settings =
                AssetDatabase.LoadAssetAtPath<
                    Tormia.Ontology.Core
                        .OntologyWorldAuthoritySettings>(
                    SettingsPath);
            var rules =
                AssetDatabase.LoadAssetAtPath<
                    Tormia.Ontology.Core.OntologyRuleDatabase>(
                    RuleDatabasePath);

            Assert.That(settings, Is.Not.Null);
            Assert.That(rules, Is.Not.Null);
            Assert.That(
                settings.developmentPackageVersion,
                Is.EqualTo("3.6.0"));
            Assert.That(
                settings.developmentRules.Any(value =>
                    value != null &&
                    value.ruleId ==
                    Tormia.Ontology.Core.OntologyRuleBlocks
                        .RespawnPlayerOnDeath &&
                    value.definitionVersion == 2),
                Is.True);

            var action = settings.developmentActions.Single(value =>
                value != null &&
                value.actionId ==
                Tormia.Ontology.Core
                    .OntologyActions.RespawnAvatar);
            Assert.That(action.definitionVersion, Is.EqualTo(1));
            Assert.That(
                action.predicateId,
                Is.EqualTo(
                    Tormia.Ontology.Core
                        .OntologyPredicates.RespawnIntent));
            StringAssert.Contains(
                Tormia.Ontology.Core
                    .OntologyRuleBlocks.RespawnPlayerOnDeath,
                action.structuredDefinitionJson);

            var rule = rules.Definitions.Single(value =>
                value != null &&
                value.id ==
                Tormia.Ontology.Core
                    .OntologyRuleBlocks.RespawnPlayerOnDeath);
            Assert.That(
                rule.conditions.Any(value =>
                    value.predicate ==
                    Tormia.Ontology.Core
                        .OntologyPredicates.IsAlive &&
                    value.obj == bool.FalseString),
                Is.True);
            Assert.That(
                rule.effects.Any(value =>
                    value.kind ==
                    Tormia.Ontology.Core
                        .OntologyEffectKind.AdjustNumberFact &&
                    value.predicate ==
                    Tormia.Ontology.Core
                        .OntologyPredicates.CurrentHealth &&
                    value.valueFrom != null &&
                    value.valueFrom.predicate ==
                    Tormia.Ontology.Core
                        .OntologyPredicates.MaximumHealth),
                Is.True);
            Assert.That(
                rule.effects.Any(value =>
                    value.kind ==
                    Tormia.Ontology.Core
                        .OntologyEffectKind.SetFact &&
                    value.predicate ==
                    Tormia.Ontology.Core
                        .OntologyPredicates.IsAlive &&
                    value.obj == bool.TrueString),
                Is.True);
        }

        [Test]
        public void ConflictingAliveProjectionDoesNotTriggerRespawn()
        {
            var avatarId = Guid.NewGuid();
            var projection =
                new Tormia.Ontology.Core
                    .OntologyAuthorityWorldProjection
                {
                    facts = new[]
                    {
                        BooleanFact(avatarId, false),
                        BooleanFact(avatarId, true)
                    }
                };

            Assert.That(
                Tormia.Ontology.Core
                    .OntologyAuthorityRespawnController
                    .TryResolveAliveState(
                        projection,
                        avatarId,
                        out _),
                Is.False);
        }

        [Test]
        public void DeadAvatarRequiresAuthorityRespawnBeforeWorldEntry()
        {
            var avatarId = Guid.NewGuid();
            var projection =
                new Tormia.Ontology.Core
                    .OntologyAuthorityWorldProjection
                {
                    facts = new[]
                    {
                        BooleanFact(avatarId, false)
                    }
                };

            Assert.That(
                Tormia.Ontology.Core
                    .OntologyAuthorityRespawnController
                    .TryResolveEntryRespawnRequirement(
                        projection,
                        avatarId,
                        out var requiresRespawn),
                Is.True);
            Assert.That(requiresRespawn, Is.True);
        }

        [Test]
        public void WorldEntryRestoresLifeBeforeLocomotionHandshake()
        {
            const string accountEntryFlowPath =
                "Assets/Scripts/Ontology/Unity/Networking/" +
                "OntologyWorldAuthorityAccountEntryFlow.cs";
            var source = File.ReadAllText(accountEntryFlowPath);
            var lifePreparationIndex = source.IndexOf(
                "PrepareAliveForWorldEntryRoutine",
                StringComparison.Ordinal);
            var locomotionPreparationIndex = source.IndexOf(
                "PrepareLocomotionPresentationRoutine",
                StringComparison.Ordinal);

            Assert.That(lifePreparationIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(
                locomotionPreparationIndex,
                Is.GreaterThan(lifePreparationIndex));
        }

        [Test]
        public void EntryLifePreparationUsesEphemeralRuntimeBeforeAdmission()
        {
            const string respawnControllerPath =
                "Assets/Scripts/Ontology/Unity/Networking/" +
                "OntologyAuthorityRespawnController.cs";
            var source = File.ReadAllText(respawnControllerPath);
            var methodStart = source.IndexOf(
                "public IEnumerator PrepareAliveForWorldEntryRoutine",
                StringComparison.Ordinal);
            var nextMethod = source.IndexOf(
                "private void HandleProjectionReceived",
                methodStart,
                StringComparison.Ordinal);
            Assert.That(methodStart, Is.GreaterThanOrEqualTo(0));
            Assert.That(nextMethod, Is.GreaterThan(methodStart));
            var methodSource = source.Substring(
                methodStart,
                nextMethod - methodStart);

            Assert.That(
                methodSource,
                Does.Contain("IsPlayerRuntimeActiveFor"));
            Assert.That(
                methodSource,
                Does.Not.Contain("IsWorldRuntimeReady"));
        }

        [Test]
        public void EntryLocomotionHandshakeUsesEphemeralRuntimeBeforeAdmission()
        {
            const string intentSenderPath =
                "Assets/Scripts/Ontology/Unity/Networking/" +
                "OntologyWorldAuthorityPlayerIntentSender.cs";
            var source = File.ReadAllText(intentSenderPath);
            var methodStart = source.IndexOf(
                "public IEnumerator PrepareLocomotionPresentationRoutine",
                StringComparison.Ordinal);
            var nextMethod = source.IndexOf(
                "private void Awake()",
                methodStart,
                StringComparison.Ordinal);
            Assert.That(methodStart, Is.GreaterThanOrEqualTo(0));
            Assert.That(nextMethod, Is.GreaterThan(methodStart));
            var methodSource = source.Substring(
                methodStart,
                nextMethod - methodStart);

            Assert.That(
                methodSource,
                Does.Contain("IsPlayerRuntimeActiveFor"));
            Assert.That(
                methodSource,
                Does.Not.Contain("IsWorldRuntimeReady"));
        }

        [Test]
        public void EntryCheckpointConfirmationAcceptsExactEphemeralRuntime()
        {
            const string checkpointPath =
                "Assets/Scripts/Ontology/Unity/Networking/" +
                "OntologyAvatarCheckpointController.cs";
            var source = File.ReadAllText(checkpointPath);
            Assert.That(source, Does.Contain("HasCheckpointAuthority"));
            Assert.That(source, Does.Contain("IsPlayerRuntimeActiveFor"));
        }

        [Test]
        public void RemovedRuleBlockIsNotReportedAsAvailable()
        {
            var avatarId = Guid.NewGuid();
            var projection =
                new Tormia.Ontology.Core
                    .OntologyAuthorityWorldProjection
                {
                    ruleBindings =
                        Array.Empty<
                            Tormia.Ontology.Core
                                .OntologyAuthorityRuleBindingProjection>()
                };

            Assert.That(
                Tormia.Ontology.Core
                    .OntologyWorldAuthorityAccountEntryFlow
                    .HasActiveRuleBinding(
                        projection,
                        avatarId,
                        Tormia.Ontology.Core
                            .OntologyRuleBlocks.RespawnPlayerOnDeath),
                Is.False);
        }

        private static Tormia.Ontology.Core
            .OntologyAuthorityFactProjection BooleanFact(
                Guid avatarId,
                bool value) =>
            new()
            {
                subjectEntityId = avatarId.ToString("D"),
                predicateId =
                    Tormia.Ontology.Core.OntologyPredicates.IsAlive,
                objectKind = "boolean",
                objectValueJson =
                    value ? "true" : "false"
            };
    }
}
