using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Tormia.Ontology.Core.Tests
{
    public sealed class OntologyAnimationProductionLineTests
    {
        [Test]
        public void ResolverMovesThroughGroundAirAndLandingWithoutDurableFacts()
        {
            var resolver = new OntologyAnimationStateResolver(
                -2f);

            Assert.That(
                resolver.Resolve(Snapshot(false, false, true, -1f)).Intent,
                Is.EqualTo(OntologyAnimationIntentIds.Idle));
            Assert.That(
                resolver.Resolve(Snapshot(true, false, true, -1f)).Intent,
                Is.EqualTo(OntologyAnimationIntentIds.Locomotion));
            Assert.That(
                resolver.Resolve(Snapshot(false, false, false, 0f)).Intent,
                Is.EqualTo(OntologyAnimationIntentIds.Airborne));
            Assert.That(
                resolver.Resolve(Snapshot(false, false, false, -3f)).Intent,
                Is.EqualTo(OntologyAnimationIntentIds.Fall));
            Assert.That(
                resolver.Resolve(Snapshot(false, false, true, -1f)).Intent,
                Is.EqualTo(OntologyAnimationIntentIds.Landing));
        }

        [Test]
        public void EquipmentIntentsOverrideBaseIdleAndLocomotion()
        {
            var resolver = new OntologyAnimationStateResolver();
            var idle = resolver.Resolve(
                new OntologyAnimationStateSnapshot(
                    false,
                    false,
                    true,
                    -1f,
                    OntologyAnimationIntentIds.WeaponIdle,
                    OntologyAnimationIntentIds.WeaponWalk));
            var moving = resolver.Resolve(
                new OntologyAnimationStateSnapshot(
                    true,
                    false,
                    true,
                    -1f,
                    OntologyAnimationIntentIds.WeaponIdle,
                    OntologyAnimationIntentIds.WeaponWalk));

            Assert.That(idle.Intent, Is.EqualTo(OntologyAnimationIntentIds.WeaponIdle));
            Assert.That(moving.Intent, Is.EqualTo(OntologyAnimationIntentIds.WeaponWalk));
        }

        [Test]
        public void LosingGroundUsesAirborneObservationWithoutGameplayAction()
        {
            var resolver = new OntologyAnimationStateResolver(
                -2f);

            resolver.Resolve(
                Snapshot(false, false, true, -1f));
            var uncommandedAirborne = resolver.Resolve(
                Snapshot(false, false, false, 0f));

            Assert.That(
                uncommandedAirborne.Intent,
                Is.EqualTo(OntologyAnimationIntentIds.Airborne),
                "A terrain contact gap or lease refresh remains a presentation " +
                "observation and does not create a gameplay action.");
        }

        [Test]
        public void ApprovedJumpOccurrenceCompletesEachPresentationPhaseOnce()
        {
            var resolver = new OntologyAnimationStateResolver(-2f);

            Assert.That(
                resolver.Resolve(
                    Snapshot(false, false, true, -1f)).Intent,
                Is.EqualTo(OntologyAnimationIntentIds.Idle));
            Assert.That(
                resolver.Resolve(
                    Snapshot(
                        false,
                        false,
                        false,
                        7f,
                        jumpOccurrence: 1)).Intent,
                Is.EqualTo(OntologyAnimationIntentIds.JumpStart));
            Assert.That(
                resolver.Resolve(
                    Snapshot(
                        false,
                        false,
                        false,
                        5f,
                        jumpOccurrence: 1,
                        presentationIntent:
                            OntologyAnimationIntentIds.JumpStart)).Intent,
                Is.EqualTo(OntologyAnimationIntentIds.JumpStart));
            Assert.That(
                resolver.Resolve(
                    Snapshot(
                        false,
                        false,
                        false,
                        3f,
                        jumpOccurrence: 1,
                        presentationIntent:
                            OntologyAnimationIntentIds.JumpStart,
                        presentationCompleted: true)).Intent,
                Is.EqualTo(OntologyAnimationIntentIds.Airborne));
            Assert.That(
                resolver.Resolve(
                    Snapshot(
                        false,
                        false,
                        false,
                        -3f,
                        jumpOccurrence: 1)).Intent,
                Is.EqualTo(OntologyAnimationIntentIds.Fall));
            Assert.That(
                resolver.Resolve(
                    Snapshot(
                        false,
                        false,
                        true,
                        -1f,
                        jumpOccurrence: 1)).Intent,
                Is.EqualTo(OntologyAnimationIntentIds.Landing));
            Assert.That(
                resolver.Resolve(
                    Snapshot(
                        false,
                        false,
                        true,
                        -1f,
                        jumpOccurrence: 1,
                        presentationIntent:
                            OntologyAnimationIntentIds.Landing,
                        presentationCompleted: true)).Intent,
                Is.EqualTo(OntologyAnimationIntentIds.Idle));
        }

        [Test]
        public void DescendingAvatarCannotRemainInJumpStart()
        {
            var resolver = new OntologyAnimationStateResolver(-2f);

            resolver.Resolve(
                Snapshot(false, false, true, -1f));
            Assert.That(
                resolver.Resolve(
                    Snapshot(
                        false,
                        false,
                        false,
                        7f,
                        jumpOccurrence: 1)).Intent,
                Is.EqualTo(OntologyAnimationIntentIds.JumpStart));

            var descending = resolver.Resolve(
                Snapshot(
                    false,
                    false,
                    false,
                    -4f,
                    jumpOccurrence: 1,
                    presentationIntent:
                        OntologyAnimationIntentIds.JumpStart,
                    presentationCompleted: false));

            Assert.That(
                descending.Intent,
                Is.EqualTo(OntologyAnimationIntentIds.Fall),
                "An authored takeoff clip may not override the observed " +
                "physical descent phase.");
        }

        [Test]
        public void ApexWithoutCollisionContactCannotSelectIdleOrLanding()
        {
            var resolver = new OntologyAnimationStateResolver(-2f);

            resolver.Resolve(
                Snapshot(false, false, true, -1f));
            resolver.Resolve(
                Snapshot(
                    false,
                    false,
                    false,
                    7f,
                    jumpOccurrence: 1));

            var apex = resolver.Resolve(
                Snapshot(
                    false,
                    false,
                    false,
                    0f,
                    jumpOccurrence: 1,
                    presentationIntent:
                        OntologyAnimationIntentIds.JumpStart,
                    presentationCompleted: true));

            Assert.That(
                apex.Intent,
                Is.EqualTo(OntologyAnimationIntentIds.Airborne));
            Assert.That(
                apex.Kind,
                Is.EqualTo(OntologyAnimationStateKind.Airborne));
        }

        [Test]
        public void PlayerManifestSeparatesApexFallAndLandingPresentation()
        {
            var manifest =
                AssetDatabase.LoadAssetAtPath<
                    OntologyAnimationContentManifest>(
                    "Assets/Data/Ontology/" +
                    "AnimationContentManifest.asset");

            Assert.That(manifest, Is.Not.Null);
            var airborne = manifest.Entries.Single(value =>
                value != null &&
                value.intents != null &&
                value.intents.Contains(
                    OntologyAnimationIntentIds.Airborne));
            var fall = manifest.Entries.Single(value =>
                value != null &&
                value.intents != null &&
                value.intents.Contains(
                    OntologyAnimationIntentIds.Fall));
            var landing = manifest.Entries.Single(value =>
                value != null &&
                value.intents != null &&
                value.intents.Contains(
                    OntologyAnimationIntentIds.Landing));

            Assert.That(
                fall.animationId,
                Is.Not.EqualTo(airborne.animationId),
                "Fall must be independently authored instead of silently " +
                "reusing the apex presentation identity.");
            Assert.That(airborne.loop, Is.True);
            Assert.That(fall.loop, Is.True);
            Assert.That(
                airborne.playbackEndNormalized,
                Is.LessThanOrEqualTo(
                    fall.playbackStartNormalized));
            Assert.That(landing.canBlend, Is.True);
            Assert.That(
                airborne.presentationOwner,
                Is.EqualTo(
                    OntologyAnimationPresentationOwner
                        .MotionStateResolver));
            Assert.That(
                fall.presentationOwner,
                Is.EqualTo(
                    OntologyAnimationPresentationOwner
                        .MotionStateResolver));
            Assert.That(
                landing.presentationOwner,
                Is.EqualTo(
                    OntologyAnimationPresentationOwner
                        .MotionStateResolver));
        }

        [Test]
        public void AuthorityJumpPresentationUsesPhysicalStateResolverOwnership()
        {
            var database =
                ScriptableObject.CreateInstance<OntologyAnimationDatabase>();
            try
            {
                database.Upsert(new OntologyAnimationDefinition
                {
                    animationId = "Anim_TestJump",
                    intents = new[]
                    {
                        OntologyAnimationIntentIds.JumpStart,
                        OntologyAnimationIntentIds.Fall
                    },
                    presentationOwner =
                        OntologyAnimationPresentationOwner.MotionStateResolver
                });
                database.Upsert(new OntologyAnimationDefinition
                {
                    animationId = "Anim_TestAttack",
                    intents = new[]
                    {
                        OntologyAnimationIntentIds.AttackLight
                    },
                    presentationOwner =
                        OntologyAnimationPresentationOwner.AuthorityIntent
                });

                Assert.That(
                    OntologyAnimationAdapter
                        .ShouldRouteAuthorityIntentToMotionStateResolver(
                            OntologyAnimationIntentIds.JumpStart,
                            true,
                            database),
                    Is.True);
                Assert.That(
                    OntologyAnimationAdapter
                        .ShouldRouteAuthorityIntentToMotionStateResolver(
                            OntologyAnimationIntentIds.Fall,
                            true,
                            database),
                    Is.True);
                Assert.That(
                    OntologyAnimationAdapter
                        .ShouldRouteAuthorityIntentToMotionStateResolver(
                            OntologyAnimationIntentIds.AttackLight,
                            true,
                            database),
                    Is.False,
                    "One-shot combat presentation must remain Authority-driven.");
                Assert.That(
                    OntologyAnimationAdapter
                        .ShouldRouteAuthorityIntentToMotionStateResolver(
                            OntologyAnimationIntentIds.JumpStart,
                            false,
                            database),
                    Is.False,
                    "Actors without the local motion-state owner keep the generic " +
                    "Authority presentation path.");
                Assert.That(
                    OntologyAnimationAdapter
                        .ShouldRouteAuthorityIntentToMotionStateResolver(
                            OntologyAnimationIntentIds.JumpStart,
                            true,
                            null),
                    Is.False,
                    "Missing manifest-derived metadata must fail closed.");
            }
            finally
            {
                Object.DestroyImmediate(database);
            }
        }

        [Test]
        public void JumpLifecycleManifestRequiresSegmentedInPlacePresentation()
        {
            var manifest =
                ScriptableObject.CreateInstance<
                    OntologyAnimationContentManifest>();
            var clip = new AnimationClip();
            manifest.ReplaceEntries(new[]
            {
                new OntologyAnimationContentEntry
                {
                    animationId = "Anim_TestJump",
                    clip = clip,
                    intents = new[]
                    {
                        OntologyAnimationIntentIds.JumpStart
                    },
                    profiles = new[]
                    {
                        ScriptableObject.CreateInstance<
                            OntologyActorProfile>()
                    },
                    playbackStartNormalized = 0.8f,
                    playbackEndNormalized = 0.2f,
                    rootMotionMode =
                        OntologyAnimationRootMotionMode.Inherit,
                    presentationOwner =
                        OntologyAnimationPresentationOwner.MotionStateResolver
                }
            });

            var issues = manifest.ValidateEntries();

            Assert.That(
                issues.Any(
                    value =>
                        value.Code == "playback_segment_invalid"),
                Is.True);
            Assert.That(
                issues.Any(
                    value =>
                        value.Code ==
                        "direct_character_root_motion_not_disabled"),
                Is.True);
            Object.DestroyImmediate(
                manifest.Entries[0].profiles[0]);
            Object.DestroyImmediate(clip);
            Object.DestroyImmediate(manifest);
        }

        [Test]
        public void PlayerJumpLifecycleUsesInPlaceSegmentsWithoutLandingRise()
        {
            var manifest =
                AssetDatabase.LoadAssetAtPath<
                    OntologyAnimationContentManifest>(
                    "Assets/Data/Ontology/AnimationContentManifest.asset");
            Assert.That(manifest, Is.Not.Null);

            var jumpStart = manifest.Entries.Single(
                value =>
                    value != null &&
                    value.intents.Contains(
                        OntologyAnimationIntentIds.JumpStart));
            var airborne = manifest.Entries.Single(
                value =>
                    value != null &&
                    value.intents.Contains(
                        OntologyAnimationIntentIds.Airborne));
            var landing = manifest.Entries.Single(
                value =>
                    value != null &&
                    value.intents.Contains(
                        OntologyAnimationIntentIds.Landing));
            var fall = manifest.Entries.Single(
                value =>
                    value != null &&
                    value.intents.Contains(
                        OntologyAnimationIntentIds.Fall));
            var groundCollapse = manifest.Entries.Single(
                value =>
                    value != null &&
                    value.animationId == "Anim_Collapse_Reaction");

            Assert.That(
                jumpStart.rootMotionMode,
                Is.EqualTo(
                    OntologyAnimationRootMotionMode.Disabled));
            Assert.That(
                airborne.rootMotionMode,
                Is.EqualTo(
                    OntologyAnimationRootMotionMode.Disabled));
            Assert.That(
                landing.rootMotionMode,
                Is.EqualTo(
                    OntologyAnimationRootMotionMode.Disabled));
            Assert.That(
                jumpStart.playbackEndNormalized,
                Is.LessThan(0.5f));
            Assert.That(
                landing.playbackStartNormalized,
                Is.GreaterThan(0.5f));
            Assert.That(
                fall.clip,
                Is.SameAs(airborne.clip),
                "Descending direct motion must continue the authored airborne " +
                "pose instead of selecting a ground-collapse animation by name.");
            Assert.That(
                fall.intents,
                Does.Not.Contain("CollapseReaction"));
            Assert.That(
                groundCollapse.intents,
                Does.Not.Contain(OntologyAnimationIntentIds.Fall),
                "A collapse clip may not claim the canonical direct-motion " +
                "Fall intent.");
            Assert.That(
                groundCollapse.legacyAnimationIds,
                Does.Contain("Anim_Fall"),
                "Existing durable animation ids remain readable during the " +
                "canonical collapse-reaction migration.");
            Assert.That(
                new[] { jumpStart, airborne, landing, fall }.All(
                    value =>
                        value.presentationOwner ==
                        OntologyAnimationPresentationOwner
                            .MotionStateResolver),
                Is.True,
                "Manifest metadata, not a hardcoded intent list, must select " +
                "the collision-observed presentation lifecycle owner.");

            var rootYBinding = AnimationUtility
                .GetCurveBindings(landing.clip)
                .Single(
                    value =>
                        value.path.Length == 0 &&
                        value.propertyName == "RootT.y");
            var rootY = AnimationUtility.GetEditorCurve(
                landing.clip,
                rootYBinding);
            Assert.That(rootY, Is.Not.Null);

            var previous = rootY.Evaluate(
                landing.clip.length *
                landing.playbackStartNormalized);
            for (var index = 1; index <= 20; index++)
            {
                var normalized = Mathf.Lerp(
                    landing.playbackStartNormalized,
                    landing.playbackEndNormalized,
                    index / 20f);
                var current = rootY.Evaluate(
                    landing.clip.length * normalized);
                Assert.That(
                    current,
                    Is.LessThanOrEqualTo(previous + 0.002f),
                    "The authored landing segment must settle downward; " +
                    "an upward root curve recreates a second visual jump.");
                previous = current;
            }
        }

        [Test]
        public void ManifestRejectsDuplicateIdsAndMissingClip()
        {
            var manifest =
                ScriptableObject.CreateInstance<OntologyAnimationContentManifest>();
            manifest.ReplaceEntries(new[]
            {
                new OntologyAnimationContentEntry
                {
                    animationId = "Anim_Test",
                    intents = new[] { "Idle" }
                },
                new OntologyAnimationContentEntry
                {
                    animationId = "Anim_Test",
                    intents = new[] { "Locomotion" }
                }
            });

            var issues = manifest.ValidateEntries();

            Assert.That(
                issues.Any(value => value.Code == "animation_id_duplicate"),
                Is.True);
            Assert.That(
                issues.Count(value => value.Code == "clip_missing"),
                Is.EqualTo(2));
            Object.DestroyImmediate(manifest);
        }

        [Test]
        public void ManifestRejectsAliasAndPresentationOwnerConflicts()
        {
            var manifest =
                ScriptableObject.CreateInstance<
                    OntologyAnimationContentManifest>();
            var database =
                ScriptableObject.CreateInstance<
                    OntologyAnimationDatabase>();
            var firstClip = new AnimationClip();
            var secondClip = new AnimationClip();
            database.Upsert(new OntologyAnimationDefinition
            {
                animationId = "Anim_Current",
                legacyAnimationIds = new[] { "Anim_Legacy" },
                intents = new[] { OntologyAnimationIntentIds.JumpStart },
                presentationOwner =
                    OntologyAnimationPresentationOwner.MotionStateResolver
            });
            Assert.That(
                database.FindById("Anim_Legacy")?.animationId,
                Is.EqualTo("Anim_Current"));
            Assert.That(
                database.IsIntentOwnedBy(
                    OntologyAnimationIntentIds.JumpStart,
                    OntologyAnimationPresentationOwner.MotionStateResolver),
                Is.True);
            manifest.ReplaceEntries(new[]
            {
                new OntologyAnimationContentEntry
                {
                    animationId = "Anim_Current",
                    legacyAnimationIds = new[] { "Anim_Legacy" },
                    clip = firstClip,
                    intents = new[] { OntologyAnimationIntentIds.JumpStart },
                    rootMotionMode =
                        OntologyAnimationRootMotionMode.Disabled,
                    presentationOwner =
                        OntologyAnimationPresentationOwner.MotionStateResolver
                },
                new OntologyAnimationContentEntry
                {
                    animationId = "Anim_Legacy",
                    clip = secondClip,
                    intents = new[] { OntologyAnimationIntentIds.JumpStart },
                    presentationOwner =
                        OntologyAnimationPresentationOwner.AuthorityIntent
                }
            });

            var issues = manifest.ValidateEntries();

            Assert.That(
                issues.Any(
                    value =>
                        value.Code == "animation_id_duplicate"),
                Is.True);
            Assert.That(
                issues.Any(
                    value =>
                        value.Code ==
                        "intent_presentation_owner_conflict"),
                Is.True);
            Object.DestroyImmediate(secondClip);
            Object.DestroyImmediate(firstClip);
            Object.DestroyImmediate(database);
            Object.DestroyImmediate(manifest);
        }

        [Test]
        public void RemovingManifestEntryRemovesGeneratedRuntimeDefinition()
        {
            var manifest =
                ScriptableObject.CreateInstance<OntologyAnimationContentManifest>();
            var database =
                ScriptableObject.CreateInstance<OntologyAnimationDatabase>();
            var clip = new AnimationClip();
            manifest.ReplaceEntries(new[]
            {
                new OntologyAnimationContentEntry
                {
                    animationId = "Anim_Test",
                    clip = clip,
                    intents = new[] { "Idle" }
                }
            });
            database.ReplaceDefinitions(
                manifest.Entries.Select(value => value.ToRuntimeDefinition()));
            Assert.That(database.Definitions.Count, Is.EqualTo(1));

            manifest.ReplaceEntries(
                System.Array.Empty<OntologyAnimationContentEntry>());
            database.ReplaceDefinitions(
                manifest.Entries.Select(value => value.ToRuntimeDefinition()));

            Assert.That(database.Definitions, Is.Empty);
            Object.DestroyImmediate(clip);
            Object.DestroyImmediate(database);
            Object.DestroyImmediate(manifest);
        }

        [Test]
        public void WizardManifestRegistrationSynchronizesDatabaseAndProfile()
        {
            var manifest =
                ScriptableObject.CreateInstance<OntologyAnimationContentManifest>();
            var database =
                ScriptableObject.CreateInstance<OntologyAnimationDatabase>();
            var profile =
                ScriptableObject.CreateInstance<OntologyActorProfile>();
            var clip = new AnimationClip();
            profile.actorType = "Player";
            profile.rigType = "Humanoid";

            var entry = OntologyAnimationContentPipeline.CreateManifestEntry(
                "Anim_Wizard_Attack",
                clip,
                OntologyAnimationIntentIds.AttackLight,
                80,
                false,
                true,
                true,
                profile);

            Assert.That(
                OntologyAnimationContentPipeline.TryAddManifestEntry(
                    manifest,
                    entry,
                    out var error),
                Is.True,
                error);
            OntologyAnimationContentPipeline.SynchronizeRuntimeData(
                manifest,
                database,
                new[] { profile });

            var definition = database.Definitions.Single();
            Assert.That(definition.animationId, Is.EqualTo("Anim_Wizard_Attack"));
            Assert.That(
                definition.intents,
                Is.EqualTo(new[] { OntologyAnimationIntentIds.AttackLight }));
            Assert.That(definition.properties, Is.Empty);
            Assert.That(definition.actorTypes, Is.EqualTo(new[] { "Player" }));
            Assert.That(definition.rigTypes, Is.EqualTo(new[] { "Humanoid" }));
            Assert.That(profile.HasAnimation("Anim_Wizard_Attack"), Is.True);

            manifest.ReplaceEntries(
                System.Array.Empty<OntologyAnimationContentEntry>());
            OntologyAnimationContentPipeline.SynchronizeRuntimeData(
                manifest,
                database,
                new[] { profile });

            Assert.That(database.Definitions, Is.Empty);
            Assert.That(profile.HasAnimation("Anim_Wizard_Attack"), Is.False);
            Object.DestroyImmediate(clip);
            Object.DestroyImmediate(profile);
            Object.DestroyImmediate(database);
            Object.DestroyImmediate(manifest);
        }

        [Test]
        public void CurrentAnimationAssetsMatchManifestProjection()
        {
            var manifest =
                AssetDatabase.LoadAssetAtPath<OntologyAnimationContentManifest>(
                    OntologyAnimationContentPipeline.ManifestPath);
            var database =
                AssetDatabase.LoadAssetAtPath<OntologyAnimationDatabase>(
                    OntologyAnimationContentPipeline.DatabasePath);
            var profiles = new[]
            {
                AssetDatabase.LoadAssetAtPath<OntologyActorProfile>(
                    OntologyAnimationContentPipeline.PlayerProfilePath),
                AssetDatabase.LoadAssetAtPath<OntologyActorProfile>(
                    OntologyAnimationContentPipeline.VillagerProfilePath),
                AssetDatabase.LoadAssetAtPath<OntologyActorProfile>(
                    OntologyAnimationContentPipeline.MonsterProfilePath)
            }.Where(value => value != null).ToArray();

            Assert.That(manifest, Is.Not.Null);
            Assert.That(database, Is.Not.Null);
            Assert.That(
                database.Definitions.Count,
                Is.EqualTo(manifest.Entries.Count));

            foreach (var entry in manifest.Entries)
            {
                var definition = database.Definitions.Single(value =>
                    value.animationId == entry.animationId);
                Assert.That(definition.clip, Is.SameAs(entry.clip), entry.animationId);
                Assert.That(
                    definition.legacyAnimationIds,
                    Is.EqualTo(entry.legacyAnimationIds),
                    entry.animationId);
                Assert.That(definition.intents, Is.EqualTo(entry.intents), entry.animationId);
                Assert.That(definition.actorTypes, Is.EqualTo(entry.actorTypes), entry.animationId);
                Assert.That(definition.rigTypes, Is.EqualTo(entry.rigTypes), entry.animationId);
                Assert.That(definition.layer, Is.EqualTo(entry.layer), entry.animationId);
                Assert.That(definition.avatarMask, Is.EqualTo(entry.avatarMask), entry.animationId);
                Assert.That(definition.loop, Is.EqualTo(entry.loop), entry.animationId);
                Assert.That(definition.interruptible, Is.EqualTo(entry.interruptible), entry.animationId);
                Assert.That(definition.priority, Is.EqualTo(entry.priority), entry.animationId);
                Assert.That(definition.canBlend, Is.EqualTo(entry.canBlend), entry.animationId);
                Assert.That(
                    definition.transitionDuration,
                    Is.EqualTo(entry.transitionDuration).Within(0.0001f),
                    entry.animationId);
                Assert.That(
                    definition.hasContactWindow,
                    Is.EqualTo(entry.hasContactWindow),
                    entry.animationId);
                Assert.That(
                    definition.contactWindowStartNormalized,
                    Is.EqualTo(entry.contactWindowStartNormalized)
                        .Within(0.0001f),
                    entry.animationId);
                Assert.That(
                    definition.contactWindowEndNormalized,
                    Is.EqualTo(entry.contactWindowEndNormalized)
                        .Within(0.0001f),
                    entry.animationId);
                Assert.That(
                    definition.rootMotionMode,
                    Is.EqualTo(entry.rootMotionMode),
                    entry.animationId);
                Assert.That(
                    definition.presentationOwner,
                    Is.EqualTo(entry.presentationOwner),
                    entry.animationId);
                Assert.That(definition.properties, Is.EqualTo(entry.properties), entry.animationId);
                foreach (var profile in entry.profiles ?? System.Array.Empty<OntologyActorProfile>())
                    Assert.That(profile.HasAnimation(entry.animationId), Is.True, entry.animationId);
            }

            Assert.That(
                database.FindById("Anim_Fall")?.animationId,
                Is.EqualTo("Anim_Collapse_Reaction"),
                "A legacy durable id must resolve to the new canonical " +
                "collapse-reaction definition.");

            foreach (var profile in profiles)
            {
                foreach (var animationId in profile.animationIds)
                {
                    var entry = manifest.Entries.Single(value =>
                        value.animationId == animationId);
                    Assert.That(entry.profiles, Does.Contain(profile), animationId);
                }
            }

            foreach (var animationId in new[]
                     {
                         "Anim_Sword_Equip",
                         "Anim_Sword_Unequip",
                         "Anim_Sword_Idle",
                         "Anim_Sword_Walk",
                         "Anim_Sword_LightAttack",
                         "Anim_Sword_HitRecovery"
                     })
            {
                Assert.That(
                    manifest.Entries.Any(value =>
                        value.animationId == animationId),
                    Is.True,
                    animationId);
            }

            var settings =
                AssetDatabase.LoadAssetAtPath<OntologyWorldAuthoritySettings>(
                    "Assets/Data/Ontology/Networking/WorldAuthoritySettings.asset");
            Assert.That(settings, Is.Not.Null);
            Assert.That(settings.animationContentManifest, Is.SameAs(manifest));
            Assert.That(
                OntologyAuthorityAnimationPackageValidator.TryValidateConfiguration(
                    settings.developmentActions,
                    settings.developmentRules,
                    settings.developmentRuleDatabase,
                    manifest,
                    out var error),
                Is.True,
                error);
        }

        [Test]
        public void AuthorityActionIntentMustExistInManifest()
        {
            var manifest =
                ScriptableObject.CreateInstance<OntologyAnimationContentManifest>();
            var clip = new AnimationClip();
            manifest.ReplaceEntries(new[]
            {
                new OntologyAnimationContentEntry
                {
                    animationId = "Anim_Attack",
                    clip = clip,
                    intents = new[] { OntologyAnimationIntentIds.AttackLight }
                }
            });
            var valid = new[]
            {
                Action(
                    "attack",
                    OntologyAnimationIntentIds.AttackLight)
            };
            var invalid = new[]
            {
                Action("attack", "UnknownAttack")
            };

            Assert.That(
                OntologyAuthorityAnimationPackageValidator
                    .TryValidateConfiguration(valid, manifest, out _),
                Is.True);
            Assert.That(
                OntologyAuthorityAnimationPackageValidator
                    .TryValidateConfiguration(invalid, manifest, out var error),
                Is.False);
            Assert.That(error, Does.Contain("UnknownAttack"));
            Object.DestroyImmediate(clip);
            Object.DestroyImmediate(manifest);
        }

        [Test]
        public void ProjectionMustConfirmExactPublishedIntentVersion()
        {
            var expected = Action(
                "attack",
                OntologyAnimationIntentIds.AttackLight);
            expected.definitionVersion = 5;
            var projection = new OntologyAuthorityWorldProjection
            {
                actions = new[]
                {
                    new OntologyAuthorityActionDefinitionProjection
                    {
                        packageId = "package_user",
                        packageVersion = "2.0.0",
                        actionId = "attack",
                        definitionVersion = 5,
                        actorAnimationIntent =
                            OntologyAnimationIntentIds.AttackLight
                    }
                }
            };

            Assert.That(
                OntologyAuthorityAnimationPackageValidator.TryValidateProjection(
                    projection,
                    "package_user",
                    "2.0.0",
                    new[] { expected },
                    out _),
                Is.True);
            projection.actions[0].actorAnimationIntent = string.Empty;
            Assert.That(
                OntologyAuthorityAnimationPackageValidator.TryValidateProjection(
                    projection,
                    "package_user",
                    "2.0.0",
                    new[] { expected },
                    out var error),
                Is.False);
            Assert.That(error, Does.Contain("intent mismatch"));
        }

        [Test]
        public void UgcCatalogUpdatesOneStableSubmissionInsteadOfDuplicatingIt()
        {
            var catalog =
                ScriptableObject
                    .CreateInstance<OntologyAnimationUgcSubmissionCatalog>();
            catalog.Upsert(new OntologyAnimationUgcSubmission
            {
                submissionId = "submission_1",
                status = OntologyAnimationUgcSubmissionStatus.Staged
            });
            catalog.Upsert(new OntologyAnimationUgcSubmission
            {
                submissionId = "submission_1",
                status = OntologyAnimationUgcSubmissionStatus.Validated
            });

            Assert.That(catalog.Submissions.Count, Is.EqualTo(1));
            Assert.That(
                catalog.Submissions[0].status,
                Is.EqualTo(
                    OntologyAnimationUgcSubmissionStatus.Validated));
            Object.DestroyImmediate(catalog);
        }

        private static OntologyAnimationStateSnapshot Snapshot(
            bool moving,
            bool running,
            bool grounded,
            float verticalVelocity,
            uint jumpOccurrence = 0,
            string presentationIntent = null,
            bool presentationCompleted = false)
        {
            return new OntologyAnimationStateSnapshot(
                moving,
                running,
                grounded,
                verticalVelocity,
                string.Empty,
                string.Empty,
                jumpOccurrence: jumpOccurrence,
                currentPresentationIntent: presentationIntent,
                currentPresentationCompleted:
                    presentationCompleted);
        }

        private static OntologyAuthorityDevelopmentAction Action(
            string actionId,
            string intent)
        {
            return new OntologyAuthorityDevelopmentAction
            {
                actionId = actionId,
                definitionVersion = 1,
                structuredDefinitionJson =
                    "{\"presentation\":{\"actorAnimationIntent\":\"" +
                    intent + "\"}}"
            };
        }
    }
}
