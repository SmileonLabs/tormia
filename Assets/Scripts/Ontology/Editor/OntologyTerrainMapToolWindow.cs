using Tormia.Ontology.Core;
using UnityEditor;
using UnityEngine;

namespace Tormia.Ontology.Editor
{
    public sealed class OntologyTerrainMapToolWindow : EditorWindow
    {
        private OntologyTerrainTileGridBuilder builder;
        private SerializedObject serializedBuilder;
        private OntologyTerrainTileDefinition selectedDefinition;
        private int paintX;
        private int paintY;
        private int fillMinX;
        private int fillMinY;
        private int fillMaxX = 2;
        private int fillMaxY = 2;

        [MenuItem("Tormia/Ontology Terrain Map Tool")]
        public static void Open()
        {
            GetWindow<OntologyTerrainMapToolWindow>("Ontology Terrain");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Ontology Terrain Map Tool", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Create a small ontology tile grid. Each generated tile gets an OntologyObject with TerrainTile concepts and tile facts.", MessageType.Info);

            builder = (OntologyTerrainTileGridBuilder)EditorGUILayout.ObjectField("Grid Builder", builder, typeof(OntologyTerrainTileGridBuilder), true);
            if (builder == null && GUILayout.Button("Create Grid Builder In Scene"))
            {
                var root = new GameObject("OntologyTerrainGrid");
                builder = root.AddComponent<OntologyTerrainTileGridBuilder>();
                Selection.activeGameObject = root;
            }

            if (builder != null)
            {
                DrawBuilderInspector();
                DrawPaintingTools();
            }

            EditorGUILayout.Space(12f);
            if (GUILayout.Button("Create/Update Default Tile Definitions"))
            {
                CreateDefaultTileDefinitions();
            }
        }

        private void DrawBuilderInspector()
        {
            if (serializedBuilder == null || serializedBuilder.targetObject != builder)
            {
                serializedBuilder = new SerializedObject(builder);
            }

            serializedBuilder.Update();
            var property = serializedBuilder.GetIterator();
            var enterChildren = true;
            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (property.name == "m_Script")
                {
                    continue;
                }

                EditorGUILayout.PropertyField(property, true);
            }

            serializedBuilder.ApplyModifiedProperties();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Rebuild Grid"))
            {
                Undo.RegisterFullObjectHierarchyUndo(builder.gameObject, "Rebuild Ontology Tile Grid");
                builder.RebuildGrid();
                EditorUtility.SetDirty(builder.gameObject);
            }

            if (GUILayout.Button("Update Existing Grid"))
            {
                Undo.RegisterFullObjectHierarchyUndo(builder.gameObject, "Update Ontology Tile Grid");
                builder.UpdateExistingGrid();
                EditorUtility.SetDirty(builder.gameObject);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Clear Grid"))
            {
                Undo.RegisterFullObjectHierarchyUndo(builder.gameObject, "Clear Ontology Tile Grid");
                builder.ClearGrid();
                EditorUtility.SetDirty(builder.gameObject);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawPaintingTools()
        {
            EditorGUILayout.Space(12f);
            EditorGUILayout.LabelField("Coordinate Paint", EditorStyles.boldLabel);
            selectedDefinition = (OntologyTerrainTileDefinition)EditorGUILayout.ObjectField("Selected Tile", selectedDefinition, typeof(OntologyTerrainTileDefinition), false);

            EditorGUILayout.BeginHorizontal();
            paintX = EditorGUILayout.IntField("X", paintX);
            paintY = EditorGUILayout.IntField("Y", paintY);
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Apply Tile At Coordinate"))
            {
                Undo.RegisterFullObjectHierarchyUndo(builder.gameObject, "Apply Ontology Tile");
                builder.ApplyTileDefinitionAndStoreOverride(paintX, paintY, selectedDefinition);
                EditorUtility.SetDirty(builder);
                EditorUtility.SetDirty(builder.gameObject);
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Fill Rectangle", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            fillMinX = EditorGUILayout.IntField("Min X", fillMinX);
            fillMinY = EditorGUILayout.IntField("Min Y", fillMinY);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            fillMaxX = EditorGUILayout.IntField("Max X", fillMaxX);
            fillMaxY = EditorGUILayout.IntField("Max Y", fillMaxY);
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Fill Rectangle With Selected Tile"))
            {
                Undo.RegisterFullObjectHierarchyUndo(builder.gameObject, "Fill Ontology Tile Rectangle");
                builder.FillRectangle(fillMinX, fillMinY, fillMaxX, fillMaxY, selectedDefinition);
                EditorUtility.SetDirty(builder);
                EditorUtility.SetDirty(builder.gameObject);
            }
        }

        private static void CreateDefaultTileDefinitions()
        {
            EnsureFolder("Assets/Data", "Ontology");
            EnsureFolder("Assets/Data/Ontology", "Tiles");
            EnsureFolder("Assets/Data/Ontology", "TileMaterials");

            CreateOrUpdate("GrassTile", new Color(0.32f, 0.62f, 0.28f),
                new OntologyFactEntry { predicate = "surface", obj = "Grass" });
            CreateOrUpdate("WaterTile", new Color(0.18f, 0.45f, 0.9f),
                new OntologyFactEntry { predicate = "surface", obj = "ShallowWater" },
                new OntologyFactEntry { predicate = "has_element", obj = "Water" });
            CreateOrUpdate("SwampTile", new Color(0.24f, 0.28f, 0.12f),
                new OntologyFactEntry { predicate = "surface", obj = "Swamp" },
                new OntologyFactEntry { predicate = "has_element", obj = "Water" });
            CreateOrUpdate("FrozenTile", new Color(0.68f, 0.9f, 1f),
                new OntologyFactEntry { predicate = "surface", obj = "Ice" },
                new OntologyFactEntry { predicate = "temperature", obj = "Freezing" });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void EnsureFolder(string parent, string folderName)
        {
            var path = parent + "/" + folderName;
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, folderName);
            }
        }

        private static void CreateOrUpdate(string assetName, Color color, params OntologyFactEntry[] facts)
        {
            var path = "Assets/Data/Ontology/Tiles/" + assetName + ".asset";
            var asset = AssetDatabase.LoadAssetAtPath<OntologyTerrainTileDefinition>(path);
            if (asset == null)
            {
                asset = CreateInstance<OntologyTerrainTileDefinition>();
                AssetDatabase.CreateAsset(asset, path);
            }

            var material = CreateOrUpdateMaterial(assetName, color);
            asset.Configure(assetName, color, facts, material);
            EditorUtility.SetDirty(asset);
        }

        private static Material CreateOrUpdateMaterial(string assetName, Color color)
        {
            var materialPath = "Assets/Data/Ontology/TileMaterials/" + assetName + "_Mat.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, materialPath);
            }

            material.color = color;
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
