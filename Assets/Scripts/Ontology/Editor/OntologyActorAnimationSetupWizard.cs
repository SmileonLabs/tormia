using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Networking;
using UnityEngine.Playables;
using Tormia.Ontology.Core;

namespace Tormia.Ontology.Editor
{
    public sealed class OntologyActorAnimationSetupWizard : EditorWindow
    {
        private static readonly string[] ActorTypes = { "Player", "NPC", "Monster" };
        private static readonly string[] RigTypes = { "Humanoid", "Quadruped", "Flying" };
        private static readonly string[] WizardTabs =
        {
            "AI Character Create",
            "Concept Review",
            "Meshy Model",
            "Model Import & Setup",
            "Player Preview & Ontology",
            "Animation Setup",
            "Fact Editor"
        };
        private GameObject actorObject;
        private OntologyWorldBootstrap bootstrap;
        private OntologyAnimationDatabase animationDatabase;
        private OntologyActorProfile profile;
        private OntologyCharacterDraft characterDraft;
        private int actorTypeIndex;
        private int rigTypeIndex;
        private string actorId = "Player";
        private string intent = "Idle";
        private int selectedTab;
        private string characterPrompt = "A friendly village guide who knows the mountain paths.";
        private Vector2 scroll;
        private Vector2 repertoireScroll;
        private Vector2 ontologyFactScroll;
        private string newFactPredicate = "status";
        private string newFactObject = "Hungry";
        private string newAnimationId = "Anim_Swimming";
        private AnimationClip newAnimationClip;
        private string newAnimationIntent = "Swimming";
        private int newAnimationPriority = 50;
        private bool newAnimationLoop = true;
        private bool newAnimationInterruptible = true;
        private bool newAnimationCanBlend = true;
        private string factEditorMessage;
        private UnityWebRequest meshyRequest;
        private MeshyRequestKind meshyRequestKind;
        private bool showProfileRepertoire = true;
        private PreviewRenderUtility previewUtility;
        private GameObject previewInstance;
        private GameObject previewSource;
        private AnimationClip previewClip;
        private bool previewIsPlaying = true;
        private float previewTime;
        private float previewSpeed = 1f;
        private float previewYaw = 20f;
        private float previewPitch = 8f;
        private float previewZoom = 1f;
        private double lastPreviewUpdateTime;
        private PlayableGraph previewGraph;
        private AnimationClipPlayable previewClipPlayable;
        private AnimationClip previewGraphClip;
        private readonly List<Material> previewMaterials = new();

        [MenuItem("Tools/Ontology/Character Wizard")]
        public static void Open()
        {
            var window = GetWindow<OntologyActorAnimationSetupWizard>("Ontology Character Wizard");
            window.titleContent = new GUIContent("Ontology Character Wizard");
            var selected = Selection.activeGameObject;
            if (selected != null && selected.GetComponentInChildren<Animator>(true) != null)
            {
                window.LoadActor(selected);
            }
            else
            {
                var existingAdapter = FindAnyObjectByType<OntologyAnimationAdapter>();
                if (existingAdapter != null) window.LoadActor(existingAdapter.gameObject);
            }
        }

        private void OnEnable()
        {
            minSize = new Vector2(980f, 620f);
            lastPreviewUpdateTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += UpdatePreview;
        }

        private void OnDisable()
        {
            EditorApplication.update -= UpdatePreview;
            DisposeMeshyRequest();
            CleanupPreview();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Ontology Character Wizard", EditorStyles.boldLabel);
            selectedTab = GUILayout.Toolbar(selectedTab, WizardTabs);
            EditorGUILayout.Space(8);
            if (selectedTab == 0)
            {
                DrawAiCharacterCreateTab();
                return;
            }
            if (selectedTab == 1)
            {
                DrawConceptReviewTab();
                return;
            }
            if (selectedTab == 2)
            {
                DrawMeshyModelTab();
                return;
            }
            if (selectedTab == 3)
            {
                DrawModelImportTab();
                return;
            }
            if (selectedTab == 4)
            {
                DrawPlayerOverviewTab();
                return;
            }
            if (selectedTab == 6)
            {
                DrawFactEditorTab();
                return;
            }
            EditorGUILayout.HelpBox("Actor 유형, 씬 오브젝트, 애니메이션 데이터를 한 번에 연결합니다.", MessageType.Info);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.BeginVertical(GUILayout.MinWidth(520f), GUILayout.ExpandWidth(true));
            actorObject = (GameObject)EditorGUILayout.ObjectField("Actor Object", actorObject, typeof(GameObject), true);
            if (GUILayout.Button("Use Selected Object")) LoadActor(Selection.activeGameObject);
            actorTypeIndex = EditorGUILayout.Popup("Actor Type", actorTypeIndex, ActorTypes);
            rigTypeIndex = EditorGUILayout.Popup("Rig Type", rigTypeIndex, RigTypes);
            actorId = EditorGUILayout.TextField("Actor Id", actorId);
            bootstrap = (OntologyWorldBootstrap)EditorGUILayout.ObjectField("World Bootstrap (optional)", bootstrap, typeof(OntologyWorldBootstrap), true);
            animationDatabase = (OntologyAnimationDatabase)EditorGUILayout.ObjectField("Animation Database", animationDatabase, typeof(OntologyAnimationDatabase), false);
            profile = (OntologyActorProfile)EditorGUILayout.ObjectField("Profile (optional)", profile, typeof(OntologyActorProfile), false);
            DrawAnimationDatabaseRegistration();
            DrawProfileRepertoire();

            EditorGUILayout.Space(6);
            if (GUILayout.Button("Apply Setup", GUILayout.Height(30))) ApplySetup();
            if (GUILayout.Button("Inject Ontology Data")) InjectFacts();

            EditorGUILayout.Space(6);
            intent = EditorGUILayout.TextField("Preview Intent", intent);
            var matchingDefinitions = new List<OntologyAnimationDefinition>(MatchingDefinitions());
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(110));
            foreach (var definition in matchingDefinitions)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("[OK] " + definition.animationId, definition.clip != null ? definition.clip.name : "Missing Clip");
                if (GUILayout.Button("Preview", GUILayout.Width(70))) SelectPreviewClip(definition.clip);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();
            EditorGUILayout.BeginVertical(GUILayout.Width(410f));
            DrawAnimationPreview(matchingDefinitions);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawAiCharacterCreateTab()
        {
            EditorGUILayout.HelpBox(
                "Step 1: save a character brief. This draft will flow into concept review, 3D generation, and ontology setup.",
                MessageType.Info);
            characterDraft = (OntologyCharacterDraft)EditorGUILayout.ObjectField(
                "Character Draft", characterDraft, typeof(OntologyCharacterDraft), false);
            if (characterDraft == null)
            {
                if (GUILayout.Button("Create Character Draft", GUILayout.Height(28f)))
                {
                    characterDraft = CreateCharacterDraft();
                }
                EditorGUILayout.HelpBox("Create a draft to store this character's prompt and later approval results as a project asset.", MessageType.None);
                return;
            }

            characterDraft.characterId = EditorGUILayout.TextField("Character Id", characterDraft.characterId);
            EditorGUILayout.LabelField("Current Stage", characterDraft.stage.ToString());
            EditorGUILayout.LabelField("User Prompt", EditorStyles.boldLabel);
            characterPrompt = EditorGUILayout.TextArea(characterPrompt, GUILayout.MinHeight(120f));
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Save Character Brief", GUILayout.Height(30f))) SaveCharacterBrief();
            if (GUILayout.Button("Load Saved Brief", GUILayout.Height(30f))) characterPrompt = characterDraft.userPrompt;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Planned output", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("• Character identity, role, personality, and visual direction");
            EditorGUILayout.LabelField("• Structured ontology facts and behavior-rule draft");
            EditorGUILayout.LabelField("• Model / rig request and initial animation repertoire proposal");
            EditorGUILayout.Space(8);
            EditorGUILayout.HelpBox(
                "The external AI connection is intentionally not invoked yet. The current Player is the verified baseline for the next integration step.",
                MessageType.None);
        }

