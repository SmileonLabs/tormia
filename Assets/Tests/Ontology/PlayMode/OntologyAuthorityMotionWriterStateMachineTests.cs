using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Tormia.Ontology.Core.Tests
{
    public sealed class OntologyAuthorityMotionWriterStateMachineTests
    {
        private readonly Guid runtime = Guid.NewGuid();
        private readonly Guid transport = Guid.NewGuid();
        private readonly Guid world = Guid.NewGuid();
        private readonly Guid actor = Guid.NewGuid();

        [Test]
        public void AdmissionAloneKeepsHttpWriterUntilPromotionAck()
        {
            var machine = CreateConnected();

            Assert.That(machine.State,
                Is.EqualTo(OntologyMotionWriterState.AdmissionConnected));
            Assert.That(machine.CanWriteHttp, Is.True);
            Assert.That(machine.CanWriteUdp, Is.False);
        }

        [Test]
        public void PromotionAckActivatesOnlyUdpWriter()
        {
            var machine = CreateConnected();
            Assert.That(machine.BeginPromotion(), Is.True);

            Assert.That(machine.AcceptPromotion(
                runtime, transport, 7, 2), Is.True);
            Assert.That(machine.CanWriteUdp, Is.True);
            Assert.That(machine.CanWriteHttp, Is.False);
        }

        [Test]
        public void StalePromotionCallbackCannotChangeWriter()
        {
            var machine = CreateConnected();
            Assert.That(machine.BeginPromotion(), Is.True);

            Assert.That(machine.AcceptPromotion(
                runtime, Guid.NewGuid(), 7, 2), Is.False);
            Assert.That(machine.State,
                Is.EqualTo(OntologyMotionWriterState.PromotionPending));
            Assert.That(machine.CanWriteUdp, Is.False);
        }

        [Test]
        public void HealthLossFencesBothLanesUntilFallbackAndRecovery()
        {
            var machine = CreateUdpActive();

            Assert.That(machine.RequireFallback(), Is.True);
            Assert.That(machine.CanWriteHttp, Is.False);
            Assert.That(machine.CanWriteUdp, Is.False);
            Assert.That(machine.AcceptFallback(
                runtime, transport, 7, 3), Is.True);
            Assert.That(machine.CanWriteHttp, Is.False);
            Assert.That(machine.CompleteReliableRecovery(
                runtime, world, "zone-a", actor, 3), Is.True);
            Assert.That(machine.CanWriteHttp, Is.True);
            Assert.That(machine.CanWriteUdp, Is.False);
        }

        [Test]
        public void FallbackRetryAndStaleRecoveryFailClosed()
        {
            var machine = CreateUdpActive();
            machine.RequireFallback();

            Assert.That(machine.RequireFallback(), Is.False);
            Assert.That(machine.AcceptFallback(
                runtime, transport, 6, 3), Is.False);
            Assert.That(machine.AcceptFallback(
                runtime, transport, 7, 3), Is.True);
            Assert.That(machine.CompleteReliableRecovery(
                runtime, world, "other-zone", actor, 3), Is.False);
            Assert.That(machine.CompleteReliableRecovery(
                runtime, world, "zone-a", actor, 1), Is.False);
            Assert.That(machine.CanWriteHttp, Is.False);
        }

        [Test]
        public void AuthorityProgressRequiresCurrentScopeAndMonotonicSequence()
        {
            var machine = CreateUdpActive();

            Assert.That(machine.ObserveAuthorityProgress(
                runtime, world, "zone-a", actor, 10), Is.True);
            Assert.That(machine.ObserveAuthorityProgress(
                runtime, world, "zone-a", actor, 10), Is.False);
            Assert.That(machine.ObserveAuthorityProgress(
                runtime, world, "zone-a", Guid.NewGuid(), 11), Is.False);
            Assert.That(machine.ObserveAuthorityProgress(
                runtime, world, "zone-a", actor, 11), Is.True);
        }

        [Test]
        public void PromotionCommitObservedAfterDisconnectAdvancesFallbackEpoch()
        {
            var machine = CreateConnected();
            machine.BeginPromotion();
            machine.RequireFallback();

            Assert.That(machine.ResolvePromotionDuringFallback(
                runtime, transport, 7, 2), Is.True);
            Assert.That(machine.WriterEpoch, Is.EqualTo(2));
            Assert.That(machine.CanWriteHttp, Is.False);
            Assert.That(machine.CanWriteUdp, Is.False);
            Assert.That(machine.AcceptFallback(
                runtime, transport, 7, 3), Is.True);
        }

        [Test]
        public void HttpFallbackAckMustEchoPreviousUdpBinding()
        {
            var host = new GameObject("TransportValidation");
            try
            {
                var component = host.AddComponent<
                    OntologyWorldAuthorityUdpMotionTransport>();
                Set(component, "runtimeSessionId", runtime);
                Set(component, "transportSessionId", transport);
                Set(component, "transportGeneration", 7UL);
                Set(component, "writerTicket",
                    new OntologyAuthorityUdpTransportTicket
                    {
                        accepted = true,
                        writerEpoch = 2,
                        worldRevision = 9
                    });
                var response = new OntologyAuthorityMotionTransportTransition
                {
                    accepted = true,
                    writerMode = "http",
                    writerEpoch = 3,
                    worldRevision = 9,
                    runtimeSessionId = runtime.ToString("D"),
                    authenticatedTransportSessionId = Guid.Empty.ToString("D"),
                    transportGeneration = 0,
                    previousAuthenticatedTransportSessionId =
                        transport.ToString("D"),
                    previousTransportGeneration = 7
                };

                Assert.That(Validate(component, response, "http"), Is.True);
                response.previousTransportGeneration = 0;
                Assert.That(Validate(component, response, "http"), Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        [Test]
        public void UnpromotedFallbackRequiresVerifiedCurrentHttpEpoch()
        {
            var machine = CreateConnected();
            machine.BeginPromotion();
            machine.RequireFallback();

            Assert.That(machine.ResolveVerifiedHttpWriter(2), Is.False);
            Assert.That(machine.CanWriteHttp, Is.False);
            Assert.That(machine.ResolveVerifiedHttpWriter(1), Is.True);
            Assert.That(machine.State,
                Is.EqualTo(OntologyMotionWriterState.RecoveryPending));
            Assert.That(machine.CanWriteHttp, Is.False);
        }

        [Test]
        public void DisableDuringUdpLeaseKeepsBothWritersFenced()
        {
            var host = new GameObject("DisableTransport");
            var component = host.AddComponent<
                OntologyWorldAuthorityUdpMotionTransport>();
            var machine = (OntologyAuthorityMotionWriterStateMachine)
                component.GetType().GetField("writerState",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(component);
            machine.RegisterConnectedAdmission(runtime, transport, 7,
                world, "zone-a", actor, 1);
            machine.BeginPromotion();
            machine.AcceptPromotion(runtime, transport, 7, 2);

            component.enabled = false;

            Assert.That(machine.State,
                Is.EqualTo(OntologyMotionWriterState.FallbackPending));
            Assert.That(machine.CanWriteHttp, Is.False);
            Assert.That(machine.CanWriteUdp, Is.False);
            UnityEngine.Object.DestroyImmediate(host);
        }

        private OntologyAuthorityMotionWriterStateMachine CreateConnected()
        {
            var machine = new OntologyAuthorityMotionWriterStateMachine();
            Assert.That(machine.RegisterConnectedAdmission(
                runtime, transport, 7, world, "zone-a", actor, 1), Is.True);
            return machine;
        }

        private OntologyAuthorityMotionWriterStateMachine CreateUdpActive()
        {
            var machine = CreateConnected();
            machine.BeginPromotion();
            machine.AcceptPromotion(runtime, transport, 7, 2);
            return machine;
        }

        private static bool Validate(
            OntologyWorldAuthorityUdpMotionTransport target,
            OntologyAuthorityMotionTransportTransition response,
            string mode) => (bool)target.GetType().GetMethod(
                "TryValidateTransition",
                BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(target, new object[] { response, mode });

        private static void Set(object target, string name, object value) =>
            target.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(target, value);
    }
}
