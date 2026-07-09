using System;

namespace Tormia.Ontology.Core
{
    [Serializable]
    public readonly struct OntologyAction : IEquatable<OntologyAction>
    {
        public readonly OntologyId ActorId;
        public readonly OntologyId Verb;
        public readonly OntologyId TargetId;
        public readonly OntologyId ToolId;

        public OntologyAction(OntologyId actorId, OntologyId verb, OntologyId targetId, OntologyId toolId = default)
        {
            ActorId = actorId;
            Verb = verb;
            TargetId = targetId;
            ToolId = toolId;
        }

        public bool IsValid => !ActorId.IsEmpty && !Verb.IsEmpty && !TargetId.IsEmpty;

        public bool Equals(OntologyAction other)
        {
            return ActorId.Equals(other.ActorId)
                && Verb.Equals(other.Verb)
                && TargetId.Equals(other.TargetId)
                && ToolId.Equals(other.ToolId);
        }

        public override bool Equals(object obj)
        {
            return obj is OntologyAction other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = ActorId.GetHashCode();
                hash = (hash * 397) ^ Verb.GetHashCode();
                hash = (hash * 397) ^ TargetId.GetHashCode();
                hash = (hash * 397) ^ ToolId.GetHashCode();
                return hash;
            }
        }

        public override string ToString()
        {
            return ToolId.IsEmpty
                ? $"{ActorId} {Verb} {TargetId}"
                : $"{ActorId} {Verb} {TargetId} with {ToolId}";
        }
    }
}
