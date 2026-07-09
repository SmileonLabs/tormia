using System.Collections.Generic;
using System.Text;

namespace Tormia.Ontology.Core
{
    public sealed class OntologySession
    {
        public List<OntologyAction> ActionHistory { get; } = new();
        public List<OntologyEvent> EventHistory { get; } = new();

        public void Clear()
        {
            ActionHistory.Clear();
            EventHistory.Clear();
        }

        public void RecordAction(OntologyAction action)
        {
            ActionHistory.Add(action);
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
                    EventHistory.Add(ontologyEvent);
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
    }
}