        private OntologyCharacterDraft CreateCharacterDraft()
        {
            const string folder = "Assets/Data/Ontology/Characters";
            if (!AssetDatabase.IsValidFolder("Assets/Data/Ontology")) AssetDatabase.CreateFolder("Assets/Data", "Ontology");
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Data/Ontology", "Characters");

            var draft = CreateInstance<OntologyCharacterDraft>();
            draft.characterId = "NewCharacter";
            draft.userPrompt = characterPrompt;
            var path = AssetDatabase.GenerateUniqueAssetPath(folder + "/NewCharacterDraft.asset");
            AssetDatabase.CreateAsset(draft, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = draft;
            return draft;
        }

        private void SaveCharacterBrief()
        {
            characterDraft.userPrompt = characterPrompt;
            characterDraft.stage = OntologyCharacterCreationStage.Brief;
            EditorUtility.SetDirty(characterDraft);
            AssetDatabase.SaveAssets();
        }

        private void DrawConceptReviewTab()
        {
            EditorGUILayout.HelpBox(
                "Step 2: review a concept image before requesting any 3D model. Only an approved image can move to Meshy generation.",
                MessageType.Info);
            characterDraft = (OntologyCharacterDraft)EditorGUILayout.ObjectField(
                "Character Draft", characterDraft, typeof(OntologyCharacterDraft), false);
            if (characterDraft == null)
            {
                EditorGUILayout.HelpBox("Create or select a Character Draft in the AI Character Create tab first.", MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField("Character", characterDraft.characterId);
            EditorGUILayout.LabelField("Brief", string.IsNullOrWhiteSpace(characterDraft.userPrompt)
                ? "No saved prompt"
                : characterDraft.userPrompt, EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(6);

            EditorGUI.BeginChangeCheck();
            var nextImage = (Texture2D)EditorGUILayout.ObjectField(
                "Concept Image", characterDraft.conceptImage, typeof(Texture2D), false);
            if (EditorGUI.EndChangeCheck())
            {
                characterDraft.conceptImage = nextImage;
                characterDraft.approvedConceptImage = null;
                characterDraft.stage = OntologyCharacterCreationStage.ConceptReview;
                SaveCharacterDraft();
            }

            if (characterDraft.conceptImage == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign a generated concept image here. The upcoming image-generation connection will fill this field automatically; you can also drag in an imported image now.",
                    MessageType.Info);
            }
            else
            {
                var previewRect = GUILayoutUtility.GetRect(10f, 330f, GUILayout.ExpandWidth(true));
                EditorGUI.DrawPreviewTexture(previewRect, characterDraft.conceptImage, null, ScaleMode.ScaleToFit);
            }

            EditorGUILayout.LabelField("Review Feedback", EditorStyles.boldLabel);
            characterDraft.conceptFeedback = EditorGUILayout.TextArea(characterDraft.conceptFeedback, GUILayout.MinHeight(70f));
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Save Review", GUILayout.Height(30f)))
            {
                if (characterDraft.stage != OntologyCharacterCreationStage.ConceptApproved)
                    characterDraft.stage = OntologyCharacterCreationStage.ConceptReview;
                SaveCharacterDraft();
            }
            using (new EditorGUI.DisabledScope(characterDraft.conceptImage == null))
            {
                if (GUILayout.Button("Approve for 3D Modeling", GUILayout.Height(30f))) ApproveConceptImage();
            }
            EditorGUILayout.EndHorizontal();

            if (characterDraft.stage == OntologyCharacterCreationStage.ConceptApproved)
            {
                EditorGUILayout.HelpBox(
                    "Approved. This exact image is locked as the input for the Meshy Image-to-3D request in step 3.",
                    MessageType.Info);
            }
        }

        private void ApproveConceptImage()
        {
            if (characterDraft.conceptImage == null) return;
            characterDraft.approvedConceptImage = characterDraft.conceptImage;
            characterDraft.stage = OntologyCharacterCreationStage.ConceptApproved;
            SaveCharacterDraft();
        }

        private void SaveCharacterDraft()
        {
            EditorUtility.SetDirty(characterDraft);
            AssetDatabase.SaveAssets();
        }

        private void DrawMeshyModelTab()
        {
            EditorGUILayout.HelpBox(
                "Step 3: send only the approved concept image to Meshy Image-to-3D. A request is sent only when you press Create Meshy Task.",
                MessageType.Info);
            characterDraft = (OntologyCharacterDraft)EditorGUILayout.ObjectField(
                "Character Draft", characterDraft, typeof(OntologyCharacterDraft), false);
            if (characterDraft == null)
            {
                EditorGUILayout.HelpBox("Select a Character Draft first.", MessageType.Warning);
                return;
            }

            if (characterDraft.stage != OntologyCharacterCreationStage.ConceptApproved ||
                characterDraft.approvedConceptImage == null)
            {
                EditorGUILayout.HelpBox("Concept approval is required before a 3D model can be requested.", MessageType.Warning);
                return;
            }

            var apiKeyAvailable = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MESHY_API_KEY"));
            EditorGUILayout.LabelField("API Key", apiKeyAvailable ? "MESHY_API_KEY detected" : "MESHY_API_KEY not found");
            if (!apiKeyAvailable)
            {
                EditorGUILayout.HelpBox(
                    "Set MESHY_API_KEY as a system environment variable, then restart Unity. The key is never saved in this project.",
                    MessageType.Info);
            }

            EditorGUILayout.LabelField("Approved Input", characterDraft.approvedConceptImage.name);
            var previewRect = GUILayoutUtility.GetRect(10f, 220f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawPreviewTexture(previewRect, characterDraft.approvedConceptImage, null, ScaleMode.ScaleToFit);

            var requestActive = meshyRequest != null;
            EditorGUI.BeginDisabledGroup(!apiKeyAvailable || requestActive);
            if (string.IsNullOrWhiteSpace(characterDraft.meshyTaskId))
            {
                if (GUILayout.Button("Create Meshy Task", GUILayout.Height(32f))) StartMeshyTaskCreation();
            }
            else if (GUILayout.Button("Check Meshy Status", GUILayout.Height(32f)))
            {
                StartMeshyStatusCheck();
            }
            EditorGUI.EndDisabledGroup();

            if (requestActive) EditorGUILayout.HelpBox("Meshy request in progress...", MessageType.Info);
            if (!string.IsNullOrWhiteSpace(characterDraft.meshyTaskId))
            {
                EditorGUILayout.LabelField("Task Id", characterDraft.meshyTaskId);
                EditorGUILayout.LabelField("Status", string.IsNullOrWhiteSpace(characterDraft.meshyStatus) ? "Pending" : characterDraft.meshyStatus);
                EditorGUILayout.IntField("Progress", characterDraft.meshyProgress);
            }
            if (!string.IsNullOrWhiteSpace(characterDraft.meshyError))
                EditorGUILayout.HelpBox(characterDraft.meshyError, MessageType.Error);

            if (characterDraft.stage == OntologyCharacterCreationStage.ModelReview)
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Generated Model URLs", EditorStyles.boldLabel);
                DrawModelUrl("GLB", characterDraft.meshyGlbUrl);
                DrawModelUrl("FBX", characterDraft.meshyFbxUrl);
                DrawModelUrl("Preview", characterDraft.meshyThumbnailUrl);
                EditorGUILayout.HelpBox("The model is ready for the next stage: download/import into Unity and validate its rig.", MessageType.Info);
            }
        }

        private static void DrawModelUrl(string label, string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(url, GUILayout.Height(EditorGUIUtility.singleLineHeight * 2f));
        }

        private void StartMeshyTaskCreation()
        {
            var apiKey = Environment.GetEnvironmentVariable("MESHY_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey)) return;

            try
            {
                var imageDataUri = BuildImageDataUri(characterDraft.approvedConceptImage);
                var payload = new MeshyImageTo3DRequest
                {
                    image_url = imageDataUri,
                    enable_pbr = true,
                    should_remesh = true,
                    should_texture = true,
                    target_polycount = 20000,
                    pose_mode = "a-pose",
                    target_formats = new[] { "glb", "fbx" },
                    multi_view_thumbnails = true
                };
                var request = CreateMeshyRequest("POST", "https://api.meshy.ai/openapi/v1/image-to-3d", apiKey,
                    JsonUtility.ToJson(payload));
                StartMeshyRequest(request, MeshyRequestKind.CreateTask);
                characterDraft.meshyError = string.Empty;
                SaveCharacterDraft();
            }
            catch (Exception exception)
            {
                characterDraft.meshyError = exception.Message;
                SaveCharacterDraft();
            }
        }

        private void StartMeshyStatusCheck()
        {
            var apiKey = Environment.GetEnvironmentVariable("MESHY_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(characterDraft.meshyTaskId)) return;
            var request = CreateMeshyRequest("GET", "https://api.meshy.ai/openapi/v1/image-to-3d/" + characterDraft.meshyTaskId, apiKey, null);
            StartMeshyRequest(request, MeshyRequestKind.CheckStatus);
        }

        private static UnityWebRequest CreateMeshyRequest(string method, string url, string apiKey, string body)
        {
            var request = new UnityWebRequest(url, method)
            {
                downloadHandler = new DownloadHandlerBuffer()
            };
            request.SetRequestHeader("Authorization", "Bearer " + apiKey);
            if (!string.IsNullOrWhiteSpace(body))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                request.SetRequestHeader("Content-Type", "application/json");
            }
            return request;
        }

        private static string BuildImageDataUri(Texture2D image)
        {
            var path = AssetDatabase.GetAssetPath(image);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new InvalidOperationException("The approved concept image must be a PNG or JPG asset inside this Unity project.");

            var extension = Path.GetExtension(path).ToLowerInvariant();
            var mimeType = extension == ".png" ? "image/png" :
                extension == ".jpg" || extension == ".jpeg" ? "image/jpeg" : null;
            if (mimeType == null)
                throw new InvalidOperationException("Meshy Image-to-3D accepts PNG or JPG concept images.");

            return "data:" + mimeType + ";base64," + Convert.ToBase64String(File.ReadAllBytes(path));
        }

        private void StartMeshyRequest(UnityWebRequest request, MeshyRequestKind kind)
        {
            DisposeMeshyRequest();
            meshyRequest = request;
            meshyRequestKind = kind;
            meshyRequest.SendWebRequest();
        }

        private void ProcessMeshyRequest()
        {
            if (meshyRequest == null || !meshyRequest.isDone) return;

            var responseText = meshyRequest.downloadHandler != null ? meshyRequest.downloadHandler.text : string.Empty;
            if (meshyRequest.result != UnityWebRequest.Result.Success)
            {
                characterDraft.meshyError = "Meshy request failed: " + meshyRequest.error + "\n" + responseText;
            }
            else if (meshyRequestKind == MeshyRequestKind.CreateTask)
            {
                var response = JsonUtility.FromJson<MeshyCreateTaskResponse>(responseText);
                if (response == null || string.IsNullOrWhiteSpace(response.result))
                {
                    characterDraft.meshyError = "Meshy did not return a task id.\n" + responseText;
                }
                else
                {
                    characterDraft.meshyTaskId = response.result;
                    characterDraft.meshyStatus = "PENDING";
                    characterDraft.meshyProgress = 0;
                    characterDraft.stage = OntologyCharacterCreationStage.ModelGeneration;
                    characterDraft.meshyError = string.Empty;
                }
            }
            else if (meshyRequestKind == MeshyRequestKind.CheckStatus)
            {
                ApplyMeshyTaskStatus(JsonUtility.FromJson<MeshyTaskResponse>(responseText), responseText);
            }
            else
            {
                ImportDownloadedModel(meshyRequest.downloadHandler.data);
            }

            SaveCharacterDraft();
            DisposeMeshyRequest();
            Repaint();
        }

        private void ApplyMeshyTaskStatus(MeshyTaskResponse response, string rawResponse)
        {
            if (response == null || string.IsNullOrWhiteSpace(response.status))
            {
                characterDraft.meshyError = "Meshy returned an invalid task response.\n" + rawResponse;
                return;
            }

            characterDraft.meshyStatus = response.status;
            characterDraft.meshyProgress = response.progress;
            characterDraft.meshyThumbnailUrl = response.thumbnail_url;
            if (response.model_urls != null)
            {
                characterDraft.meshyGlbUrl = response.model_urls.glb;
                characterDraft.meshyFbxUrl = response.model_urls.fbx;
            }
            characterDraft.meshyError = response.task_error != null ? response.task_error.message : string.Empty;
            if (response.status == "SUCCEEDED")
            {
                characterDraft.stage = OntologyCharacterCreationStage.ModelReview;
                characterDraft.meshyError = string.Empty;
            }
        }

        private void DisposeMeshyRequest()
        {
            if (meshyRequest == null) return;
            if (!meshyRequest.isDone) meshyRequest.Abort();
            meshyRequest.Dispose();
            meshyRequest = null;
        }

        private void DrawModelImportTab()
        {
            EditorGUILayout.HelpBox(
                "Step 4: download the successful Meshy FBX, import it into Unity, then create an actor ready for animation and ontology setup.",
                MessageType.Info);
            characterDraft = (OntologyCharacterDraft)EditorGUILayout.ObjectField(
                "Character Draft", characterDraft, typeof(OntologyCharacterDraft), false);
            if (characterDraft == null)
            {
                EditorGUILayout.HelpBox("Select a Character Draft first.", MessageType.Warning);
                return;
            }

            if (characterDraft.stage != OntologyCharacterCreationStage.ModelReview &&
                characterDraft.generatedModelPrefab == null)
            {
                EditorGUILayout.HelpBox("Wait for a successful Meshy task before importing a model.", MessageType.Warning);
                return;
            }

            if (characterDraft.generatedModelPrefab == null)
            {
                if (string.IsNullOrWhiteSpace(characterDraft.meshyFbxUrl))
                {
                    EditorGUILayout.HelpBox("No FBX output URL is available. Check the Meshy task status again.", MessageType.Warning);
                    return;
                }

                using (new EditorGUI.DisabledScope(meshyRequest != null))
                {
                    if (GUILayout.Button("Download and Import FBX", GUILayout.Height(32f))) StartModelDownload();
                }
                return;
            }

            EditorGUILayout.ObjectField("Imported Model", characterDraft.generatedModelPrefab, typeof(GameObject), false);
            EditorGUILayout.LabelField("Asset Path", characterDraft.importedModelAssetPath);
            EditorGUILayout.LabelField("Humanoid Rig", characterDraft.hasHumanoidRig ? "Detected" : "Not detected");
            if (!string.IsNullOrWhiteSpace(characterDraft.rigValidationMessage))
                EditorGUILayout.HelpBox(characterDraft.rigValidationMessage,
                    characterDraft.hasHumanoidRig ? MessageType.Info : MessageType.Warning);

            using (new EditorGUI.DisabledScope(!characterDraft.hasHumanoidRig))
            {
                if (GUILayout.Button("Instantiate and Configure Character", GUILayout.Height(32f)))
                {
                    InstantiateAndConfigureImportedCharacter();
                }
            }
            if (!characterDraft.hasHumanoidRig)
                EditorGUILayout.HelpBox("A Humanoid rig is required before this model can use the current animation system. Configure its Avatar in the FBX import settings, then reimport.", MessageType.Info);
        }

        private void StartModelDownload()
        {
            var request = UnityWebRequest.Get(characterDraft.meshyFbxUrl);
            StartMeshyRequest(request, MeshyRequestKind.DownloadModel);
            characterDraft.meshyError = string.Empty;
            SaveCharacterDraft();
        }

        private void ImportDownloadedModel(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                characterDraft.meshyError = "Meshy returned an empty FBX download.";
                return;
            }

            var safeId = string.IsNullOrWhiteSpace(characterDraft.characterId) ? "GeneratedCharacter" : characterDraft.characterId;
            foreach (var invalid in Path.GetInvalidFileNameChars()) safeId = safeId.Replace(invalid, '_');
            const string rootFolder = "Assets/GeneratedCharacters";
            if (!AssetDatabase.IsValidFolder(rootFolder)) AssetDatabase.CreateFolder("Assets", "GeneratedCharacters");
            var characterFolder = rootFolder + "/" + safeId;
            if (!AssetDatabase.IsValidFolder(characterFolder)) AssetDatabase.CreateFolder(rootFolder, safeId);

            var assetPath = characterFolder + "/" + safeId + ".fbx";
            File.WriteAllBytes(assetPath, bytes);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
            {
                characterDraft.meshyError = "Unity could not import the downloaded FBX.";
                return;
            }

            characterDraft.generatedModelPrefab = prefab;
            characterDraft.importedModelAssetPath = assetPath;
            var animator = prefab.GetComponentInChildren<Animator>(true);
            characterDraft.hasHumanoidRig = animator != null && animator.avatar != null && animator.isHuman;
            characterDraft.rigValidationMessage = characterDraft.hasHumanoidRig
                ? "Humanoid Avatar detected. This model can enter the animation setup step."
                : "No valid Humanoid Avatar was detected in this FBX.";
            SaveCharacterDraft();
        }

        private void InstantiateAndConfigureImportedCharacter()
        {
            var instance = PrefabUtility.InstantiatePrefab(characterDraft.generatedModelPrefab) as GameObject;
            if (instance == null)
            {
                characterDraft.rigValidationMessage = "Unable to instantiate the imported model.";
                SaveCharacterDraft();
                return;
            }

            instance.name = characterDraft.characterId;
            actorObject = instance;
            actorId = characterDraft.characterId;
            bootstrap ??= FindAnyObjectByType<OntologyWorldBootstrap>();
            animationDatabase ??= AssetDatabase.LoadAssetAtPath<OntologyAnimationDatabase>("Assets/Data/Ontology/AnimationDatabase.asset");
            profile = characterDraft.actorProfile;
            ApplySetup();
            characterDraft.actorProfile = profile;
            characterDraft.stage = OntologyCharacterCreationStage.UnitySetup;
            characterDraft.rigValidationMessage = "Character instantiated and linked. Select its animation repertoire in Animation Setup, then add initial Facts.";
            SaveCharacterDraft();
        }

        private void DrawPlayerOverviewTab()
        {
            EnsureCurrentPlayerLoaded();
            if (actorObject == null)
            {
                EditorGUILayout.HelpBox("No configured actor was found in the open scene.", MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField("Current Character", EditorStyles.boldLabel);
            EditorGUILayout.ObjectField("Actor Object", actorObject, typeof(GameObject), true);
            EditorGUILayout.LabelField("Actor Id", actorId);
            EditorGUILayout.LabelField("Profile", profile != null ? profile.name : "None");
            EditorGUILayout.LabelField("Registered Animations", profile == null || profile.animationIds == null
                ? "0"
                : profile.animationIds.Length.ToString());

            EditorGUILayout.Space(6);
            intent = EditorGUILayout.TextField("Preview Intent", intent);
            var matchingDefinitions = new List<OntologyAnimationDefinition>(MatchingDefinitions());
            DrawAnimationPreview(matchingDefinitions);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Current Ontology Data", EditorStyles.boldLabel);
            DrawActorFacts();
        }

        private void DrawFactEditorTab()
        {
            EnsureCurrentPlayerLoaded();
            EditorGUILayout.HelpBox(
                "Edit the selected character's live ontology facts. These edits affect the current world immediately; Reset World or Inject Ontology Data can reconstruct configured facts.",
                MessageType.Info);
            DrawFactEditor();
        }

        private void EnsureCurrentPlayerLoaded()
        {
            if (actorObject != null) return;
            var adapter = FindAnyObjectByType<OntologyAnimationAdapter>();
            if (adapter != null) LoadActor(adapter.gameObject);
        }

        private void DrawActorFacts()
        {
            if (bootstrap == null) bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            if (bootstrap == null || bootstrap.World == null)
            {
                EditorGUILayout.HelpBox("World Bootstrap has no active ontology world. Use Inject Ontology Data, then enter Play Mode to inspect live facts.", MessageType.Info);
                return;
            }

            var facts = new List<string>();
            foreach (var fact in bootstrap.World.Facts)
            {
                if (fact.Subject.ToString() == actorId)
                    facts.Add(fact.Subject + "  — " + fact.Predicate + " → " + fact.Object);
            }
            facts.Sort(StringComparer.Ordinal);
            if (facts.Count == 0)
            {
                EditorGUILayout.HelpBox("No facts are currently registered for this actor.", MessageType.Warning);
                return;
            }

            ontologyFactScroll = EditorGUILayout.BeginScrollView(ontologyFactScroll, GUILayout.Height(180f));
            foreach (var fact in facts) EditorGUILayout.SelectableLabel(fact, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            EditorGUILayout.EndScrollView();
        }

        private void DrawFactEditor()
        {
            if (bootstrap == null) bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            if (bootstrap == null)
            {
                EditorGUILayout.HelpBox("World Bootstrap is required to edit facts.", MessageType.Warning);
                return;
            }

            if (bootstrap.World == null)
            {
                if (GUILayout.Button("Initialize World From Scene"))
                {
                    bootstrap.ResetWorld(logReport: false);
                    factEditorMessage = "World initialized from the current scene.";
                }
                return;
            }

            EditorGUILayout.LabelField("Character", actorId);
            newFactPredicate = EditorGUILayout.TextField("Relationship", newFactPredicate);
            newFactObject = EditorGUILayout.TextField("Value", newFactObject);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Fact", GUILayout.Height(26f))) AddCharacterFact();
            if (GUILayout.Button("Run Simulation", GUILayout.Height(26f)))
            {
                bootstrap.RunSimulation();
                factEditorMessage = "Simulation ran with the current facts.";
            }
            if (GUILayout.Button("Reset World", GUILayout.Height(26f)))
            {
                bootstrap.ResetWorld(logReport: false);
                factEditorMessage = "World reset from scene data.";
            }
            EditorGUILayout.EndHorizontal();
            if (!string.IsNullOrWhiteSpace(factEditorMessage))
                EditorGUILayout.HelpBox(factEditorMessage, MessageType.None);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Facts for " + actorId, EditorStyles.boldLabel);
            var facts = new List<OntologyFact>();
            foreach (var fact in bootstrap.World.Facts)
                if (fact.Subject.ToString() == actorId) facts.Add(fact);
            facts.Sort((left, right) => string.CompareOrdinal(left.ToString(), right.ToString()));

            if (facts.Count == 0)
            {
                EditorGUILayout.HelpBox("No facts are currently registered for this character.", MessageType.Warning);
                return;
            }

            ontologyFactScroll = EditorGUILayout.BeginScrollView(ontologyFactScroll, GUILayout.Height(250f));
            foreach (var fact in facts)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(fact.Subject + "  /  " + fact.Predicate + "  /  " + fact.Object);
                if (GUILayout.Button("Delete", GUILayout.Width(64f))) RemoveCharacterFact(fact);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        private void AddCharacterFact()
        {
            if (string.IsNullOrWhiteSpace(actorId) || string.IsNullOrWhiteSpace(newFactPredicate) || string.IsNullOrWhiteSpace(newFactObject))
            {
                factEditorMessage = "Character, relationship, and value are all required.";
                return;
            }

            var added = bootstrap.World.AddFact(actorId.Trim(), newFactPredicate.Trim(), newFactObject.Trim());
            factEditorMessage = added
                ? "Fact added: " + actorId + " / " + newFactPredicate + " / " + newFactObject
                : "That fact already exists or is invalid.";
        }

        private void RemoveCharacterFact(OntologyFact fact)
        {
            var removed = bootstrap.World.RemoveFact(fact.Subject, fact.Predicate, fact.Object);
            factEditorMessage = removed ? "Fact deleted." : "The fact could not be deleted.";
        }

        private void DrawAnimationDatabaseRegistration()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(
                "Register Animation through Manifest",
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "The manifest is the authoring source. Registration validates the entry, then synchronizes the runtime database and ActorProfile repertoire. The intent describes what the clip expresses; ontology rules decide when that intent is active.",
                MessageType.None);

            using (new EditorGUI.DisabledScope(animationDatabase == null))
            {
                newAnimationId = EditorGUILayout.TextField("Animation Id", newAnimationId);
                newAnimationClip = (AnimationClip)EditorGUILayout.ObjectField("Animation Clip", newAnimationClip, typeof(AnimationClip), false);
                newAnimationIntent = EditorGUILayout.TextField("Ontology Intent", newAnimationIntent);
                newAnimationPriority = EditorGUILayout.IntField("Priority", newAnimationPriority);
                newAnimationLoop = EditorGUILayout.Toggle("Loop", newAnimationLoop);
                newAnimationInterruptible =
                    EditorGUILayout.Toggle(
                        "Interruptible",
                        newAnimationInterruptible);
                newAnimationCanBlend =
                    EditorGUILayout.Toggle("Can Blend", newAnimationCanBlend);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Add to Manifest"))
                    AddAnimationToManifest(false);
                if (GUILayout.Button("Add and Register for Profile"))
                    AddAnimationToManifest(true);
                EditorGUILayout.EndHorizontal();
            }

            if (animationDatabase == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign the synchronized Animation Database before registering a clip.",
                    MessageType.Info);
            }
        }

        private void AddAnimationToManifest(bool registerForProfile)
        {
            if (animationDatabase == null || newAnimationClip == null ||
                string.IsNullOrWhiteSpace(newAnimationId) || string.IsNullOrWhiteSpace(newAnimationIntent))
            {
                Debug.LogWarning("Animation registration requires an id, clip, and ontology intent.", animationDatabase);
                return;
            }

            var targetProfile = registerForProfile ? EnsureProfile() : null;
            var manifest =
                OntologyAnimationContentPipeline.CreateOrMigrateManifest();
            var previousEntries = manifest.Entries
                .Where(value => value != null)
                .ToArray();
            var entry = OntologyAnimationContentPipeline.CreateManifestEntry(
                newAnimationId,
                newAnimationClip,
                newAnimationIntent,
                newAnimationPriority,
                newAnimationLoop,
                newAnimationInterruptible,
                newAnimationCanBlend,
                targetProfile);
            if (!OntologyAnimationContentPipeline.TryAddManifestEntry(
                    manifest,
                    entry,
                    out var error))
            {
                Debug.LogWarning(error, manifest);
                return;
            }

            EditorUtility.SetDirty(manifest);
            if (!OntologyAnimationContentPipeline.ValidateAndSynchronize(
                    manifest,
                    true))
            {
                manifest.ReplaceEntries(previousEntries);
                EditorUtility.SetDirty(manifest);
                AssetDatabase.SaveAssets();
                Debug.LogError(
                    "Animation manifest synchronization failed for: " +
                    newAnimationId,
                    manifest);
                return;
            }

            animationDatabase =
                AssetDatabase.LoadAssetAtPath<OntologyAnimationDatabase>(
                    OntologyAnimationContentPipeline.DatabasePath);
            profile = registerForProfile ? targetProfile : profile;
            Debug.Log(
                "Registered ontology animation through manifest: " +
                newAnimationId + " (" + newAnimationIntent + ")",
                manifest);
            newAnimationClip = null;
        }

        private void DrawProfileRepertoire()
        {
            EditorGUILayout.Space(4);
            showProfileRepertoire = EditorGUILayout.Foldout(showProfileRepertoire, "Profile Animation Repertoire", true);
            if (!showProfileRepertoire) return;

            EditorGUILayout.HelpBox(
                "Select the manifest entries this ActorProfile can express. Changes are validated in the manifest, then synchronized to the generated profile repertoire.",
                MessageType.None);
            if (animationDatabase == null || animationDatabase.Definitions == null)
            {
                EditorGUILayout.HelpBox("Assign an Animation Database to browse its full list.", MessageType.Info);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Select All Database"))
            {
                var ids = new List<string>();
                foreach (var definition in animationDatabase.Definitions)
                    if (definition != null && !string.IsNullOrWhiteSpace(definition.animationId)) ids.Add(definition.animationId);
                SetProfileAnimations(ids);
            }
            if (GUILayout.Button("Clear Profile List")) SetProfileAnimations(Array.Empty<string>());
            EditorGUILayout.EndHorizontal();

            repertoireScroll = EditorGUILayout.BeginScrollView(repertoireScroll, GUILayout.Height(190));
            foreach (var definition in animationDatabase.Definitions)
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.animationId)) continue;
                var selected = profile != null && profile.HasAnimation(definition.animationId);
                var intents = definition.intents == null || definition.intents.Length == 0
                    ? "No intent"
                    : string.Join(", ", definition.intents);
                var label = definition.animationId + "  [" + intents + "]";
                var next = EditorGUILayout.ToggleLeft(label, selected);
                if (next != selected) SetProfileAnimation(definition.animationId, next);
            }
            EditorGUILayout.EndScrollView();
        }

        private void SetProfileAnimation(string animationId, bool selected)
        {
            var ids = new HashSet<string>(profile != null && profile.animationIds != null
                ? profile.animationIds
                : Array.Empty<string>(), StringComparer.Ordinal);
            if (selected) ids.Add(animationId);
            else ids.Remove(animationId);
            SetProfileAnimations(ids);
        }

        private void SetProfileAnimations(IEnumerable<string> animationIds)
        {
            profile = EnsureProfile();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var animationId in animationIds)
                if (!string.IsNullOrWhiteSpace(animationId)) ids.Add(animationId);
            var manifest =
                OntologyAnimationContentPipeline.CreateOrMigrateManifest();
            var entries = manifest.Entries
                .Where(value => value != null)
                .ToArray();
            var previousAssignments = entries.ToDictionary(
                value => value,
                value => value.profiles == null
                    ? Array.Empty<OntologyActorProfile>()
                    : value.profiles.ToArray());

            foreach (var entry in entries)
            {
                var assigned = new HashSet<OntologyActorProfile>(
                    entry.profiles == null
                        ? Array.Empty<OntologyActorProfile>()
                        : entry.profiles);
                if (ids.Contains(entry.animationId)) assigned.Add(profile);
                else assigned.Remove(profile);
                entry.profiles = assigned
                    .Where(value => value != null)
                    .ToArray();
            }

            EditorUtility.SetDirty(manifest);
            if (OntologyAnimationContentPipeline.ValidateAndSynchronize(
                    manifest,
                    false))
            {
                animationDatabase =
                    AssetDatabase.LoadAssetAtPath<OntologyAnimationDatabase>(
                        OntologyAnimationContentPipeline.DatabasePath);
                return;
            }

            foreach (var pair in previousAssignments)
                pair.Key.profiles = pair.Value;
            EditorUtility.SetDirty(manifest);
            AssetDatabase.SaveAssets();
            Debug.LogError(
                "ActorProfile repertoire change failed manifest validation and was reverted.",
                manifest);
        }

        private void DrawAnimationPreview(IReadOnlyList<OntologyAnimationDefinition> matchingDefinitions)
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Character Animation Preview", EditorStyles.boldLabel);

            if (actorObject == null)
            {
                EditorGUILayout.HelpBox("Select an Actor Object to preview its model and animation.", MessageType.Info);
                return;
            }

            if (previewClip == null && matchingDefinitions.Count > 0)
            {
                SelectPreviewClip(matchingDefinitions[0].clip);
            }

            if (previewClip == null)
            {
                EditorGUILayout.HelpBox("No playable clip matches this intent.", MessageType.Warning);
                return;
            }

            EnsurePreviewInstance();
            var previewRect = GUILayoutUtility.GetRect(10f, 260f, GUILayout.ExpandWidth(true));
            DrawPreviewTexture(previewRect);
            HandlePreviewInput(previewRect);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(previewIsPlaying ? "Pause" : "Play")) previewIsPlaying = !previewIsPlaying;
            if (GUILayout.Button("Stop"))
            {
                previewIsPlaying = false;
                previewTime = 0f;
                SamplePreviewAnimation();
            }
            if (GUILayout.Button("Restart"))
            {
                previewTime = 0f;
                previewIsPlaying = true;
                SamplePreviewAnimation();
            }
            EditorGUILayout.EndHorizontal();
            previewSpeed = EditorGUILayout.Slider("Preview Speed", previewSpeed, 0.1f, 2f);
            EditorGUILayout.LabelField("Clip", previewClip.name + "  |  Drag to rotate, scroll to zoom.", EditorStyles.miniLabel);
        }

