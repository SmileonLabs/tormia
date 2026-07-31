using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Marks an actor-owned presentation socket. The socket is authored on the
    /// avatar rig and identified by a stable presentation id rather than a
    /// prefab, mesh, or hierarchy-name gameplay exception.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyAttachmentSocket : MonoBehaviour
    {
        [SerializeField] private string socketId = "RightHand";
        [SerializeField] private Transform actorRoot;
        [SerializeField] private HumanBodyBones sourceBone =
            HumanBodyBones.RightHand;
        [SerializeField] private string sourceChildPath = "RightHandProp";
        [SerializeField] private Vector3 localPosition;
        [SerializeField] private Vector3 localEulerAngles;
        private bool ambiguousRigLogged;

        public string SocketId => socketId;
        public Transform ActorRoot => actorRoot;
        public HumanBodyBones SourceBone => sourceBone;
        public string SourceChildPath => sourceChildPath;
        public Vector3 LocalPosition => localPosition;
        public Vector3 LocalEulerAngles => localEulerAngles;

        public void Configure(
            string id,
            Transform targetActorRoot,
            HumanBodyBones bone,
            string childPath,
            Vector3 position,
            Vector3 eulerAngles)
        {
            socketId = id;
            actorRoot = targetActorRoot;
            sourceBone = bone;
            sourceChildPath = childPath;
            localPosition = position;
            localEulerAngles = eulerAngles;
        }

        private void LateUpdate()
        {
            if (Application.isPlaying)
            {
                ApplyAuthoredPose();
            }
        }

        public bool ApplyAuthoredPose()
        {
            var source = ResolveSourceAnchor();
            if (source == null)
            {
                return false;
            }

            transform.SetPositionAndRotation(
                source.TransformPoint(localPosition),
                source.rotation * Quaternion.Euler(localEulerAngles));
            return true;
        }

        public bool CaptureCurrentPose()
        {
            var source = ResolveSourceAnchor();
            if (source == null)
            {
                return false;
            }

            localPosition = source.InverseTransformPoint(transform.position);
            localEulerAngles =
                (Quaternion.Inverse(source.rotation) * transform.rotation)
                .eulerAngles;
            return true;
        }

        private Transform ResolveSourceAnchor()
        {
            if (actorRoot == null)
            {
                return null;
            }

            Transform resolved = null;
            foreach (var animator in
                     actorRoot.GetComponentsInChildren<Animator>(true))
            {
                if (animator == null ||
                    !animator.isHuman ||
                    !animator.isActiveAndEnabled ||
                    !animator.gameObject.activeInHierarchy)
                {
                    continue;
                }

                var bone = animator.GetBoneTransform(sourceBone);
                if (bone == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(sourceChildPath))
                {
                    if (resolved != null && resolved != bone)
                    {
                        LogAmbiguousRig();
                        return null;
                    }
                    resolved = bone;
                    continue;
                }

                var child = bone.Find(sourceChildPath);
                if (child != null)
                {
                    if (resolved != null && resolved != child)
                    {
                        LogAmbiguousRig();
                        return null;
                    }
                    resolved = child;
                }
            }

            if (resolved != null)
            {
                ambiguousRigLogged = false;
            }
            return resolved;
        }

        private void LogAmbiguousRig()
        {
            if (ambiguousRigLogged)
            {
                return;
            }

            Debug.LogError(
                "[OntologyAttachment] Multiple active humanoid rigs resolve " +
                "socket '" + socketId + "'. Attachment presentation is " +
                "disabled until the visual rig binding is unambiguous.",
                this);
            ambiguousRigLogged = true;
        }
    }

    /// <summary>
    /// Marks the exact point and orientation on an item that must meet the
    /// actor attachment anchor. The prefab owns this presentation metadata,
    /// so differently shaped items do not require item-name exceptions.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyAttachmentGripPoint : MonoBehaviour
    {
        [SerializeField, Min(0)] private int calibrationVersion;

        public int CalibrationVersion => calibrationVersion;
        public bool IsCalibrated => calibrationVersion > 0;

        public void MarkCalibrated(int version = 1)
        {
            calibrationVersion = Mathf.Max(1, version);
        }
    }

    public static class OntologyAttachmentPoseUtility
    {
        public static Transform ResolveActorSocket(
            Transform actorRoot,
            string socketId)
        {
            if (actorRoot == null || string.IsNullOrWhiteSpace(socketId))
            {
                return null;
            }

            foreach (var socket in
                     actorRoot.GetComponentsInChildren<
                         OntologyAttachmentSocket>(true))
            {
                if (socket != null &&
                    string.Equals(
                        socket.SocketId,
                        socketId,
                        System.StringComparison.Ordinal))
                {
                    return socket.transform;
                }
            }

            return null;
        }

        public static bool Apply(
            Transform itemRoot,
            Transform actorAnchor,
            OntologyAttachmentProfile profile)
        {
            if (itemRoot == null || actorAnchor == null || profile == null)
            {
                return false;
            }

            var gripPoint =
                itemRoot.GetComponentInChildren<OntologyAttachmentGripPoint>(
                    true);
            if (profile.requireItemGripPoint &&
                (gripPoint == null || !gripPoint.IsCalibrated))
            {
                return false;
            }

            itemRoot.SetParent(actorAnchor, true);
            itemRoot.localScale = profile.localScale;

            var targetPosition =
                actorAnchor.TransformPoint(profile.localPosition);
            var targetRotation =
                actorAnchor.rotation *
                Quaternion.Euler(profile.localEulerAngles);
            if (gripPoint == null)
            {
                itemRoot.SetPositionAndRotation(
                    targetPosition,
                    targetRotation);
                return true;
            }

            var gripTransform = gripPoint.transform;
            var gripRotationRelativeToRoot =
                Quaternion.Inverse(itemRoot.rotation) *
                gripTransform.rotation;
            itemRoot.rotation =
                targetRotation *
                Quaternion.Inverse(gripRotationRelativeToRoot);
            itemRoot.position += targetPosition - gripTransform.position;
            return true;
        }

        public static bool CaptureCurrentPose(
            Transform itemRoot,
            Transform actorAnchor,
            OntologyAttachmentProfile profile,
            out OntologyAttachmentGripPoint gripPoint)
        {
            gripPoint = null;
            if (itemRoot == null || actorAnchor == null || profile == null)
            {
                return false;
            }

            gripPoint =
                itemRoot.GetComponentInChildren<OntologyAttachmentGripPoint>(
                    true);
            if (gripPoint == null)
            {
                return false;
            }

            gripPoint.transform.SetPositionAndRotation(
                actorAnchor.TransformPoint(profile.localPosition),
                actorAnchor.rotation *
                Quaternion.Euler(profile.localEulerAngles));
            return true;
        }
    }
}
