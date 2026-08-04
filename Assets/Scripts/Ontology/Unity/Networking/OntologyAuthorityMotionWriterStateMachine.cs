using System;

namespace Tormia.Ontology.Core
{
    public enum OntologyMotionWriterState
    {
        HttpActive,
        AdmissionConnected,
        PromotionPending,
        UdpActive,
        FallbackPending,
        RecoveryPending
    }

    /// <summary>
    /// Pure transition policy for the one-writer motion transport lease.
    /// Network callbacks provide evidence to this type; it never performs IO,
    /// changes a Transform, or grants gameplay permission.
    /// </summary>
    public sealed class OntologyAuthorityMotionWriterStateMachine
    {
        private Guid runtimeSessionId;
        private Guid transportSessionId;
        private Guid worldId;
        private Guid actorId;
        private string zoneKey = string.Empty;
        private ulong transportGeneration;
        private long writerEpoch;
        private long lastProcessedIntentSequence;

        public OntologyMotionWriterState State { get; private set; } =
            OntologyMotionWriterState.HttpActive;
        public bool CanWriteHttp =>
            State == OntologyMotionWriterState.HttpActive ||
            State == OntologyMotionWriterState.AdmissionConnected ||
            State == OntologyMotionWriterState.PromotionPending;
        public bool CanWriteUdp => State == OntologyMotionWriterState.UdpActive;
        public long WriterEpoch => writerEpoch;
        public long LastProcessedIntentSequence =>
            lastProcessedIntentSequence;

        public bool RegisterConnectedAdmission(
            Guid runtimeSession,
            Guid transportSession,
            ulong generation,
            Guid world,
            string zone,
            Guid actor,
            long currentWriterEpoch = 0)
        {
            if (State != OntologyMotionWriterState.HttpActive ||
                runtimeSession == Guid.Empty ||
                transportSession == Guid.Empty || generation == 0 ||
                world == Guid.Empty || actor == Guid.Empty ||
                string.IsNullOrWhiteSpace(zone) || currentWriterEpoch < 0)
                return false;

            runtimeSessionId = runtimeSession;
            transportSessionId = transportSession;
            transportGeneration = generation;
            worldId = world;
            zoneKey = zone.Trim();
            actorId = actor;
            writerEpoch = currentWriterEpoch;
            lastProcessedIntentSequence = 0;
            State = OntologyMotionWriterState.AdmissionConnected;
            return true;
        }

        public bool BeginPromotion()
        {
            if (State != OntologyMotionWriterState.AdmissionConnected)
                return false;
            State = OntologyMotionWriterState.PromotionPending;
            return true;
        }

        public bool AcceptPromotion(
            Guid runtimeSession,
            Guid transportSession,
            ulong generation,
            long acceptedWriterEpoch)
        {
            if (State != OntologyMotionWriterState.PromotionPending ||
                !MatchesTransport(runtimeSession, transportSession, generation) ||
                acceptedWriterEpoch <= writerEpoch) return false;

            writerEpoch = acceptedWriterEpoch;
            State = OntologyMotionWriterState.UdpActive;
            return true;
        }

        public bool ObserveAuthorityProgress(
            Guid runtimeSession,
            Guid world,
            string zone,
            Guid actor,
            long processedIntentSequence)
        {
            if (State != OntologyMotionWriterState.UdpActive ||
                runtimeSession != runtimeSessionId || world != worldId ||
                actor != actorId ||
                !string.Equals(zone?.Trim(), zoneKey,
                    StringComparison.Ordinal) ||
                processedIntentSequence <= lastProcessedIntentSequence)
                return false;

            lastProcessedIntentSequence = processedIntentSequence;
            return true;
        }

        public bool RequireFallback()
        {
            if (State != OntologyMotionWriterState.UdpActive &&
                State != OntologyMotionWriterState.PromotionPending &&
                State != OntologyMotionWriterState.AdmissionConnected)
                return false;

            // Both lanes remain fenced until Authority acknowledges fallback.
            State = OntologyMotionWriterState.FallbackPending;
            return true;
        }

        public bool CancelAdmission()
        {
            if (State != OntologyMotionWriterState.AdmissionConnected &&
                State != OntologyMotionWriterState.PromotionPending)
                return false;
            ClearTransportIdentity();
            State = OntologyMotionWriterState.HttpActive;
            return true;
        }

        public bool AcceptFallback(
            Guid runtimeSession,
            Guid transportSession,
            ulong generation,
            long acceptedWriterEpoch)
        {
            if (State != OntologyMotionWriterState.FallbackPending ||
                !MatchesTransport(runtimeSession, transportSession, generation) ||
                acceptedWriterEpoch <= writerEpoch) return false;

            writerEpoch = acceptedWriterEpoch;
            State = OntologyMotionWriterState.RecoveryPending;
            return true;
        }

        public bool ResolvePromotionDuringFallback(
            Guid runtimeSession,
            Guid transportSession,
            ulong generation,
            long acceptedWriterEpoch)
        {
            if (State != OntologyMotionWriterState.FallbackPending ||
                !MatchesTransport(runtimeSession, transportSession, generation) ||
                acceptedWriterEpoch <= writerEpoch) return false;
            writerEpoch = acceptedWriterEpoch;
            return true;
        }

        public bool ResolveVerifiedHttpWriter(long verifiedWriterEpoch)
        {
            if (State != OntologyMotionWriterState.FallbackPending ||
                verifiedWriterEpoch != writerEpoch || writerEpoch <= 0)
                return false;
            State = OntologyMotionWriterState.RecoveryPending;
            return true;
        }

        public bool CompleteReliableRecovery(
            Guid runtimeSession,
            Guid world,
            string zone,
            Guid actor,
            long recoveryWriterEpoch)
        {
            if (State != OntologyMotionWriterState.RecoveryPending ||
                runtimeSession != runtimeSessionId || world != worldId ||
                actor != actorId || recoveryWriterEpoch != writerEpoch ||
                !string.Equals(zone?.Trim(), zoneKey,
                    StringComparison.Ordinal)) return false;

            ClearTransportIdentity();
            State = OntologyMotionWriterState.HttpActive;
            return true;
        }

        public void Reset()
        {
            writerEpoch = 0;
            lastProcessedIntentSequence = 0;
            ClearTransportIdentity();
            State = OntologyMotionWriterState.HttpActive;
        }

        private bool MatchesTransport(
            Guid runtimeSession,
            Guid transportSession,
            ulong generation) =>
            runtimeSession == runtimeSessionId &&
            transportSession == transportSessionId &&
            generation == transportGeneration;

        private void ClearTransportIdentity()
        {
            runtimeSessionId = Guid.Empty;
            transportSessionId = Guid.Empty;
            transportGeneration = 0;
            worldId = Guid.Empty;
            actorId = Guid.Empty;
            zoneKey = string.Empty;
            lastProcessedIntentSequence = 0;
        }
    }
}
