using System.Reflection;
using NUnit.Framework;
using Tormia.Ontology.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyAnimationTransitionTests
    {
        private const BindingFlags PrivateInstance =
            BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void ClipToClipBlendKeepsFullBodyLayerFullyWeighted()
        {
            var gameObject = new GameObject("OntologyAnimationTransitionTest");

            try
            {
                var animator = gameObject.AddComponent<Animator>();
                animator.runtimeAnimatorController =
                    AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                        "Assets/ithappy/Creative_Characters_FREE/Animations/" +
                        "Animation_Controllers/Character_Movement.controller");
                var adapter = gameObject.AddComponent<OntologyAnimationAdapter>();
                SetField(adapter, "animator", animator);
                SetField(adapter, "targetAnimator", animator);
                var idle = LoadClip(
                    "Assets/ithappy/Creative_Characters_FREE/Animations/" +
                    "Other_Animations/Idle_Relaxed.anim");
                var walk = LoadClip(
                    "Assets/ithappy/Creative_Characters_FREE/Animations/" +
                    "Other_Animations/Walk_Forward.anim");

                SetField(adapter, "playSelectedClip", true);
                SetField(adapter, "selectedFromOntologyIntent", true);
                SetField(adapter, "selectedClip", idle);
                SetField(adapter, "selectedAnimationId", "TestIdle");
                SetField(adapter, "selectedCanBlend", true);
                Invoke(adapter, "ApplySelectedClipPlayback");

                SetField(adapter, "selectedClip", walk);
                SetField(adapter, "selectedAnimationId", "TestWalk");
                SetField(adapter, "selectedCanBlend", true);
                Invoke(adapter, "ApplySelectedClipPlayback");

                var layerMixer =
                    GetField<AnimationLayerMixerPlayable>(adapter, "animationMixer");
                var clipMixer =
                    GetField<AnimationMixerPlayable>(adapter, "clipMixer");

                Assert.That(layerMixer.IsValid(), Is.True);
                Assert.That(layerMixer.GetInputCount(), Is.EqualTo(2));
                Assert.That(layerMixer.GetInputWeight(0), Is.EqualTo(0f).Within(0.0001f));
                Assert.That(layerMixer.GetInputWeight(1), Is.EqualTo(1f).Within(0.0001f));
                Assert.That(clipMixer.IsValid(), Is.True);
                Assert.That(clipMixer.GetInputCount(), Is.EqualTo(2));
                Assert.That(clipMixer.GetInputWeight(0), Is.EqualTo(1f).Within(0.0001f));
                Assert.That(clipMixer.GetInputWeight(1), Is.EqualTo(0f).Within(0.0001f));

                SetField(adapter, "selectedTransitionDuration", 1f);
                SetField(adapter, "transitionElapsed", 0.5f);
                Invoke(adapter, "UpdateTransitionBlend");

                Assert.That(layerMixer.GetInputWeight(0), Is.EqualTo(0f).Within(0.0001f));
                Assert.That(layerMixer.GetInputWeight(1), Is.EqualTo(1f).Within(0.0001f));
                Assert.That(
                    clipMixer.GetInputWeight(0) + clipMixer.GetInputWeight(1),
                    Is.EqualTo(1f).Within(0.0001f),
                    "Full-body clip transitions must be normalized inside the clip mixer. " +
                    "Complementary weights on separate animation layers reintroduce the " +
                    "controller or bind pose and make the avatar sink.");
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void NonBlendableLandingCutsAirbornePoseImmediately()
        {
            var gameObject = new GameObject(
                "NonBlendableLandingTransitionTest");

            try
            {
                var animator = gameObject.AddComponent<Animator>();
                animator.runtimeAnimatorController =
                    AssetDatabase.LoadAssetAtPath<
                        RuntimeAnimatorController>(
                        "Assets/ithappy/Creative_Characters_FREE/Animations/" +
                        "Animation_Controllers/" +
                        "Character_Movement.controller");
                var adapter =
                    gameObject.AddComponent<OntologyAnimationAdapter>();
                SetField(adapter, "animator", animator);
                SetField(adapter, "targetAnimator", animator);
                SetField(adapter, "playSelectedClip", true);
                SetField(
                    adapter,
                    "selectedFromOntologyIntent",
                    true);
                SetField(
                    adapter,
                    "selectedClip",
                    LoadClip(
                        "Assets/ithappy/Creative_Characters_FREE/" +
                        "Animations/Other_Animations/Jump_Loop.anim"));
                SetField(
                    adapter,
                    "selectedAnimationId",
                    "TestAirborne");
                SetField(adapter, "selectedCanBlend", true);
                Invoke(adapter, "ApplySelectedClipPlayback");

                SetField(
                    adapter,
                    "selectedClip",
                    LoadClip(
                        "Assets/ithappy/Creative_Characters_FREE/" +
                        "Animations/Other_Animations/Jump_End.anim"));
                SetField(
                    adapter,
                    "selectedAnimationId",
                    "TestLanding");
                SetField(adapter, "selectedCanBlend", false);
                Invoke(adapter, "ApplySelectedClipPlayback");

                var clipMixer =
                    GetField<AnimationMixerPlayable>(
                        adapter,
                        "clipMixer");
                Assert.That(
                    clipMixer.GetInputWeight(0),
                    Is.EqualTo(0f).Within(0.0001f));
                Assert.That(
                    clipMixer.GetInputWeight(1),
                    Is.EqualTo(1f).Within(0.0001f));
                Assert.That(
                    GetField<object>(adapter, "transitionMode")
                        .ToString(),
                    Is.EqualTo("None"));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void RepeatedTransientIntent_ReplaysSameAnimationClip()
        {
            var gameObject = new GameObject("RepeatedTransientAnimationTest");

            try
            {
                var animator = gameObject.AddComponent<Animator>();
                animator.runtimeAnimatorController =
                    AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                        "Assets/ithappy/Creative_Characters_FREE/Animations/" +
                        "Animation_Controllers/Character_Movement.controller");
                var adapter = gameObject.AddComponent<OntologyAnimationAdapter>();
                SetField(adapter, "animator", animator);
                SetField(adapter, "targetAnimator", animator);
                var attack = LoadClip(
                    "Assets/ithappy/Creative_Characters_FREE/Animations/" +
                    "Other_Animations/Idle_Relaxed.anim");

                SetField(adapter, "playSelectedClip", true);
                SetField(adapter, "selectedFromOntologyIntent", true);
                SetField(adapter, "selectedClip", attack);
                SetField(adapter, "selectedAnimationId", "TestAttack");
                Invoke(adapter, "ApplySelectedClipPlayback");

                var firstPlayable =
                    GetField<AnimationClipPlayable>(adapter, "clipPlayable");
                firstPlayable.SetTime(0.5d);
                SetField(adapter, "replaySelectedClipRequested", true);
                Invoke(adapter, "ApplySelectedClipPlayback");

                var replayed =
                    GetField<AnimationClipPlayable>(adapter, "clipPlayable");
                Assert.That(replayed.IsValid(), Is.True);
                Assert.That(
                    replayed.GetTime(),
                    Is.EqualTo(0d).Within(0.0001d),
                    "A second accepted action using the same animation id must start a new clip occurrence.");
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void PersistentSameAnimationSelection_DoesNotRestartWithoutOccurrence()
        {
            var gameObject = new GameObject("PersistentAnimationSelectionTest");

            try
            {
                var animator = gameObject.AddComponent<Animator>();
                animator.runtimeAnimatorController =
                    AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                        "Assets/ithappy/Creative_Characters_FREE/Animations/" +
                        "Animation_Controllers/Character_Movement.controller");
                var adapter = gameObject.AddComponent<OntologyAnimationAdapter>();
                SetField(adapter, "animator", animator);
                SetField(adapter, "targetAnimator", animator);
                var idle = LoadClip(
                    "Assets/ithappy/Creative_Characters_FREE/Animations/" +
                    "Other_Animations/Idle_Relaxed.anim");

                SetField(adapter, "playSelectedClip", true);
                SetField(adapter, "selectedFromOntologyIntent", true);
                SetField(adapter, "selectedClip", idle);
                SetField(adapter, "selectedAnimationId", "TestIdle");
                Invoke(adapter, "ApplySelectedClipPlayback");

                var playable =
                    GetField<AnimationClipPlayable>(adapter, "clipPlayable");
                playable.SetTime(0.5d);
                Invoke(adapter, "ApplySelectedClipPlayback");

                Assert.That(
                    GetField<AnimationClipPlayable>(adapter, "clipPlayable")
                        .GetTime(),
                    Is.EqualTo(0.5d).Within(0.0001d),
                    "An unchanged persistent state must not restart every frame.");
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [TestCase(OntologyAnimationStateKind.JumpStart)]
        [TestCase(OntologyAnimationStateKind.Airborne)]
        [TestCase(OntologyAnimationStateKind.Landing)]
        public void PhysicalJumpPhaseOwnsBasePresentationOverProjectedFact(
            OntologyAnimationStateKind kind)
        {
            Assert.That(
                OntologyAnimationAdapter.ShouldPrioritizeResolvedMotionState(
                    true,
                    true,
                    kind),
                Is.True,
                "A projected ambient animation fact must not replace the " +
                "collision-resolved jump phase before contact-owned landing " +
                "completes.");
        }

        [TestCase(OntologyAnimationStateKind.Idle)]
        [TestCase(OntologyAnimationStateKind.Locomotion)]
        [TestCase(OntologyAnimationStateKind.Equipment)]
        public void GroundedBaseStateDoesNotSuppressProjectedSemanticIntent(
            OntologyAnimationStateKind kind)
        {
            Assert.That(
                OntologyAnimationAdapter.ShouldPrioritizeResolvedMotionState(
                    true,
                    true,
                    kind),
                Is.False);
        }

        private static AnimationClip LoadClip(string path)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            Assert.That(clip, Is.Not.Null, path);
            return clip;
        }

        private static T GetField<T>(object target, string name)
        {
            var field = target.GetType().GetField(name, PrivateInstance);
            Assert.That(field, Is.Not.Null, name);
            return (T)field.GetValue(target);
        }

        private static void SetField(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, PrivateInstance);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(target, value);
        }

        private static void Invoke(object target, string name)
        {
            var method = target.GetType().GetMethod(name, PrivateInstance);
            Assert.That(method, Is.Not.Null, name);
            method.Invoke(target, null);
        }
    }
}
