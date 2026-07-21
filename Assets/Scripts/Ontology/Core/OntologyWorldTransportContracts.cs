using System;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Stable command names shared by the local authority, a future game server,
    /// and its persistence layer. A client requests one of these; it never sends
    /// an authoritative fact or inferred result directly.
    /// </summary>
    public static class OntologyWorldCommandKinds
    {
        public const string PlaceEntity = "place_entity";
        public const string MoveEntity = "move_entity";
        public const string SetAuthoredFact = "set_authored_fact";
        public const string RetractAuthoredFact = "retract_authored_fact";
        public const string AddRuleBlock = "add_rule_block";
        public const string RemoveRuleBlock = "remove_rule_block";
        public const string DefineZone = "define_zone";
        public const string EquipEntity = "equip_entity";
        public const string UnequipEntity = "unequip_entity";
    }

    /// <summary>
    /// Envelope for a user intent. Payload is intentionally opaque at this layer:
    /// the authority validates the command-specific semantic graph before it can
    /// mutate the durable authored world.
    /// </summary>
    [Serializable]
    public sealed class OntologyWorldCommand
    {
        public const int CurrentContractVersion = 1;

        public int contractVersion = CurrentContractVersion;
        public string commandId;
        public string worldId;
        public string actorUserId;
        public long expectedRevision;
        public string commandType;
        public string payloadJson;

        public bool TryValidateEnvelope(out string rejectionCode)
        {
            if (contractVersion != CurrentContractVersion)
            {
                rejectionCode = "unsupported_contract_version";
                return false;
            }

            if (!Guid.TryParse(commandId, out _))
            {
                rejectionCode = "invalid_command_id";
                return false;
            }

            if (!Guid.TryParse(worldId, out _))
            {
                rejectionCode = "invalid_world_id";
                return false;
            }

            if (!Guid.TryParse(actorUserId, out _))
            {
                rejectionCode = "invalid_actor_user_id";
                return false;
            }

            if (expectedRevision < 0)
            {
                rejectionCode = "invalid_expected_revision";
                return false;
            }

            if (string.IsNullOrWhiteSpace(commandType))
            {
                rejectionCode = "missing_command_type";
                return false;
            }

            if (string.IsNullOrWhiteSpace(payloadJson))
            {
                rejectionCode = "missing_payload";
                return false;
            }

            rejectionCode = string.Empty;
            return true;
        }
    }

    /// <summary>
    /// Result produced by a world authority after validation. Events represent
    /// durable authored changes only; observation and inference remain runtime
    /// state and are replicated as session deltas instead of being persisted here.
    /// </summary>
    [Serializable]
    public sealed class OntologyWorldEventRecord
    {
        public int contractVersion = OntologyWorldCommand.CurrentContractVersion;
        public string eventId;
        public string worldId;
        public string commandId;
        public string actorUserId;
        public long sequenceNumber;
        public string eventType;
        public string payloadJson;
        public long occurredAtUnixMilliseconds;
    }

    /// <summary>
    /// Metadata for an immutable world checkpoint. The serialized snapshot itself
    /// is stored in local files during development and object storage in production.
    /// </summary>
    [Serializable]
    public sealed class OntologyWorldSnapshotDescriptor
    {
        public int contractVersion = OntologyWorldCommand.CurrentContractVersion;
        public string snapshotId;
        public string worldId;
        public long revision;
        public string storageUri;
        public string checksum;
        public long createdAtUnixMilliseconds;
    }
}
