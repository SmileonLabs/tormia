using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyWorldEntryBoundaryTests
    {
        [Test]
        public void RuntimeEntryPreflightsBeforeAnyAvatarMutation()
        {
            var source = ReadSource(
                "Scripts/Ontology/Unity/Networking/" +
                "OntologyWorldAuthorityAccountEntryFlow.cs");
            var body = SliceMethod(source, "private IEnumerator EnterRoutineCore()",
                "private IEnumerator RegisterAvatarRoutine(");

            var preflight = body.IndexOf(
                "VerifyDevelopmentContentReleaseRoutine",
                StringComparison.Ordinal);
            var projectionRead = body.IndexOf(
                "LoadWorldForEntryWithRetryRoutine",
                StringComparison.Ordinal);
            var semanticPreflight = body.IndexOf(
                "VerifyAvatarSemanticContractRoutine",
                StringComparison.Ordinal);

            Assert.That(preflight, Is.GreaterThanOrEqualTo(0));
            Assert.That(projectionRead, Is.GreaterThan(preflight));
            Assert.That(semanticPreflight, Is.GreaterThan(projectionRead));
            Assert.That(
                body,
                Does.Not.Contain("PrepareDevelopmentContentReleaseRoutine"));
            Assert.That(
                body,
                Does.Not.Contain("EnsureDevelopmentActionPackageRoutine"));
        }

        [Test]
        public void RuntimeEntryCommitsAfterPreparedProjectionRead()
        {
            var source = ReadSource(
                "Scripts/Ontology/Unity/Networking/" +
                "OntologyWorldAuthorityAccountEntryFlow.cs");
            var body = SliceMethod(source, "private IEnumerator EnterRoutineCore()",
                "private IEnumerator RegisterAvatarRoutine(");

            var projectionReady = body.IndexOf(
                "LoadWorldForEntryWithRetryRoutine",
                StringComparison.Ordinal);
            var entryCommit = body.IndexOf(
                "EnterWorldRoutine",
                StringComparison.Ordinal);

            Assert.That(projectionReady, Is.GreaterThanOrEqualTo(0));
            Assert.That(entryCommit, Is.GreaterThan(projectionReady));
        }

        [Test]
        public void RuntimeEntryOnlyPreflightsAvatarSemanticContract()
        {
            var source = ReadSource(
                "Scripts/Ontology/Unity/Networking/" +
                "OntologyWorldAuthorityAccountEntryFlow.cs");
            var body = SliceMethod(source, "private IEnumerator EnterRoutineCore()",
                "private IEnumerator RegisterAvatarRoutine(");

            Assert.That(
                body,
                Does.Contain("VerifyAvatarSemanticContractRoutine"));
            Assert.That(
                body,
                Does.Not.Contain("EnsureAvatarSemanticContractRoutine"));
            Assert.That(
                body,
                Does.Not.Contain("RepairLegacySemanticContractsRoutine"));
            Assert.That(
                body,
                Does.Not.Contain("PrepareSemanticContractRoutine"));
        }

        [Test]
        public void ExplicitAvatarPreparationOwnsReleaseAndAtomicContractCommand()
        {
            var source = ReadSource(
                "Scripts/Ontology/Unity/Networking/" +
                "OntologyWorldAuthorityAccountEntryFlow.cs");
            var body = SliceMethod(
                source,
                "private IEnumerator PrepareAvatarSemanticContractRoutine(",
                "private bool TryCreateAvatarSemanticContractPayload(");

            Assert.That(
                body,
                Does.Contain("PrepareDevelopmentContentReleaseRoutine"));
            Assert.That(
                body,
                Does.Contain("PreparePlayerAvatarRoutine"));
            Assert.That(
                body,
                Does.Contain("VerifySemanticContractRoutine"));
            Assert.That(
                body.IndexOf(
                    "VerifySemanticContractRoutine",
                    StringComparison.Ordinal),
                Is.GreaterThan(body.IndexOf(
                    "PreparePlayerAvatarRoutine",
                    StringComparison.Ordinal)));
            Assert.That(
                body,
                Does.Not.Contain("SetAuthoredFact"));
            Assert.That(
                body,
                Does.Not.Contain("AddRuleBlock"));
            Assert.That(
                body,
                Does.Not.Contain("RemoveRuleBlock"));
        }

        [Test]
        public void RuntimeEntryDoesNotProvisionAvatarOrWorldFoundation()
        {
            var source = ReadSource(
                "Scripts/Ontology/Unity/Networking/" +
                "OntologyWorldAuthorityAccountEntryFlow.cs");
            var body = SliceMethod(source, "private IEnumerator EnterRoutineCore()",
                "private IEnumerator RegisterAvatarRoutine(");

            Assert.That(body, Does.Not.Contain("EnsureRuntimeZoneRoutine"));
            Assert.That(body, Does.Not.Contain("RegisterAvatarRoutine"));
            Assert.That(body, Does.Not.Contain("PlaceAvatarRoutine"));
            Assert.That(
                body,
                Does.Not.Contain("EnsureAvatarRuntimeFoundationRoutine"));
        }

        [Test]
        public void PendingCommandRecoveryPrecedesEntryCommit()
        {
            var source = ReadSource(
                "Scripts/Ontology/Unity/Networking/" +
                "OntologyWorldAuthorityAccountEntryFlow.cs");
            var body = SliceMethod(source, "private IEnumerator EnterRoutineCore()",
                "private IEnumerator RegisterAvatarRoutine(");

            Assert.That(
                body.IndexOf("ReplayPendingCommandsRoutine", StringComparison.Ordinal),
                Is.LessThan(body.IndexOf("EnterWorldRoutine", StringComparison.Ordinal)));
        }

        [Test]
        public void NewWorldCreationRunsExplicitPreparationBeforeCompletion()
        {
            var source = ReadSource(
                "Scripts/Ontology/Unity/Networking/" +
                "OntologyWorldAuthorityAccountEntryFlow.cs");
            var body = SliceMethod(
                source,
                "private IEnumerator CreateWorldRoutine(",
                "private IEnumerator ConnectRoutine(");

            Assert.That(body, Does.Contain("PrepareAvatarSemanticContractRoutine"));
            Assert.That(
                body.IndexOf("PrepareAvatarSemanticContractRoutine", StringComparison.Ordinal),
                Is.LessThan(body.IndexOf("completed?.Invoke(created)", StringComparison.Ordinal)));
        }

        [Test]
        public void FailedAdmissionRollsBackExactEphemeralRuntimeSession()
        {
            var source = ReadSource(
                "Scripts/Ontology/Unity/Networking/" +
                "OntologyWorldAuthorityAccountEntryFlow.cs");
            var enterWrapper = SliceMethod(
                source,
                "private IEnumerator EnterRoutine(Action<bool> completed)",
                "private IEnumerator EnterRoutineCore()");
            var enterCore = SliceMethod(
                source,
                "private IEnumerator EnterRoutineCore()",
                "private IEnumerator RegisterAvatarRoutine(");

            Assert.That(
                enterCore.IndexOf("ActivatePlayerRuntimeRoutine", StringComparison.Ordinal),
                Is.LessThan(enterCore.IndexOf("EnterWorldRoutine", StringComparison.Ordinal)));
            Assert.That(
                enterCore,
                Does.Contain("entryRuntimeRollbackSessionId"));
            Assert.That(
                enterWrapper,
                Does.Contain("RollbackEntryRuntimeRoutine"));
            Assert.That(
                source,
                Does.Contain("DeactivatePlayerRuntimeRoutine"));
        }

        [Test]
        public void ActivationLeaseFailureDeactivatesBeforeSessionIdentityIsCleared()
        {
            var source = ReadSource(
                "Scripts/Ontology/Unity/Networking/" +
                "OntologyWorldAuthorityClient.cs");
            var body = SliceMethod(
                source,
                "public IEnumerator ActivatePlayerRuntimeRoutine(",
                "public IEnumerator DeactivatePlayerRuntimeRoutine(");

            var leaseRenewal = body.IndexOf(
                "RenewRuntimeSessionLeaseRoutine",
                StringComparison.Ordinal);
            var rollback = body.IndexOf(
                "DeactivatePlayerRuntimeRoutine",
                StringComparison.Ordinal);
            var localClear = body.LastIndexOf(
                "activeRuntimeSessionId = string.Empty",
                StringComparison.Ordinal);
            Assert.That(leaseRenewal, Is.GreaterThanOrEqualTo(0));
            Assert.That(rollback, Is.GreaterThan(leaseRenewal));
            Assert.That(localClear, Is.GreaterThan(rollback));
            Assert.That(body, Does.Contain("activatedSessionId"));
        }

        [Test]
        public void RuntimeCombatCannotRepublishDevelopmentContent()
        {
            var source = ReadSource(
                "Scripts/Ontology/Unity/OntologyCombatController.cs");

            Assert.That(
                source,
                Does.Not.Contain("EnsureDevelopmentActionPackageRoutine"));
            Assert.That(
                source,
                Does.Contain("VerifyDevelopmentContentReleaseRoutine"));
        }

        private static string ReadSource(string relativePath)
        {
            var path = Path.Combine(Application.dataPath, relativePath);
            Assert.That(File.Exists(path), Is.True, path);
            return File.ReadAllText(path);
        }

        private static string SliceMethod(
            string source,
            string startMarker,
            string endMarker)
        {
            var start = source.IndexOf(startMarker, StringComparison.Ordinal);
            var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0));
            Assert.That(end, Is.GreaterThan(start));
            return source.Substring(start, end - start);
        }
    }
}
