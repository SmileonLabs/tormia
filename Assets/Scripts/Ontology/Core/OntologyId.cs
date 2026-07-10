using System;

namespace Tormia.Ontology.Core
{
    [Serializable]
    public readonly struct OntologyId : IEquatable<OntologyId>
    {
        public readonly string Value;

        public OntologyId(string value)
        {
            Value = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        public bool IsEmpty => string.IsNullOrEmpty(Value);

        public bool Equals(OntologyId other)
        {
            return string.Equals(Value ?? string.Empty, other.Value ?? string.Empty, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is OntologyId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (Value ?? string.Empty).GetHashCode();
        }

        public override string ToString()
        {
            return Value;
        }

        public static implicit operator OntologyId(string value)
        {
            return new OntologyId(value);
        }

        public static implicit operator string(OntologyId id)
        {
            return id.Value;
        }
    }
}
