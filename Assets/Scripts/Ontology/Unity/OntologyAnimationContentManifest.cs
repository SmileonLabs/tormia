using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    [CreateAssetMenu(menuName = "Tormia/Ontology/Animation Content Manifest")]
    public sealed class OntologyAnimationContentManifest : ScriptableObject
    {
        [SerializeField] private string contentVersion = "1.0.0";
        [SerializeField] private List<OntologyAnimationContentEntry> entries = new();

        public string ContentVersion => contentVersion;
        public IReadOnlyList<OntologyAnimationContentEntry> Entries => entries;

        public void SetContentVersion(string value)
        {
            contentVersion = string.IsNullOrWhiteSpace(value)
                ? "1.0.0"
                : value.Trim();
        }

        public void ReplaceEntries(IEnumerable<OntologyAnimationContentEntry> values)
        {
            entries.Clear();
            if (values == null) return;
            foreach (var value in values)
            {
                if (value != null) entries.Add(value);
            }
        }

        public IReadOnlyList<OntologyAnimationContentIssue> ValidateEntries()
        {
            return OntologyAnimationContentValidator.Validate(this);
        }
    }

    [Serializable]
    public sealed class OntologyAnimationContentEntry
    {
        [Tooltip("Canonical stable identifier. Never derive this from a file name at runtime.")]
        public string animationId;
        [Tooltip(
            "Previous canonical ids accepted while durable data migrates to " +
            "animationId. Aliases are compatibility only and are never newly authored.")]
        public string[] legacyAnimationIds = Array.Empty<string>();
        public AnimationClip clip;
        [Tooltip("Editor source path is provenance only and is never authored as a world Fact.")]
        public string sourceAssetPath;
        [Tooltip("Future Addressables or bundle key. The world stores only animationId.")]
        public string deliveryKey;
        public string[] intents = Array.Empty<string>();
        public string[] actorTypes = Array.Empty<string>();
        public string[] rigTypes = Array.Empty<string>();
        public OntologyActorProfile[] profiles = Array.Empty<OntologyActorProfile>();
        public OntologyAnimationLayer layer = OntologyAnimationLayer.FullBody;
        public AvatarMask avatarMask;
        public bool loop;
        public bool loopPose;
        public OntologyAnimationRootMotionMode rootMotionMode =
            OntologyAnimationRootMotionMode.Inherit;
        [Tooltip(
            "Selects the presentation lifecycle owner. Gameplay permission " +
            "still belongs to Rule Blocks and Authority.")]
        public OntologyAnimationPresentationOwner presentationOwner =
            OntologyAnimationPresentationOwner.AuthorityIntent;
        public bool interruptible = true;
        public int priority;
        public bool canBlend = true;
        [Min(0f)] public float transitionDuration = 0.25f;
        [Tooltip(
            "Enables a data-authored physical contact window for this clip. " +
            "The window is presentation metadata; Authority still owns damage.")]
        public bool hasContactWindow;
        [Range(0f, 1f)] public float contactWindowStartNormalized;
        [Range(0f, 1f)] public float contactWindowEndNormalized = 1f;
        [Tooltip(
            "Data-authored start of the usable clip segment. This allows a " +
            "project-owned in-place presentation segment without changing the source asset.")]
        [Range(0f, 1f)] public float playbackStartNormalized;
        [Tooltip(
            "Data-authored exclusive end of the usable clip segment.")]
        [Range(0f, 1f)] public float playbackEndNormalized = 1f;
        [Tooltip("Data-authored playback rate. Existing content with an unset value resolves to 1x.")]
        [Min(0.01f)] public float playbackSpeed = 1f;
        public string[] properties = Array.Empty<string>();
        public string contentVersion = "1.0.0";
        public string checksum;

        public OntologyAnimationDefinition ToRuntimeDefinition()
        {
            return new OntologyAnimationDefinition
            {
                animationId = animationId == null ? string.Empty : animationId.Trim(),
                legacyAnimationIds = Copy(legacyAnimationIds),
                clip = clip,
                actorTypes = Copy(actorTypes),
                rigTypes = Copy(rigTypes),
                layer = layer,
                avatarMask = avatarMask,
                interruptible = interruptible,
                loop = loop,
                transitionDuration = Mathf.Max(0f, transitionDuration),
                hasContactWindow = hasContactWindow,
                contactWindowStartNormalized =
                    Mathf.Clamp01(contactWindowStartNormalized),
                contactWindowEndNormalized =
                    Mathf.Clamp01(contactWindowEndNormalized),
                playbackStartNormalized =
                    Mathf.Clamp01(playbackStartNormalized),
                playbackEndNormalized =
                    ResolvePlaybackEndNormalized(
                        playbackStartNormalized,
                        playbackEndNormalized),
                playbackSpeed = playbackSpeed > 0f ? playbackSpeed : 1f,
                rootMotionMode = rootMotionMode,
                presentationOwner = presentationOwner,
                properties = Copy(properties),
                intents = Copy(intents),
                priority = priority,
                canBlend = canBlend
            };
        }

        private static string[] Copy(string[] source)
        {
            if (source == null || source.Length == 0) return Array.Empty<string>();
            var result = new string[source.Length];
            Array.Copy(source, result, source.Length);
            return result;
        }

        private static float ResolvePlaybackEndNormalized(
            float start,
            float end)
        {
            // Assets authored before segmented playback have no serialized end
            // field. Preserve their full-clip behavior during migration.
            return end <= 0f && start <= 0f
                ? 1f
                : Mathf.Clamp01(end);
        }
    }

    public enum OntologyAnimationContentIssueSeverity
    {
        Info = 0,
        Warning = 1,
        Error = 2
    }

    public readonly struct OntologyAnimationContentIssue
    {
        public OntologyAnimationContentIssue(
            OntologyAnimationContentIssueSeverity severity,
            string animationId,
            string code,
            string message)
        {
            Severity = severity;
            AnimationId = animationId ?? string.Empty;
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public OntologyAnimationContentIssueSeverity Severity { get; }
        public string AnimationId { get; }
        public string Code { get; }
        public string Message { get; }
    }

    public static class OntologyAnimationContentValidator
    {
        private static readonly Regex CanonicalIdPattern = new(
            "^[A-Za-z][A-Za-z0-9_]*$",
            RegexOptions.CultureInvariant);

        public static IReadOnlyList<OntologyAnimationContentIssue> Validate(
            OntologyAnimationContentManifest manifest)
        {
            var issues = new List<OntologyAnimationContentIssue>();
            if (manifest == null)
            {
                issues.Add(new OntologyAnimationContentIssue(
                    OntologyAnimationContentIssueSeverity.Error,
                    string.Empty,
                    "manifest_missing",
                    "Animation content manifest is missing."));
                return issues;
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var intentOwners =
                new Dictionary<
                    string,
                    OntologyAnimationPresentationOwner>(
                    StringComparer.Ordinal);
            foreach (var entry in manifest.Entries)
            {
                if (entry == null)
                {
                    issues.Add(new OntologyAnimationContentIssue(
                        OntologyAnimationContentIssueSeverity.Error,
                        string.Empty,
                        "entry_missing",
                        "Manifest contains an empty entry."));
                    continue;
                }

                var id = entry.animationId == null
                    ? string.Empty
                    : entry.animationId.Trim();
                if (id.Length == 0)
                {
                    AddError(issues, id, "animation_id_missing",
                        "A canonical animationId is required.");
                }
                else if (!ids.Add(id))
                {
                    AddError(issues, id, "animation_id_duplicate",
                        "Duplicate canonical animationId.");
                }
                else if (!CanonicalIdPattern.IsMatch(id))
                {
                    AddError(issues, id, "animation_id_invalid",
                        "animationId must use canonical ASCII letters, digits, and underscores.");
                }
                ValidateLegacyIds(issues, ids, id, entry.legacyAnimationIds);

                if (entry.clip == null)
                    AddError(issues, id, "clip_missing", "AnimationClip is required.");
                if (!HasCanonicalValues(entry.intents))
                    AddError(issues, id, "intent_missing",
                        "At least one canonical presentation intent is required.");
                else
                {
                    foreach (var intent in entry.intents)
                    {
                        if (string.IsNullOrWhiteSpace(intent) ||
                            CanonicalIdPattern.IsMatch(intent.Trim()))
                        {
                            ValidateIntentOwner(
                                issues,
                                intentOwners,
                                id,
                                intent,
                                entry.presentationOwner);
                            continue;
                        }
                        AddError(issues, id, "intent_invalid",
                            "Intent '" + intent +
                            "' is not a canonical ASCII identifier.");
                    }
                }
                if (entry.layer != OntologyAnimationLayer.FullBody &&
                    entry.avatarMask == null)
                {
                    AddError(issues, id, "avatar_mask_missing",
                        "UpperBody and Additive entries require an AvatarMask.");
                }
                if (entry.transitionDuration < 0f)
                    AddError(issues, id, "transition_negative",
                        "Transition duration cannot be negative.");
                if (entry.playbackSpeed < 0f)
                    AddError(issues, id, "playback_speed_invalid",
                        "Playback speed cannot be negative.");
                if (entry.hasContactWindow &&
                    (entry.contactWindowStartNormalized < 0f ||
                     entry.contactWindowStartNormalized > 1f ||
                     entry.contactWindowEndNormalized < 0f ||
                     entry.contactWindowEndNormalized > 1f ||
                     entry.contactWindowEndNormalized <=
                     entry.contactWindowStartNormalized))
                {
                    AddError(
                        issues,
                        id,
                        "contact_window_invalid",
                        "Contact window must satisfy 0 <= start < end <= 1.");
                }
                var playbackEnd =
                    entry.playbackEndNormalized <= 0f &&
                    entry.playbackStartNormalized <= 0f
                        ? 1f
                        : entry.playbackEndNormalized;
                if (entry.playbackStartNormalized < 0f ||
                    entry.playbackStartNormalized >= 1f ||
                    playbackEnd <= 0f ||
                    playbackEnd > 1f ||
                    playbackEnd <=
                    entry.playbackStartNormalized)
                {
                    AddError(
                        issues,
                        id,
                        "playback_segment_invalid",
                        "Playback segment must satisfy 0 <= start < end <= 1.");
                }
                if (entry.presentationOwner ==
                    OntologyAnimationPresentationOwner.MotionStateResolver &&
                    entry.rootMotionMode !=
                    OntologyAnimationRootMotionMode.Disabled)
                {
                    AddError(
                        issues,
                        id,
                        "direct_character_root_motion_not_disabled",
                        "Motion-state-resolver presentation must explicitly " +
                        "disable root motion because the configured motion " +
                        "driver owns world movement.");
                }
                if (entry.profiles == null || entry.profiles.Length == 0)
                {
                    issues.Add(new OntologyAnimationContentIssue(
                        OntologyAnimationContentIssueSeverity.Warning,
                        id,
                        "profile_missing",
                        "No ActorProfile receives this animation."));
                }
            }
            return issues;
        }

        private static bool HasCanonicalValues(string[] values)
        {
            if (values == null || values.Length == 0) return false;
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value)) return true;
            }
            return false;
        }

        private static void ValidateLegacyIds(
            ICollection<OntologyAnimationContentIssue> issues,
            ISet<string> ids,
            string canonicalId,
            string[] legacyIds)
        {
            if (legacyIds == null) return;
            foreach (var value in legacyIds)
            {
                var legacyId = value == null
                    ? string.Empty
                    : value.Trim();
                if (legacyId.Length == 0 ||
                    !CanonicalIdPattern.IsMatch(legacyId))
                {
                    AddError(
                        issues,
                        canonicalId,
                        "legacy_animation_id_invalid",
                        "Legacy animation ids must use canonical ASCII " +
                        "letters, digits, and underscores.");
                }
                else if (!ids.Add(legacyId))
                {
                    AddError(
                        issues,
                        canonicalId,
                        "animation_id_alias_conflict",
                        "Animation id or compatibility alias is already claimed: " +
                        legacyId);
                }
            }
        }

        private static void ValidateIntentOwner(
            ICollection<OntologyAnimationContentIssue> issues,
            IDictionary<
                string,
                OntologyAnimationPresentationOwner> owners,
            string animationId,
            string intent,
            OntologyAnimationPresentationOwner owner)
        {
            if (string.IsNullOrWhiteSpace(intent)) return;
            var canonical = intent.Trim();
            if (!owners.TryGetValue(canonical, out var existing))
            {
                owners.Add(canonical, owner);
                return;
            }

            if (existing != owner)
            {
                AddError(
                    issues,
                    animationId,
                    "intent_presentation_owner_conflict",
                    "Intent '" + canonical +
                    "' is assigned to multiple presentation lifecycle owners.");
            }
        }

        private static void AddError(
            ICollection<OntologyAnimationContentIssue> issues,
            string id,
            string code,
            string message)
        {
            issues.Add(new OntologyAnimationContentIssue(
                OntologyAnimationContentIssueSeverity.Error,
                id,
                code,
                message));
        }
    }
}
