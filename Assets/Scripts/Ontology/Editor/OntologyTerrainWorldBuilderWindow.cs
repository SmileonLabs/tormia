using System;
using System.Collections.Generic;
using System.Linq;
using Tormia.Ontology.Core;
using UnityEditor;
using UnityEngine;

namespace Tormia.Ontology.Editor
{
    /// <summary>
    /// Editor-facing entry point for converting loaded Unity Terrains into
    /// inspectable ontology zones.  Visual terrain remains read-only here.
    /// </summary>
    public sealed class OntologyTerrainWorldBuilderWindow : EditorWindow
    {
        private OntologyTerrainWorldData worldData;
        private Vector2 zoneScroll;
        private Vector2 detailScroll;
        private string filter = string.Empty;
        private int selectedZoneIndex = -1;
        private string newPredicate = "state";
        private string newObject = "";
        private string status;

        [MenuItem("Tools/Ontology/Terrain World Builder")]
        public static void Open()
        {
            var window = GetWindow<OntologyTerrainWorldBuilderWindow>("Terrain World Builder");
            window.titleContent = new GUIContent("Terrain World Builder");
            window.TryLoadDefaultWorld();
        }

        private void OnEnable()
        {
            minSize = new Vector2(950f, 560f);
            TryLoadDefaultWorld();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Terrain World Builder", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Unity Terrain is the visual source. Analysis creates separate semantic Zones and automatic Facts; manual Facts are your intentional exceptions and survive re-analysis.",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            worldData = (OntologyTerrainWorldData)EditorGUILayout.ObjectField("Terrain World Data", worldData, typeof(OntologyTerrainWorldData), false);
            if (EditorGUI.EndChangeCheck()) selectedZoneIndex = -1;
            if (GUILayout.Button("New Data", GUILayout.Width(95f))) CreateWorldData();
            EditorGUILayout.EndHorizontal();

            if (worldData == null)
            {
                EditorGUILayout.HelpBox("Create or assign a Terrain World Data asset to begin.", MessageType.Warning);
                return;
            }

            DrawSourceAndAnalysis();
            DrawMappings();
            EditorGUILayout.Space(8f);
            EditorGUILayout.BeginHorizontal();
            DrawZoneLibrary();
            DrawZoneDetail();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSourceAndAnalysis()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("1. Visual Source and Analysis", EditorStyles.boldLabel);
            var loadedTerrains = Terrain.activeTerrains ?? Array.Empty<Terrain>();
            EditorGUILayout.LabelField($"Loaded Terrain sources: {loadedTerrains.Length}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("If no explicit source is stored, analysis uses all loaded Terrains. This avoids storing fragile scene-object references in a project asset.", EditorStyles.wordWrappedMiniLabel);

            EditorGUI.BeginChangeCheck();
            var resolution = EditorGUILayout.Vector2IntField("Zone Resolution", worldData.zoneResolution);
            resolution.x = Mathf.Max(1, resolution.x);
            resolution.y = Mathf.Max(1, resolution.y);
            if (EditorGUI.EndChangeCheck()) RecordChange("Change terrain zone resolution", () => worldData.zoneResolution = resolution);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Analyze Loaded Terrains", GUILayout.Height(26f))) AnalyzeLoadedTerrains();
            if (GUILayout.Button("Clear Analysed Zones", GUILayout.Height(26f))) ClearZones();
            EditorGUILayout.EndHorizontal();
            if (!string.IsNullOrWhiteSpace(status)) EditorGUILayout.HelpBox(status, MessageType.None);
            EditorGUILayout.EndVertical();
        }

        private void DrawMappings()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("2. Terrain Layer Meaning Mappings", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Optional: when a Terrain Layer covers enough of a Zone, its mapped Facts are added automatically.", EditorStyles.wordWrappedMiniLabel);
            worldData.terrainLayerMappings ??= new List<OntologyTerrainLayerMapping>();

            for (var index = worldData.terrainLayerMappings.Count - 1; index >= 0; index--)
            {
                var mapping = worldData.terrainLayerMappings[index] ?? new OntologyTerrainLayerMapping();
                if (worldData.terrainLayerMappings[index] == null) worldData.terrainLayerMappings[index] = mapping;
                mapping.facts ??= new List<OntologyFactTemplateEntry>();
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUI.BeginChangeCheck();
                var layer = (TerrainLayer)EditorGUILayout.ObjectField("Terrain Layer", mapping.terrainLayer, typeof(TerrainLayer), false);
                var coverage = EditorGUILayout.Slider("Minimum Coverage", mapping.minimumCoverage, 0f, 1f);
                if (EditorGUI.EndChangeCheck()) RecordChange("Edit terrain layer mapping", () =>
                {
                    mapping.terrainLayer = layer;
                    mapping.minimumCoverage = coverage;
                });
                DrawFactEntries(mapping.facts, () => RecordChange("Add mapped terrain fact", () => mapping.facts.Add(new OntologyFactTemplateEntry { predicate = "surface", obj = "NewSurface" })));
                if (GUILayout.Button("Remove Mapping")) RecordChange("Remove terrain layer mapping", () => worldData.terrainLayerMappings.RemoveAt(index));
                EditorGUILayout.EndVertical();
            }
            if (GUILayout.Button("Add Terrain Layer Mapping")) RecordChange("Add terrain layer mapping", () => worldData.terrainLayerMappings.Add(new OntologyTerrainLayerMapping()));
            EditorGUILayout.EndVertical();
        }

        private void DrawZoneLibrary()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(355f), GUILayout.ExpandHeight(true));
            EditorGUILayout.LabelField("3. Semantic Zones", EditorStyles.boldLabel);
            filter = EditorGUILayout.TextField("Search", filter);
            var zones = worldData.zones ?? new List<OntologyTerrainZoneData>();
            EditorGUILayout.LabelField($"{zones.Count} analysed zone(s)", EditorStyles.miniLabel);
            zoneScroll = EditorGUILayout.BeginScrollView(zoneScroll);
            for (var index = 0; index < zones.Count; index++)
            {
                var zone = zones[index];
                if (zone == null || !Matches(zone)) continue;
                var label = string.IsNullOrWhiteSpace(zone.zoneId) ? "(Unnamed Zone)" : zone.zoneId;
                if (GUILayout.Toggle(selectedZoneIndex == index, label, "Button")) selectedZoneIndex = index;
                EditorGUILayout.LabelField($"{zone.terrainName}  |  height {zone.averageHeight:F1}  |  slope {zone.averageSlope:F1}\u00b0", EditorStyles.miniLabel);
                EditorGUILayout.Space(3f);
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawZoneDetail()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            var zones = worldData.zones;
            if (zones == null || selectedZoneIndex < 0 || selectedZoneIndex >= zones.Count || zones[selectedZoneIndex] == null)
            {
                EditorGUILayout.HelpBox("Run analysis, then choose a Zone to inspect its ontology facts.", MessageType.None);
                EditorGUILayout.EndVertical();
                return;
            }

            var zone = zones[selectedZoneIndex];
            EditorGUILayout.LabelField(zone.zoneId, EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Source: {zone.terrainName}  |  Grid: ({zone.coordinate.x}, {zone.coordinate.y})", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Bounds: {zone.worldBounds.center:F1} / {zone.worldBounds.size:F1}", EditorStyles.miniLabel);
            detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
            DrawReadOnlyFacts("Automatic Facts (terrain-derived)", zone.automaticFacts);
            EditorGUILayout.Space(7f);
            EditorGUILayout.LabelField("Manual Facts (designer-authored)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Use this only for intended semantic exceptions or additions. It does not change the Unity Terrain material or geometry.", MessageType.None);
            DrawEditableFacts(zone);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawReadOnlyFacts(string title, List<OntologyFactTemplateEntry> facts)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            if (facts == null || facts.Count == 0)
            {
                EditorGUILayout.LabelField("No facts", EditorStyles.miniLabel);
                return;
            }
            foreach (var fact in facts)
            {
                if (fact == null) continue;
                EditorGUILayout.LabelField($"{fact.predicate}  →  {fact.obj}", EditorStyles.wordWrappedMiniLabel);
            }
        }