        private void SelectPreviewClip(AnimationClip clip)
        {
            if (clip == null) return;
            previewClip = clip;
            previewTime = 0f;
            previewIsPlaying = true;
            RebuildPreviewPlayable();
            SamplePreviewAnimation();
            Repaint();
        }

        private void LoadActor(GameObject target)
        {
            if (target == null) return;
            actorObject = target;
            var adapter = actorObject.GetComponent<OntologyAnimationAdapter>();
            if (adapter == null)
            {
                CleanupPreview();
                Repaint();
                return;
            }

            var serialized = new SerializedObject(adapter);
            bootstrap = serialized.FindProperty("bootstrap").objectReferenceValue as OntologyWorldBootstrap;
            animationDatabase = serialized.FindProperty("animationDatabase").objectReferenceValue as OntologyAnimationDatabase;
            profile = serialized.FindProperty("actorProfile").objectReferenceValue as OntologyActorProfile;
            actorId = serialized.FindProperty("actorId").stringValue;
            if (profile != null)
            {
                actorTypeIndex = Array.IndexOf(ActorTypes, profile.actorType);
                rigTypeIndex = Array.IndexOf(RigTypes, profile.rigType);
                if (actorTypeIndex < 0) actorTypeIndex = 0;
                if (rigTypeIndex < 0) rigTypeIndex = 0;
            }
            CleanupPreview();
            Repaint();
        }

