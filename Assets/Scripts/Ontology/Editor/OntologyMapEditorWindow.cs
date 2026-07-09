using System.Collections.Generic;
using System.IO;
using System.Text;
using Tormia.Ontology.Core;
using UnityEditor;
using UnityEngine;

namespace Tormia.Ontology.Editor
{
    public sealed class OntologyMapEditorWindow : EditorWindow
    {
        private const string DefaultMapFolder = "Assets/Data/Ontology/Maps";
        private const string DefaultTemplateFolder = "Assets/Data/Ontology/MapTileTemplates";
        private const string RuleDatabasePath = "Assets/Data/Ontology/RuleDatabase.asset";

        private readonly List<OntologyTileTemplate> templates = new();
        private readonly Dictionary<string, OntologyTileTemplate> templateById = new();

        private OntologyMapData mapData;
        private OntologyTileTemplate selectedTemplate;
        private OntologyRuleDatabase ruleDatabase;
        private Vector2Int selectedCoordinate = new(-1, -1);
        private Vector2 leftScroll;
        private Vector2 gridScroll;
        private Vector2 rightScroll;
        private Vector2 reportScroll;
        private float cellSize = 34f;
        private string newMapName = "NewOntologyMapData";
        private string newTemplateName = "Tile_New";
        private string validationReport = "Validation report will appear here.";
        private bool validationHasErrors;

        [MenuItem("Tormia/Ontology Map Editor")]
        public static void Open()
        {
            GetWindow<OntologyMapEditorWindow>("Ontology Map");
        }

        private void OnEnable()
        {
            ruleDatabase = AssetDatabase.LoadAssetAtPath<OntologyRuleDatabase>(RuleDatabasePath);
            LoadTemplates();
        }

        private void OnGUI()
        {
            DrawToolbar();

            EditorGUILayout.BeginHorizontal();
            DrawTemplatePane(260f);
            DrawGridPane();
            DrawInspectorPane(330f);
            EditorGUILayout.EndHorizontal();

            DrawValidationPane();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            mapData = (OntologyMapData)EditorGUILayout.ObjectField(mapData, typeof(OntologyMapData), false, GUILayout.Width(300f));
            EditorGUILayout.LabelField("New Map", GUILayout.Width(60f));
            newMapName = EditorGUILayout.TextField(newMapName, GUILayout.Width(170f));
            if (GUILayout.Button("Create Map Data", EditorStyles.toolbarButton, GUILayout.Width(120f)))
            {
                mapData = CreateMapDataAsset();
            }

            if (GUILayout.Button("Rename Selected", EditorStyles.toolbarButton, GUILayout.Width(120f)))
            {
                RenameSelectedMapDataAsset();
            }

            if (GUILayout.Button("Reload Templates", EditorStyles.toolbarButton, GUILayout.Width(120f)))
            {
                LoadTemplates();
            }

            if (GUILayout.Button("Build This Map To Scene", EditorStyles.toolbarButton, GUILayout.Width(170f)))
            {
                BuildCurrentMapToScene();
            }

            GUILayout.FlexibleSpace();
            ruleDatabase = (OntologyRuleDatabase)EditorGUILayout.ObjectField(ruleDatabase, typeof(OntologyRuleDatabase), false, GUILayout.Width(260f));
            EditorGUILayout.EndHorizontal();

            if (mapData != null)
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.BeginHorizontal();
                mapData.mapWidth = Mathf.Max(1, EditorGUILayout.IntField("Width", mapData.mapWidth, GUILayout.Width(180f)));
                mapData.mapHeight = Mathf.Max(1, EditorGUILayout.IntField("Height", mapData.mapHeight, GUILayout.Width(180f)));
                cellSize = Mathf.Clamp(EditorGUILayout.FloatField("Cell Size", cellSize, GUILayout.Width(200f)), 18f, 80f);
                EditorGUILayout.EndHorizontal();
                if (EditorGUI.EndChangeCheck())
                {
                    MarkMapDirty();
                }
            }
        }

