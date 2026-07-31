using System;
using System.Collections.Generic;
using System.Text;

namespace Tormia.Ontology.Core
{
    public sealed class OntologyQuest
    {
        public OntologyQuest(
            OntologyId id,
            string title,
            string reason,
            string titleKey = null,
            string reasonKey = null,
            string[] reasonArguments = null)
        {
            Id = id;
            Title = title;
            Reason = reason;
            TitleKey = titleKey;
            ReasonKey = reasonKey;
            ReasonArguments = reasonArguments ?? Array.Empty<string>();
        }

        public OntologyId Id { get; }
        public string Title { get; }
        public string Reason { get; }
        public string TitleKey { get; }
        public string ReasonKey { get; }
        public IReadOnlyList<string> ReasonArguments { get; }
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
        public OntologyQuestGoal(
            OntologyAction recommendedAction,
            string description,
            bool isCompleted = false,
            string descriptionKey = null,
            string[] descriptionArguments = null)
        {
            RecommendedAction = recommendedAction;
            Description = description;
            IsCompleted = isCompleted;
            DescriptionKey = descriptionKey;
            DescriptionArguments =
                descriptionArguments ?? Array.Empty<string>();
        }

        public OntologyAction RecommendedAction { get; }
        public string Description { get; }
        public bool IsCompleted { get; }
        public string DescriptionKey { get; }
        public IReadOnlyList<string> DescriptionArguments { get; }

        public override string ToString()
        {
            return (IsCompleted ? "[x] " : "[ ] ") + Description;
        }
    }
}
