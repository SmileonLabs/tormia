using System;
using UnityEngine;
using UnityEngine.UI;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Presentation-only 3D previews for the authored account character cards.
    /// Account-owned equippedPartIds are projected onto temporary visual clones;
    /// no world Facts or durable profile data are changed here.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class OntologyAccountCharacterSelectionPreviewPresenter : MonoBehaviour
    {
        [Serializable]
        private sealed class PreviewSlot
        {
            public Transform modelAnchor;
            public Camera previewCamera;
            public RawImage liveImage;
            public RawImage fallbackImage;
            [Tooltip("Render the currently selected character instead of the character at this slot index.")]
            public bool useSelectedCharacter;
            [Range(8, 31)] public int previewLayer = 28;

            [NonSerialized] public GameObject model;
            [NonSerialized] public RenderTexture texture;
            [NonSerialized] public string appearanceFingerprint;
        }

        [SerializeField] private OntologyWorldAuthorityAccountEntryFlow entryFlow;
        [SerializeField] private CanvasGroup panelGroup;
        [SerializeField] private OntologyCharacterPartAdapter sourcePartAdapter;
        [SerializeField] private OntologyCharacterPartDatabase partDatabase;
        [SerializeField] private Transform previewStudioRoot;
        [SerializeField] private PreviewSlot[] previewSlots = Array.Empty<PreviewSlot>();
        [SerializeField, Tooltip("Use the selected account character for every configured slot (appearance review).")]
        private bool useCurrentCharacterOnly;
        [SerializeField, Range(256, 1024)] private int textureResolution = 512;
        [SerializeField, Range(18f, 55f)] private float fieldOfView = 25f;
        [SerializeField, Range(1f, 1.8f)] private float framingPadding = 1.15f;
        [SerializeField, Tooltip("Scales the renderer bounds used for framing. Keep (1,1) for a full body; use smaller values for a portrait crop.")]
        private Vector2 framingExtentScale = Vector2.one;
        [SerializeField, Range(0f, 1f), Tooltip("Vertical focus within the visible renderer bounds.")]
        private float framingFocusY = 0.51f;
        [SerializeField] private Vector3 viewDirection = new(0f, 0.06f, 1f);

        private OntologyWorldAuthorityAccountEntryFlow subscribedEntryFlow;
        private float nextDependencyRetryTime;

        private void Awake() => ResolveDependencies();

        private void OnEnable()
        {
            ResolveDependencies();
            UpdateEntryFlowSubscription();
            RefreshPreviews();
        }

        private void OnDisable()
        {
            if (subscribedEntryFlow != null)
            {
                subscribedEntryFlow.StateChanged -= RefreshPreviews;
                subscribedEntryFlow = null;
            }
            SetCamerasEnabled(false);
        }

        private void LateUpdate()
        {
            var visible = panelGroup != null && panelGroup.alpha > 0.01f;
            SetCamerasEnabled(visible);
            if (!visible) return;

            // TormiaUI can become visible before the additive world scene has
            // composed its inactive player adapter. Retry until that visual
            // source exists so previews do not depend on another account event.
            if (Application.isPlaying && sourcePartAdapter == null &&
                Time.unscaledTime >= nextDependencyRetryTime)
            {
                nextDependencyRetryTime = Time.unscaledTime + 0.25f;
                RefreshPreviews();
            }

            for (var index = 0; index < previewSlots.Length; index++)
            {
                var slot = previewSlots[index];
                if (slot?.model != null) Frame(slot);
            }
        }

        public void RefreshPreviews()
        {
            ResolveDependencies();
            UpdateEntryFlowSubscription();
            if (!Application.isPlaying) SetCamerasEnabled(false);
            var characters = entryFlow?.Characters;
            for (var index = 0; index < previewSlots.Length; index++)
            {
                var slot = previewSlots[index];
                var character = useCurrentCharacterOnly || (slot != null && slot.useSelectedCharacter)
                    ? entryFlow?.CurrentCharacter
                    : characters != null && index < characters.Count ? characters[index] : null;
                RefreshSlot(slot, character, index);
            }

            var visible = panelGroup != null && panelGroup.alpha > 0.01f;
            SetCamerasEnabled(visible);
            if (!Application.isPlaying && visible)
            {
                foreach (var slot in previewSlots)
                {
                    if (slot?.model == null || slot.previewCamera == null) continue;
                    Frame(slot);
                }
            }
        }

        private void RefreshSlot(PreviewSlot slot, OntologyAuthorityPlayerCharacter character, int index)
        {
            if (slot == null) return;
            var editorSample = !Application.isPlaying && character == null;
            // In Edit Mode keep the authored fallback visible. RenderTextures do
            // not reliably repaint while the Game View is idle, which otherwise
            // leaves artists with blank preview boxes in the hierarchy.
            if (!Application.isPlaying)
            {
                if (slot.liveImage != null) slot.liveImage.gameObject.SetActive(false);
                if (slot.fallbackImage != null) slot.fallbackImage.gameObject.SetActive(true);
                return;
            }
            var canRender = (character != null || editorSample) && sourcePartAdapter != null && sourcePartAdapter.VisualRoot != null
                && slot.modelAnchor != null && slot.previewCamera != null && slot.liveImage != null;
            if (!canRender)
            {
                if (slot.liveImage != null) slot.liveImage.gameObject.SetActive(false);
                if (slot.fallbackImage != null) slot.fallbackImage.gameObject.SetActive(character != null || editorSample);
                return;
            }

            EnsureRenderTarget(slot, index);
            EnsureModel(slot, index);
            var fingerprint = editorSample ? "editor-default" : BuildFingerprint(character);
            if (!string.Equals(slot.appearanceFingerprint, fingerprint, StringComparison.Ordinal))
            {
                OntologyCharacterAppearanceProjector.ApplyPresentation(
                    partDatabase,
                    slot.model.transform,
                    editorSample ? null : character.equippedPartIds);
                slot.appearanceFingerprint = fingerprint;
            }

            slot.liveImage.texture = slot.texture;
            slot.liveImage.gameObject.SetActive(true);
            if (slot.fallbackImage != null) slot.fallbackImage.gameObject.SetActive(false);
            Frame(slot);
        }

        private void EnsureRenderTarget(PreviewSlot slot, int index)
        {
            var height = Mathf.Clamp(textureResolution, 256, 1024);
            var imageRect = slot.liveImage.rectTransform.rect;
            var imageAspect = imageRect.height > 0.01f
                ? Mathf.Clamp(imageRect.width / imageRect.height, 0.5f, 2.5f)
                : 1f;
            var width = Mathf.Max(256, Mathf.RoundToInt(height * imageAspect));
            if (slot.texture == null || slot.texture.width != width || slot.texture.height != height)
            {
                if (slot.texture != null)
                {
                    slot.texture.Release();
                    DestroyPreviewObject(slot.texture);
                }
                slot.texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
                {
                    name = "CharacterSelectionPreview_" + (index + 1),
                    antiAliasing = 4,
                    useMipMap = false,
                    autoGenerateMips = false
                };
                slot.texture.Create();
            }

            var camera = slot.previewCamera;
            camera.targetTexture = slot.texture;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            camera.fieldOfView = fieldOfView;
            camera.aspect = imageAspect;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 50f;
            camera.allowHDR = false;
            camera.allowMSAA = true;
            camera.cullingMask = 1 << slot.previewLayer;
        }

        private void EnsureModel(PreviewSlot slot, int index)
        {
            if (slot.model != null)
            {
                SetLayerRecursively(slot.model.transform, slot.previewLayer);
                return;
            }
            for (var childIndex = slot.modelAnchor.childCount - 1; childIndex >= 1; childIndex--)
                DestroyPreviewObject(slot.modelAnchor.GetChild(childIndex).gameObject);
            slot.model = slot.modelAnchor.childCount > 0
                ? slot.modelAnchor.GetChild(0).gameObject
                : Instantiate(sourcePartAdapter.VisualRoot.gameObject, slot.modelAnchor);
            // Keep the source visual-root name so canonical renderer paths in
            // CharacterPartDatabase continue to resolve on the preview clone.
            slot.model.name = sourcePartAdapter.VisualRoot.name;
            if (!Application.isPlaying) slot.model.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            slot.model.transform.localPosition = Vector3.zero;
            slot.model.transform.localRotation = Quaternion.identity;
            slot.model.transform.localScale = Vector3.one;
            foreach (var collider in slot.model.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var animator in slot.model.GetComponentsInChildren<Animator>(true))
            {
                animator.applyRootMotion = false;
                animator.updateMode = AnimatorUpdateMode.UnscaledTime;
            }
            SetLayerRecursively(slot.model.transform, slot.previewLayer);
        }

        private void Frame(PreviewSlot slot)
        {
            var renderers = slot.model.GetComponentsInChildren<Renderer>(true);
            var found = false;
            var bounds = new Bounds(slot.model.transform.position, Vector3.one);
            foreach (var renderer in renderers)
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            if (!found) return;

            var camera = slot.previewCamera;
            var halfHeight = Mathf.Max(bounds.extents.y * Mathf.Max(framingExtentScale.y, 0.05f), 0.1f) * framingPadding;
            var halfWidth = Mathf.Max(bounds.extents.x * Mathf.Max(framingExtentScale.x, 0.05f), 0.1f) * framingPadding;
            var verticalDistance = halfHeight / Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            var horizontalFov = 2f * Mathf.Atan(Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * camera.aspect);
            var horizontalDistance = halfWidth / Mathf.Tan(horizontalFov * 0.5f);
            var distance = Mathf.Max(verticalDistance, horizontalDistance, 1f);
            var target = new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * framingFocusY, bounds.center.z);
            var direction = slot.model.transform.TransformDirection(viewDirection.normalized);
            camera.transform.position = target + direction * distance;
            camera.transform.LookAt(target, Vector3.up);
        }

        private void ResolveDependencies()
        {
            if (entryFlow == null)
                entryFlow = FindAnyObjectByType<OntologyWorldAuthorityAccountEntryFlow>(FindObjectsInactive.Include);
            if (panelGroup == null) panelGroup = GetComponent<CanvasGroup>();
            if (sourcePartAdapter == null) sourcePartAdapter = OntologyCharacterPartAdapter.FindAvailable();
            if (partDatabase == null && sourcePartAdapter != null) partDatabase = sourcePartAdapter.PartDatabase;
        }

        private void UpdateEntryFlowSubscription()
        {
            if (!isActiveAndEnabled || ReferenceEquals(subscribedEntryFlow, entryFlow)) return;
            if (subscribedEntryFlow != null) subscribedEntryFlow.StateChanged -= RefreshPreviews;
            subscribedEntryFlow = entryFlow;
            if (subscribedEntryFlow != null) subscribedEntryFlow.StateChanged += RefreshPreviews;
        }

        private void SetCamerasEnabled(bool enabled)
        {
            foreach (var slot in previewSlots)
                if (slot?.previewCamera != null) slot.previewCamera.enabled = enabled && slot.model != null;
        }

        private static string BuildFingerprint(OntologyAuthorityPlayerCharacter character) =>
            (character.characterId ?? string.Empty) + "|" + string.Join("|", character.equippedPartIds ?? Array.Empty<string>());

        private static void SetLayerRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (var index = 0; index < root.childCount; index++) SetLayerRecursively(root.GetChild(index), layer);
        }

        private void OnDestroy()
        {
            foreach (var slot in previewSlots)
            {
                if (slot == null) continue;
                if (slot.model != null) DestroyPreviewObject(slot.model);
                if (slot.texture == null) continue;
                slot.texture.Release();
                DestroyPreviewObject(slot.texture);
            }
        }

        private static void DestroyPreviewObject(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}
