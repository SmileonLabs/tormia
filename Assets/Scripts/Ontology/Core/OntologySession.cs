using System.Collections.Generic;
using System.Text;

namespace Tormia.Ontology.Core
{
    public sealed class OntologySession
    {
        public const int DefaultMaxActionHistory = 128;
        public const int DefaultMaxEventHistory = 512;

        public List<OntologyAction> ActionHistory { get; } = new();
        public List<OntologyEvent> EventHistory { get; } = new();
        public int MaxActionHistory { get; }
        public int MaxEventHistory { get; }

        public OntologySession(
            int maxActionHistory = DefaultMaxActionHistory,
            int maxEventHistory = DefaultMaxEventHistory)
        {
            MaxActionHistory = System.Math.Max(1, maxActionHistory);
            MaxEventHistory = System.Math.Max(1, maxEventHistory);
        }

        public void Clear()
        {
            ActionHistory.Clear();
            EventHistory.Clear();
        }

        public void RecordAction(OntologyAction action)
        {
            ActionHistory.Add(action);
            TrimToCapacity(ActionHistory, MaxActionHistory);
        }

        public void RecordEvent(OntologyEvent ontologyEvent)
        {
            if (ontologyEvent == null)
            {
                return;
            }

            EventHistory.Add(ontologyEvent);
            TrimToCapacity(EventHistory, MaxEventHistory);
        }

        public void RecordEvents(OntologySimulationResult result)
        {
            if (result == null)
            {
                return;
            }

            foreach (var step in result.Steps)
            {
                foreach (var ontologyEvent in step.Events)
                {
                    RecordEvent(ontologyEvent);
                }
            }
        }

        public string DumpHistory()
        {
            var builder = new StringBuilder();
            builder.AppendLine("[History]");
            if (ActionHistory.Count == 0 && EventHistory.Count == 0)
            {
                builder.AppendLine("- none");
                return builder.ToString().TrimEnd();
            }

            foreach (var action in ActionHistory)
            {
                builder.Append("[Action] ");
                builder.AppendLine(action.ToString());
            }

            foreach (var ontologyEvent in EventHistory)
            {
                builder.Append("[EventHistory] ");
                builder.AppendLine(ontologyEvent.ToString());
            }

            return builder.ToString().TrimEnd();
        }

        private static void TrimToCapacity<T>(List<T> values, int capacity)
        {
            var overflow = values.Count - capacity;
            if (overflow > 0)
            {
                values.RemoveRange(0, overflow);
            }
        }
    }
}
