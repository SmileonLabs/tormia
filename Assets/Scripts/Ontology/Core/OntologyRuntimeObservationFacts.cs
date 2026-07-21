using System.Collections.Generic;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Owns the small but important contract between a runtime observation adapter and
    /// the ontology world: an adapter may retract only the facts that it published.
    /// Authored facts and observations from another adapter must survive when a sensor
    /// leaves a volume, is disabled, or refreshes after a world reset.
    /// </summary>
    public static class OntologyRuntimeObservationFacts
    {
        public static bool SynchronizeSet(
            OntologyWorldState world,
            string subject,
            string predicate,
            ISet<string> observedObjects,
            ISet<string> publishedObjects,
            IList<string> removalBuffer)
        {
            if (world == null ||
                string.IsNullOrWhiteSpace(subject) ||
                string.IsNullOrWhiteSpace(predicate) ||
                observedObjects == null ||
                publishedObjects == null ||
                removalBuffer == null)
            {
                return false;
            }

            var changed = false;
            removalBuffer.Clear();
            foreach (var publishedObject in publishedObjects)
            {
                if (!observedObjects.Contains(publishedObject))
                {
                    changed |= world.RemoveFact(subject, predicate, publishedObject);
                    removalBuffer.Add(publishedObject);
                }
            }

            foreach (var publishedObject in removalBuffer)
            {
                publishedObjects.Remove(publishedObject);
            }

            foreach (var observedObject in observedObjects)
            {
                if (string.IsNullOrWhiteSpace(observedObject) ||
                    publishedObjects.Contains(observedObject) ||
                    world.HasFact(subject, predicate, observedObject))
                {
                    continue;
                }

                if (world.AddFact(subject, predicate, observedObject))
                {
                    publishedObjects.Add(observedObject);
                    changed = true;
                }
            }

            return changed;
        }

        public static bool SynchronizeSingleValue(
            OntologyWorldState world,
            string subject,
            string predicate,
            string observedValue,
            ref string publishedValue)
        {
            if (world == null ||
                string.IsNullOrWhiteSpace(subject) ||
                string.IsNullOrWhiteSpace(predicate))
            {
                return false;
            }

            var changed = false;
            if (!string.IsNullOrWhiteSpace(publishedValue) &&
                !string.Equals(publishedValue, observedValue, System.StringComparison.Ordinal))
            {
                changed |= world.RemoveFact(subject, predicate, publishedValue);
                publishedValue = string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(observedValue) &&
                !string.Equals(publishedValue, observedValue, System.StringComparison.Ordinal) &&
                !world.HasFact(subject, predicate, observedValue) &&
                world.AddFact(subject, predicate, observedValue))
            {
                publishedValue = observedValue;
                changed = true;
            }

            return changed;
        }

        public static bool RemovePublishedSet(
            OntologyWorldState world,
            string subject,
            string predicate,
            ISet<string> publishedObjects)
        {
            if (world == null ||
                string.IsNullOrWhiteSpace(subject) ||
                string.IsNullOrWhiteSpace(predicate) ||
                publishedObjects == null)
            {
                return false;
            }

            var changed = false;
            foreach (var publishedObject in publishedObjects)
            {
                changed |= world.RemoveFact(subject, predicate, publishedObject);
            }

            publishedObjects.Clear();
            return changed;
        }

        public static bool RemovePublishedSingleValue(
            OntologyWorldState world,
            string subject,
            string predicate,
            ref string publishedValue)
        {
            if (world == null ||
                string.IsNullOrWhiteSpace(subject) ||
                string.IsNullOrWhiteSpace(predicate) ||
                string.IsNullOrWhiteSpace(publishedValue))
            {
                return false;
            }

            var changed = world.RemoveFact(subject, predicate, publishedValue);
            publishedValue = string.Empty;
            return changed;
        }
    }
}
