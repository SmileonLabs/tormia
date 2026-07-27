using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Shared low-level projection for account previews, creator assistants, and
    /// remote avatars. It changes renderers only and never writes ontology facts.
    /// Slot ownership and equip rules remain with the caller.
    /// </summary>
    public static class OntologyCharacterAppearanceProjector
    {
        public static bool ApplyPresentation(
            OntologyCharacterPartDatabase database,
            Transform visualRoot,
            IReadOnlyList<string> requestedPartIds,
            bool useDefaultsWhenEmpty = true)
        {
            if (database?.Definitions == null || visualRoot == null)
            {
                return false;
            }

            var activeIds = new HashSet<string>(StringComparer.Ordinal);
            if (requestedPartIds != null)
            {
                foreach (var partId in requestedPartIds)
                {
                    if (!string.IsNullOrWhiteSpace(partId))
                    {
                        activeIds.Add(partId);
                    }
                }
            }

            if (useDefaultsWhenEmpty && activeIds.Count == 0)
            {
                foreach (var definition in database.Definitions)
                {
                    if (definition != null &&
                        definition.enabledByDefault &&
                        !string.IsNullOrWhiteSpace(definition.partId))
                    {
                        activeIds.Add(definition.partId);
                    }
                }
            }

            ExpandLinkedPartIds(database, activeIds);

            var clearedPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var definition in database.Definitions)
            {
                if (definition == null ||
                    string.IsNullOrWhiteSpace(definition.rendererPath) ||
                    !clearedPaths.Add(definition.rendererPath))
                {
                    continue;
                }

                var renderer = FindRenderer(visualRoot, definition.rendererPath);
                if (renderer != null)
                {
                    renderer.enabled = false;
                }
            }

            foreach (var definition in database.Definitions)
            {
                if (definition == null ||
                    string.IsNullOrWhiteSpace(definition.partId) ||
                    !activeIds.Contains(definition.partId))
                {
                    continue;
                }

                var renderer = FindRenderer(visualRoot, definition.rendererPath);
                if (renderer == null)
                {
                    continue;
                }

                renderer.enabled = true;
                if (!renderer.gameObject.activeSelf)
                {
                    renderer.gameObject.SetActive(true);
                }

                if (!ApplyVariantMeshAndMaterials(definition, renderer) &&
                    definition.material != null)
                {
                    renderer.sharedMaterial = definition.material;
                }
            }

            return true;
        }

        public static void ExpandLinkedPartIds(
            OntologyCharacterPartDatabase database,
            ISet<string> activeIds)
        {
            if (database?.Definitions == null || activeIds == null)
            {
                return;
            }

            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var definition in database.Definitions)
                {
                    if (definition == null || !activeIds.Contains(definition.partId))
                    {
                        continue;
                    }

                    foreach (var linkedPartId in definition.linkedPartIds ?? Array.Empty<string>())
                    {
                        changed |= !string.IsNullOrWhiteSpace(linkedPartId) &&
                                   activeIds.Add(linkedPartId);
                    }
                }
            }
        }

        public static Renderer FindRenderer(Transform visualRoot, string rendererPath)
        {
            if (visualRoot == null || string.IsNullOrWhiteSpace(rendererPath))
            {
                return null;
            }

            var normalized = rendererPath.StartsWith(
                visualRoot.name + "/",
                StringComparison.Ordinal)
                ? rendererPath.Substring(visualRoot.name.Length + 1)
                : rendererPath;
            return visualRoot.Find(normalized)?.GetComponent<Renderer>();
        }

        public static bool ApplyVariantMeshAndMaterials(
            OntologyCharacterPartDefinition definition,
            Renderer targetRenderer)
        {
            if (definition?.variantPrefab == null ||
                targetRenderer is not SkinnedMeshRenderer targetSkinnedRenderer)
            {
                return false;
            }

            var sourceRenderer =
                definition.variantPrefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (sourceRenderer == null || sourceRenderer.sharedMesh == null)
            {
                return false;
            }

            targetSkinnedRenderer.sharedMesh = sourceRenderer.sharedMesh;
            targetSkinnedRenderer.sharedMaterials = sourceRenderer.sharedMaterials;
            targetSkinnedRenderer.localBounds = sourceRenderer.localBounds;
            return true;
        }
    }
}
