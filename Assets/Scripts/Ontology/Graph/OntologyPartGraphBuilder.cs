using System.Collections.Generic;
using System.Text;
using Tormia.Ontology.Core;
using Tormia.Ontology.Localization;
using UnityEngine;

namespace Tormia.Ontology.Graph
{
    public static class OntologyPartGraphBuilder
    {
        public static OntologyGraphViewModel Build(
            IReadOnlyList<OntologyCharacterPartDefinition> definitions,
            OntologyTermLocalization localization,
            OntologyLanguage language)
        {
            var graph = new OntologyGraphViewModel();
            if (definitions == null || definitions.Count == 0)
            {
                return graph;
            }

            BuildAttachedParts(definitions, localization, language, graph);
            return graph;
        }

        private static void BuildAttachedParts(
            IReadOnlyList<OntologyCharacterPartDefinition> definitions,
            OntologyTermLocalization localization,
            OntologyLanguage language,
            OntologyGraphViewModel graph)
        {
            var slotCounts = new Dictionary<string, int>();
            for (var i = 0; i < definitions.Count; i++)
            {
                var definition = definitions[i];
                if (definition == null || string.IsNullOrWhiteSpace(definition.partId))
                {
                    continue;
                }

                var slotKey = GetSlotKey(definition);
                slotCounts.TryGetValue(slotKey, out var slotIndex);
                slotCounts[slotKey] = slotIndex + 1;

                var anchor = GetBodyAnchor(slotKey);
                var outward = GetBodyOutward(slotKey);
                var tangent = Vector3.Cross(Vector3.forward, outward).normalized;
                if (tangent.sqrMagnitude < 0.01f)
                {
                    tangent = Vector3.right;
                }

                var stackedOffset = slotIndex * 0.18f;
                var sideOffset = (slotIndex % 2 == 0 ? 1f : -1f) * stackedOffset * 0.45f;
                var partPosition = anchor + outward * (0.3f + stackedOffset) + tangent * sideOffset;

                var partNode = FindOrCreateNode(graph, definition.partId, OntologyGraphNodeKind.Semantic, localization, language);
                partNode.Label = BuildPartSummaryLabel(definition, localization, language);
                partNode.Position = partPosition;

                var facts = definition.facts ?? System.Array.Empty<OntologyFactEntry>();
                for (var j = 0; j < facts.Length; j++)
                {
                    var fact = facts[j];
                    if (fact == null || string.IsNullOrWhiteSpace(fact.predicate) || string.IsNullOrWhiteSpace(fact.obj))
                    {
                        continue;
                    }

                    var nodeKind = ToNodeKind(fact.predicate);
                    var objectNode = FindOrCreateNode(graph, definition.partId + "_" + fact.obj, nodeKind, localization, language);
                    objectNode.Label = Localize(fact.obj, localization, language);
                    var spread = j - (facts.Length - 1) * 0.5f;
                    objectNode.Position = partPosition + outward * 0.48f + tangent * spread * 0.28f + Vector3.forward * spread * 0.1f;
                    graph.Edges.Add(new OntologyGraphEdge
                    {
                        FromId = definition.partId,
                        ToId = objectNode.Id,
                        Label = Localize(fact.predicate, localization, language),
                        PredicateId = fact.predicate,
                        Color = ColorForKind(nodeKind, 0.78f)
                    });
                }
            }
        }

        private static OntologyGraphNode FindOrCreateNode(
            OntologyGraphViewModel graph,
            string id,
            OntologyGraphNodeKind kind,
            OntologyTermLocalization localization,
            OntologyLanguage language)
        {
            foreach (var node in graph.Nodes)
            {
                if (node.Id == id)
                {
                    return node;
                }
            }

            var created = new OntologyGraphNode
            {
                Id = id,
                Label = Localize(id, localization, language),
                Kind = kind,
                Color = ColorForKind(kind, 1f),
                Radius = 0.11f
            };
            graph.Nodes.Add(created);
            return created;
        }