        private void EnsurePreviewInstance()
        {
            if (previewUtility != null && previewSource == actorObject && previewInstance != null) return;

            CleanupPreview();
            previewUtility = new PreviewRenderUtility();
            previewUtility.cameraFieldOfView = 30f;
            previewSource = actorObject;
            previewInstance = Instantiate(actorObject);
            previewInstance.name = actorObject.name + "_AnimationPreview";
            SetPreviewHideFlags(previewInstance);
            foreach (var behaviour in previewInstance.GetComponentsInChildren<Behaviour>(true))
            {
                if (behaviour is Animator) continue;
                behaviour.enabled = false;
            }
            ApplyPreviewMaterials(previewInstance);
            previewUtility.AddSingleGO(previewInstance);
            RebuildPreviewPlayable();
            SamplePreviewAnimation();
        }

        private void DrawPreviewTexture(Rect rect)
        {
            if (previewUtility == null || previewInstance == null) return;

            SamplePreviewAnimation();
            var bounds = CalculatePreviewBounds(previewInstance);
            var radius = Mathf.Max(0.5f, bounds.extents.magnitude);
            var target = bounds.center;
            var rotation = Quaternion.Euler(previewPitch, previewYaw, 0f);
            var cameraOffset = rotation * new Vector3(0f, radius * 0.15f, -radius * 3.2f * previewZoom);
            previewUtility.BeginPreview(rect, GUIStyle.none);
            previewUtility.camera.clearFlags = CameraClearFlags.Color;
            previewUtility.camera.backgroundColor = new Color(0.14f, 0.15f, 0.17f, 1f);
            previewUtility.camera.transform.position = target + cameraOffset;
            previewUtility.camera.transform.rotation = Quaternion.LookRotation(target - previewUtility.camera.transform.position, Vector3.up);
            previewUtility.lights[0].transform.rotation = Quaternion.Euler(35f, -30f, 0f);
            previewUtility.lights[0].intensity = 1.2f;
            previewUtility.lights[1].intensity = 0.8f;
            previewUtility.ambientColor = new Color(0.55f, 0.55f, 0.55f, 1f);
            previewUtility.Render();
            GUI.DrawTexture(rect, previewUtility.EndPreview(), ScaleMode.StretchToFill, false);
        }

