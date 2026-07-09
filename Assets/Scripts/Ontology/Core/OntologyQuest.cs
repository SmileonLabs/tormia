using System.Collections.Generic;
using System.Text;

namespace Tormia.Ontology.Core
{
    public sealed class OntologyQuest
    {
        public OntologyQuest(OntologyId id, string title, string reason)
        {
            Id = id;
            Title = title;
            Reason = reason;
        }

        public OntologyId Id { get; }
        public string Title { get; }
        public string Reason { get; }
        public List<OntologyQuestGoal> Goals { get; } = new();
        public bool IsCompleted
        {
            get
            {
                return Goals.Count > 0 && Goals.TrueForAll(goal => goal.IsCompleted);
            }
        }

        public override string ToString()
        {
            var builder = new StringBuilder();
            builder.Append(IsCompleted ? "[Completed] " : "[Active] ");
            builder.Append(Title);
            if (!string.IsNullOrWhiteSpace(Reason))
            {
                builder.Append(" - ");
                builder.Append(Reason);
            }

            foreach (var goal in Goals)
            {
                builder.AppendLine();
                builder.Append("  * ");
                builder.Append(goal);
            }

            return builder.ToString();
        }
    }

    public sealed class OntologyQuestGoal
    {
        public OntologyQuestGoal(OntologyAction recommendedAction, string description, bool isCompleted = false)
        {
            RecommendedAction = recommendedAction;
            Description = description;
            IsCompleted = isCompleted;
        }

        public OntologyAction RecommendedAction { get; }
        public string Description { get; }
        public bool IsCompleted { get; }

        public override string ToString()
        {
            return (IsCompleted ? "[x] " : "[ ] ") + Description;
        }
    }
}
