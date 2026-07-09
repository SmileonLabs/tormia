using System;

namespace Tormia.Ontology.Core
{
    [Serializable]
    public readonly struct OntologyFact : IEquatable<OntologyFact>
    {
        public readonly OntologyId Subject;
        public readonly OntologyId Predicate;
        public readonly OntologyId Object;

        public OntologyFact(OntologyId subject, OntologyId predicate, OntologyId obj)
        {
            Subject = subject;
            Predicate = predicate;
            Object = obj;
        }

        public bool IsValid => !Subject.IsEmpty && !Predicate.IsEmpty && !Object.IsEmpty;

        public bool Equals(OntologyFact other)
        {
            return Subject.Equals(other.Subject)
                && Predicate.Equals(other.Predicate)
                && Object.Equals(other.Object);
        }

        public override bool Equals(object obj)
        {
            return obj is OntologyFact other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Subject.GetHashCode();
                hash = (hash * 397) ^ Predicate.GetHashCode();
                hash = (hash * 397) ^ Object.GetHashCode();
                return hash;
            }
        }

        public override string ToString()
        {
            return $"{Subject} {Predicate} {Object}";
        }
    }
}
