using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Presentation-only camera for the hierarchy-authored character creator.
    /// It renders the same avatar that the part adapter updates; it owns no
    /// account data, ontology facts, or equipment rules.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyCharacterCreationPreviewPresenter : MonoBehaviour
    {
        [SerializeField] private OntologyCharacterPartAdapter partAdapter;
        [SerializeField] private CanvasGroup panelGroup;
        [SerializeField] private Camera previewCamera;
        [SerializeField] private RawImage previewImage;
        [SerializeField] private Vector3 viewDirection = new(0f, 0.08f, 1f);
        [SerializeField, Range(18f, 55f)] private float fieldOfView = 28f;
        [SerializeField, Range(3f, 7f)] private float framingDistance = 4.6f;
        [SerializeField, Range(0.4f, 1f)] private float characterScale = 0.7f;
        [SerializeField, Range(8, 31)] private int previewLayer = 31;

        private readonly Dictionary<GameObject, int> originalRendererLayers = new();
        private Transform previewCloneRoot;
        private Transform clonedSourceRoot;
        private Renderer[] sourceRenderers;
        private OntologyCharacterPartAdapter subscribedAdapter;
        private bool partAdapterSubscriptionActive;
        private int sourceVisualSignature;

        public Transform PreviewCloneRoot => previewCloneRoot;
        public OntologyCharacterPartAdapter PartAdapter => partAdapter;

        public void Configure(Camera camera, RawImage image)
        {
            previewCamera = camera;
            previewImage = image;
            Resolve();
            ApplyCameraSettings();
        }

        public Transform SynchronizeNow()
        {
            Resolve();
            var renderRoot = PreparePreviewRoot();
            SyncPreviewClone();
            return renderRoot;
        }

        private void Awake()
        {
            Resolve();
            ApplyCameraSettings();
        }

        private void OnEnable()
        {
            Resolve();
            UpdatePartAdapterSubscription();
        }

        private void LateUpdate()
        {
            Resolve();
            var visible = panelGroup != null && panelGroup.alpha > 0.01f;
            if (previewCamera != null) previewCamera.enabled = visible;
            if (!visible || previewCamera == null || partAdapter == null)
            {
                if (previewCloneRoot != null)
                    previewCloneRoot.gameObject.SetActive(false);
                RestoreRendererLayers();
                return;
            }

            var renderRoot = PreparePreviewRoot();
            if (renderRoot == null) return;
            var renderers = renderRoot.GetComponentsInChildren<Renderer>(true);
            if (renderRoot == partAdapter.VisualRoot)
                ApplyPreviewLayers(renderers);
            var foundBounds = false;
            var bounds = new Bounds(renderRoot.position, Vector3.one);
            foreach (var renderer in renderers)
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (!foundBounds)
                {
                    bounds = renderer.bounds;
                    foundBounds = true;
                }
                else bounds.Encapsulate(renderer.bounds);
            }

            if (!foundBounds) return;
            var direction = renderRoot.TransformDirection(viewDirection.normalized);
            var scale = Mathf.Max(0.1f, characterScale);
            var distance = Mathf.Max(bounds.extents.y * framingDistance / scale,
                bounds.extents.x * 3.2f / scale, 1.4f / scale);
            var target = bounds.center + Vector3.up * bounds.extents.y * 0.03f;
            previewCamera.transform.position = target + direction * distance;
            previewCamera.transform.LookAt(target, Vector3.up);
        }

        private Transform PreparePreviewRoot()
        {
            partAdapter?.EnsureAppearanceInitialized();
            var sourceRoot = partAdapter == null ? null : partAdapter.VisualRoot;
            if (sourceRoot == null) return null;
            var currentSignature = ComputeVisualSignature(sourceRoot);

            if (previewCloneRoot == null ||
                clonedSourceRoot != sourceRoot ||
                sourceVisualSignature != currentSignature)
            {
                DestroyPreviewClone();
                var cloneObject = Instantiate(sourceRoot.gameObject);
                cloneObject.name = sourceRoot.name + "_PreviewClone";
                cloneObject.hideFlags = HideFlags.HideAndDontSave;
                previewCloneRoot = cloneObject.transform;
                DisablePreviewBehaviours(previewCloneRoot);
                clonedSourceRoot = sourceRoot;
                sourceRenderers = sourceRoot.GetComponentsInChildren<Renderer>(true);
                SetLayerRecursively(previewCloneRoot, previewLayer);
                sourceVisualSignature = currentSignature;
            }

            previewCloneRoot.gameObject.SetActive(true);
            SyncPreviewClone();
            return previewCloneRoot;
        }

        private static void DisablePreviewBehaviours(Transform cloneRoot)
        {
            if (cloneRoot == null) return;
            foreach (var behaviour in cloneRoot.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour != null)
                    behaviour.enabled = false;
            }
        }

        private static int ComputeVisualSignature(Transform sourceRoot)
        {
            unchecked
            {
                var signature = 17;
                foreach (var renderer in sourceRoot.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == null) continue;
                    signature = signature * 31 + renderer.GetInstanceID();
                    signature = signature * 31 + (renderer.enabled ? 1 : 0);
                    signature = signature * 31 + (renderer.gameObject.activeSelf ? 1 : 0);
                    if (renderer is SkinnedMeshRenderer skinned)
                        signature = signature * 31 +
                            (skinned.sharedMesh == null ? 0 : skinned.sharedMesh.GetInstanceID());
                    foreach (var material in renderer.sharedMaterials)
                        signature = signature * 31 +
                            (material == null ? 0 : material.GetInstanceID());
                }

                return signature;
            }
        }

        private void SyncPreviewClone()
        {
            if (previewCloneRoot == null || clonedSourceRoot == null ||
                sourceRenderers == null)
            {
                return;
            }

            SyncRendererHierarchy(clonedSourceRoot, previewCloneRoot);
        }

        private static void SyncRendererHierarchy(Transform sourceRoot, Transform cloneRoot)
        {
            if (sourceRoot == null || cloneRoot == null) return;
            var sourceComponents = sourceRoot.GetComponents<Renderer>();
            var cloneComponents = cloneRoot.GetComponents<Renderer>();
            var rendererCount = Mathf.Min(sourceComponents.Length, cloneComponents.Length);
            for (var index = 0; index < rendererCount; index++)
            {
                var source = sourceComponents[index];
                var clone = cloneComponents[index];
                if (source == null || clone == null) continue;

                clone.gameObject.SetActive(source.gameObject.activeSelf);
                clone.enabled = source.enabled;
                clone.sharedMaterials = source.sharedMaterials;
                if (source is SkinnedMeshRenderer sourceSkinned
                    && clone is SkinnedMeshRenderer cloneSkinned)
                {
                    cloneSkinned.sharedMesh = sourceSkinned.sharedMesh;
                    cloneSkinned.localBounds = sourceSkinned.localBounds;
                    cloneSkinned.updateWhenOffscreen = sourceSkinned.updateWhenOffscreen;
                }
            }

            var childCount = Mathf.Min(sourceRoot.childCount, cloneRoot.childCount);
            for (var childIndex = 0; childIndex < childCount; childIndex++)
            {
                SyncRendererHierarchy(
                    sourceRoot.GetChild(childIndex),
                    cloneRoot.GetChild(childIndex));
            }
        }

        private static void SetLayerRecursively(Transform root, int layer)
        {
            if (root == null) return;
            root.gameObject.layer = layer;
            for (var i = 0; i < root.childCount; i++)
                SetLayerRecursively(root.GetChild(i), layer);
        }

        private void DestroyPreviewClone()
        {
            if (previewCloneRoot != null)
                Destroy(previewCloneRoot.gameObject);
            previewCloneRoot = null;
            clonedSourceRoot = null;
            sourceRenderers = null;
            sourceVisualSignature = 0;
        }

        private void Resolve()
        {
            if (partAdapter == null)
            {
                partAdapter = GetComponent<OntologyCharacterCustomizationPanel>()?.PartAdapter;
                if (partAdapter == null)
                    partAdapter = OntologyCharacterPartAdapter.FindAvailable();
            }
            UpdatePartAdapterSubscription();
            if (panelGroup == null) panelGroup = GetComponent<CanvasGroup>();
            if (previewImage == null)
                previewImage = transform.Find("CharacterPreviewFrame/LiveCharacterPreview")
                    ?.GetComponent<RawImage>();
            if (previewCamera == null)
                previewCamera = transform.Find("CharacterPreviewCamera")?.GetComponent<Camera>();
        }

        private void UpdatePartAdapterSubscription()
        {
            if (subscribedAdapter != partAdapter)
            {
                RemovePartAdapterSubscription();
                subscribedAdapter = partAdapter;
            }

            if (!partAdapterSubscriptionActive &&
                subscribedAdapter != null &&
                isActiveAndEnabled)
            {
                subscribedAdapter.EquippedPartsChanged += HandleEquippedPartsChanged;
                partAdapterSubscriptionActive = true;
            }
        }

        private void HandleEquippedPartsChanged()
        {
            if (panelGroup == null || panelGroup.alpha <= 0.01f)
            {
                return;
            }

            DestroyPreviewClone();
            SynchronizeNow();
        }

        private void RemovePartAdapterSubscription()
        {
            if (partAdapterSubscriptionActive && subscribedAdapter != null)
            {
                subscribedAdapter.EquippedPartsChanged -= HandleEquippedPartsChanged;
            }

            partAdapterSubscriptionActive = false;
            subscribedAdapter = null;
        }

        private void ApplyCameraSettings()
        {
            if (previewCamera == null) return;
            previewCamera.fieldOfView = fieldOfView;
            previewCamera.nearClipPlane = 0.03f;
            previewCamera.farClipPlane = 80f;
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = new Color(0.94f, 0.86f, 0.72f, 0f);
            previewCamera.allowHDR = false;
            previewCamera.allowMSAA = true;
            previewCamera.cullingMask = 1 << previewLayer;
            if (previewImage != null) previewImage.texture = previewCamera.targetTexture;
        }

        private void ApplyPreviewLayers(Renderer[] renderers)
        {
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                var rendererObject = renderer.gameObject;
                if (!originalRendererLayers.ContainsKey(rendererObject))
                    originalRendererLayers.Add(rendererObject, rendererObject.layer);
                rendererObject.layer = previewLayer;
            }
        }

        private void RestoreRendererLayers()
        {
            foreach (var pair in originalRendererLayers)
                if (pair.Key != null) pair.Key.layer = pair.Value;
            originalRendererLayers.Clear();
        }

        private void OnDisable()
        {
            RemovePartAdapterSubscription();
            RestoreRendererLayers();
            DestroyPreviewClone();
        }

        private void OnDestroy()
        {
            RemovePartAdapterSubscription();
            RestoreRendererLayers();
            DestroyPreviewClone();
        }
    }
}
