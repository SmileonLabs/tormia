using UnityEngine;
using UnityEngine.UI;

namespace Tormia.Ontology.Core
{
    /// <summary>Renders a catalog prefab in an isolated, rotating 3D preview stage.</summary>
    public sealed class OntologyPlaceablePreviewRenderer : MonoBehaviour
    {
        [SerializeField] private RawImage target;
        [SerializeField] private int textureSize = 512;
        [SerializeField] private float rotationDegreesPerSecond = 20f;
        [SerializeField] private float previewSize = 2.4f;

        private Camera previewCamera;
        private Transform previewRoot;
        private GameObject previewInstance;
        private RenderTexture texture;

        public void Configure(RawImage image) => target = image;

        private void Awake() => EnsureStage();

        private void Update()
        {
            if (previewRoot != null) previewRoot.Rotate(Vector3.up, rotationDegreesPerSecond * Time.unscaledDeltaTime, Space.World);
        }

        private void OnDestroy()
        {
            if (texture != null) texture.Release();
            if (previewCamera != null) Destroy(previewCamera.gameObject);
            if (previewRoot != null) Destroy(previewRoot.gameObject);
        }

        public void Show(OntologyPlaceableDefinition definition)
        {
            EnsureStage();
            if (previewInstance != null) Destroy(previewInstance);
            if (definition == null || (definition.prefab == null && definition.previewPrefab == null)) return;

            previewInstance = Instantiate(definition.previewPrefab != null ? definition.previewPrefab : definition.prefab, previewRoot);
            previewInstance.name = "PreviewModel_" + definition.definitionId;
            foreach (var collider in previewInstance.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var behaviour in previewInstance.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
            CenterAndFrame(previewRoot, previewInstance, previewSize);
            if (definition.useAuthoredPreviewTransform)
                ApplyAuthoredAdjustment(previewRoot, previewInstance, definition.previewLocalPosition, definition.previewLocalEulerAngles, definition.previewLocalScale);
        }

        private void EnsureStage()
        {
            if (previewRoot == null)
            {
                var root = new GameObject("ObjectCatalogPreviewStage");
                root.transform.position = new Vector3(0f, -10000f, 0f);
                previewRoot = root.transform;
            }
            if (previewCamera == null)
            {
                var cameraObject = new GameObject("ObjectCatalogPreviewCamera", typeof(Camera));
                // A three-quarter perspective exposes depth; a front orthographic view makes flat props look crushed.
                cameraObject.transform.position = previewRoot.position + new Vector3(3.5f, 1.8f, -5.5f);
                cameraObject.transform.LookAt(previewRoot);
                previewCamera = cameraObject.GetComponent<Camera>();
                previewCamera.clearFlags = CameraClearFlags.SolidColor;
                // The preview is an overlay: only the model should appear over the live game view.
                previewCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                previewCamera.orthographic = false;
                previewCamera.fieldOfView = 30f;
                previewCamera.nearClipPlane = 0.01f;
                previewCamera.farClipPlane = 100f;
            }
            if (texture == null)
            {
                texture = new RenderTexture(textureSize, textureSize, 16, RenderTextureFormat.ARGB32);
                texture.Create(); previewCamera.targetTexture = texture;
            }
            if (target != null) target.texture = texture;
        }

        /// <summary>Fits the visible model, irrespective of its prefab pivot, into a centered preview stage.</summary>
        public static void CenterAndFrame(Transform root, GameObject value, float size)
        {
            value.transform.localPosition = Vector3.zero;
            value.transform.localRotation = Quaternion.identity;
            if (!TryGetBounds(value, out var bounds)) return;
            var largest = Mathf.Max(0.01f, bounds.size.x, bounds.size.y, bounds.size.z);
            var scale = size / largest;
            value.transform.localScale *= scale;
            CenterVisibleBounds(root, value);
        }

        /// <summary>Applies per-catalog fine tuning without losing automatic preview centering.</summary>
        public static void ApplyAuthoredAdjustment(Transform root, GameObject value, Vector3 positionOffset, Vector3 eulerOffset, Vector3 scaleMultiplier)
        {
            value.transform.localScale = Vector3.Scale(value.transform.localScale, scaleMultiplier);
            value.transform.localRotation = Quaternion.Euler(eulerOffset) * value.transform.localRotation;
            // Scale and rotation can move a visible mesh when its prefab pivot is off-center.
            // Recenter first, then apply the author's intentional positional offset.
            CenterVisibleBounds(root, value);
            value.transform.localPosition += positionOffset;
        }

        private static void CenterVisibleBounds(Transform root, GameObject value)
        {
            if (TryGetBounds(value, out var bounds)) value.transform.position += root.position - bounds.center;
        }

        public static bool TryGetBounds(GameObject value, out Bounds bounds)
        {
            var renderers = value.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                bounds = default;
                return false;
            }
            bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return true;
        }
    }
}