        private void HandlePreviewInput(Rect rect)
        {
            var currentEvent = Event.current;
            if (!rect.Contains(currentEvent.mousePosition)) return;

            if (currentEvent.type == EventType.MouseDrag && currentEvent.button == 0)
            {
                previewYaw += currentEvent.delta.x;
                previewPitch = Mathf.Clamp(previewPitch - currentEvent.delta.y, -60f, 60f);
                currentEvent.Use();
                Repaint();
            }
            else if (currentEvent.type == EventType.ScrollWheel)
            {
                previewZoom = Mathf.Clamp(previewZoom + currentEvent.delta.y * 0.05f, 0.45f, 2.5f);
                currentEvent.Use();
                Repaint();
            }
        }

        private void UpdatePreview()
        {
            ProcessMeshyRequest();
            var currentTime = EditorApplication.timeSinceStartup;
            var delta = (float)(currentTime - lastPreviewUpdateTime);
            lastPreviewUpdateTime = currentTime;
            if (previewIsPlaying && previewClip != null)
            {
                previewTime += Mathf.Max(0f, delta) * previewSpeed;
                if (previewClip.length > 0f) previewTime %= previewClip.length;
                SamplePreviewAnimation();
                Repaint();
            }
        }

        private void SamplePreviewAnimation()
        {
            if (previewInstance == null || previewClip == null) return;
            RebuildPreviewPlayable();
            if (!previewGraph.IsValid() || !previewClipPlayable.IsValid()) return;

            previewClipPlayable.SetTime(previewTime);
            previewGraph.Evaluate(0f);
        }

