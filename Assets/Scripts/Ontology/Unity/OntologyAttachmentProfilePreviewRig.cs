using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Editor-authoring bridge for adjusting a prefab-owned grip point while
    /// the item is visibly attached to an actor. Preview scenes using this
    /// component are deliberately excluded from player build settings.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    public sealed class OntologyAttachmentProfilePreviewRig : MonoBehaviour
    {
        public const string PreviewScenePath =
            "Assets/Scenes/Editor/RightHandAttachmentPreview.unity";

        [SerializeField] private OntologyAttachmentProfile profile;
        [SerializeField] private Animator actorAnimator;
        [SerializeField] private Transform attachmentPreview;
        [SerializeField] private GameObject sourcePrefab;

        public OntologyAttachmentProfile Profile => profile;
        public Animator ActorAnimator => actorAnimator;
        public Transform AttachmentPreview => attachmentPreview;
        public GameObject SourcePrefab => sourcePrefab;

        private void Awake()
        {
            // The preview may be left loaded additively while the developer
            // presses Play from Bootstrap. It must never join the runtime
            // ontology scan or render beside the real player.
            if (Application.isPlaying)
                gameObject.SetActive(false);
        }

        public void Configure(
            OntologyAttachmentProfile targetProfile,
            Animator targetAnimator,
            Transform targetAttachment,
            GameObject targetSourcePrefab)
        {
            profile = targetProfile;
            actorAnimator = targetAnimator;
            attachmentPreview = targetAttachment;
            sourcePrefab = targetSourcePrefab;
        }

        public bool ApplyProfileToPreview()
        {
            if (!TryResolveAnchor(out var anchor)) return false;

            OntologyAttachmentPoseUtility.Apply(
                attachmentPreview,
                anchor,
                profile);
            return attachmentPreview.GetComponentInChildren<
                OntologyAttachmentGripPoint>(true) != null;
        }

        public bool CapturePreviewToSourceGripPoint()
        {
            if (!TryResolveAnchor(out var anchor) ||
                sourcePrefab == null ||
                !OntologyAttachmentPoseUtility.CaptureCurrentPose(
                    attachmentPreview,
                    anchor,
                    profile,
                    out var previewGripPoint))
            {
                return false;
            }

#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(
                previewGripPoint.transform,
                "Capture attachment grip point");
            var prefabPath =
                UnityEditor.AssetDatabase.GetAssetPath(sourcePrefab);
            var prefabRoot =
                UnityEditor.PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var sourceGripPoint = prefabRoot.GetComponentInChildren<
                    OntologyAttachmentGripPoint>(true);
                if (sourceGripPoint == null)
                {
                    return false;
                }

                sourceGripPoint.transform.localPosition =
                    previewGripPoint.transform.localPosition;
                sourceGripPoint.transform.localRotation =
                    previewGripPoint.transform.localRotation;
                sourceGripPoint.transform.localScale =
                    previewGripPoint.transform.localScale;
                sourceGripPoint.MarkCalibrated();
                UnityEditor.EditorUtility.SetDirty(
                    sourceGripPoint.transform);
                UnityEditor.EditorUtility.SetDirty(sourceGripPoint);
                UnityEditor.PrefabUtility.SaveAsPrefabAsset(
                    prefabRoot,
                    prefabPath);
            }
            finally
            {
                UnityEditor.PrefabUtility.UnloadPrefabContents(prefabRoot);
            }

            UnityEditor.EditorUtility.SetDirty(previewGripPoint.transform);
            UnityEditor.AssetDatabase.SaveAssets();
#endif
            return true;
        }

        private bool TryResolveAnchor(out Transform anchor)
        {
            anchor = null;
            if (profile == null ||
                actorAnimator == null ||
                attachmentPreview == null)
            {
                return false;
            }

            anchor = OntologyAttachmentPoseUtility.ResolveActorSocket(
                transform,
                profile.actorSocketId);
            if (anchor != null)
            {
                return true;
            }

            anchor = actorAnimator.GetBoneTransform(profile.actorAnchorBone);
            if (anchor == null &&
                !string.IsNullOrWhiteSpace(profile.actorAnchorPath))
            {
                anchor = actorAnimator.transform.Find(profile.actorAnchorPath);
            }

            return anchor != null;
        }
    }
}