        private static string GetSlotKey(OntologyCharacterPartDefinition definition)
        {
            var source = (definition.slot + " " + definition.partId + " " + definition.displayName).ToLowerInvariant();
            if (source.Contains("hat") || source.Contains("hair")) return "head";
            if (source.Contains("glass") || source.Contains("face")) return "face";
            if (source.Contains("glove") || source.Contains("hand")) return "hands";
            if (source.Contains("shoe") || source.Contains("foot")) return "feet";
            if (source.Contains("pant") || source.Contains("leg")) return "legs";
            if (source.Contains("outfit") || source.Contains("outer") || source.Contains("costume") || source.Contains("body")) return "torso";
            if (source.Contains("acc") || source.Contains("accessor")) return "accessory";
            return "torso";
        }

        private static Vector3 GetBodyAnchor(string slotKey)
        {
            return slotKey switch
            {
                "head" => new Vector3(0f, 1.28f, 0.02f),
                "face" => new Vector3(0f, 1.08f, -0.16f),
                "hands" => new Vector3(0.74f, 0.36f, -0.06f),
                "feet" => new Vector3(0.5f, -1.05f, -0.08f),
                "legs" => new Vector3(-0.45f, -0.58f, 0f),
                "torso" => new Vector3(-0.36f, 1.05f, -0.08f),
                "accessory" => new Vector3(0.68f, 0.82f, -0.08f),
                _ => new Vector3(-0.36f, 0.98f, -0.08f)
            };
        }

        private static Vector3 GetBodyOutward(string slotKey)
        {
            return slotKey switch
            {
                "head" => new Vector3(0f, 0.82f, -0.28f).normalized,
                "face" => new Vector3(0.12f, 0.2f, -0.95f).normalized,
                "hands" => new Vector3(0.92f, 0.06f, -0.25f).normalized,
                "feet" => new Vector3(0.55f, -0.58f, -0.28f).normalized,
                "legs" => new Vector3(-0.55f, -0.2f, -0.28f).normalized,
                "torso" => new Vector3(-0.72f, 0.2f, -0.38f).normalized,
                "accessory" => new Vector3(0.76f, 0.28f, -0.45f).normalized,
                _ => new Vector3(-0.72f, 0.14f, -0.34f).normalized
            };
        }

        private static OntologyGraphNodeKind ToNodeKind(string predicate)
        {
            if (predicate == OntologyPredicates.GrantsCapability || predicate == OntologyPredicates.ConflictsWithSlot)
            {
                return OntologyGraphNodeKind.Functional;
            }

            if (predicate == OntologyPredicates.Provides)
            {
                return OntologyGraphNodeKind.Simulation;
            }

            if (predicate == "covers" || predicate == "style")
            {
                return OntologyGraphNodeKind.Visual;
            }

            return OntologyGraphNodeKind.Semantic;
        }

        private static Color ColorForKind(OntologyGraphNodeKind kind, float alpha)
        {
            var color = kind switch
            {
                OntologyGraphNodeKind.Center => new Color(1f, 0.78f, 0.28f, 1f),
                OntologyGraphNodeKind.Visual => new Color(0.78f, 0.86f, 1f, 1f),
                OntologyGraphNodeKind.Semantic => new Color(0.34f, 0.64f, 1f, 1f),
                OntologyGraphNodeKind.Functional => new Color(0.38f, 1f, 0.64f, 1f),
                OntologyGraphNodeKind.Simulation => new Color(0.72f, 0.48f, 1f, 1f),
                _ => new Color(0.86f, 0.88f, 0.94f, 1f)
            };
            color.a = alpha;
            return color;
        }

        private static string Localize(string termId, OntologyTermLocalization localization, OntologyLanguage language)
        {
            return OntologyLocalizer.Format(termId, language, localization);
        }

        private static string BuildPartSummaryLabel(
            OntologyCharacterPartDefinition definition,
            OntologyTermLocalization localization,
            OntologyLanguage language)
        {
            var builder = new StringBuilder(Localize(definition.partId, localization, language));
            var facts = definition.facts ?? System.Array.Empty<OntologyFactEntry>();
            for (var i = 0; i < facts.Length; i++)
            {
                var fact = facts[i];
                if (fact == null || string.IsNullOrWhiteSpace(fact.predicate) || string.IsNullOrWhiteSpace(fact.obj))
                {
                    continue;
                }

                builder.AppendLine();
                builder.Append(Localize(fact.predicate, localization, language));
                builder.Append(" -> ");
                builder.Append(Localize(fact.obj, localization, language));
            }

            return builder.ToString();
        }
    }
}