        private void RebuildPreviewPlayable()
        {
            if (previewInstance == null || previewClip == null) return;
            if (previewGraph.IsValid() && previewGraphClip == previewClip) return;

            DestroyPreviewPlayable();
            var animator = FindAnimator(previewInstance);
            if (animator == null) return;

            previewGraph = PlayableGraph.Create("OntologyAnimationPreview");
            previewGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(previewGraph, "PreviewAnimation", animator);
            previewClipPlayable = AnimationClipPlayable.Create(previewGraph, previewClip);
            previewClipPlayable.SetApplyFootIK(false);
            previewClipPlayable.SetApplyPlayableIK(false);
            output.SetSourcePlayable(previewClipPlayable);
            previewGraph.Play();
            previewGraphClip = previewClip;
        }

        private void DestroyPreviewPlayable()
        {
            if (previewGraph.IsValid()) previewGraph.Destroy();
            previewGraphClip = null;
        }

        private static Bounds CalculatePreviewBounds(GameObject target)
        {
            var renderers = target.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.one);

            var result = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) result.Encapsulate(renderers[i].bounds);
            return result;
        }

        private static void SetPreviewHideFlags(GameObject target)
        {
            target.hideFlags = HideFlags.HideAndDontSave;
            foreach (var transform in target.GetComponentsInChildren<Transform>(true)) transform.gameObject.hideFlags = HideFlags.HideAndDontSave;
        }

        private void ApplyPreviewMaterials(GameObject target)
        {
            foreach (var renderer in target.GetComponentsInChildren<Renderer>(true))
            {
                var sourceMaterials = renderer.sharedMaterials;
                var compatibleMaterials = new Material[sourceMaterials.Length];
                for (var i = 0; i < sourceMaterials.Length; i++)
                {
                    compatibleMaterials[i] = CreatePreviewMaterial(sourceMaterials[i]);
                }
                renderer.sharedMaterials = compatibleMaterials;
            }
        }

        private Material CreatePreviewMaterial(Material source)
        {
            var texture = source != null && source.HasProperty("_BaseMap")
                ? source.GetTexture("_BaseMap")
                : source != null ? source.mainTexture : null;
            var color = source != null && source.HasProperty("_BaseColor")
                ? source.GetColor("_BaseColor")
                : source != null ? source.color : Color.white;

            var shader = Shader.Find(texture != null ? "Unlit/Texture" : "Unlit/Color");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            if (texture != null && material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            if (material.HasProperty("_Color")) material.color = color;
            previewMaterials.Add(material);
            return material;
        }

        private void CleanupPreview()
        {
            DestroyPreviewPlayable();
            foreach (var material in previewMaterials) DestroyImmediate(material);
            previewMaterials.Clear();
            if (previewUtility != null) previewUtility.Cleanup();
            previewUtility = null;
            previewInstance = null;
            previewSource = null;
        }

        private IEnumerable<OntologyAnimationDefinition> MatchingDefinitions()
        {
            if (animationDatabase == null || animationDatabase.Definitions == null) yield break;
            var actorType = ActorTypes[Mathf.Clamp(actorTypeIndex, 0, ActorTypes.Length - 1)];
            var rigType = RigTypes[Mathf.Clamp(rigTypeIndex, 0, RigTypes.Length - 1)];
            foreach (var definition in animationDatabase.Definitions)
            {
                if (definition == null || definition.clip == null || !HasIntent(definition, intent)) continue;
                if (!Matches(definition.actorTypes, actorType) || !Matches(definition.rigTypes, rigType)) continue;
                yield return definition;
            }
        }

        private void ApplySetup()
        {
            if (actorObject == null) { Debug.LogError("Actor Animation Setup: Actor Object is required."); return; }
            if (bootstrap == null) bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            if (animationDatabase == null) animationDatabase = AssetDatabase.LoadAssetAtPath<OntologyAnimationDatabase>("Assets/Data/Ontology/AnimationDatabase.asset");
            if (bootstrap == null) { Debug.LogError("Actor Animation Setup: World Bootstrap is missing."); return; }
            if (animationDatabase == null) { Debug.LogError("Actor Animation Setup: Animation Database is missing."); return; }
            if (string.IsNullOrWhiteSpace(actorId)) actorId = actorObject.name;

            profile = EnsureProfile();
            var animator = FindAnimator(actorObject);
            var adapter = GetOrAdd<OntologyAnimationAdapter>(actorObject);
            var adapterSerialized = new SerializedObject(adapter);
            adapterSerialized.FindProperty("bootstrap").objectReferenceValue = bootstrap;
            adapterSerialized.FindProperty("animationDatabase").objectReferenceValue = animationDatabase;
            adapterSerialized.FindProperty("actorProfile").objectReferenceValue = profile;
            adapterSerialized.FindProperty("targetAnimator").objectReferenceValue = animator;
            adapterSerialized.FindProperty("actorId").stringValue = actorId;
            adapterSerialized.ApplyModifiedPropertiesWithoutUndo();

            var synchronizer = GetOrAdd<OntologyActorProfileFactSynchronizer>(actorObject);
            var syncSerialized = new SerializedObject(synchronizer);
            syncSerialized.FindProperty("bootstrap").objectReferenceValue = bootstrap;
            syncSerialized.FindProperty("profile").objectReferenceValue = profile;
            syncSerialized.FindProperty("actorId").stringValue = actorId;
            syncSerialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(actorObject);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(actorObject.scene);
            Selection.activeGameObject = actorObject;
            Debug.Log("Actor Animation Setup complete: " + actorId + " (" + ActorTypes[actorTypeIndex] + ").");
        }

        private void InjectFacts()
        {
            if (actorObject == null) { Debug.LogError("Select an actor object first."); return; }
            var sync = actorObject.GetComponent<OntologyActorProfileFactSynchronizer>();
            if (sync == null) { ApplySetup(); sync = actorObject.GetComponent<OntologyActorProfileFactSynchronizer>(); }
            if (sync != null) sync.Sync();
        }

        private OntologyActorProfile EnsureProfile()
        {
            if (profile == null)
            {
                const string folder = "Assets/Data/Ontology/Actors";
                if (!AssetDatabase.IsValidFolder("Assets/Data/Ontology")) AssetDatabase.CreateFolder("Assets/Data", "Ontology");
                if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Data/Ontology", "Actors");
                var path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + actorId + "Profile.asset");
                profile = CreateInstance<OntologyActorProfile>();
                profile.actorType = ActorTypes[actorTypeIndex];
                profile.rigType = RigTypes[rigTypeIndex];
                profile.capabilities = DefaultCapabilities(profile.actorType);
                AssetDatabase.CreateAsset(profile, path);
                AssetDatabase.SaveAssets();
            }
            else
            {
                profile.actorType = ActorTypes[actorTypeIndex];
                profile.rigType = RigTypes[rigTypeIndex];
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
            }
            return profile;
        }

        private static string[] DefaultCapabilities(string type)
        {
            if (type == "Monster") return new[] { "Locomotion", "Combat", "Aggro" };
            if (type == "NPC") return new[] { "Locomotion", "Talk", "Work" };
            return new[] { "Locomotion", "Jump", "Combat", "Dodge" };
        }

        private static Animator FindAnimator(GameObject root)
        {
            foreach (var candidate in root.GetComponentsInChildren<Animator>(true))
                if (candidate != null && candidate.avatar != null && candidate.isHuman) return candidate;
            return root.GetComponentInChildren<Animator>(true);
        }

        private static T GetOrAdd<T>(GameObject target) where T : Component
        {
            var value = target.GetComponent<T>();
            return value != null ? value : Undo.AddComponent<T>(target);
        }

        private static bool HasIntent(OntologyAnimationDefinition definition, string value)
        {
            if (definition.intents == null) return false;
            foreach (var item in definition.intents) if (item == value) return true;
            return false;
        }

        private static bool Matches(string[] values, string expected)
        {
            if (values == null || values.Length == 0) return true;
            foreach (var item in values) if (item == expected) return true;
            return false;
        }

        private enum MeshyRequestKind
        {
            CreateTask,
            CheckStatus,
            DownloadModel
        }

        [Serializable]
        private sealed class MeshyImageTo3DRequest
        {
            public string image_url;
            public bool enable_pbr;
            public bool should_remesh;
            public bool should_texture;
            public int target_polycount;
            public string pose_mode;
            public string[] target_formats;
            public bool multi_view_thumbnails;
        }

        [Serializable]
        private sealed class MeshyCreateTaskResponse
        {
            public string result;
        }

        [Serializable]
        private sealed class MeshyTaskResponse
        {
            public string status;
            public int progress;
            public string thumbnail_url;
            public MeshyModelUrls model_urls;
            public MeshyTaskError task_error;
        }

        [Serializable]
        private sealed class MeshyModelUrls
        {
            public string glb;
            public string fbx;
        }

        [Serializable]
        private sealed class MeshyTaskError
        {
            public string message;
        }
    }
}