        private void DrawTemplatePane(float width)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(width), GUILayout.ExpandHeight(true));
            EditorGUILayout.LabelField("Template Palette", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Select a template, then click cells in the grid to paint placements.", MessageType.None);

            EditorGUILayout.BeginHorizontal();
            newTemplateName = EditorGUILayout.TextField(newTemplateName);
            if (GUILayout.Button("New", GUILayout.Width(56f)))
            {
                selectedTemplate = CreateTemplateAsset(newTemplateName);
                LoadTemplates();
            }
            EditorGUILayout.EndHorizontal();

            leftScroll = EditorGUILayout.BeginScrollView(leftScroll);
            foreach (var template in templates)
            {
                if (template == null)
                {
                    continue;
                }

                var oldColor = GUI.backgroundColor;
                GUI.backgroundColor = template.previewColor;
                if (GUILayout.Toggle(selectedTemplate == template, template.EffectiveTemplateId, "Button"))
                {
                    selectedTemplate = template;
                }
                GUI.backgroundColor = oldColor;
            }
            EditorGUILayout.EndScrollView();

            DrawSelectedTemplateEditor();
            EditorGUILayout.EndVertical();
        }

        private void DrawSelectedTemplateEditor()
        {
            if (selectedTemplate == null)
            {
                EditorGUILayout.HelpBox("No tile template selected.", MessageType.Info);
                return;
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Template Editor", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            selectedTemplate.templateId = EditorGUILayout.TextField("Template Id", selectedTemplate.templateId);
            selectedTemplate.previewColor = EditorGUILayout.ColorField("Preview Color", selectedTemplate.previewColor);
            selectedTemplate.previewMaterial = (Material)EditorGUILayout.ObjectField("Material", selectedTemplate.previewMaterial, typeof(Material), false);
            selectedTemplate.tilePrefab = (GameObject)EditorGUILayout.ObjectField("Prefab", selectedTemplate.tilePrefab, typeof(GameObject), false);
            DrawFactTemplateList("Base Facts", selectedTemplate.baseFacts);
            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(selectedTemplate);
                LoadTemplates();
            }
        }

        private void DrawGridPane()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUILayout.LabelField("Grid Map View", EditorStyles.boldLabel);
            if (mapData == null)
            {
                EditorGUILayout.HelpBox("Assign or create an OntologyMapData asset.", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            gridScroll = EditorGUILayout.BeginScrollView(gridScroll, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            for (var y = mapData.mapHeight - 1; y >= 0; y--)
            {
                EditorGUILayout.BeginHorizontal();
                for (var x = 0; x < mapData.mapWidth; x++)
                {
                    DrawGridCell(x, y);
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawGridCell(int x, int y)
        {
            var placement = GetPlacement(x, y);
            var template = placement != null ? FindTemplate(placement.templateId) : null;
            var label = template != null ? ShortLabel(template.EffectiveTemplateId) : "+";
            var oldColor = GUI.backgroundColor;
            GUI.backgroundColor = template != null ? template.previewColor : Color.gray;
            if (selectedCoordinate.x == x && selectedCoordinate.y == y)
            {
                GUI.backgroundColor = Color.yellow;
            }

            if (GUILayout.Button(label, GUILayout.Width(cellSize), GUILayout.Height(cellSize)))
            {
                selectedCoordinate = new Vector2Int(x, y);
                if (selectedTemplate != null)
                {
                    PaintTile(x, y, selectedTemplate);
                }
            }
            GUI.backgroundColor = oldColor;
        }

        private void DrawInspectorPane(float width)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(width), GUILayout.ExpandHeight(true));
            EditorGUILayout.LabelField("Fact Inspector", EditorStyles.boldLabel);
            rightScroll = EditorGUILayout.BeginScrollView(rightScroll);

            if (mapData == null || selectedCoordinate.x < 0 || selectedCoordinate.y < 0)
            {
                EditorGUILayout.HelpBox("Select a grid cell to inspect final facts.", MessageType.Info);
                EditorGUILayout.EndScrollView();
                EditorGUILayout.EndVertical();
                return;
            }

            var subject = GetTileSubject(selectedCoordinate.x, selectedCoordinate.y);
            var placement = GetPlacement(selectedCoordinate.x, selectedCoordinate.y);
            if (placement == null)
            {
                EditorGUILayout.LabelField("Coordinate", selectedCoordinate.ToString());
                EditorGUILayout.LabelField("Subject", subject);
                EditorGUILayout.LabelField("Template", "<none>");
                EditorGUILayout.HelpBox("This cell has no placement yet. Select a template in the left palette and click this grid cell to paint it.", MessageType.Info);
                if (selectedTemplate != null && GUILayout.Button("Paint Selected Template Here"))
                {
                    PaintTile(selectedCoordinate.x, selectedCoordinate.y, selectedTemplate);
                }

                EditorGUILayout.EndScrollView();
                EditorGUILayout.EndVertical();
                return;
            }

            var template = FindTemplate(placement.templateId);

            EditorGUILayout.LabelField("Coordinate", selectedCoordinate.ToString());
            EditorGUILayout.LabelField("Subject", subject);
            EditorGUILayout.LabelField("Template", string.IsNullOrWhiteSpace(placement.templateId) ? "<none>" : placement.templateId);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Final Facts Preview", EditorStyles.boldLabel);
            foreach (var fact in BuildFinalFactPreview(placement))
            {
                EditorGUILayout.LabelField(fact, EditorStyles.miniLabel);
            }

            if (template == null)
            {
                EditorGUILayout.HelpBox("This placement has no valid template. Select a template and click the cell to paint it.", MessageType.Warning);
            }

            EditorGUILayout.Space(8f);
            EditorGUI.BeginChangeCheck();
            DrawFactTemplateList("Unique Facts", placement.uniqueFacts);
            if (EditorGUI.EndChangeCheck())
            {
                MarkMapDirty();
            }

            if (GUILayout.Button("Clear Tile Placement"))
            {
                mapData.placements.Remove(placement);
                MarkMapDirty();
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawValidationPane()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Height(165f));
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Dry Run & Validation", EditorStyles.boldLabel);
            if (GUILayout.Button("Validate Map Facts", GUILayout.Width(180f)))
            {
                ValidateMapFacts();
            }
            EditorGUILayout.EndHorizontal();

            var oldColor = GUI.contentColor;
            GUI.contentColor = validationHasErrors ? Color.red : Color.green;
            reportScroll = EditorGUILayout.BeginScrollView(reportScroll);
            EditorGUILayout.TextArea(validationReport, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
            GUI.contentColor = oldColor;
            EditorGUILayout.EndVertical();
        }

        private void DrawFactTemplateList(string title, List<OntologyFactTemplateEntry> facts)
        {
            if (facts == null)
            {
                return;
            }

            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            for (var i = 0; i < facts.Count; i++)
            {
                var fact = facts[i];
                if (fact == null)
                {
                    facts[i] = new OntologyFactTemplateEntry();
                    fact = facts[i];
                }

                EditorGUILayout.BeginHorizontal();
                fact.predicate = EditorGUILayout.TextField(fact.predicate);
                fact.obj = EditorGUILayout.TextField(fact.obj);
                if (GUILayout.Button("-", GUILayout.Width(24f)))
                {
                    facts.RemoveAt(i);
                    i--;
                }
                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("Add Fact"))
            {
                facts.Add(new OntologyFactTemplateEntry { predicate = "surface", obj = "Grass" });
            }
        }

        private void ValidateMapFacts()
        {
            var builder = new StringBuilder();
            var hasErrors = false;

            builder.AppendLine("[Map Validation]");
            var warnings = ValidateData();
            foreach (var warning in warnings)
            {
                hasErrors = true;
                builder.AppendLine("ERROR: " + warning);
            }

            if (warnings.Count == 0)
            {
                builder.AppendLine("Data integrity: ok");
            }

            if (ruleDatabase == null)
            {
                hasErrors = true;
                builder.AppendLine("ERROR: RuleDatabase is missing.");
            }
            else
            {
                var ruleWarnings = OntologyRuleValidator.Validate(ruleDatabase.Definitions);
                if (ruleWarnings.Count == 0)
                {
                    builder.AppendLine("RuleDatabase: ok");
                }
                else
                {
                    hasErrors = true;
                    foreach (var warning in ruleWarnings)
                    {
                        builder.AppendLine("ERROR: " + warning);
                    }
                }
            }

            if (!hasErrors && mapData != null && ruleDatabase != null)
            {
                RunDrySimulation(builder);
            }

            validationHasErrors = hasErrors;
            validationReport = builder.ToString().TrimEnd();
        }

        private List<string> ValidateData()
        {
            var warnings = new List<string>();
            if (mapData == null)
            {
                warnings.Add("MapData is missing.");
                return warnings;
            }

            if (mapData.mapWidth < 1 || mapData.mapHeight < 1)
            {
                warnings.Add("Map dimensions must be greater than zero.");
            }

            var seenCoordinates = new HashSet<Vector2Int>();
            var seenTemplateIds = new HashSet<string>();
            foreach (var template in templates)
            {
                if (template == null)
                {
                    continue;
                }

                var id = template.EffectiveTemplateId;
                if (string.IsNullOrWhiteSpace(id))
                {
                    warnings.Add("A tile template has an empty templateId.");
                }
                else if (!seenTemplateIds.Add(id))
                {
                    warnings.Add("Duplicated templateId: " + id);
                }

                ValidateFactEntries("Template " + id, template.baseFacts, warnings);
            }

            foreach (var placement in mapData.placements)
            {
                if (placement == null)
                {
                    warnings.Add("A placement entry is null.");
                    continue;
                }

                if (!seenCoordinates.Add(placement.coordinate))
                {
                    warnings.Add("Duplicated placement coordinate: " + placement.coordinate);
                }

                if (placement.coordinate.x < 0 || placement.coordinate.y < 0 || placement.coordinate.x >= mapData.mapWidth || placement.coordinate.y >= mapData.mapHeight)
                {
                    warnings.Add("Placement out of bounds: " + placement.coordinate);
                }

                if (string.IsNullOrWhiteSpace(placement.templateId))
                {
                    warnings.Add("Placement " + placement.coordinate + " has an empty templateId.");
                }
                else if (FindTemplate(placement.templateId) == null)
                {
                    warnings.Add("Placement " + placement.coordinate + " references missing templateId: " + placement.templateId);
                }

                ValidateFactEntries("Placement " + placement.coordinate, placement.uniqueFacts, warnings);
            }

            return warnings;
        }

        private void RunDrySimulation(StringBuilder builder)
        {
            builder.AppendLine();
            builder.AppendLine("[Dry Run]");
            var engine = new OntologyRuleEngine();
            OntologyDefaultRules.RegisterRules(engine, ruleDatabase.Definitions);
            var simulation = new OntologySimulation(maxIterations: 1);

            if (mapData.placements.Count == 0)
            {
                builder.AppendLine("No placements to simulate.");
                return;
            }

            foreach (var placement in mapData.placements)
            {
                var world = BuildWorldFromMap();
                var subject = GetTileSubject(placement.coordinate.x, placement.coordinate.y);
                world.AddFact("Player", "standing_on", subject);
                world.AddFact("Player", "core_vitality", "10");
                world.AddFact("Simulation", "current_tick", "DryRunTick");

                var result = simulation.RunUntilStable(world, engine);
                builder.AppendLine(subject + ":");
                var produced = 0;
                foreach (var step in result.Steps)
                {
                    foreach (var ontologyEvent in step.Events)
                    {
                        foreach (var fact in ontologyEvent.AddedFacts)
                        {
                            produced++;
                            builder.AppendLine("+ " + fact);
                        }

                        foreach (var fact in ontologyEvent.SetFacts)
                        {
                            produced++;
                            builder.AppendLine("= " + fact);
                        }

                        foreach (var fact in ontologyEvent.AdjustedNumberFacts)
                        {
                            produced++;
                            builder.AppendLine("~ " + fact.Subject + " " + fact.Predicate + " " + fact.Object);
                        }
                    }
                }

                if (produced == 0)
                {
                    builder.AppendLine("- no rule output");
                }
            }
        }

        private OntologyWorldState BuildWorldFromMap()
        {
            var world = new OntologyWorldState();
            foreach (var placement in mapData.placements)
            {
                if (placement == null || string.IsNullOrWhiteSpace(placement.templateId))
                {
                    continue;
                }

                var template = FindTemplate(placement.templateId);
                if (template == null)
                {
                    continue;
                }

                var subject = GetTileSubject(placement.coordinate.x, placement.coordinate.y);
                world.AddConcept(subject, "TerrainTile");
                world.AddFact(subject, "grid_x", placement.coordinate.x.ToString());
                world.AddFact(subject, "grid_y", placement.coordinate.y.ToString());
                world.AddFact(subject, "tile_type", template.EffectiveTemplateId);
                AddEntriesToWorld(world, subject, template.baseFacts);
                AddEntriesToWorld(world, subject, placement.uniqueFacts);
            }

            return world;
        }

        private void AddEntriesToWorld(OntologyWorldState world, string subject, List<OntologyFactTemplateEntry> entries)
        {
            if (entries == null)
            {
                return;
            }

            foreach (var entry in entries)
            {
                if (entry != null && !string.IsNullOrWhiteSpace(entry.predicate) && !string.IsNullOrWhiteSpace(entry.obj))
                {
                    world.AddFact(subject, entry.predicate, entry.obj);
                }
            }
        }

        private List<string> BuildFinalFactPreview(TilePlacementData placement)
        {
            var facts = new List<string>();
            if (placement == null)
            {
                return facts;
            }

            var subject = GetTileSubject(placement.coordinate.x, placement.coordinate.y);
            facts.Add(subject + " has_concept TerrainTile");
            facts.Add(subject + " grid_x " + placement.coordinate.x);
            facts.Add(subject + " grid_y " + placement.coordinate.y);
            facts.Add(subject + " tile_type " + placement.templateId);

            var template = FindTemplate(placement.templateId);
            if (template != null)
            {
                AppendPreviewFacts(facts, subject, template.baseFacts);
            }

            AppendPreviewFacts(facts, subject, placement.uniqueFacts);
            return facts;
        }

        private void AppendPreviewFacts(List<string> output, string subject, List<OntologyFactTemplateEntry> entries)
        {
            if (entries == null)
            {
                return;
            }

            foreach (var entry in entries)
            {
                if (entry != null && !string.IsNullOrWhiteSpace(entry.predicate) && !string.IsNullOrWhiteSpace(entry.obj))
                {
                    output.Add(subject + " " + entry.predicate + " " + entry.obj);
                }
            }
        }

        private void PaintTile(int x, int y, OntologyTileTemplate template)
        {
            if (mapData == null || template == null)
            {
                return;
            }

            var placement = GetOrCreatePlacement(x, y);
            placement.templateId = template.EffectiveTemplateId;
            MarkMapDirty();
        }

        private TilePlacementData GetPlacement(int x, int y)
        {
            if (mapData == null)
            {
                return null;
            }

            foreach (var placement in mapData.placements)
            {
                if (placement != null && placement.coordinate.x == x && placement.coordinate.y == y)
                {
                    return placement;
                }
            }

            return null;
        }

        private TilePlacementData GetOrCreatePlacement(int x, int y)
        {
            var placement = GetPlacement(x, y);
            if (placement != null)
            {
                return placement;
            }

            placement = new TilePlacementData
            {
                coordinate = new Vector2Int(x, y)
            };
            mapData.placements.Add(placement);
            MarkMapDirty();
            return placement;
        }

        private OntologyTileTemplate FindTemplate(string templateId)
        {
            if (string.IsNullOrWhiteSpace(templateId))
            {
                return null;
            }

            templateById.TryGetValue(templateId, out var template);
            return template;
        }

        private void LoadTemplates()
        {
            templates.Clear();
            templateById.Clear();
            var guids = AssetDatabase.FindAssets("t:OntologyTileTemplate");
            if (guids.Length == 0 && AssetDatabase.IsValidFolder(DefaultTemplateFolder))
            {
                guids = AssetDatabase.FindAssets("", new[] { DefaultTemplateFolder });
            }

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var template = AssetDatabase.LoadAssetAtPath<OntologyTileTemplate>(path);
                if (template == null)
                {
                    continue;
                }

                templates.Add(template);
                var id = template.EffectiveTemplateId;
                if (!string.IsNullOrWhiteSpace(id) && !templateById.ContainsKey(id))
                {
                    templateById.Add(id, template);
                }
            }
        }

        private OntologyMapData CreateMapDataAsset()
        {
            EnsureFolder(DefaultMapFolder);
            var safeName = GetSafeAssetName(newMapName, "NewOntologyMapData");
            var path = AssetDatabase.GenerateUniqueAssetPath(DefaultMapFolder + "/" + safeName + ".asset");
            var asset = CreateInstance<OntologyMapData>();
            asset.name = safeName;
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
            validationHasErrors = false;
            validationReport = "[Create Map Data]\nCreated map asset: " + path;
            return asset;
        }

        private void RenameSelectedMapDataAsset()
        {
            if (mapData == null)
            {
                validationHasErrors = true;
                validationReport = "[Rename Map Data]\nERROR: MapData is missing.";
                return;
            }

            var safeName = GetSafeAssetName(newMapName, mapData.name);
            var path = AssetDatabase.GetAssetPath(mapData);
            if (string.IsNullOrWhiteSpace(path))
            {
                validationHasErrors = true;
                validationReport = "[Rename Map Data]\nERROR: Selected MapData is not an asset.";
                return;
            }

            var error = AssetDatabase.RenameAsset(path, safeName);
            if (!string.IsNullOrWhiteSpace(error))
            {
                validationHasErrors = true;
                validationReport = "[Rename Map Data]\nERROR: " + error;
                return;
            }

            mapData.name = safeName;
            EditorUtility.SetDirty(mapData);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = mapData;
            EditorGUIUtility.PingObject(mapData);
            validationHasErrors = false;
            validationReport = "[Rename Map Data]\nRenamed selected map to: " + safeName;
        }

        private OntologyTileTemplate CreateTemplateAsset(string assetName)
        {
            EnsureFolder(DefaultTemplateFolder);
            var safeName = string.IsNullOrWhiteSpace(assetName) ? "NewTileTemplate" : assetName.Trim();
            var path = AssetDatabase.GenerateUniqueAssetPath(DefaultTemplateFolder + "/" + safeName + ".asset");
            var asset = CreateInstance<OntologyTileTemplate>();
            asset.templateId = safeName;
            asset.previewColor = Color.white;
            asset.baseFacts.Add(new OntologyFactTemplateEntry { predicate = "surface", obj = "Grass" });
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            return asset;
        }

        private static void EnsureFolder(string path)
        {
            var parts = path.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }

        private void ValidateFactEntries(string owner, List<OntologyFactTemplateEntry> entries, List<string> warnings)
        {
            if (entries == null)
            {
                return;
            }

            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null)
                {
                    warnings.Add(owner + " fact[" + i + "] is null.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(entry.predicate))
                {
                    warnings.Add(owner + " fact[" + i + "] has empty predicate.");
                }

                if (string.IsNullOrWhiteSpace(entry.obj))
                {
                    warnings.Add(owner + " fact[" + i + "] has empty object.");
                }
            }
        }

        private string GetTileSubject(int x, int y)
        {
            return "Tile_" + x + "_" + y;
        }

        private string ShortLabel(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "?";
            }

            return value.Length <= 4 ? value : value.Substring(0, 4);
        }

        private void MarkMapDirty()
        {
            if (mapData != null)
            {
                EditorUtility.SetDirty(mapData);
            }
        }

        private void BuildCurrentMapToScene()
        {
            if (mapData == null)
            {
                validationHasErrors = true;
                validationReport = "[Build Scene]\nERROR: MapData is missing.";
                return;
            }

            LoadTemplates();
            var builder = FindOrCreateSceneBuilder();
            ConfigureSceneBuilder(builder);
            Undo.RegisterFullObjectHierarchyUndo(builder.gameObject, "Build Ontology Map To Scene");
            builder.BuildSceneTiles();
            EditorUtility.SetDirty(builder.gameObject);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(builder.gameObject.scene);

            validationHasErrors = false;
            validationReport = "[Build Scene]\nBuilt map '" + mapData.name + "' to scene.\n"
                + "Builder: " + builder.gameObject.name + "\n"
                + "Generated tiles: " + builder.transform.childCount;
        }

        private OntologyMapDataSceneBuilder FindOrCreateSceneBuilder()
        {
            var builder = FindFirstObjectByType<OntologyMapDataSceneBuilder>();
            if (builder != null)
            {
                builder.gameObject.name = GetSceneBuilderName();
                return builder;
            }

            var root = new GameObject(GetSceneBuilderName());
            Undo.RegisterCreatedObjectUndo(root, "Create Ontology Map Scene Builder");
            return root.AddComponent<OntologyMapDataSceneBuilder>();
        }

        private string GetSceneBuilderName()
        {
            var mapName = mapData != null ? mapData.name : "Map";
            return "Map_" + GetSafeAssetName(mapName, "Map");
        }

        private string GetSafeAssetName(string value, string fallback)
        {
            var safeName = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            var invalidChars = Path.GetInvalidFileNameChars();
            for (var i = 0; i < invalidChars.Length; i++)
            {
                safeName = safeName.Replace(invalidChars[i], '_');
            }

            return string.IsNullOrWhiteSpace(safeName) ? fallback : safeName;
        }

        private void ConfigureSceneBuilder(OntologyMapDataSceneBuilder builder)
        {
            var serialized = new SerializedObject(builder);
            serialized.FindProperty("mapData").objectReferenceValue = mapData;
            serialized.FindProperty("gridRoot").objectReferenceValue = builder.transform;
            serialized.FindProperty("tileSize").floatValue = 1f;
            serialized.FindProperty("resetWorldAfterBuild").boolValue = true;

            var templateList = serialized.FindProperty("tileTemplates");
            templateList.arraySize = templates.Count;
            for (var i = 0; i < templates.Count; i++)
            {
                templateList.GetArrayElementAtIndex(i).objectReferenceValue = templates[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
