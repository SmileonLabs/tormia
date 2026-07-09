using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Characters
{
    public class CharacterAppearanceApplier : MonoBehaviour
    {
        [Serializable]
        public class PartOption
        {
            public string displayName;
            public Mesh mesh;
            public Mesh linkedHatMesh;
        }

        [Serializable]
        public class PartCategory
        {
            public string displayName;
            public SkinnedMeshRenderer targetRenderer;
            public PartOption[] options;
        }

        [Serializable]
        private class SavedCharacterData
        {
            public SavedCategorySelection[] selections;
        }

        [Serializable]
        private class SavedCategorySelection
        {
            public string categoryName;
            public int selectedIndex;
        }

        private struct RendererDefaultState
        {
            public SkinnedMeshRenderer renderer;
            public Mesh mesh;
            public Bounds localBounds;
            public bool enabled;
        }

        private const string SavedCharacterKey = "Tormia.CharacterCreator.SelectedParts";

        [SerializeField] private PartCategory[] partCategories;

        private RendererDefaultState[] rendererDefaults;

        private void Awake()
        {
            CacheDefaultRendererState();
            ApplySavedAppearance();
        }

        public void ApplySavedAppearance()
        {
            RestoreDefaultRendererState();

            if (partCategories == null || !PlayerPrefs.HasKey(SavedCharacterKey))
            {
                ApplyDefaultAppearance();
                return;
            }

            var data = JsonUtility.FromJson<SavedCharacterData>(PlayerPrefs.GetString(SavedCharacterKey));
            if (data?.selections == null)
            {
                ApplyDefaultAppearance();
                return;
            }

            PartCategory costumeCategory = null;
            var costumeIndex = -1;

            foreach (var selection in data.selections)
            {
                var category = FindCategory(selection.categoryName);
                if (category == null || category.targetRenderer == null || category.options == null)
                {
                    continue;
                }

                if (IsCostumeCategory(category))
                {
                    costumeCategory = category;
                    costumeIndex = selection.selectedIndex;
                    continue;
                }

                var index = Mathf.Clamp(selection.selectedIndex, 0, Mathf.Max(0, category.options.Length - 1));
                if (!IsCostumeOverriddenCategory(category))
                {
                    ApplyMeshOption(category.targetRenderer, category.options[index]);
                }
            }

            if (costumeCategory != null && costumeIndex >= 0 && costumeIndex < costumeCategory.options.Length)
            {
                ApplyCostume(costumeCategory, costumeCategory.options[costumeIndex]);
            }
            else if (costumeCategory?.targetRenderer != null)
            {
                costumeCategory.targetRenderer.enabled = false;
            }
        }

        public IReadOnlyList<string> GetCurrentOntologyPartIds()
        {
            var partIds = new List<string>();
            if (partCategories == null)
            {
                return partIds;
            }

            if (!PlayerPrefs.HasKey(SavedCharacterKey))
            {
                AddDefaultPartIds(partIds);
                return partIds;
            }

            var data = JsonUtility.FromJson<SavedCharacterData>(PlayerPrefs.GetString(SavedCharacterKey));
            if (data?.selections == null)
            {
                AddDefaultPartIds(partIds);
                return partIds;
            }

            PartCategory costumeCategory = null;
            var costumeIndex = -1;
            foreach (var selection in data.selections)
            {
                var category = FindCategory(selection.categoryName);
                if (!HasOptions(category))
                {
                    continue;
                }

                if (IsCostumeCategory(category))
                {
                    costumeCategory = category;
                    costumeIndex = selection.selectedIndex;
                    continue;
                }

                if (IsCostumeOverriddenCategory(category))
                {
                    continue;
                }

                var index = Mathf.Clamp(selection.selectedIndex, 0, category.options.Length - 1);
                AddPartId(partIds, category.options[index]);
            }

            if (costumeCategory != null && costumeIndex >= 0 && costumeIndex < costumeCategory.options.Length)
            {
                AddPartId(partIds, costumeCategory.options[costumeIndex]);
            }

            return partIds;
        }

        private void AddDefaultPartIds(List<string> partIds)
        {
            foreach (var category in partCategories)
            {
                if (!HasOptions(category) || IsCostumeCategory(category))
                {
                    continue;
                }

                AddPartId(partIds, category.options[0]);
            }
        }

        private static void AddPartId(List<string> partIds, PartOption option)
        {
            if (option == null)
            {
                return;
            }

            AddMeshNameOrDisplayName(partIds, option.mesh, option.displayName);
            AddMeshNameOrDisplayName(partIds, option.linkedHatMesh, string.Empty);
        }

        private static void AddMeshNameOrDisplayName(List<string> partIds, Mesh mesh, string displayName)
        {
            var partId = mesh != null && !string.IsNullOrWhiteSpace(mesh.name)
                ? mesh.name
                : displayName;

            if (!string.IsNullOrWhiteSpace(partId) && !partIds.Contains(partId))
            {
                partIds.Add(partId);
            }
        }

        private void ApplyDefaultAppearance()
        {
            if (partCategories == null)
            {
                return;
            }

            foreach (var category in partCategories)
            {
                if (!HasOptions(category) || category.targetRenderer == null)
                {
                    continue;
                }

                if (IsCostumeCategory(category))
                {
                    category.targetRenderer.enabled = false;
                    continue;
                }

                ApplyMeshOption(category.targetRenderer, category.options[0]);
            }
        }

        private void ApplyCostume(PartCategory category, PartOption option)
        {
            if (option == null || option.mesh == null)
            {
                return;
            }

            SetCostumeOverriddenRenderersEnabled(false);
            ApplyMeshOption(category.targetRenderer, option);
            ApplyLinkedHatMesh(option);
        }

        private void ApplyLinkedHatMesh(PartOption option)
        {
            if (option == null || option.linkedHatMesh == null)
            {
                return;
            }

            var hat = FindCategory("Hat");
            if (hat != null)
            {
                ApplyMeshOption(hat.targetRenderer, new PartOption { mesh = option.linkedHatMesh });
            }
        }

        private static void ApplyMeshOption(SkinnedMeshRenderer renderer, PartOption option)
        {
            if (renderer == null || option == null || option.mesh == null)
            {
                return;
            }

            renderer.sharedMesh = option.mesh;
            renderer.localBounds = option.mesh.bounds;
            renderer.enabled = true;
        }

        private void CacheDefaultRendererState()
        {
            var renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            rendererDefaults = new RendererDefaultState[renderers.Length];
            for (var i = 0; i < renderers.Length; i++)
            {
                rendererDefaults[i] = new RendererDefaultState
                {
                    renderer = renderers[i],
                    mesh = renderers[i].sharedMesh,
                    localBounds = renderers[i].localBounds,
                    enabled = renderers[i].enabled
                };
            }
        }

        private void RestoreDefaultRendererState()
        {
            if (rendererDefaults == null)
            {
                return;
            }

            foreach (var state in rendererDefaults)
            {
                if (state.renderer == null)
                {
                    continue;
                }

                state.renderer.sharedMesh = state.mesh;
                state.renderer.localBounds = state.localBounds;
                state.renderer.enabled = state.enabled;
            }
        }

        private void SetCostumeOverriddenRenderersEnabled(bool enabled)
        {
            if (partCategories == null)
            {
                return;
            }

            foreach (var category in partCategories)
            {
                if (category?.targetRenderer != null && IsCostumeOverriddenCategory(category))
                {
                    category.targetRenderer.enabled = enabled;
                }
            }
        }

        private PartCategory FindCategory(string categoryName)
        {
            if (partCategories == null || string.IsNullOrEmpty(categoryName))
            {
                return null;
            }

            foreach (var category in partCategories)
            {
                if (category != null && string.Equals(category.displayName, categoryName, StringComparison.OrdinalIgnoreCase))
                {
                    return category;
                }
            }

            return null;
        }

        private static bool HasOptions(PartCategory category)
        {
            return category != null && category.options != null && category.options.Length > 0;
        }

        private static bool IsCostumeCategory(PartCategory category)
        {
            return category != null && string.Equals(category.displayName, "Costume", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCostumeOverriddenCategory(PartCategory category)
        {
            if (category == null)
            {
                return false;
            }

            return string.Equals(category.displayName, "Outfit", StringComparison.OrdinalIgnoreCase)
                || string.Equals(category.displayName, "Pants", StringComparison.OrdinalIgnoreCase)
                || string.Equals(category.displayName, "Shoes", StringComparison.OrdinalIgnoreCase)
                || string.Equals(category.displayName, "Gloves", StringComparison.OrdinalIgnoreCase)
                || string.Equals(category.displayName, "Hat", StringComparison.OrdinalIgnoreCase);
        }
    }
}
