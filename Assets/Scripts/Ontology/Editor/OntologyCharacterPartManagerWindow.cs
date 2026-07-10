using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Tormia.Ontology.Core.Editor
{
    public sealed class OntologyCharacterPartManagerWindow : EditorWindow
    {
        private const string DefaultDatabasePath = "Assets/Data/Ontology/CharacterPartDatabase.asset";
        private const string ThumbnailFolderPath = "Assets/Data/Ontology/CharacterPartThumbnails";
        private const string WindowTitle = "Character Part Manager";
        private const string AllCategories = "All";

        private readonly string[] tabs = { "Database", "Validation", "Runtime Test", "Bulk Tools", "Settings" };
        private readonly string[] categoryOptions = OntologyCharacterCustomizationUiConfig.CategoryOrder;
        private readonly string[] categoryFilterOptions = CategoryFilterOptions;
        private readonly string[] knownPredicates =
        {
            "covers",
            "provides",
            OntologyPredicates.GrantsCapability,
            OntologyPredicates.ConflictsWithSlot,
            "style"
        };

        private readonly string[] knownObjects = KnownObjects;

        private OntologyCharacterPartDatabase database;
        private SerializedObject databaseObject;
        private SerializedProperty definitionsProperty;
        private int selectedIndex;
        private int selectedTab;
        private string search = string.Empty;
        private int categoryFilterIndex;
        private Vector2 leftScroll;
        private Vector2 rightScroll;
        private Vector2 reportScroll;
        private string prefabSelectionWarning = string.Empty;

        private GameObject previewTarget;
        private OntologyCharacterPartAdapter adapter;
        private OntologyWorldBootstrap bootstrap;
        private OntologySaveController saveController;
        private string report = string.Empty;

        [MenuItem("Tools/Ontology/Character Part Manager")]
        public static void Open()
        {
            GetWindow<OntologyCharacterPartManagerWindow>(WindowTitle);
        }

        private static string[] CategoryFilterOptions
        {
            get
            {
                var options = new string[OntologyCharacterCustomizationUiConfig.CategoryOrder.Length + 1];
                options[0] = AllCategories;
                OntologyCharacterCustomizationUiConfig.CategoryOrder.CopyTo(options, 1);
                return options;
            }
        }

        private static string[] KnownObjects
        {
            get
            {
                var categories = OntologyCharacterCustomizationUiConfig.CategoryOrder;
                var objects = new string[categories.Length + 3];
                categories.CopyTo(objects, 0);
                objects[categories.Length] = "Warmth";
                objects[categories.Length + 1] = OntologyObjects.SwampResistance;
                objects[categories.Length + 2] = OntologyObjects.ColdProtection;
                return objects;
            }
        }

        private void OnEnable()
        {
            if (database == null)
            {
                database = AssetDatabase.LoadAssetAtPath<OntologyCharacterPartDatabase>(DefaultDatabasePath);
            }

            EnsureDatabaseObject();
            FindRuntimeTargets();
        }

        private void OnGUI()
        {
            DrawToolbar();
            selectedTab = GUILayout.Toolbar(selectedTab, tabs);
            EditorGUILayout.Space(6f);

            if (database == null)
            {
                EditorGUILayout.HelpBox("Assign a CharacterPartDatabase asset to begin.", MessageType.Info);
                return;
            }

            EnsureDatabaseObject();
            databaseObject.Update();

            switch (selectedTab)
            {
                case 0:
                    DrawDatabaseTab();
                    break;
                case 1:
                    DrawValidationTab();
                    break;
                case 2:
                    DrawRuntimeTestTab();
                    break;
                case 3:
                    DrawBulkToolsTab();
                    break;
                default:
                    DrawSettingsTab();
                    break;
            }

            databaseObject.ApplyModifiedProperties();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var nextDatabase = (OntologyCharacterPartDatabase)EditorGUILayout.ObjectField(database, typeof(OntologyCharacterPartDatabase), false, GUILayout.MinWidth(260f));
                if (nextDatabase != database)
                {
                    database = nextDatabase;
                    selectedIndex = 0;
                    EnsureDatabaseObject();
                }

                if (GUILayout.Button("Load Default", EditorStyles.toolbarButton, GUILayout.Width(90f)))
                {
                    database = AssetDatabase.LoadAssetAtPath<OntologyCharacterPartDatabase>(DefaultDatabasePath);
                    selectedIndex = 0;
                    EnsureDatabaseObject();
                }

                if (GUILayout.Button("Find Runtime", EditorStyles.toolbarButton, GUILayout.Width(95f)))
                {
                    FindRuntimeTargets();
                }

                if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(55f)))
                {
                    SaveDatabase();
                }

                GUILayout.FlexibleSpace();
            }
        }

        private void DrawDatabaseTab()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawPartList();
                DrawPartEditor();
            }
        }

        private void DrawPartList()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(310f)))
            {
                EditorGUILayout.LabelField("Parts", EditorStyles.boldLabel);
                search = EditorGUILayout.TextField("Search", search);
                categoryFilterIndex = EditorGUILayout.Popup("Category", categoryFilterIndex, categoryFilterOptions);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Add"))
                    {
                        AddPart();
                    }

                    if (GUILayout.Button("Duplicate"))
                    {
                        DuplicatePart();
                    }

                    if (GUILayout.Button("Delete"))
                    {
                        DeletePart();
                    }
                }

                if (GUILayout.Button("Sort By Slot"))
                {
                    SortBySlot();
                }

                leftScroll = EditorGUILayout.BeginScrollView(leftScroll);
                var lastVisibleSlot = string.Empty;
                for (var i = 0; i < definitionsProperty.arraySize; i++)
                {
                    var element = definitionsProperty.GetArrayElementAtIndex(i);
                    if (!PassesListFilter(element))
                    {
                        continue;
                    }

                    var slot = element.FindPropertyRelative("slot").stringValue;
                    if (slot != lastVisibleSlot)
                    {
                        EditorGUILayout.LabelField(string.IsNullOrWhiteSpace(slot) ? "Uncategorized" : slot, EditorStyles.miniBoldLabel);
                        lastVisibleSlot = slot;
                    }

                    var label = GetPartListLabel(element, i);
                    var style = i == selectedIndex ? EditorStyles.helpBox : EditorStyles.miniButton;
                    if (GUILayout.Button(label, style))
                    {
                        selectedIndex = i;
                        GUI.FocusControl(null);
                    }
                }
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawPartEditor()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                if (definitionsProperty.arraySize == 0)
                {
                    EditorGUILayout.HelpBox("No part definitions.", MessageType.Info);
                    return;
                }

                selectedIndex = Mathf.Clamp(selectedIndex, 0, definitionsProperty.arraySize - 1);
                var element = definitionsProperty.GetArrayElementAtIndex(selectedIndex);
                rightScroll = EditorGUILayout.BeginScrollView(rightScroll);

                EditorGUILayout.LabelField("Selected Part", EditorStyles.boldLabel);
                DrawRelativeProperty(element, "partId");
                DrawRelativeProperty(element, "displayName");
                DrawCategoryProperty(element);
                DrawRendererPathProperty(element);
                DrawVariantPrefabProperty(element);
                DrawRelativeProperty(element, "useBaseRendererMesh");
                DrawRelativeProperty(element, "icon");
                DrawRelativeProperty(element, "material");
                DrawRelativeProperty(element, "enabledByDefault");
                DrawRelativeProperty(element, "visibleInCustomization");
                DrawRelativeProperty(element, "linkedPartIds", true);

                EditorGUILayout.Space(8f);
                DrawPrefabPreview(element);

                EditorGUILayout.Space(8f);
                DrawFactEditor(element.FindPropertyRelative("facts"));

                EditorGUILayout.Space(8f);
                DrawFactPresets(element.FindPropertyRelative("facts"));
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawPrefabPreview(SerializedProperty element)
        {
            EditorGUILayout.LabelField("Thumbnail Preview", EditorStyles.boldLabel);
            var prefab = element.FindPropertyRelative("variantPrefab").objectReferenceValue as GameObject;
            var icon = element.FindPropertyRelative("icon").objectReferenceValue as Sprite;
            if (prefab == null && icon == null)
            {
                EditorGUILayout.HelpBox("No icon or variant prefab assigned.", MessageType.Info);
                return;
            }

            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                var preview = GetPartPreviewTexture(icon, prefab);
                var previewRect = GUILayoutUtility.GetRect(160f, 160f, GUILayout.Width(170f), GUILayout.Height(170f));
                if (preview != null)
                {
                    GUI.DrawTexture(previewRect, preview, ScaleMode.ScaleToFit, true);
                }
                else
                {
                    EditorGUI.HelpBox(previewRect, "Preview loading...", MessageType.Info);
                    Repaint();
                }

                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField("Preview Source", icon != null ? "Icon" : "Prefab fallback");
                    if (icon != null)
                    {
                        EditorGUILayout.LabelField("Icon", icon.name);
                    }

                    if (prefab == null)
                    {
                        EditorGUILayout.HelpBox("No variant prefab assigned. Runtime can still show the icon, but equip needs a prefab for mesh/material copy.", MessageType.Warning);
                    }
                    else
                    {
                        EditorGUILayout.LabelField("Prefab", prefab.name);
                        var renderer = prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
                        if (renderer == null)
                        {
                            EditorGUILayout.HelpBox("No SkinnedMeshRenderer found in prefab.", MessageType.Warning);
                        }
                        else
                        {
                            EditorGUILayout.LabelField("Renderer", renderer.name);
                            EditorGUILayout.LabelField("Mesh", renderer.sharedMesh != null ? renderer.sharedMesh.name : "None");
                            EditorGUILayout.LabelField("Materials", renderer.sharedMaterials != null ? renderer.sharedMaterials.Length.ToString() : "0");
                            if (renderer.sharedMesh != null)
                            {
                                EditorGUILayout.LabelField("Vertices", renderer.sharedMesh.vertexCount.ToString());
                            }
                        }
                    }

                    if (prefab != null)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (GUILayout.Button("Generate Thumbnail"))
                            {
                                GenerateThumbnail(element);
                            }

                            if (icon != null && GUILayout.Button("Ping Icon"))
                            {
                                EditorGUIUtility.PingObject(icon);
                            }
                        }

                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (GUILayout.Button("Ping Prefab"))
                            {
                                EditorGUIUtility.PingObject(prefab);
                            }

                            if (GUILayout.Button("Open Prefab"))
                            {
                                AssetDatabase.OpenAsset(prefab);
                            }
                        }
                    }
                }
            }
        }

        private void GenerateThumbnail(SerializedProperty element)
        {
            var prefab = element.FindPropertyRelative("variantPrefab").objectReferenceValue as GameObject;
            if (prefab == null)
            {
                report = "No variant prefab to generate a thumbnail from.";
                return;
            }

            var partId = element.FindPropertyRelative("partId").stringValue;
            var sprite = GenerateThumbnailSprite(prefab, partId, out var assetPath);
            if (sprite == null)
            {
                report = "Prefab preview is not ready yet. Try Generate Thumbnail again.";
                Repaint();
                return;
            }

            element.FindPropertyRelative("icon").objectReferenceValue = sprite;
            databaseObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssets();
            report = "Generated thumbnail: " + assetPath;
        }

        private static Sprite GenerateThumbnailSprite(GameObject prefab, string partId, out string assetPath)
        {
            assetPath = string.Empty;
            if (prefab == null)
            {
                return null;
            }

            var readable = RenderPrefabThumbnail(prefab) ?? RenderAssetPreviewThumbnail(prefab);
            if (readable == null)
            {
                return null;
            }

            EnsureThumbnailFolder();
            var fileName = SanitizeFileName(string.IsNullOrWhiteSpace(partId) ? prefab.name : partId) + ".png";
            assetPath = ThumbnailFolderPath + "/" + fileName;

            File.WriteAllBytes(assetPath, readable.EncodeToPNG());
            DestroyImmediate(readable);

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        private static Texture2D RenderPrefabThumbnail(GameObject prefab)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (instance == null)
            {
                instance = Instantiate(prefab);
            }

            if (instance == null)
            {
                return null;
            }

            var cameraObject = new GameObject("CharacterPartThumbnailCamera", typeof(Camera));
            var lightObject = new GameObject("CharacterPartThumbnailLight", typeof(Light));
            instance.hideFlags = HideFlags.HideAndDontSave;
            cameraObject.hideFlags = HideFlags.HideAndDontSave;
            lightObject.hideFlags = HideFlags.HideAndDontSave;

            try
            {
                instance.transform.position = Vector3.zero;
                instance.transform.rotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;
                var renderer = instance.GetComponentInChildren<SkinnedMeshRenderer>(true) as Renderer
                    ?? instance.GetComponentInChildren<MeshRenderer>(true);
                if (renderer == null)
                {
                    return null;
                }

                foreach (var childRenderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    childRenderer.gameObject.SetActive(childRenderer == renderer);
                }

                var bounds = renderer.bounds;
                if (bounds.size == Vector3.zero)
                {
                    bounds = new Bounds(renderer.transform.position, Vector3.one);
                }

                instance.transform.position -= bounds.center;
                bounds.center = Vector3.zero;

                var camera = cameraObject.GetComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                camera.orthographic = true;
                camera.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z, 0.05f) * 1.28f;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = 100f;
                camera.transform.position = new Vector3(0f, 0f, -6f);
                camera.transform.LookAt(Vector3.zero);

                var light = lightObject.GetComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.7f;
                light.transform.rotation = Quaternion.Euler(35f, -30f, 0f);

                var renderTexture = RenderTexture.GetTemporary(512, 512, 24, RenderTextureFormat.ARGB32);
                var previous = RenderTexture.active;
                camera.targetTexture = renderTexture;
                camera.Render();
                RenderTexture.active = renderTexture;

                var readable = new Texture2D(512, 512, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
                readable.Apply();

                camera.targetTexture = null;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(renderTexture);
                return readable;
            }
            finally
            {
                DestroyImmediate(instance);
                DestroyImmediate(cameraObject);
                DestroyImmediate(lightObject);
            }
        }

        private static Texture2D RenderAssetPreviewThumbnail(GameObject prefab)
        {
            var preview = AssetPreview.GetAssetPreview(prefab) ?? AssetPreview.GetMiniThumbnail(prefab);
            return preview != null ? CopyToReadableTexture(preview) : null;
        }

        private static Texture2D CopyToReadableTexture(Texture2D source)
        {
            var renderTexture = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, renderTexture);

            var previous = RenderTexture.active;
            RenderTexture.active = renderTexture;
            var readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            readable.Apply();

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(renderTexture);
            return readable;
        }

        private static void EnsureThumbnailFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Data"))
            {
                AssetDatabase.CreateFolder("Assets", "Data");
            }

            if (!AssetDatabase.IsValidFolder("Assets/Data/Ontology"))
            {
                AssetDatabase.CreateFolder("Assets/Data", "Ontology");
            }

            if (!AssetDatabase.IsValidFolder(ThumbnailFolderPath))
            {
                AssetDatabase.CreateFolder("Assets/Data/Ontology", "CharacterPartThumbnails");
            }
        }

        private static string SanitizeFileName(string value)
        {
            var builder = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                builder.Append(System.Array.IndexOf(Path.GetInvalidFileNameChars(), character) >= 0 ? '_' : character);
            }

            return builder.ToString();
        }

        private static Texture GetPartPreviewTexture(Sprite icon, GameObject prefab)
        {
            if (icon != null)
            {
                return AssetPreview.GetAssetPreview(icon) ?? icon.texture;
            }

            return prefab != null ? AssetPreview.GetAssetPreview(prefab) ?? AssetPreview.GetMiniThumbnail(prefab) : null;
        }

        private void DrawFactEditor(SerializedProperty facts)
        {
            EditorGUILayout.LabelField("Facts", EditorStyles.boldLabel);
            for (var i = 0; i < facts.arraySize; i++)
            {
                var fact = facts.GetArrayElementAtIndex(i);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(fact.FindPropertyRelative("predicate"), GUIContent.none, GUILayout.MinWidth(150f));
                    EditorGUILayout.PropertyField(fact.FindPropertyRelative("obj"), GUIContent.none, GUILayout.MinWidth(150f));
                    if (GUILayout.Button("-", GUILayout.Width(24f)))
                    {
                        facts.DeleteArrayElementAtIndex(i);
                        break;
                    }
                }
            }

            if (GUILayout.Button("+ Add Fact"))
            {
                facts.InsertArrayElementAtIndex(facts.arraySize);
                var fact = facts.GetArrayElementAtIndex(facts.arraySize - 1);
                fact.FindPropertyRelative("predicate").stringValue = "provides";
                fact.FindPropertyRelative("obj").stringValue = "Warmth";
            }
        }

        private void DrawFactPresets(SerializedProperty facts)
        {
            EditorGUILayout.LabelField("Fact Presets", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Covers Body")) AddFact(facts, "covers", OntologyCharacterCustomizationUiConfig.SlotBody);
                if (GUILayout.Button("Covers Feet")) AddFact(facts, "covers", "Feet");
                if (GUILayout.Button("Warmth")) AddFact(facts, "provides", "Warmth");
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("SwampResistance")) AddFact(facts, OntologyPredicates.GrantsCapability, OntologyObjects.SwampResistance);
                if (GUILayout.Button("ColdProtection")) AddFact(facts, OntologyPredicates.GrantsCapability, OntologyObjects.ColdProtection);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Conflict Upper")) AddFact(facts, OntologyPredicates.ConflictsWithSlot, OntologyCharacterCustomizationUiConfig.SlotUpperBody);
                if (GUILayout.Button("Conflict Lower")) AddFact(facts, OntologyPredicates.ConflictsWithSlot, OntologyCharacterCustomizationUiConfig.SlotLowerBody);
                if (GUILayout.Button("Conflict Outer")) AddFact(facts, OntologyPredicates.ConflictsWithSlot, OntologyCharacterCustomizationUiConfig.SlotOuterwear);
            }
        }

        private void DrawValidationTab()
        {
            var messages = BuildValidationMessages();
            EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(messages.Count == 0 ? "Validation OK" : messages.Count + " issue(s) found.", messages.Count == 0 ? MessageType.Info : MessageType.Warning);

            reportScroll = EditorGUILayout.BeginScrollView(reportScroll);
            foreach (var message in messages)
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(message.Text, GUILayout.MinHeight(20f));
                    if (message.Index >= 0 && GUILayout.Button("Select", GUILayout.Width(70f)))
                    {
                        selectedIndex = message.Index;
                        selectedTab = 0;
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawRuntimeTestTab()
        {
            DrawRuntimeTargetFields();
            EditorGUILayout.Space(8f);

            var partId = GetSelectedPartId();
            EditorGUILayout.LabelField("Selected Part", partId, EditorStyles.boldLabel);
            DrawSelectedRuntimePreview(partId);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Test Equip")) RunAdapterAction(() => adapter.EquipPart(partId), "Equip " + partId);
                if (GUILayout.Button("Test Unequip")) RunAdapterAction(() => adapter.UnequipPart(partId), "Unequip " + partId);
                if (GUILayout.Button("Apply Default")) RunAdapterAction(() => { adapter.ApplyDefaultPreset(); return true; }, "Apply default preset");
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Inject Facts")) RunAdapterAction(() => { adapter.InjectActivePartFacts(); return true; }, "Inject active part facts");
                if (GUILayout.Button("Sync From World")) RunAdapterAction(() => { adapter.SyncFromWorldFacts(); return true; }, "Sync from world facts");
                if (GUILayout.Button("FullBody Test")) RunFullBodyTest();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Run Simulation")) report = bootstrap != null ? bootstrap.RunSimulation() : "No bootstrap.";
                if (GUILayout.Button("Reset World")) report = bootstrap != null ? bootstrap.ResetWorld(false) : "No bootstrap.";
                if (GUILayout.Button("Save Snapshot")) report = saveController != null ? saveController.SaveSnapshot() : "No save controller.";
                if (GUILayout.Button("Load Snapshot")) report = saveController != null ? saveController.LoadSnapshot() : "No save controller.";
            }

            DrawReport();
        }

        private void DrawBulkToolsTab()
        {
            EditorGUILayout.HelpBox("Bulk scan is intentionally minimal in this MVP. Use selected prefabs to register draft parts, then edit details in the Database tab.", MessageType.Info);
            if (GUILayout.Button("Register Selected Prefabs As Draft Parts"))
            {
                RegisterSelectedPrefabs();
            }

            EditorGUILayout.Space(8f);
            if (GUILayout.Button("Generate Missing Thumbnails"))
            {
                GenerateMissingThumbnails();
            }
        }

        private void DrawSettingsTab()
        {
            EditorGUILayout.LabelField("Defaults", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Database", DefaultDatabasePath);
            if (GUILayout.Button("Open Fact Debugger"))
            {
                OntologyFactDebuggerWindow.Open();
            }
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Known Predicates", EditorStyles.boldLabel);
            foreach (var predicate in knownPredicates)
            {
                EditorGUILayout.LabelField("- " + predicate);
            }
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Known Objects", EditorStyles.boldLabel);
            foreach (var obj in knownObjects)
            {
                EditorGUILayout.LabelField("- " + obj);
            }
        }

        private void DrawRuntimeTargetFields()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                previewTarget = (GameObject)EditorGUILayout.ObjectField("Preview Target", previewTarget, typeof(GameObject), true);
                adapter = (OntologyCharacterPartAdapter)EditorGUILayout.ObjectField("Part Adapter", adapter, typeof(OntologyCharacterPartAdapter), true);
                bootstrap = (OntologyWorldBootstrap)EditorGUILayout.ObjectField("Bootstrap", bootstrap, typeof(OntologyWorldBootstrap), true);
                saveController = (OntologySaveController)EditorGUILayout.ObjectField("Save Controller", saveController, typeof(OntologySaveController), true);
                if (GUILayout.Button("Find Runtime Targets"))
                {
                    FindRuntimeTargets();
                }
            }
        }

        private void DrawSelectedRuntimePreview(string partId)
        {
            if (adapter == null || string.IsNullOrWhiteSpace(partId))
            {
                EditorGUILayout.HelpBox("Select a part and runtime adapter.", MessageType.Info);
                return;
            }

            var equipped = adapter.IsPartEquipped(partId);
            var hasFact = adapter.HasEquippedPartFact(partId);
            EditorGUILayout.LabelField("Equipped", equipped ? "Yes" : "No");
            EditorGUILayout.LabelField("World Fact", hasFact ? "Yes" : "No");
            if (!adapter.CanEquipPart(partId, out var equipReason))
            {
                EditorGUILayout.HelpBox("Equip unavailable: " + GetFailureReasonLabel(equipReason), MessageType.Warning);
            }
            if (!adapter.CanUnequipPart(partId, out var unequipReason))
            {
                EditorGUILayout.HelpBox("Unequip unavailable: " + GetFailureReasonLabel(unequipReason), MessageType.Info);
            }
        }

        private static string GetFailureReasonLabel(string reason)
        {
            switch (reason)
            {
                case OntologyCharacterPartAdapter.FailureDefinitionMissing:
                    return "part definition is missing";
                case OntologyCharacterPartAdapter.FailureRendererMissing:
                    return "target renderer is missing";
                case OntologyCharacterPartAdapter.FailureWorldMissing:
                    return "ontology world is not ready";
                case OntologyCharacterPartAdapter.FailureAlreadyEquipped:
                    return "already equipped";
                case OntologyCharacterPartAdapter.FailureAlreadyUnequipped:
                    return "already unequipped";
                default:
                    return string.IsNullOrWhiteSpace(reason) ? "unknown reason" : reason;
            }
        }

        private void DrawReport()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Report", EditorStyles.boldLabel);
            reportScroll = EditorGUILayout.BeginScrollView(reportScroll, GUILayout.MinHeight(180f));
            EditorGUILayout.TextArea(report, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private void EnsureDatabaseObject()
        {
            databaseObject = database != null ? new SerializedObject(database) : null;
            definitionsProperty = databaseObject != null ? databaseObject.FindProperty("definitions") : null;
        }

        private void DrawRelativeProperty(SerializedProperty parent, string propertyName, bool includeChildren = false)
        {
            EditorGUILayout.PropertyField(parent.FindPropertyRelative(propertyName), includeChildren);
        }

        private void DrawCategoryProperty(SerializedProperty element)
        {
            var slotProperty = element.FindPropertyRelative("slot");
            var rendererPathProperty = element.FindPropertyRelative("rendererPath");
            var currentSlot = slotProperty.stringValue;
            var currentIndex = GetCategoryIndex(currentSlot);

            if (currentIndex < 0 && !string.IsNullOrWhiteSpace(currentSlot))
            {
                EditorGUILayout.HelpBox("Unknown category: " + currentSlot + ". Select a known category or keep editing the raw value below.", MessageType.Warning);
                DrawRelativeProperty(element, "slot");
                return;
            }

            EditorGUI.BeginChangeCheck();
            var nextIndex = EditorGUILayout.Popup("Category / Slot", Mathf.Max(0, currentIndex), categoryOptions);
            if (EditorGUI.EndChangeCheck())
            {
                var previousRendererPath = rendererPathProperty.stringValue;
                var previousSuggestedPath = GuessRendererPath(currentSlot);
                var nextSlot = categoryOptions[nextIndex];
                slotProperty.stringValue = nextSlot;

                if (string.IsNullOrWhiteSpace(previousRendererPath) || previousRendererPath == previousSuggestedPath)
                {
                    rendererPathProperty.stringValue = GuessRendererPath(nextSlot);
                }
            }
        }

        private void DrawRendererPathProperty(SerializedProperty element)
        {
            var slot = element.FindPropertyRelative("slot").stringValue;
            var rendererPath = element.FindPropertyRelative("rendererPath");
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(rendererPath);
                if (GUILayout.Button("Use Suggested", GUILayout.Width(115f)))
                {
                    rendererPath.stringValue = GuessRendererPath(slot);
                    GUI.FocusControl(null);
                }
            }
        }

        private void DrawVariantPrefabProperty(SerializedProperty element)
        {
            var property = element.FindPropertyRelative("variantPrefab");
            var currentPrefab = property.objectReferenceValue as GameObject;

            EditorGUI.BeginChangeCheck();
            var nextPrefab = (GameObject)EditorGUILayout.ObjectField("Variant Prefab", currentPrefab, typeof(GameObject), false);
            if (EditorGUI.EndChangeCheck())
            {
                var duplicateIndex = FindPrefabOwnerIndex(nextPrefab, selectedIndex);
                if (nextPrefab != null && duplicateIndex >= 0)
                {
                    prefabSelectionWarning = nextPrefab.name + " is already registered by " + GetPartSummary(duplicateIndex) + ".";
                    GUI.FocusControl(null);
                }
                else
                {
                    property.objectReferenceValue = nextPrefab;
                    prefabSelectionWarning = string.Empty;
                }
            }

            var existingDuplicateIndex = FindPrefabOwnerIndex(currentPrefab, selectedIndex);
            if (currentPrefab != null && existingDuplicateIndex >= 0)
            {
                EditorGUILayout.HelpBox("Duplicate prefab: already registered by " + GetPartSummary(existingDuplicateIndex) + ".", MessageType.Error);
            }
            else if (!string.IsNullOrWhiteSpace(prefabSelectionWarning))
            {
                EditorGUILayout.HelpBox(prefabSelectionWarning, MessageType.Warning);
            }
        }

        private bool PassesListFilter(SerializedProperty element)
        {
            var displayName = element.FindPropertyRelative("displayName").stringValue;
            var partId = element.FindPropertyRelative("partId").stringValue;
            var slot = element.FindPropertyRelative("slot").stringValue;

            if (!string.IsNullOrWhiteSpace(search)
                && displayName.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) < 0
                && partId.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            return categoryFilterIndex <= 0 || slot == categoryFilterOptions[categoryFilterIndex];
        }

        private string GetPartListLabel(SerializedProperty element, int index)
        {
            var displayName = element.FindPropertyRelative("displayName").stringValue;
            var partId = element.FindPropertyRelative("partId").stringValue;
            var slot = element.FindPropertyRelative("slot").stringValue;
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = string.IsNullOrWhiteSpace(partId) ? "Part " + index : partId;
            }

            return displayName + "  [" + slot + "]";
        }

        private int FindPrefabOwnerIndex(GameObject prefab, int excludedIndex)
        {
            if (prefab == null || definitionsProperty == null)
            {
                return -1;
            }

            for (var i = 0; i < definitionsProperty.arraySize; i++)
            {
                if (i == excludedIndex)
                {
                    continue;
                }

                var element = definitionsProperty.GetArrayElementAtIndex(i);
                if (element.FindPropertyRelative("variantPrefab").objectReferenceValue == prefab)
                {
                    return i;
                }
            }

            return -1;
        }

        private string GetPartSummary(int index)
        {
            if (definitionsProperty == null || index < 0 || index >= definitionsProperty.arraySize)
            {
                return "another part";
            }

            var element = definitionsProperty.GetArrayElementAtIndex(index);
            var displayName = element.FindPropertyRelative("displayName").stringValue;
            var partId = element.FindPropertyRelative("partId").stringValue;
            if (!string.IsNullOrWhiteSpace(displayName) && !string.IsNullOrWhiteSpace(partId))
            {
                return displayName + " (" + partId + ")";
            }

            return string.IsNullOrWhiteSpace(displayName) ? partId : displayName;
        }

        private int GetCategoryIndex(string slot)
        {
            for (var i = 0; i < categoryOptions.Length; i++)
            {
                if (categoryOptions[i] == slot)
                {
                    return i;
                }
            }

            return -1;
        }

        private string GetSelectedPartId()
        {
            if (definitionsProperty == null || definitionsProperty.arraySize == 0)
            {
                return string.Empty;
            }

            selectedIndex = Mathf.Clamp(selectedIndex, 0, definitionsProperty.arraySize - 1);
            return definitionsProperty.GetArrayElementAtIndex(selectedIndex).FindPropertyRelative("partId").stringValue;
        }

        private void AddPart()
        {
            Undo.RecordObject(database, "Add Character Part");
            definitionsProperty.InsertArrayElementAtIndex(definitionsProperty.arraySize);
            selectedIndex = definitionsProperty.arraySize - 1;
            var element = definitionsProperty.GetArrayElementAtIndex(selectedIndex);
            element.FindPropertyRelative("partId").stringValue = "Part_New";
            element.FindPropertyRelative("displayName").stringValue = "New Part";
            element.FindPropertyRelative("slot").stringValue = OntologyCharacterCustomizationUiConfig.SlotAccessory;
            element.FindPropertyRelative("rendererPath").stringValue = GuessRendererPath(OntologyCharacterCustomizationUiConfig.SlotAccessory);
            element.FindPropertyRelative("variantPrefab").objectReferenceValue = null;
            element.FindPropertyRelative("useBaseRendererMesh").boolValue = false;
            element.FindPropertyRelative("icon").objectReferenceValue = null;
            element.FindPropertyRelative("material").objectReferenceValue = null;
            element.FindPropertyRelative("enabledByDefault").boolValue = false;
            element.FindPropertyRelative("visibleInCustomization").boolValue = true;
            element.FindPropertyRelative("linkedPartIds").arraySize = 0;
            element.FindPropertyRelative("facts").arraySize = 0;
        }

        private void DuplicatePart()
        {
            if (definitionsProperty.arraySize == 0)
            {
                return;
            }

            Undo.RecordObject(database, "Duplicate Character Part");
            definitionsProperty.InsertArrayElementAtIndex(selectedIndex);
            selectedIndex++;
            var element = definitionsProperty.GetArrayElementAtIndex(selectedIndex);
            element.FindPropertyRelative("partId").stringValue += "_Copy";
            element.FindPropertyRelative("displayName").stringValue += " Copy";
            element.FindPropertyRelative("variantPrefab").objectReferenceValue = null;
            element.FindPropertyRelative("useBaseRendererMesh").boolValue = false;
            element.FindPropertyRelative("icon").objectReferenceValue = null;
            prefabSelectionWarning = "Duplicated part created without a prefab. Assign a different prefab before saving it as a real part.";
        }

        private void DeletePart()
        {
            if (definitionsProperty.arraySize == 0)
            {
                return;
            }

            var partId = GetSelectedPartId();
            if (!EditorUtility.DisplayDialog("Delete Part", "Delete " + partId + "?", "Delete", "Cancel"))
            {
                return;
            }

            Undo.RecordObject(database, "Delete Character Part");
            definitionsProperty.DeleteArrayElementAtIndex(selectedIndex);
            selectedIndex = Mathf.Clamp(selectedIndex, 0, definitionsProperty.arraySize - 1);
        }

        private void SortBySlot()
        {
            databaseObject.ApplyModifiedProperties();
            var list = new List<OntologyCharacterPartDefinition>(database.Definitions);
            list.Sort((a, b) => string.Compare((a?.slot ?? string.Empty) + (a?.displayName ?? string.Empty), (b?.slot ?? string.Empty) + (b?.displayName ?? string.Empty), System.StringComparison.Ordinal));

            Undo.RecordObject(database, "Sort Character Parts");
            for (var i = 0; i < list.Count; i++)
            {
                var element = definitionsProperty.GetArrayElementAtIndex(i);
                WriteDefinition(element, list[i]);
            }
        }

        private void WriteDefinition(SerializedProperty element, OntologyCharacterPartDefinition definition)
        {
            element.FindPropertyRelative("partId").stringValue = definition.partId;
            element.FindPropertyRelative("displayName").stringValue = definition.displayName;
            element.FindPropertyRelative("slot").stringValue = definition.slot;
            element.FindPropertyRelative("rendererPath").stringValue = definition.rendererPath;
            element.FindPropertyRelative("variantPrefab").objectReferenceValue = definition.variantPrefab;
            element.FindPropertyRelative("useBaseRendererMesh").boolValue = definition.useBaseRendererMesh;
            element.FindPropertyRelative("icon").objectReferenceValue = definition.icon;
            element.FindPropertyRelative("material").objectReferenceValue = definition.material;
            element.FindPropertyRelative("enabledByDefault").boolValue = definition.enabledByDefault;
            element.FindPropertyRelative("visibleInCustomization").boolValue = definition.visibleInCustomization;

            var linkedPartIds = element.FindPropertyRelative("linkedPartIds");
            linkedPartIds.arraySize = definition.linkedPartIds?.Length ?? 0;
            for (var i = 0; i < linkedPartIds.arraySize; i++)
            {
                linkedPartIds.GetArrayElementAtIndex(i).stringValue = definition.linkedPartIds[i];
            }

            var facts = element.FindPropertyRelative("facts");
            facts.arraySize = definition.facts?.Length ?? 0;
            for (var i = 0; i < facts.arraySize; i++)
            {
                var fact = facts.GetArrayElementAtIndex(i);
                fact.FindPropertyRelative("predicate").stringValue = definition.facts[i]?.predicate ?? string.Empty;
                fact.FindPropertyRelative("obj").stringValue = definition.facts[i]?.obj ?? string.Empty;
            }
        }

        private void AddFact(SerializedProperty facts, string predicate, string obj)
        {
            facts.InsertArrayElementAtIndex(facts.arraySize);
            var fact = facts.GetArrayElementAtIndex(facts.arraySize - 1);
            fact.FindPropertyRelative("predicate").stringValue = predicate;
            fact.FindPropertyRelative("obj").stringValue = obj;
        }

        private List<ValidationMessage> BuildValidationMessages()
        {
            databaseObject.ApplyModifiedProperties();
            var messages = new List<ValidationMessage>();
            var ids = new HashSet<string>();
            var slots = new HashSet<string>();
            var defaultSlots = new Dictionary<string, string>();
            var prefabOwners = new Dictionary<GameObject, string>();
            var knownPartIds = new HashSet<string>();

            foreach (var definition in database.Definitions)
            {
                if (definition == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(definition.slot))
                {
                    slots.Add(definition.slot);
                }

                if (!string.IsNullOrWhiteSpace(definition.partId))
                {
                    knownPartIds.Add(definition.partId);
                }
            }

            for (var i = 0; i < database.Definitions.Count; i++)
            {
                var definition = database.Definitions[i];
                if (definition == null)
                {
                    messages.Add(new ValidationMessage(i, "Error: Part[" + i + "] is null."));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(definition.partId)) messages.Add(new ValidationMessage(i, "Error: empty partId."));
                if (!string.IsNullOrWhiteSpace(definition.partId) && !ids.Add(definition.partId)) messages.Add(new ValidationMessage(i, "Error: duplicate partId " + definition.partId));
                if (string.IsNullOrWhiteSpace(definition.displayName)) messages.Add(new ValidationMessage(i, "Warning: empty displayName for " + definition.partId));
                if (string.IsNullOrWhiteSpace(definition.slot)) messages.Add(new ValidationMessage(i, "Warning: empty slot for " + definition.partId));
                if (string.IsNullOrWhiteSpace(definition.rendererPath)) messages.Add(new ValidationMessage(i, "Error: empty rendererPath for " + definition.partId));
                if (definition.variantPrefab == null)
                {
                    if (!definition.useBaseRendererMesh) messages.Add(new ValidationMessage(i, "Warning: missing variantPrefab for " + definition.partId));
                }
                else if (definition.variantPrefab.GetComponentInChildren<SkinnedMeshRenderer>(true) == null) messages.Add(new ValidationMessage(i, "Error: variantPrefab has no SkinnedMeshRenderer for " + definition.partId));
                if (definition.variantPrefab != null)
                {
                    if (prefabOwners.TryGetValue(definition.variantPrefab, out var existingPartId))
                    {
                        messages.Add(new ValidationMessage(i, "Error: duplicate variantPrefab " + definition.variantPrefab.name + " (" + existingPartId + ", " + definition.partId + ")"));
                    }
                    else
                    {
                        prefabOwners.Add(definition.variantPrefab, definition.partId);
                    }
                }
                if (definition.material == null) messages.Add(new ValidationMessage(i, "Info: material is not assigned for " + definition.partId));

                if (adapter != null && !string.IsNullOrWhiteSpace(definition.rendererPath) && !RendererExists(definition.rendererPath))
                {
                    messages.Add(new ValidationMessage(i, "Error: rendererPath not found on preview target: " + definition.rendererPath));
                }

                if (definition.enabledByDefault && !string.IsNullOrWhiteSpace(definition.slot))
                {
                    if (defaultSlots.TryGetValue(definition.slot, out var existing))
                    {
                        messages.Add(new ValidationMessage(i, "Warning: multiple default parts in slot " + definition.slot + " (" + existing + ", " + definition.partId + ")"));
                    }
                    else
                    {
                        defaultSlots.Add(definition.slot, definition.partId);
                    }
                }

                foreach (var linkedPartId in definition.linkedPartIds ?? System.Array.Empty<string>())
                {
                    if (linkedPartId == definition.partId) messages.Add(new ValidationMessage(i, "Error: linkedPartIds contains itself on " + definition.partId));
                    else if (!knownPartIds.Contains(linkedPartId)) messages.Add(new ValidationMessage(i, "Error: linkedPartIds references unknown part " + linkedPartId));
                }

                if (definition.facts == null)
                {
                    continue;
                }

                var factKeys = new HashSet<string>();
                foreach (var fact in definition.facts)
                {
                    if (fact == null) continue;
                    if (string.IsNullOrWhiteSpace(fact.predicate) || string.IsNullOrWhiteSpace(fact.obj)) messages.Add(new ValidationMessage(i, "Warning: empty fact on " + definition.partId));
                    if (!factKeys.Add(fact.predicate + "|" + fact.obj)) messages.Add(new ValidationMessage(i, "Info: duplicate fact " + fact.predicate + " " + fact.obj));
                    if (fact.predicate == OntologyPredicates.ConflictsWithSlot && !slots.Contains(fact.obj)) messages.Add(new ValidationMessage(i, "Warning: conflicts_with_slot references unknown slot " + fact.obj));
                }
            }

            return messages;
        }

        private bool RendererExists(string rendererPath)
        {
            var visualRoot = adapter.transform.Find("Visual_Base_Mesh");
            var root = visualRoot != null ? visualRoot : adapter.transform;
            var normalized = rendererPath.StartsWith(root.name + "/") ? rendererPath.Substring(root.name.Length + 1) : rendererPath;
            return root.Find(normalized) != null;
        }

        private void FindRuntimeTargets()
        {
            previewTarget = GameObject.Find("OntologyPlayer");
            adapter = previewTarget != null ? previewTarget.GetComponent<OntologyCharacterPartAdapter>() : FindAnyObjectByType<OntologyCharacterPartAdapter>();
            bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            saveController = FindAnyObjectByType<OntologySaveController>();
        }

        private void RunAdapterAction(System.Func<bool> action, string label)
        {
            if (adapter == null)
            {
                report = "No OntologyCharacterPartAdapter.";
                return;
            }

            var success = action();
            report = label + ": " + (success ? "ok" : "failed");
            Repaint();
        }

        private void RunFullBodyTest()
        {
            if (adapter == null)
            {
                report = "No adapter.";
                return;
            }

            adapter.EquipPart("Part_TShirt_Base");
            adapter.EquipPart("Part_Pants_Base");
            adapter.EquipPart("Part_Outerwear_Base");
            adapter.EquipPart("Part_FullBody_Base");
            report = "FullBody=" + adapter.IsPartEquipped("Part_FullBody_Base")
                + ", TShirt=" + adapter.IsPartEquipped("Part_TShirt_Base")
                + ", Pants=" + adapter.IsPartEquipped("Part_Pants_Base")
                + ", Outerwear=" + adapter.IsPartEquipped("Part_Outerwear_Base");
        }

        private void RegisterSelectedPrefabs()
        {
            var prefabs = Selection.GetFiltered<GameObject>(SelectionMode.Assets);
            if (prefabs == null || prefabs.Length == 0)
            {
                report = "No prefab assets selected.";
                return;
            }

            var registered = 0;
            var skipped = 0;
            foreach (var prefab in prefabs)
            {
                if (PrefabUtility.GetPrefabAssetType(prefab) == PrefabAssetType.NotAPrefab)
                {
                    continue;
                }

                if (FindPrefabOwnerIndex(prefab, -1) >= 0)
                {
                    skipped++;
                    continue;
                }

                definitionsProperty.InsertArrayElementAtIndex(definitionsProperty.arraySize);
                var element = definitionsProperty.GetArrayElementAtIndex(definitionsProperty.arraySize - 1);
                var slot = GuessSlot(prefab.name);
                element.FindPropertyRelative("partId").stringValue = "Part_" + prefab.name.Replace(" ", "_");
                element.FindPropertyRelative("displayName").stringValue = ObjectNames.NicifyVariableName(prefab.name.Replace('_', ' '));
                element.FindPropertyRelative("slot").stringValue = slot;
                element.FindPropertyRelative("rendererPath").stringValue = GuessRendererPathForPrefab(prefab.name, slot);
                element.FindPropertyRelative("variantPrefab").objectReferenceValue = prefab;
                element.FindPropertyRelative("useBaseRendererMesh").boolValue = false;
                element.FindPropertyRelative("icon").objectReferenceValue = null;
                element.FindPropertyRelative("enabledByDefault").boolValue = false;
                element.FindPropertyRelative("visibleInCustomization").boolValue = true;
                element.FindPropertyRelative("linkedPartIds").arraySize = 0;
                element.FindPropertyRelative("facts").arraySize = 0;
                registered++;
            }

            report = "Registered " + registered + " selected prefab(s) as draft parts.";
            if (skipped > 0)
            {
                report += " Skipped " + skipped + " already registered prefab(s).";
            }
        }

        private void GenerateMissingThumbnails()
        {
            if (definitionsProperty == null)
            {
                report = "No database loaded.";
                return;
            }

            var generated = 0;
            var skipped = 0;
            for (var i = 0; i < definitionsProperty.arraySize; i++)
            {
                var element = definitionsProperty.GetArrayElementAtIndex(i);
                if (element.FindPropertyRelative("icon").objectReferenceValue != null)
                {
                    skipped++;
                    continue;
                }

                var prefab = element.FindPropertyRelative("variantPrefab").objectReferenceValue as GameObject;
                var partId = element.FindPropertyRelative("partId").stringValue;
                var sprite = GenerateThumbnailSprite(prefab, partId, out _);
                if (sprite == null)
                {
                    skipped++;
                    continue;
                }

                element.FindPropertyRelative("icon").objectReferenceValue = sprite;
                generated++;
            }

            databaseObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssets();
            report = "Generated " + generated + " thumbnail(s). Skipped " + skipped + " part(s).";
            if (generated == 0)
            {
                report += " If previews are not ready, wait a moment and run this again.";
            }
        }

        private static string GuessSlot(string name)
        {
            var lower = name.ToLowerInvariant();
            if (lower.Contains("full")) return OntologyCharacterCustomizationUiConfig.SlotFullBody;
            if (lower.Contains("costume") || lower.Contains("outfit")) return OntologyCharacterCustomizationUiConfig.SlotFullBody;
            if (lower.Contains("mascot") || lower.Contains("outwear") || lower.Contains("outerwear")) return OntologyCharacterCustomizationUiConfig.SlotOuterwear;
            if (lower.Contains("body")) return OntologyCharacterCustomizationUiConfig.SlotBody;
            if (lower.Contains("emotion")) return OntologyCharacterCustomizationUiConfig.SlotFace;
            if (lower.Contains("hair")) return OntologyCharacterCustomizationUiConfig.SlotHair;
            if (lower.Contains("shoe") || lower.Contains("sneaker")) return OntologyCharacterCustomizationUiConfig.SlotFootwear;
            if (lower.Contains("sock")) return OntologyCharacterCustomizationUiConfig.SlotFootwear;
            if (lower.Contains("pants") || lower.Contains("shorts")) return OntologyCharacterCustomizationUiConfig.SlotLowerBody;
            if (lower.Contains("shirt") || lower.Contains("t_shirt")) return OntologyCharacterCustomizationUiConfig.SlotUpperBody;
            if (lower.Contains("hat")) return OntologyCharacterCustomizationUiConfig.SlotHeadwear;
            if (lower.Contains("glass")) return OntologyCharacterCustomizationUiConfig.SlotEyewear;
            if (lower.Contains("glove")) return OntologyCharacterCustomizationUiConfig.SlotHandwear;
            if (lower.Contains("mustache")) return OntologyCharacterCustomizationUiConfig.SlotFacialHair;
            return OntologyCharacterCustomizationUiConfig.SlotAccessory;
        }

        private static string GuessRendererPathForPrefab(string prefabName, string slot)
        {
            var lower = prefabName.ToLowerInvariant();
            if (lower.Contains("costume_13_001") || lower.Contains("outfit") || lower.Contains("mascot")) return "Outerwear";
            if (lower.Contains("costume_13_002")) return "Hat";
            return GuessRendererPath(slot);
        }

        private static string GuessRendererPath(string slot)
        {
            switch (slot)
            {
                case OntologyCharacterCustomizationUiConfig.SlotBody: return "Body";
                case OntologyCharacterCustomizationUiConfig.SlotFace: return "Faces";
                case OntologyCharacterCustomizationUiConfig.SlotHair: return "Hairstyle";
                case OntologyCharacterCustomizationUiConfig.SlotFootwear: return "Shoes";
                case OntologyCharacterCustomizationUiConfig.SlotLowerBody: return "Pants";
                case OntologyCharacterCustomizationUiConfig.SlotUpperBody: return "T_Shirt";
                case OntologyCharacterCustomizationUiConfig.SlotAccessory: return "Accessories";
                case OntologyCharacterCustomizationUiConfig.SlotOuterwear: return "Outerwear";
                case OntologyCharacterCustomizationUiConfig.SlotFullBody: return "Full_body";
                case OntologyCharacterCustomizationUiConfig.SlotHeadwear: return "Hat";
                case OntologyCharacterCustomizationUiConfig.SlotEyewear: return "Glasses";
                case OntologyCharacterCustomizationUiConfig.SlotHandwear: return "Gloves";
                case OntologyCharacterCustomizationUiConfig.SlotFacialHair: return "Mustache";
                default: return string.Empty;
            }
        }

        private void SaveDatabase()
        {
            if (databaseObject != null)
            {
                databaseObject.ApplyModifiedProperties();
            }

            if (database != null)
            {
                EditorUtility.SetDirty(database);
            }
            AssetDatabase.SaveAssets();
        }

        private readonly struct ValidationMessage
        {
            public ValidationMessage(int index, string text)
            {
                Index = index;
                Text = text;
            }

            public int Index { get; }
            public string Text { get; }
        }
    }
}