        private void DrawEditableFacts(OntologyTerrainZoneData zone)
        {
            zone.manualFacts ??= new List<OntologyFactTemplateEntry>();
            for (var index = zone.manualFacts.Count - 1; index >= 0; index--)
            {
                var fact = zone.manualFacts[index];
                if (fact == null)
                {
                    RecordChange("Remove empty terrain fact", () => zone.manualFacts.RemoveAt(index));
                    continue;
                }
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                var predicate = EditorGUILayout.TextField(fact.predicate ?? string.Empty);
                var obj = EditorGUILayout.TextField(fact.obj ?? string.Empty);
                if (EditorGUI.EndChangeCheck()) RecordChange("Edit manual terrain fact", () => { fact.predicate = predicate; fact.obj = obj; });
                if (GUILayout.Button("X", GUILayout.Width(26f))) RecordChange("Remove manual terrain fact", () => zone.manualFacts.RemoveAt(index));
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.BeginHorizontal();
            newPredicate = EditorGUILayout.TextField(newPredicate);
            newObject = EditorGUILayout.TextField(newObject);
            if (GUILayout.Button("Add", GUILayout.Width(58f)))
            {
                if (string.IsNullOrWhiteSpace(newPredicate) || string.IsNullOrWhiteSpace(newObject))
                    status = "A Fact needs both a relation and a value.";
                else
                {
                    var predicate = newPredicate.Trim();
                    var obj = newObject.Trim();
                    RecordChange("Add manual terrain fact", () => zone.manualFacts.Add(new OntologyFactTemplateEntry { predicate = predicate, obj = obj }));
                    newObject = string.Empty;
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawFactEntries(List<OntologyFactTemplateEntry> facts, Action addFact)
        {
            facts ??= new List<OntologyFactTemplateEntry>();
            for (var factIndex = facts.Count - 1; factIndex >= 0; factIndex--)
            {
                var fact = facts[factIndex];
                if (fact == null) continue;
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                var predicate = EditorGUILayout.TextField(fact.predicate ?? string.Empty);
                var obj = EditorGUILayout.TextField(fact.obj ?? string.Empty);
                if (EditorGUI.EndChangeCheck()) RecordChange("Edit mapped terrain fact", () => { fact.predicate = predicate; fact.obj = obj; });
                if (GUILayout.Button("X", GUILayout.Width(26f))) RecordChange("Remove mapped terrain fact", () => facts.RemoveAt(factIndex));
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button("Add Mapping Fact")) addFact();
        }

        private void AnalyzeLoadedTerrains()
        {
            if (Terrain.activeTerrains == null || Terrain.activeTerrains.Length == 0)
            {
                status = "No loaded Terrain was found. Open the Terrain demo scene first.";
                return;
            }
            // Scene object references are intentionally not written to this asset.
            // An empty source list makes the analyzer read all currently loaded Terrains.
            Undo.RecordObject(worldData, "Analyze ontology terrain world");
            worldData.terrain = null;
            worldData.terrains.Clear();
            var summary = OntologyTerrainAnalyzer.Analyze(worldData);
            EditorUtility.SetDirty(worldData);
            AssetDatabase.SaveAssets();
            selectedZoneIndex = worldData.zones.Count > 0 ? 0 : -1;
            status = $"Analysis finished: {summary.zoneCount} Zone(s) created." +
                     (summary.warnings.Count > 0 ? "\n" + string.Join("\n", summary.warnings) : string.Empty);
        }

        private void ClearZones()
        {
            if (!EditorUtility.DisplayDialog("Clear analysed zones", "Remove all generated Zones and their manual Facts from this Terrain World Data asset?", "Clear", "Cancel")) return;
            RecordChange("Clear ontology terrain zones", () => worldData.zones.Clear());
            selectedZoneIndex = -1;
            status = "Analysed Zones cleared.";
        }

        private void CreateWorldData()
        {
            const string directory = "Assets/Data/Ontology";
            if (!AssetDatabase.IsValidFolder("Assets/Data")) AssetDatabase.CreateFolder("Assets", "Data");
            if (!AssetDatabase.IsValidFolder(directory)) AssetDatabase.CreateFolder("Assets/Data", "Ontology");
            var path = AssetDatabase.GenerateUniqueAssetPath(directory + "/TerrainWorldData.asset");
            worldData = CreateInstance<OntologyTerrainWorldData>();
            AssetDatabase.CreateAsset(worldData, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = worldData;
            status = "Created " + path;
        }

        private void RecordChange(string label, Action mutation)
        {
            Undo.RecordObject(worldData, label);
            mutation();
            EditorUtility.SetDirty(worldData);
            AssetDatabase.SaveAssets();
        }

        private bool Matches(OntologyTerrainZoneData zone)
        {
            if (string.IsNullOrWhiteSpace(filter)) return true;
            return (zone.zoneId ?? string.Empty).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                   || (zone.terrainName ?? string.Empty).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                   || (zone.automaticFacts ?? new List<OntologyFactTemplateEntry>()).Any(fact => fact != null && ((fact.predicate ?? string.Empty).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 || (fact.obj ?? string.Empty).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private void TryLoadDefaultWorld()
        {
            if (worldData != null) return;
            var guids = AssetDatabase.FindAssets("t:OntologyTerrainWorldData");
            if (guids.Length == 0) return;
            worldData = AssetDatabase.LoadAssetAtPath<OntologyTerrainWorldData>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }
}
