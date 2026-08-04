using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    [CreateAssetMenu(menuName = "Tormia/Ontology/Animation Database")]
    public sealed class OntologyAnimationDatabase : ScriptableObject
    {
        [SerializeField] private List<OntologyAnimationDefinition> definitions = new();

        public IReadOnlyList<OntologyAnimationDefinition> Definitions => definitions;

        public OntologyAnimationDefinition FindById(string animationId)
        {
            if (string.IsNullOrWhiteSpace(animationId)) return null;
            var canonical = animationId.Trim();
            foreach (var definition in definitions)
            {
                if (definition != null &&
                    definition.MatchesAnimationId(canonical))
                {
                    return definition;
                }
            }

            return null;
        }

        public bool IsIntentOwnedBy(
            string intent,
            OntologyAnimationPresentationOwner owner)
        {
            if (string.IsNullOrWhiteSpace(intent)) return false;
            var canonical = intent.Trim();
            var found = false;
            foreach (var definition in definitions)
            {
                if (definition == null ||
                    !definition.HasIntent(canonical))
                {
                    continue;
                }

                if (definition.presentationOwner != owner)
                {
                    return false;
                }

                found = true;
            }

            return found;
        }

        public void Upsert(OntologyAnimationDefinition value)
        {
            if (value == null || string.IsNullOrWhiteSpace(value.animationId)) return;
            var index = definitions.FindIndex(candidate =>
                candidate != null &&
                string.Equals(
                    candidate.animationId,
                    value.animationId,
                    StringComparison.Ordinal));
            if (index >= 0) definitions[index] = value;
            else definitions.Add(value);
        }

        public void ReplaceDefinitions(IEnumerable<OntologyAnimationDefinition> values)
        {
            definitions.Clear();
            if (values == null) return;
            foreach (var value in values)
            {
                Upsert(value);
            }
        }
    }

    [Serializable]
    public sealed class OntologyAnimationDefinition
    {
        public string animationId;
        [Tooltip(
            "Previous canonical ids accepted while durable profile/world data " +
            "migrates to animationId.")]
        public string[] legacyAnimationIds = Array.Empty<string>();
        public AnimationClip clip;
        public string[] actorTypes = Array.Empty<string>();
        public string[] rigTypes = Array.Empty<string>();
        public OntologyAnimationLayer layer = OntologyAnimationLayer.FullBody;
        public AvatarMask avatarMask;
        public bool interruptible = true;
        public bool loop;
        [Min(0f)] public float transitionDuration = 0.25f;
        [Tooltip(
            "When enabled, this animation exposes an authored normalized-time " +
            "window in which a physical contact observation may request impact.")]
        public bool hasContactWindow;
        [Range(0f, 1f)] public float contactWindowStartNormalized;
        [Range(0f, 1f)] public float contactWindowEndNormalized = 1f;
        [Range(0f, 1f)] public float playbackStartNormalized;
        [Range(0f, 1f)] public float playbackEndNormalized = 1f;
        [Min(0.01f)] public float playbackSpeed = 1f;
        public OntologyAnimationRootMotionMode rootMotionMode =
            OntologyAnimationRootMotionMode.Inherit;
        public OntologyAnimationPresentationOwner presentationOwner =
            OntologyAnimationPresentationOwner.AuthorityIntent;
        public string[] properties = Array.Empty<string>();
        public string[] intents = Array.Empty<string>();
        public int priority;
        public bool canBlend;

        public bool MatchesAnimationId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            var canonical = value.Trim();
            if (string.Equals(
                    animationId,
                    canonical,
                    StringComparison.Ordinal))
            {
                return true;
            }

            if (legacyAnimationIds == null) return false;
            foreach (var legacyId in legacyAnimationIds)
            {
                if (string.Equals(
                        legacyId?.Trim(),
                        canonical,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public bool HasIntent(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || intents == null)
            {
                return false;
            }

            var canonical = value.Trim();
            foreach (var intent in intents)
            {
                if (string.Equals(
                        intent,
                        canonical,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public enum OntologyAnimationLayer
    {
        FullBody = 0,
        UpperBody = 1,
        Additive = 2
    }

    public enum OntologyAnimationRootMotionMode
    {
        Inherit = 0,
        Disabled = 1,
        Enabled = 2
    }

    public enum OntologyAnimationPresentationOwner
    {
        AuthorityIntent = 0,
        MotionStateResolver = 1
    }
}
