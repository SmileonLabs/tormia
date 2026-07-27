using System;
using System.Collections.Generic;
using System.Linq;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Ephemeral creator request. Capturing a prompt does not mutate durable
    /// world data; only later reviewed authority commands may do that.
    /// </summary>
    public sealed class OntologyCreatorWorkDraft
    {
        public string RequestId { get; }
        public string Prompt { get; }
        public string Status { get; }
        public IReadOnlyList<string> ServiceRoute { get; }

        public OntologyCreatorWorkDraft(
            string requestId,
            string prompt,
            string status,
            IReadOnlyList<string> serviceRoute)
        {
            RequestId = requestId;
            Prompt = prompt;
            Status = status;
            ServiceRoute = serviceRoute;
        }
    }

    public enum OntologyCreatorDraftError
    {
        None = 0,
        PromptTooShort = 1,
        MissingPromptEntryService = 2,
        DuplicateServiceId = 3
    }

    /// <summary>
    /// Builds a deterministic production route from service catalog data.
    /// No Unity object, world fact, or Authority transport is involved.
    /// </summary>
    public static class OntologyCreatorRoutePlanner
    {
        public static bool TryBuild(
            IEnumerable<OntologyCreatorServiceDefinition> services,
            out string[] route,
            out OntologyCreatorDraftError error)
        {
            route = Array.Empty<string>();
            var serviceList = services?
                .Where(value => value != null && value.IsValid)
                .ToArray() ?? Array.Empty<OntologyCreatorServiceDefinition>();
            if (serviceList.Length == 0 ||
                !serviceList.Any(value => value.acceptsInitialPrompt))
            {
                error = OntologyCreatorDraftError.MissingPromptEntryService;
                return false;
            }

            if (serviceList
                .GroupBy(value => value.serviceId, StringComparer.Ordinal)
                .Any(group => group.Count() > 1))
            {
                error = OntologyCreatorDraftError.DuplicateServiceId;
                return false;
            }

            route = serviceList
                .OrderBy(value => value.stageOrder)
                .ThenBy(value => value.serviceId, StringComparer.Ordinal)
                .Select(value => value.serviceId)
                .ToArray();
            error = OntologyCreatorDraftError.None;
            return true;
        }
    }

    public static class OntologyCreatorWorkDraftFactory
    {
        public const string DraftStatus = "Draft";

        public static bool TryCreate(
            string prompt,
            IEnumerable<OntologyCreatorServiceDefinition> services,
            out OntologyCreatorWorkDraft draft,
            out OntologyCreatorDraftError error)
        {
            draft = null;
            var normalizedPrompt = prompt == null ? string.Empty : prompt.Trim();
            if (normalizedPrompt.Length < 10)
            {
                error = OntologyCreatorDraftError.PromptTooShort;
                return false;
            }

            if (!OntologyCreatorRoutePlanner.TryBuild(
                    services,
                    out var route,
                    out error))
            {
                return false;
            }

            draft = new OntologyCreatorWorkDraft(
                Guid.NewGuid().ToString("D"),
                normalizedPrompt,
                DraftStatus,
                route);
            error = OntologyCreatorDraftError.None;
            return true;
        }
    }
}
