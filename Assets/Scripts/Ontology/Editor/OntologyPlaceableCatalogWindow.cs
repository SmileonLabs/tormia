using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>Registers arbitrary prefabs in a placement catalog without changing runtime code.</summary>
    public sealed class OntologyPlaceableCatalogWindow : EditorWindow
    {
        private const string DefaultCatalogPath = "Assets/Data/Ontology/Placeables/PolyStylePlaceables.asset";
        private OntologyPlaceableCatalog catalog;
        private int selectedIndex = -1;
        private Vector2 scroll;
        private string definitionId;
        private string displayNameKey;
        private string displayName;
        private OntologyPlaceableKind placementKind = OntologyPlaceableKind.Object;
        private string category = "Nature";
        private string description;
        private GameObject prefab;
        private GameObject previewPrefab;
        private OntologyMapObjectTemplate template;
        private OntologyPlacementPolicy policy = new();
        private bool useAuthoredPreviewTransform;
        private Vector3 previewLocalPosition;
        private Vector3 previewLocalEulerAngles;
        private Vector3 previewLocalScale = Vector3.one;
        private OntologyPhysicalProfile physicalProfile;
        private OntologyAttachmentProfile attachmentProfile;
        private List<OntologyRuleBlockBinding> defaultRuleBlocks = new();

        [MenuItem("Tools/Ontology/Placeable Catalog")]
        public static void Open() => GetWindow<OntologyPlaceableCatalogWindow>("Placeable Catalog");

        private void OnEnable()
        {
            if (catalog == null) catalog = AssetDatabase.LoadAssetAtPath<OntologyPlaceableCatalog>(DefaultCatalogPath);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Runtime Placeable Catalog", EditorStyles.boldLabel);
            catalog = (OntologyPlaceableCatalog)EditorGUILayout.ObjectField("Catalog", catalog, typeof(OntologyPlaceableCatalog), false);
            if (catalog == null)
            {
                EditorGUILayout.HelpBox("Assign a placeable catalog to add reusable objects.", MessageType.Info);
                return;
            }

            DrawExistingEntries();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(selectedIndex >= 0 ? "Edit Selected Entry" : "Register New Entry", EditorStyles.boldLabel);
            definitionId = EditorGUILayout.TextField("Stable Id", definitionId);
            displayNameKey = EditorGUILayout.TextField("Language Key", displayNameKey);
            displayName = EditorGUILayout.TextField("Display Name", displayName);
            if (GUILayout.Button(
                    "Suggest Missing Name Metadata",
                    EditorStyles.miniButton))
            {
                SuggestMissingNameMetadata();
            }
            placementKind = (OntologyPlaceableKind)EditorGUILayout.EnumPopup("Placement Kind", placementKind);
            category = EditorGUILayout.TextField("Category", category);
            description = EditorGUILayout.TextArea(description, GUILayout.MinHeight(38));
            prefab = (GameObject)EditorGUILayout.ObjectField("Placement Prefab", prefab, typeof(GameObject), false);
            previewPrefab = (GameObject)EditorGUILayout.ObjectField("Preview Prefab (optional)", previewPrefab, typeof(GameObject), false);
            template = (OntologyMapObjectTemplate)EditorGUILayout.ObjectField("Ontology Template", template, typeof(OntologyMapObjectTemplate), false);
            DrawPreviewAuthoring();
            DrawPolicy();
            DrawSemanticBehaviour();

            using (new EditorGUI.DisabledScope(prefab == null || string.IsNullOrWhiteSpace(definitionId)))
            {
                if (GUILayout.Button(selectedIndex >= 0 ? "Save Entry" : "Add Entry")) SaveEntry();
            }
            if (selectedIndex >= 0 && GUILayout.Button("Cancel Edit")) ClearDraft();
        }

        private void DrawExistingEntries()
        {
            EditorGUILayout.LabelField("Registered Objects (" + catalog.Definitions.Count + ")", EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MaxHeight(140));
            for (var i = 0; i < catalog.Definitions.Count; i++)
            {
                var entry = catalog.Definitions[i];
                if (entry == null) continue;
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(
                        entry.EffectiveDisplayName + "  [" + entry.placementKind + " / " + entry.category + "]",
                        EditorStyles.miniButtonLeft))
                    LoadDraft(i);
                if (GUILayout.Button("Remove", EditorStyles.miniButtonRight, GUILayout.Width(58))) Remove(i);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawPolicy()
        {
            policy ??= new OntologyPlacementPolicy();
            EditorGUILayout.LabelField("Placement Policy", EditorStyles.miniBoldLabel);
            policy.maximumDistanceFromActor = EditorGUILayout.FloatField("Maximum Distance", policy.maximumDistanceFromActor);
            policy.maximumSlopeAngle = EditorGUILayout.Slider("Maximum Slope", policy.maximumSlopeAngle, 0f, 89f);
            policy.minimumDistanceFromPlacedObject = EditorGUILayout.FloatField("Minimum Object Distance", policy.minimumDistanceFromPlacedObject);
            policy.requiredSurface = (OntologyPlacementSurfaceKind)EditorGUILayout.EnumPopup("Required Surface", policy.requiredSurface);
            policy.allowUnclassifiedSurface = EditorGUILayout.Toggle("Allow Unclassified", policy.allowUnclassifiedSurface);
            policy.alignToSurfaceNormal = EditorGUILayout.Toggle("Align to Surface", policy.alignToSurfaceNormal);
            policy.randomYawOnPlacement = EditorGUILayout.Toggle("Random Yaw", policy.randomYawOnPlacement);
            policy.defaultLocalScale = EditorGUILayout.Vector3Field("Default Scale", policy.defaultLocalScale);
        }

        private void DrawPreviewAuthoring()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Preview Authoring", EditorStyles.miniBoldLabel);
            useAuthoredPreviewTransform = EditorGUILayout.Toggle(
                "Use Authored Adjustment",
                useAuthoredPreviewTransform);
            using (new EditorGUI.DisabledScope(!useAuthoredPreviewTransform))
            {
                previewLocalPosition = EditorGUILayout.Vector3Field(
                    "Local Position Offset",
                    previewLocalPosition);
                previewLocalEulerAngles = EditorGUILayout.Vector3Field(
                    "Local Rotation Offset",
                    previewLocalEulerAngles);
                previewLocalScale = EditorGUILayout.Vector3Field(
                    "Local Scale Multiplier",
                    previewLocalScale);
            }
        }

        private void DrawSemanticBehaviour()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Semantic Behaviour", EditorStyles.miniBoldLabel);
            physicalProfile = (OntologyPhysicalProfile)EditorGUILayout.ObjectField(
                "Initial Physical Profile",
                physicalProfile,
                typeof(OntologyPhysicalProfile),
                false);
            attachmentProfile = (OntologyAttachmentProfile)EditorGUILayout.ObjectField(
                "Attachment Profile",
                attachmentProfile,
                typeof(OntologyAttachmentProfile),
                false);

            defaultRuleBlocks ??= new List<OntologyRuleBlockBinding>();
            EditorGUILayout.LabelField(
                "Default Rule Blocks (" + defaultRuleBlocks.Count + ")",
                EditorStyles.miniLabel);
            for (var index = 0; index < defaultRuleBlocks.Count; index++)
            {
                var binding = defaultRuleBlocks[index] ?? new OntologyRuleBlockBinding();
                defaultRuleBlocks[index] = binding;
                EditorGUILayout.BeginHorizontal();
                binding.ruleId = EditorGUILayout.TextField(binding.ruleId);
                binding.bindingVariable = EditorGUILayout.TextField(
                    binding.bindingVariable,
                    GUILayout.Width(90));
                if (GUILayout.Button("-", GUILayout.Width(24)))
                {
                    defaultRuleBlocks.RemoveAt(index);
                    index--;
                }
                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("+ Add Default Rule Block", EditorStyles.miniButton))
            {
                defaultRuleBlocks.Add(new OntologyRuleBlockBinding());
            }
        }

        private void LoadDraft(int index)
        {
            var entry = catalog.Definitions[index];
            selectedIndex = index; definitionId = entry.definitionId; displayNameKey = entry.displayNameKey; displayName = entry.displayName;
            placementKind = entry.placementKind; category = entry.category;
            description = entry.description; prefab = entry.prefab; previewPrefab = entry.previewPrefab; template = entry.ontologyTemplate;
            policy = ClonePolicy(entry.placementPolicy);
            useAuthoredPreviewTransform = entry.useAuthoredPreviewTransform;
            previewLocalPosition = entry.previewLocalPosition;
            previewLocalEulerAngles = entry.previewLocalEulerAngles;
            previewLocalScale = entry.previewLocalScale;
            physicalProfile = entry.physicalProfile;
            attachmentProfile = entry.attachmentProfile;
            defaultRuleBlocks = CloneRuleBlocks(entry.defaultRuleBlocks);
        }

        private void SaveEntry()
        {
            var entries = catalog.Definitions.ToList();
            var duplicate = entries.FindIndex(entry => entry != null && entry.definitionId == definitionId);
            if (duplicate >= 0 && duplicate != selectedIndex)
            {
                EditorUtility.DisplayDialog("Duplicate Id", "Stable Id must be unique.", "OK");
                return;
            }
            var entry = new OntologyPlaceableDefinition
            {
                definitionId = definitionId.Trim(),
                displayNameKey = displayNameKey?.Trim(),
                displayName = displayName?.Trim(),
                placementKind = placementKind,
                category = category?.Trim(),
                description = description,
                prefab = prefab,
                previewPrefab = previewPrefab,
                useAuthoredPreviewTransform = useAuthoredPreviewTransform,
                previewLocalPosition = previewLocalPosition,
                previewLocalEulerAngles = previewLocalEulerAngles,
                previewLocalScale = previewLocalScale,
                ontologyTemplate = template,
                placementPolicy = ClonePolicy(policy),
                physicalProfile = physicalProfile,
                attachmentProfile = attachmentProfile,
                defaultRuleBlocks = CloneRuleBlocks(defaultRuleBlocks)
            };
            Undo.RecordObject(catalog, selectedIndex >= 0 ? "Edit placeable entry" : "Add placeable entry");
            if (selectedIndex >= 0) entries[selectedIndex] = entry; else entries.Add(entry);
            catalog.ReplaceDefinitions(entries);
            EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
            ClearDraft(); Selection.activeObject = catalog;
        }

        private void SuggestMissingNameMetadata()
        {
            var stableId = string.IsNullOrWhiteSpace(definitionId)
                ? prefab != null ? prefab.name : string.Empty
                : definitionId.Trim();
            if (string.IsNullOrWhiteSpace(stableId))
                return;

            if (string.IsNullOrWhiteSpace(displayNameKey))
                displayNameKey = "placeable." + stableId;
            if (!string.IsNullOrWhiteSpace(displayName))
                return;

            var sourceName = prefab != null ? prefab.name : stableId;
            if (sourceName.EndsWith(
                    "(Clone)",
                    System.StringComparison.OrdinalIgnoreCase))
            {
                sourceName = sourceName.Substring(
                    0,
                    sourceName.Length - "(Clone)".Length);
            }
            displayName = ObjectNames.NicifyVariableName(sourceName.Trim());
        }

        private void Remove(int index)
        {
            var entry = catalog.Definitions[index];
            if (!EditorUtility.DisplayDialog("Remove Placeable", "Remove '" + entry.EffectiveDisplayName + "' from this catalog? The prefab and ontology template are not deleted.", "Remove", "Cancel")) return;
            var entries = catalog.Definitions.ToList(); Undo.RecordObject(catalog, "Remove placeable entry"); entries.RemoveAt(index);
            catalog.ReplaceDefinitions(entries); EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets(); ClearDraft();
        }

        private void ClearDraft()
        {
            selectedIndex = -1;
            definitionId = displayNameKey = displayName = description = string.Empty;
            category = "Nature";
            placementKind = OntologyPlaceableKind.Object;
            prefab = previewPrefab = null;
            template = null;
            policy = new OntologyPlacementPolicy();
            useAuthoredPreviewTransform = false;
            previewLocalPosition = Vector3.zero;
            previewLocalEulerAngles = Vector3.zero;
            previewLocalScale = Vector3.one;
            physicalProfile = null;
            attachmentProfile = null;
            defaultRuleBlocks = new List<OntologyRuleBlockBinding>();
        }

        private static List<OntologyRuleBlockBinding> CloneRuleBlocks(
            IEnumerable<OntologyRuleBlockBinding> values)
        {
            return values == null
                ? new List<OntologyRuleBlockBinding>()
                : values
                    .Where(value => value != null)
                    .Select(value => new OntologyRuleBlockBinding
                    {
                        ruleId = value.ruleId,
                        bindingVariable = value.bindingVariable
                    })
                    .ToList();
        }

        private static OntologyPlacementPolicy ClonePolicy(OntologyPlacementPolicy value) => value == null ? new OntologyPlacementPolicy() : new OntologyPlacementPolicy
        {
            maximumDistanceFromActor = value.maximumDistanceFromActor, maximumSlopeAngle = value.maximumSlopeAngle,
            minimumDistanceFromPlacedObject = value.minimumDistanceFromPlacedObject, requiredSurface = value.requiredSurface,
            allowUnclassifiedSurface = value.allowUnclassifiedSurface, alignToSurfaceNormal = value.alignToSurfaceNormal,
            randomYawOnPlacement = value.randomYawOnPlacement, defaultLocalScale = value.defaultLocalScale
        };
    }
}
