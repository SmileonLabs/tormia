using System;
using ithappy.Creative_Characters_FREE.Controller;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    [RequireComponent(typeof(Animator))]
    public sealed class OntologyAnimationAdapter : MonoBehaviour
    {
        [SerializeField] private OntologyWorldBootstrap bootstrap;
        [SerializeField] private OntologyAnimationDatabase animationDatabase;
        [SerializeField] private Animator targetAnimator;
        [SerializeField] private string actorId = "Player";
        [SerializeField] private bool useAnimatorSpeed = true;
        [SerializeField] private float normalAnimatorSpeed = 1.0f;
        [SerializeField] private float slowedAnimatorSpeed = 0.75f;
        [SerializeField] private bool playSelectedClip;
        [SerializeField] private bool loopBlendableClips = true;
        [SerializeField] private string[] playableIntents =
        {
            "SevereDamageReaction",
            "DeathReaction",
            "Attack",
            "Defense",
            "Evasion"
        };
        [SerializeField] private bool useDefaultIntentWhenNoFact = true;
        [SerializeField] private string defaultIdleIntent = "Idle";
        [SerializeField] private string defaultMoveIntent = "Locomotion";
        [SerializeField] private float movementIntentThreshold = 0.01f;
        [SerializeField] private OntologyAnimatorBoolBinding[] boolBindings =
        {
            new OntologyAnimatorBoolBinding
            {
                predicate = "movement_state",
                obj = "Slowed",
                parameter = "isSlowed"
            },
            new OntologyAnimatorBoolBinding
            {
                predicate = "exposed_to",
                obj = "ColdEnvironment",
                parameter = "isFreezing"
            }
        };

        private Animator animator;
        private int[] boolParameterHashes = Array.Empty<int>();
        [SerializeField] private string selectedAnimationId;
        [SerializeField] private string selectedClipName;
        [SerializeField] private string selectedIntent;
        [SerializeField] private bool selectedCanBlend;

        public string SelectedAnimationId => selectedAnimationId;
        public string SelectedClipName => selectedClipName;
        public string SelectedIntent => selectedIntent;
        public bool SelectedCanBlend => selectedCanBlend;

        private AnimationClip selectedClip;
        private CharacterMover characterMover;
        private PlayableGraph playableGraph;
        private AnimationClipPlayable clipPlayable;
        private string playingAnimationId;

        private void Awake()
        {
            animator = GetComponent<Animator>();
            if (targetAnimator == null)
            {
                targetAnimator = FindBestAnimatorTarget();
            }

            if (targetAnimator != null)
            {
                animator = targetAnimator;
            }

            characterMover = GetComponent<CharacterMover>();
            if (bootstrap == null)
            {
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            }

            RebuildParameterCache();
        }

        private void OnValidate()
        {
            normalAnimatorSpeed = Mathf.Max(0f, normalAnimatorSpeed);
            slowedAnimatorSpeed = Mathf.Max(0f, slowedAnimatorSpeed);
        }

        private Animator FindBestAnimatorTarget()
        {
            var animators = GetComponentsInChildren<Animator>();
            foreach (var candidate in animators)
            {
                if (candidate != null && candidate.avatar != null && candidate.isHuman)
                {
                    return candidate;
                }
            }

            return GetComponent<Animator>();
        }

        private void Update()
        {
            if (bootstrap == null || bootstrap.World == null || animator == null)
            {
                return;
            }

            var isSlowed = bootstrap.World.HasFact(actorId, "movement_state", "Slowed");
            if (useAnimatorSpeed)
            {
                animator.speed = isSlowed ? slowedAnimatorSpeed : normalAnimatorSpeed;
            }

            ApplyBoolBindings();
            SelectAnimationCandidate();
            ApplySelectedClipPlayback();
        }

        private void OnDisable()
        {
            StopSelectedClipPlayback();
        }

        private void SelectAnimationCandidate()
        {
            selectedAnimationId = string.Empty;
            selectedClipName = string.Empty;
            selectedIntent = string.Empty;
            selectedCanBlend = false;
            selectedClip = null;

            if (animationDatabase == null || animationDatabase.Definitions == null)
            {
                return;
            }

            OntologyAnimationDefinition bestDefinition = null;
            string bestIntent = null;
            var foundFactIntent = false;
            foreach (var fact in bootstrap.World.Facts)
            {
                if (fact.Subject.ToString() != actorId || fact.Predicate.ToString() != "animation_intent")
                {
                    continue;
                }

                var intent = fact.Object.ToString();
                foundFactIntent = true;
                foreach (var definition in animationDatabase.Definitions)
                {
                    if (definition == null || definition.clip == null || !HasIntent(definition, intent))
                    {
                        continue;
                    }

                    if (bestDefinition == null || definition.priority > bestDefinition.priority)
                    {
                        bestDefinition = definition;
                        bestIntent = intent;
                    }
                }
            }

            if (!foundFactIntent && useDefaultIntentWhenNoFact)
            {
                var defaultIntent = IsMoving() ? defaultMoveIntent : defaultIdleIntent;
                foreach (var definition in animationDatabase.Definitions)
                {
                    if (definition == null || definition.clip == null || !HasIntent(definition, defaultIntent))
                    {
                        continue;
                    }

                    if (bestDefinition == null || definition.priority > bestDefinition.priority)
                    {
                        bestDefinition = definition;
                        bestIntent = defaultIntent;
                    }
                }
            }

            if (bestDefinition == null)
            {
                return;
            }

            selectedAnimationId = bestDefinition.animationId;
            selectedClipName = bestDefinition.clip.name;
            selectedIntent = bestIntent;
            selectedCanBlend = bestDefinition.canBlend;
            selectedClip = bestDefinition.clip;
        }

        private bool IsMoving()
        {
            if (characterMover != null)
            {
                return characterMover.Axis.sqrMagnitude > movementIntentThreshold * movementIntentThreshold;
            }

            return false;
        }

        private void ApplySelectedClipPlayback()
        {
            if (!playSelectedClip || selectedClip == null || !IsPlayableIntent(selectedIntent))
            {
                StopSelectedClipPlayback();
                return;
            }

            if (playingAnimationId == selectedAnimationId && playableGraph.IsValid())
            {
                return;
            }

            StopSelectedClipPlayback();
            playableGraph = PlayableGraph.Create("OntologyAnimationAdapter_" + actorId);
            var output = AnimationPlayableOutput.Create(playableGraph, "OntologyAnimation", animator);
            clipPlayable = AnimationClipPlayable.Create(playableGraph, selectedClip);
            clipPlayable.SetApplyFootIK(false);
            clipPlayable.SetApplyPlayableIK(false);
            clipPlayable.SetDuration(selectedClip.length);
            clipPlayable.SetTime(0d);
            clipPlayable.SetSpeed(1d);
            output.SetSourcePlayable(clipPlayable);
            playableGraph.Play();
            playingAnimationId = selectedAnimationId;
        }

        private void StopSelectedClipPlayback()
        {
            if (playableGraph.IsValid())
            {
                playableGraph.Destroy();
            }

            playingAnimationId = string.Empty;
        }

        private void LateUpdate()
        {
            if (!playSelectedClip || !playableGraph.IsValid() || !clipPlayable.IsValid())
            {
                return;
            }

            if (selectedCanBlend && loopBlendableClips && selectedClip != null && selectedClip.length > 0f)
            {
                var time = clipPlayable.GetTime();
                if (time >= selectedClip.length)
                {
                    clipPlayable.SetTime(0d);
                }
            }
        }

        private bool IsPlayableIntent(string intent)
        {
            if (string.IsNullOrWhiteSpace(intent) || playableIntents == null)
            {
                return false;
            }

            foreach (var playableIntent in playableIntents)
            {
                if (playableIntent == intent)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasIntent(OntologyAnimationDefinition definition, string intent)
        {
            if (definition.intents == null)
            {
                return false;
            }

            foreach (var candidate in definition.intents)
            {
                if (candidate == intent)
                {
                    return true;
                }
            }

            return false;
        }

        private void ApplyBoolBindings()
        {
            if (boolBindings == null)
            {
                return;
            }

            if (boolParameterHashes == null || boolParameterHashes.Length != boolBindings.Length)
            {
                RebuildParameterCache();
            }

            for (var i = 0; i < boolBindings.Length; i++)
            {
                var binding = boolBindings[i];
                if (binding == null || boolParameterHashes[i] == 0)
                {
                    continue;
                }

                var value = bootstrap.World.HasFact(actorId, binding.predicate, binding.obj);
                animator.SetBool(boolParameterHashes[i], value);
            }
        }

        private void RebuildParameterCache()
        {
            if (animator == null)
            {
                return;
            }

            if (boolBindings == null)
            {
                boolParameterHashes = Array.Empty<int>();
                return;
            }

            boolParameterHashes = new int[boolBindings.Length];
            for (var i = 0; i < boolBindings.Length; i++)
            {
                var binding = boolBindings[i];
                if (binding == null || string.IsNullOrWhiteSpace(binding.parameter))
                {
                    continue;
                }

                boolParameterHashes[i] = FindBoolParameterHash(binding.parameter);
            }
        }

        private int FindBoolParameterHash(string parameter)
        {
            foreach (var animatorParameter in animator.parameters)
            {
                if (animatorParameter.type != AnimatorControllerParameterType.Bool)
                {
                    continue;
                }

                if (animatorParameter.name == parameter)
                {
                    return animatorParameter.nameHash;
                }
            }

            return 0;
        }
    }

    [Serializable]
    public sealed class OntologyAnimatorBoolBinding
    {
        public string predicate;
        public string obj;
        public string parameter;
    }
}
