using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    public static class OntologyAnimationContentPipeline
    {
        public const string ManifestPath =
            "Assets/Data/Ontology/AnimationContentManifest.asset";
        public const string DatabasePath =
            "Assets/Data/Ontology/AnimationDatabase.asset";
        public const string PlayerProfilePath =
            "Assets/Data/Ontology/Actors/PlayerProfile.asset";
        public const string VillagerProfilePath =
            "Assets/Data/Ontology/Actors/VillagerProfile.asset";
        public const string MonsterProfilePath =
            "Assets/Data/Ontology/Actors/MonsterProfile.asset";

        [MenuItem("Tools/Ontology/Animation Content/Create or Migrate Manifest")]
        public static void CreateOrMigrateManifestMenu()
        {
            var manifest = CreateOrMigrateManifest();
            Selection.activeObject = manifest;
            EditorGUIUtility.PingObject(manifest);
        }

        [MenuItem("Tools/Ontology/Animation Content/Validate and Synchronize")]
        public static void ValidateAndSynchronizeMenu()
        {
            var manifest =
                AssetDatabase.LoadAssetAtPath<OntologyAnimationContentManifest>(
                    ManifestPath);
            if (!ValidateAndSynchronize(manifest, true))
                throw new InvalidOperationException(
                    "Animation content validation failed. See the Unity Console.");
        }

        public static OntologyAnimationContentManifest CreateOrMigrateManifest()
        {
            EnsureFolder("Assets/Data/Ontology");
            var manifest =
                AssetDatabase.LoadAssetAtPath<OntologyAnimationContentManifest>(
                    ManifestPath);
            if (manifest == null)
            {
                manifest =
                    ScriptableObject.CreateInstance<OntologyAnimationContentManifest>();
                AssetDatabase.CreateAsset(manifest, ManifestPath);
            }

            // After the first migration the manifest is the authoring source.
            // Rebuilding it from the runtime database would resurrect a removed
            // animation and violate the "remove the rule, remove the behavior"
            // contract.
            if (manifest.Entries.Count > 0)
            {
                EnsureMonsterProfile();
                foreach (var entry in manifest.Entries)
                {
                    if (entry == null) continue;
                    NormalizeSerializedDefaults(entry);
                    if (entry.clip == null) continue;
                    var assetPath = AssetDatabase.GetAssetPath(entry.clip);
                    entry.sourceAssetPath = assetPath;
                    entry.checksum = GetContentHash(assetPath);
                }

                EditorUtility.SetDirty(manifest);
                AssetDatabase.SaveAssets();
                return manifest;
            }

            var database =
                AssetDatabase.LoadAssetAtPath<OntologyAnimationDatabase>(
                    DatabasePath);
            if (database == null)
                throw new InvalidOperationException("AnimationDatabase is missing.");

            var player =
                AssetDatabase.LoadAssetAtPath<OntologyActorProfile>(
                    PlayerProfilePath);
            var villager =
                AssetDatabase.LoadAssetAtPath<OntologyActorProfile>(
                    VillagerProfilePath);
            var monster = EnsureMonsterProfile();
            var previous = manifest.Entries
                .Where(value => value != null &&
                                !string.IsNullOrWhiteSpace(value.animationId))
                .ToDictionary(value => value.animationId, StringComparer.Ordinal);
            var entries = new List<OntologyAnimationContentEntry>();
            foreach (var definition in database.Definitions)
            {
                if (definition == null ||
                    string.IsNullOrWhiteSpace(definition.animationId))
                    continue;

                previous.TryGetValue(definition.animationId, out var existing);
                var assetPath = definition.clip == null
                    ? string.Empty
                    : AssetDatabase.GetAssetPath(definition.clip);
                var profiles = new List<OntologyActorProfile>();
                if (ProfileHasAnimation(player, definition))
                    profiles.Add(player);
                if (ProfileHasAnimation(villager, definition))
                    profiles.Add(villager);
                if (monster != null &&
                    (ProfileHasAnimation(monster, definition) ||
                     Contains(definition.actorTypes, "Monster")))
                    profiles.Add(monster);
                var entry = new OntologyAnimationContentEntry
                {
                    animationId = definition.animationId,
                    legacyAnimationIds =
                        existing?.legacyAnimationIds != null
                            ? Copy(existing.legacyAnimationIds)
                            : Copy(definition.legacyAnimationIds),
                    clip = definition.clip,
                    sourceAssetPath = assetPath,
                    deliveryKey = existing?.deliveryKey ??
                                  "animations/" + definition.animationId,
                    intents = existing?.intents != null &&
                              existing.intents.Length > 0
                        ? Copy(existing.intents)
                        : Copy(definition.intents),
                    actorTypes = Copy(definition.actorTypes),
                    rigTypes = Copy(definition.rigTypes),
                    profiles = profiles.ToArray(),
                    layer = definition.layer,
                    avatarMask = definition.avatarMask,
                    loop = existing?.loop ?? definition.loop,
                    loopPose = existing?.loopPose ?? false,
                    rootMotionMode = definition.rootMotionMode,
                    presentationOwner =
                        existing?.presentationOwner ??
                        definition.presentationOwner,
                    interruptible = definition.interruptible,
                    priority = definition.priority,
                    canBlend = definition.canBlend,
                    transitionDuration = definition.transitionDuration <= 0f
                        ? 0.25f
                        : definition.transitionDuration,
                    hasContactWindow =
                        existing?.hasContactWindow ??
                        definition.hasContactWindow,
                    contactWindowStartNormalized =
                        existing?.contactWindowStartNormalized ??
                        definition.contactWindowStartNormalized,
                    contactWindowEndNormalized =
                        existing?.contactWindowEndNormalized ??
                        definition.contactWindowEndNormalized,
                    playbackStartNormalized =
                        existing?.playbackStartNormalized ??
                        definition.playbackStartNormalized,
                    playbackEndNormalized =
                        existing?.playbackEndNormalized ??
                        definition.playbackEndNormalized,
                    properties = Copy(definition.properties),
                    contentVersion = existing?.contentVersion ?? "1.0.0",
                    checksum = GetContentHash(assetPath)
                };
                NormalizeSerializedDefaults(entry);
                entries.Add(entry);
            }

            manifest.ReplaceEntries(entries);
            EditorUtility.SetDirty(manifest);
            AssetDatabase.SaveAssets();
            return manifest;
        }

        private static OntologyActorProfile EnsureMonsterProfile()
        {
            var profile =
                AssetDatabase.LoadAssetAtPath<OntologyActorProfile>(
                    MonsterProfilePath);
            if (profile != null) return profile;
            profile = ScriptableObject.CreateInstance<OntologyActorProfile>();
            profile.actorType = "Monster";
            profile.rigType = "Generic";
            profile.defaultConcepts = new[] { "Actor", "Monster" };
            profile.idleAnimationIntent = "MonsterIdle";
            profile.moveAnimationIntent = "MonsterWalk";
            profile.fastMoveAnimationIntent = "MonsterRun";
            AssetDatabase.CreateAsset(profile, MonsterProfilePath);
            return profile;
        }

        public static bool ValidateAndSynchronize(
            OntologyAnimationContentManifest manifest,
            bool configureImporters)
        {
            var issues = OntologyAnimationContentValidator.Validate(manifest);
            var errors = 0;
            foreach (var issue in issues)
            {
                var message = string.IsNullOrWhiteSpace(issue.AnimationId)
                    ? issue.Code + ": " + issue.Message
                    : issue.AnimationId + " - " + issue.Code + ": " +
                      issue.Message;
                if (issue.Severity == OntologyAnimationContentIssueSeverity.Error)
                {
                    Debug.LogError(message, manifest);
                    errors++;
                }
                else if (issue.Severity ==
                         OntologyAnimationContentIssueSeverity.Warning)
                {
                    Debug.LogWarning(message, manifest);
                }
                else
                {
                    Debug.Log(message, manifest);
                }
            }
            if (errors > 0) return false;

            if (configureImporters)
            {
                foreach (var entry in manifest.Entries)
                    ConfigureImporter(entry);
            }

            var database =
                AssetDatabase.LoadAssetAtPath<OntologyAnimationDatabase>(
                    DatabasePath);
            if (database == null)
            {
                Debug.LogError("AnimationDatabase is missing.", manifest);
                return false;
            }

            var profiles = CollectProfiles(manifest);
            SynchronizeRuntimeData(manifest, database, profiles);
            EditorUtility.SetDirty(database);
            foreach (var profile in profiles)
                if (profile != null) EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            Debug.Log(
                "Animation content synchronized: " +
                manifest.Entries.Count + " entries.",
                manifest);
            return true;
        }

        public static OntologyAnimationContentEntry CreateManifestEntry(
            string animationId,
            AnimationClip clip,
            string intent,
            int priority,
            bool loop,
            bool interruptible,
            bool canBlend,
            OntologyActorProfile profile)
        {
            var sourcePath = clip == null
                ? string.Empty
                : AssetDatabase.GetAssetPath(clip);
            return new OntologyAnimationContentEntry
            {
                animationId = animationId == null
                    ? string.Empty
                    : animationId.Trim(),
                legacyAnimationIds = Array.Empty<string>(),
                clip = clip,
                sourceAssetPath = sourcePath,
                deliveryKey = string.IsNullOrWhiteSpace(animationId)
                    ? string.Empty
                    : "animations/" + animationId.Trim(),
                intents = string.IsNullOrWhiteSpace(intent)
                    ? Array.Empty<string>()
                    : new[] { intent.Trim() },
                actorTypes = profile == null ||
                             string.IsNullOrWhiteSpace(profile.actorType)
                    ? Array.Empty<string>()
                    : new[] { profile.actorType.Trim() },
                rigTypes = profile == null ||
                           string.IsNullOrWhiteSpace(profile.rigType)
                    ? Array.Empty<string>()
                    : new[] { profile.rigType.Trim() },
                profiles = profile == null
                    ? Array.Empty<OntologyActorProfile>()
                    : new[] { profile },
                layer = OntologyAnimationLayer.FullBody,
                avatarMask = null,
                loop = loop,
                loopPose = false,
                rootMotionMode = OntologyAnimationRootMotionMode.Inherit,
                presentationOwner =
                    OntologyAnimationPresentationOwner.AuthorityIntent,
                interruptible = interruptible,
                priority = priority,
                canBlend = canBlend,
                transitionDuration = 0.25f,
                hasContactWindow = false,
                contactWindowStartNormalized = 0f,
                contactWindowEndNormalized = 1f,
                playbackStartNormalized = 0f,
                playbackEndNormalized = 1f,
                properties = Array.Empty<string>(),
                contentVersion = "1.0.0",
                checksum = GetContentHash(sourcePath)
            };
        }

        public static bool TryAddManifestEntry(
            OntologyAnimationContentManifest manifest,
            OntologyAnimationContentEntry entry,
            out string error)
        {
            error = string.Empty;
            if (manifest == null)
            {
                error = "Animation content manifest is missing.";
                return false;
            }
            if (entry == null)
            {
                error = "Animation content entry is missing.";
                return false;
            }

            var id = entry.animationId == null
                ? string.Empty
                : entry.animationId.Trim();
            if (manifest.Entries.Any(value =>
                    value != null &&
                    string.Equals(
                        value.animationId,
                        id,
                        StringComparison.Ordinal)))
            {
                error = "Animation id already exists: " + id;
                return false;
            }

            var next = manifest.Entries
                .Where(value => value != null)
                .ToList();
            next.Add(entry);
            var candidate =
                ScriptableObject.CreateInstance<OntologyAnimationContentManifest>();
            candidate.SetContentVersion(manifest.ContentVersion);
            candidate.ReplaceEntries(next);
            var issues = OntologyAnimationContentValidator.Validate(candidate);
            UnityEngine.Object.DestroyImmediate(candidate);
            var failures = issues
                .Where(value =>
                    value.Severity ==
                    OntologyAnimationContentIssueSeverity.Error)
                .Select(value => value.Code + ": " + value.Message)
                .ToArray();
            if (failures.Length > 0)
            {
                error = string.Join(Environment.NewLine, failures);
                return false;
            }

            manifest.ReplaceEntries(next);
            return true;
        }

        public static void SynchronizeRuntimeData(
            OntologyAnimationContentManifest manifest,
            OntologyAnimationDatabase database,
            IEnumerable<OntologyActorProfile> profiles)
        {
            if (manifest == null || database == null) return;
            database.ReplaceDefinitions(
                manifest.Entries
                    .Where(value => value != null)
                    .Select(value => value.ToRuntimeDefinition()));

            if (profiles == null) return;
            foreach (var profile in profiles
                         .Where(value => value != null)
                         .Distinct())
            {
                profile.animationIds = manifest.Entries
                    .Where(entry =>
                        entry != null &&
                        entry.profiles != null &&
                        entry.profiles.Contains(profile) &&
                        !string.IsNullOrWhiteSpace(entry.animationId))
                    .Select(entry => entry.animationId.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray();
            }
        }

        private static HashSet<OntologyActorProfile> CollectProfiles(
            OntologyAnimationContentManifest manifest)
        {
            var profiles = new HashSet<OntologyActorProfile>();
            AddKnownProfile(profiles, PlayerProfilePath);
            AddKnownProfile(profiles, VillagerProfilePath);
            AddKnownProfile(profiles, MonsterProfilePath);
            foreach (var entry in manifest.Entries)
            {
                if (entry?.profiles == null) continue;
                foreach (var profile in entry.profiles)
                    if (profile != null) profiles.Add(profile);
            }
            return profiles;
        }

        private static void AddKnownProfile(
            ISet<OntologyActorProfile> profiles,
            string assetPath)
        {
            var profile =
                AssetDatabase.LoadAssetAtPath<OntologyActorProfile>(assetPath);
            if (profile != null) profiles.Add(profile);
        }

        private static void ConfigureImporter(
            OntologyAnimationContentEntry entry)
        {
            if (entry == null || entry.clip == null) return;
            var path = AssetDatabase.GetAssetPath(entry.clip);
            if (!IsProjectOwnedAnimationPath(path)) return;
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) return;

            var changed = false;
            var humanoid = Contains(entry.rigTypes, "Humanoid");
            var generic = Contains(entry.rigTypes, "Generic");
            var animationType = humanoid
                ? ModelImporterAnimationType.Human
                : generic
                    ? ModelImporterAnimationType.Generic
                    : importer.animationType;
            if (importer.animationType != animationType)
            {
                importer.animationType = animationType;
                changed = true;
            }
            if (!importer.importAnimation)
            {
                importer.importAnimation = true;
                changed = true;
            }

            var clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0)
                clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                if (!string.Equals(
                        clip.name,
                        entry.clip.name,
                        StringComparison.Ordinal) &&
                    clips.Length > 1)
                    continue;
                if (clip.loopTime != entry.loop)
                {
                    clip.loopTime = entry.loop;
                    changed = true;
                }
                if (clip.loopPose != entry.loopPose)
                {
                    clip.loopPose = entry.loopPose;
                    changed = true;
                }
            }
            if (changed)
            {
                importer.clipAnimations = clips;
                try
                {
                    importer.SaveAndReimport();
                }
                catch (Exception exception)
                {
                    Debug.LogError(
                        "Animation importer update failed for " + path +
                        ": " + exception.Message,
                        entry.clip);
                    throw;
                }
            }
        }

        private static bool IsProjectOwnedAnimationPath(string path)
        {
            return !string.IsNullOrWhiteSpace(path) &&
                   (path.StartsWith(
                        "Assets/Animations/",
                        StringComparison.Ordinal) ||
                    path.StartsWith(
                        "Assets/UGC/Approved/Animations/",
                        StringComparison.Ordinal));
        }

        private static void NormalizeSerializedDefaults(
            OntologyAnimationContentEntry entry)
        {
            if (entry.playbackEndNormalized <= 0f &&
                entry.playbackStartNormalized <= 0f)
            {
                entry.playbackEndNormalized = 1f;
            }
        }

        private static string GetContentHash(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : AssetDatabase.GetAssetDependencyHash(path).ToString();
        }

        private static bool Contains(string[] values, string expected)
        {
            if (values == null) return false;
            return values.Any(value =>
                string.Equals(value, expected, StringComparison.Ordinal));
        }

        private static bool ProfileHasAnimation(
            OntologyActorProfile profile,
            OntologyAnimationDefinition definition)
        {
            if (profile == null || definition == null) return false;
            if (profile.HasAnimation(definition.animationId)) return true;
            if (definition.legacyAnimationIds == null) return false;
            foreach (var legacyId in definition.legacyAnimationIds)
            {
                if (!string.IsNullOrWhiteSpace(legacyId) &&
                    profile.HasAnimation(legacyId.Trim()))
                {
                    return true;
                }
            }

            return false;
        }

        private static string[] Copy(string[] source)
        {
            if (source == null || source.Length == 0) return Array.Empty<string>();
            var result = new string[source.Length];
            Array.Copy(source, result, source.Length);
            return result;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var slash = path.LastIndexOf('/');
            if (slash <= 0) return;
            var parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
    }
}
