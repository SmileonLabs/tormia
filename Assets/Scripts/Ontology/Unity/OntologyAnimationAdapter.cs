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
        [SerializeField] private OntologyActorProfile actorProfile;
        [SerializeField] private Animator targetAnimator;
        [SerializeField] private OntologyObject actorObject;
        [SerializeField, Tooltip("Legacy fallback only. Leave empty; the OntologyObject id is authoritative.")]
        private string actorId;
        [SerializeField] private bool useAnimatorSpeed = true;
        [SerializeField] private float normalAnimatorSpeed = 1.0f;
        [SerializeField] private float slowedAnimatorSpeed = 0.75f;
        [SerializeField] private bool playSelectedClip;
        [SerializeField] private bool loopBlendableClips = true;
        [SerializeField, Min(0f)] private float transitionBlendDuration = 0.25f;
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
        [SerializeField] private bool selectedFromOntologyIntent;

        public string SelectedAnimationId => selectedAnimationId;
        public string SelectedClipName => selectedClipName;
        public string SelectedIntent => selectedIntent;
        public bool SelectedCanBlend => selectedCanBlend;
        public OntologyActorProfile ActorProfile => actorProfile;

        private AnimationClip selectedClip;
        private CharacterMover characterMover;
        private OntologyInputSystemPlayerInput ontologyInput;
        private PlayableGraph playableGraph;
        private AnimationClipPlayable clipPlayable;
        private AnimatorControllerPlayable controllerPlayable;
        private AnimationMixerPlayable animationMixer;
        private string playingAnimationId;
        private int activeClipInput = -1;
        private int transitionFromInput = -1;
        private int transitionToInput = -1;
        private AnimationTransitionMode transitionMode;
        private float transitionElapsed;
        private bool lastDefaultMovementState;
        private bool hasDefaultMovementState;
        private string ActorId => actorObject != null &&
                                  !string.IsNullOrWhiteSpace(actorObject.EntityId)
            ? actorObject.EntityId
            : actorId;

        private enum AnimationTransitionMode
        {
            None,
            ControllerToClip,
            ClipToClip,
            ClipToController
        }

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
            ontologyInput = GetComponent<OntologyInputSystemPlayerInput>();
            if (actorObject == null) actorObject = GetComponentInParent<OntologyObject>();
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
            transitionBlendDuration = Mathf.Max(0f, transitionBlendDuration);
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

        private void OnEnable()
        {
            if (bootstrap == null)
            {
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            }

            if (bootstrap != null)
            {
                bootstrap.WorldChanged += RefreshFromWorld;
            }
        }

        private void Start()
        {
            RefreshFromWorld();
        }

        private void Update()
        {
            // Only the Animator-controller fallback depends on direct movement input.
            // Ontology-driven intents are refreshed by WorldChanged instead of rescanning
            // every fact and animation definition on every frame.
            var currentMovementState = IsMoving();
            if (!hasDefaultMovementState || currentMovementState != lastDefaultMovementState)
            {
                lastDefaultMovementState = currentMovementState;
                hasDefaultMovementState = true;
                RefreshFromWorld();
            }
        }

        private void RefreshFromWorld()
        {
            if (bootstrap == null || bootstrap.World == null || animator == null)
            {
                return;
            }

            var isSlowed = bootstrap.World.HasFact(ActorId, "movement_state", "Slowed");
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
            if (bootstrap != null)
            {
                bootstrap.WorldChanged -= RefreshFromWorld;
            }
            StopSelectedClipPlayback();
        }

        private void SelectAnimationCandidate()
        {
            selectedAnimationId = string.Empty;
            selectedClipName = string.Empty;
            selectedIntent = string.Empty;
            selectedCanBlend = false;
            selectedFromOntologyIntent = false;
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
                if (fact.Subject.ToString() != ActorId || fact.Predicate.ToString() != "animation_intent")
                {
                    continue;
                }

                var intent = fact.Object.ToString();
                foundFactIntent = true;
                foreach (var definition in animationDatabase.Definitions)
                {
                    if (!CanUse(definition) || !HasIntent(definition, intent))
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
                    if (!CanUse(definition) || !HasIntent(definition, defaultIntent))
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
            selectedFromOntologyIntent = foundFactIntent;
            selectedClip = bestDefinition.clip;
        }

        private bool IsMoving()
        {
            if (ontologyInput != null)
            {
                return ontologyInput.IsMovingIntent;
            }

            if (characterMover != null)
            {
                return characterMover.Axis.sqrMagnitude > movementIntentThreshold * movementIntentThreshold;
            }

            return false;
        }

        private void ApplySelectedClipPlayback()
        {
            // Explicit ontology intent + actor repertoire is the only per-animation permission.
            // Default Idle/Locomotion remains under the Animator controller.
            if (!playSelectedClip || selectedClip == null || !selectedFromOntologyIntent)
            {
                BeginReturnToController();
                return;
            }

            if (playingAnimationId == selectedAnimationId && playableGraph.IsValid())
            {
                if (transitionMode == AnimationTransitionMode.ClipToController)
                {
                    transitionMode = AnimationTransitionMode.ControllerToClip;
                    transitionElapsed = 0f;
                }
                return;
            }

            if (playableGraph.IsValid() && animationMixer.IsValid() && activeClipInput >= 1)
            {
                BeginClipToClipBlend();
                return;
            }

            StopSelectedClipPlayback();
            playableGraph = PlayableGraph.Create("OntologyAnimationAdapter_" + ActorId);
            var output = AnimationPlayableOutput.Create(playableGraph, "OntologyAnimation", animator);
            controllerPlayable = AnimatorControllerPlayable.Create(playableGraph, animator.runtimeAnimatorController);
            clipPlayable = AnimationClipPlayable.Create(playableGraph, selectedClip);
            clipPlayable.SetApplyFootIK(false);
            clipPlayable.SetApplyPlayableIK(false);
            clipPlayable.SetDuration(selectedClip.length);
            clipPlayable.SetTime(0d);
            clipPlayable.SetSpeed(1d);
            animationMixer = AnimationMixerPlayable.Create(playableGraph, 3, true);
            playableGraph.Connect(controllerPlayable, 0, animationMixer, 0);
            playableGraph.Connect(clipPlayable, 0, animationMixer, 1);
            animationMixer.SetInputWeight(0, 1f);
            animationMixer.SetInputWeight(1, 0f);
            output.SetSourcePlayable(animationMixer);
            playableGraph.Play();
            playingAnimationId = selectedAnimationId;
            activeClipInput = 1;
            transitionFromInput = 0;
            transitionToInput = activeClipInput;
            transitionMode = AnimationTransitionMode.ControllerToClip;
            transitionElapsed = 0f;
        }

        private void BeginClipToClipBlend()
        {
            var nextInput = activeClipInput == 1 ? 2 : 1;
            playableGraph.Disconnect(animationMixer, nextInput);
            var nextClip = AnimationClipPlayable.Create(playableGraph, selectedClip);
            nextClip.SetApplyFootIK(false);
            nextClip.SetApplyPlayableIK(false);
            nextClip.SetDuration(selectedClip.length);
            nextClip.SetTime(0d);
            nextClip.SetSpeed(1d);
            playableGraph.Connect(nextClip, 0, animationMixer, nextInput);
            animationMixer.SetInputWeight(0, 0f);
            animationMixer.SetInputWeight(activeClipInput, 1f);
            animationMixer.SetInputWeight(nextInput, 0f);
            clipPlayable = nextClip;
            playingAnimationId = selectedAnimationId;
            transitionFromInput = activeClipInput;
            transitionToInput = nextInput;
            transitionMode = AnimationTransitionMode.ClipToClip;
            transitionElapsed = 0f;
        }

        private void BeginReturnToController()
        {
            if (!playableGraph.IsValid())
            {
                return;
            }

            if (!animationMixer.IsValid() || !controllerPlayable.IsValid())
            {
                StopSelectedClipPlayback();
                return;
            }

            if (transitionMode != AnimationTransitionMode.ClipToController)
            {
                transitionFromInput = activeClipInput;
                transitionToInput = 0;
                transitionMode = AnimationTransitionMode.ClipToController;
                transitionElapsed = 0f;
            }
        }

        private void StopSelectedClipPlayback()
        {
            if (playableGraph.IsValid())
            {
                playableGraph.Destroy();
            }

            playingAnimationId = string.Empty;
            activeClipInput = -1;
            transitionFromInput = -1;
            transitionToInput = -1;
            transitionMode = AnimationTransitionMode.None;
            transitionElapsed = 0f;
            animationMixer = default;
            controllerPlayable = default;
            clipPlayable = default;
        }

        private void LateUpdate()
        {
            if (!playSelectedClip || !playableGraph.IsValid() || !clipPlayable.IsValid())
            {
                return;
            }

            UpdateTransitionBlend();

            if (selectedCanBlend && loopBlendableClips && selectedClip != null && selectedClip.length > 0f)
            {
                // A graph can be torn down during an intent change between frames.
                // Treat an invalid native playable as a completed transition instead
                // of allowing GetTime() to raise and interrupt the player loop.
                try
                {
                    if (!clipPlayable.IsValid()) return;
                    var time = clipPlayable.GetTime();
                    if (time >= selectedClip.length) clipPlayable.SetTime(0d);
                }
                catch (ArgumentNullException)
                {
                    StopSelectedClipPlayback();
                }
                catch (InvalidOperationException)
                {
                    StopSelectedClipPlayback();
                }
            }
        }

        private void UpdateTransitionBlend()
        {
            if (!animationMixer.IsValid())
            {
                return;
            }

            var duration = Mathf.Max(0f, transitionBlendDuration);
            transitionElapsed = duration <= 0f ? duration : transitionElapsed + Time.deltaTime;
            var weight = duration <= 0f ? 1f : Mathf.Clamp01(transitionElapsed / duration);
            if (transitionMode == AnimationTransitionMode.ClipToController)
            {
                animationMixer.SetInputWeight(0, weight);
                if (transitionFromInput >= 1) animationMixer.SetInputWeight(transitionFromInput, 1f - weight);
                if (weight >= 1f)
                {
                    StopSelectedClipPlayback();
                }
            }
            else if (transitionMode == AnimationTransitionMode.ClipToClip)
            {
                animationMixer.SetInputWeight(0, 0f);
                animationMixer.SetInputWeight(transitionFromInput, 1f - weight);
                animationMixer.SetInputWeight(transitionToInput, weight);
                if (weight >= 1f)
                {
                    activeClipInput = transitionToInput;
                    transitionMode = AnimationTransitionMode.None;
                }
            }
            else if (transitionMode == AnimationTransitionMode.ControllerToClip)
            {
                animationMixer.SetInputWeight(0, 1f - weight);
                animationMixer.SetInputWeight(activeClipInput, weight);
                if (weight >= 1f)
                {
                    transitionMode = AnimationTransitionMode.None;
                }
            }
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

        private bool CanUse(OntologyAnimationDefinition definition)
        {
            if (definition == null || definition.clip == null)
            {
                return false;
            }

            // Profile selection defines the actor's repertoire; ontology rules decide intent.
            if (HasRegisteredAnimationsInWorld())
            {
                return bootstrap.World.HasFact(ActorId, OntologyPredicates.HasAnimation, definition.animationId);
            }

            if (actorProfile != null)
            {
                return actorProfile.HasAnimation(definition.animationId);
            }

            return true;
        }

        private bool HasRegisteredAnimationsInWorld()
        {
            if (bootstrap == null || bootstrap.World == null)
            {
                return false;
            }

            foreach (var fact in bootstrap.World.Facts)
            {
                if (fact.Subject.ToString() == ActorId &&
                    fact.Predicate.ToString() == OntologyPredicates.HasAnimation)
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

                var value = bootstrap.World.HasFact(ActorId, binding.predicate, binding.obj);
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
