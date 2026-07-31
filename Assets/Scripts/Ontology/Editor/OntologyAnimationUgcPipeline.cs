using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    public static class OntologyAnimationUgcPipeline
    {
        public const string CatalogPath =
            "Assets/Data/Ontology/AnimationUgcSubmissionCatalog.asset";
        public const string StagingFolder =
            "Assets/UGC/Staging/Animations";
        public const string ApprovedFolder =
            "Assets/UGC/Approved/Animations";

        public static OntologyAnimationUgcSubmissionCatalog EnsureCatalog()
        {
            EnsureFolder("Assets/Data/Ontology");
            var catalog =
                AssetDatabase.LoadAssetAtPath<OntologyAnimationUgcSubmissionCatalog>(
                    CatalogPath);
            if (catalog != null) return catalog;
            catalog =
                ScriptableObject.CreateInstance<OntologyAnimationUgcSubmissionCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
            AssetDatabase.SaveAssets();
            return catalog;
        }

        public static OntologyAnimationUgcSubmission Stage(
            string sourceFile,
            string uploaderAccountId,
            string animationId,
            string intent,
            string actorType,
            string rigType,
            OntologyActorProfile targetProfile,
            string licenseId,
            string attribution)
        {
            if (string.IsNullOrWhiteSpace(sourceFile) ||
                !File.Exists(sourceFile))
                throw new FileNotFoundException("Animation upload source is missing.");
            var extension = Path.GetExtension(sourceFile);
            if (!string.Equals(extension, ".fbx", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "The first UGC slice accepts FBX Binary files only.");
            if (new FileInfo(sourceFile).Length > 100L * 1024L * 1024L)
                throw new InvalidOperationException(
                    "Animation upload exceeds the 100 MB development limit.");
            if (string.IsNullOrWhiteSpace(licenseId))
                throw new InvalidOperationException(
                    "A license identifier is required before staging.");

            EnsureFolder(StagingFolder);
            var id = Guid.NewGuid().ToString("N");
            var safeFileName = SanitizeFileName(Path.GetFileName(sourceFile));
            var destination = StagingFolder + "/" + id + "_" + safeFileName;
            File.Copy(sourceFile, ToAbsolutePath(destination), false);
            AssetDatabase.ImportAsset(
                destination,
                ImportAssetOptions.ForceSynchronousImport);

            var submission = new OntologyAnimationUgcSubmission
            {
                submissionId = id,
                uploaderAccountId = uploaderAccountId?.Trim() ?? string.Empty,
                originalFileName = Path.GetFileName(sourceFile),
                stagedAssetPath = destination,
                animationId = animationId?.Trim() ?? string.Empty,
                intent = intent?.Trim() ?? string.Empty,
                actorType = actorType?.Trim() ?? string.Empty,
                rigType = rigType?.Trim() ?? string.Empty,
                targetProfile = targetProfile,
                licenseId = licenseId.Trim(),
                attribution = attribution?.Trim() ?? string.Empty,
                status = OntologyAnimationUgcSubmissionStatus.Staged,
                checksum = AssetDatabase.GetAssetDependencyHash(destination)
                    .ToString()
            };
            Validate(submission);
            var catalog = EnsureCatalog();
            catalog.Upsert(submission);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            return submission;
        }

        public static bool Validate(OntologyAnimationUgcSubmission submission)
        {
            if (submission == null) return false;
            var importer =
                AssetImporter.GetAtPath(submission.stagedAssetPath) as ModelImporter;
            var clips = AssetDatabase
                .LoadAllAssetsAtPath(submission.stagedAssetPath)
                .OfType<AnimationClip>()
                .Where(value =>
                    !value.name.StartsWith("__preview__", StringComparison.Ordinal))
                .ToArray();
            if (importer == null || clips.Length == 0)
            {
                submission.status =
                    OntologyAnimationUgcSubmissionStatus.Rejected;
                submission.validationMessage =
                    "The staged FBX has no importable animation clip.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(submission.animationId) ||
                string.IsNullOrWhiteSpace(submission.intent) ||
                submission.targetProfile == null)
            {
                submission.status =
                    OntologyAnimationUgcSubmissionStatus.Rejected;
                submission.validationMessage =
                    "Canonical animationId, intent, and target profile are required.";
                return false;
            }

            submission.status = OntologyAnimationUgcSubmissionStatus.Validated;
            submission.validationMessage =
                clips.Length + " animation clip(s) validated for review.";
            return true;
        }

        public static bool ApproveAndPublish(
            OntologyAnimationUgcSubmission submission,
            OntologyAnimationContentManifest manifest)
        {
            if (submission == null || manifest == null ||
                submission.status !=
                OntologyAnimationUgcSubmissionStatus.Validated)
                return false;
            if (manifest.Entries.Any(value =>
                    value != null &&
                    string.Equals(
                        value.animationId,
                        submission.animationId,
                        StringComparison.Ordinal)))
            {
                submission.validationMessage =
                    "The canonical animationId is already published.";
                return false;
            }
            if (AssetDatabase.LoadMainAssetAtPath(
                    submission.stagedAssetPath) == null)
            {
                submission.validationMessage =
                    "The staged FBX is no longer available.";
                return false;
            }

            EnsureFolder(ApprovedFolder);
            var approvedPath = ApprovedFolder + "/" +
                               Path.GetFileName(submission.stagedAssetPath);
            var stagedPath = submission.stagedAssetPath;
            var moveError = AssetDatabase.MoveAsset(
                stagedPath,
                approvedPath);
            if (!string.IsNullOrWhiteSpace(moveError))
            {
                submission.validationMessage = moveError;
                return false;
            }

            submission.approvedAssetPath = approvedPath;
            submission.status = OntologyAnimationUgcSubmissionStatus.Approved;
            var clip = AssetDatabase
                .LoadAllAssetsAtPath(approvedPath)
                .OfType<AnimationClip>()
                .FirstOrDefault(value =>
                    !value.name.StartsWith("__preview__", StringComparison.Ordinal));
            if (clip == null)
            {
                submission.validationMessage =
                    "Approved FBX has no animation clip.";
                AssetDatabase.MoveAsset(approvedPath, stagedPath);
                submission.approvedAssetPath = string.Empty;
                submission.status =
                    OntologyAnimationUgcSubmissionStatus.Validated;
                return false;
            }

            var previousEntries = manifest.Entries
                .Where(value => value != null)
                .ToList();
            var entries = previousEntries.ToList();
            entries.Add(new OntologyAnimationContentEntry
            {
                animationId = submission.animationId,
                clip = clip,
                sourceAssetPath = approvedPath,
                deliveryKey = "ugc/" + submission.submissionId + "/" +
                              submission.animationId,
                intents = new[] { submission.intent },
                actorTypes = new[] { submission.actorType },
                rigTypes = new[] { submission.rigType },
                profiles = new[] { submission.targetProfile },
                playbackStartNormalized = 0f,
                playbackEndNormalized = 1f,
                contentVersion = submission.contentVersion,
                checksum = AssetDatabase.GetAssetDependencyHash(approvedPath)
                    .ToString()
            });
            manifest.ReplaceEntries(entries);
            EditorUtility.SetDirty(manifest);
            if (!OntologyAnimationContentPipeline.ValidateAndSynchronize(
                    manifest,
                    true))
            {
                manifest.ReplaceEntries(previousEntries);
                EditorUtility.SetDirty(manifest);
                AssetDatabase.MoveAsset(approvedPath, stagedPath);
                submission.approvedAssetPath = string.Empty;
                submission.status =
                    OntologyAnimationUgcSubmissionStatus.Validated;
                submission.validationMessage =
                    "Publishing failed validation; the FBX returned to quarantine.";
                AssetDatabase.SaveAssets();
                return false;
            }

            submission.status =
                OntologyAnimationUgcSubmissionStatus.Published;
            submission.validationMessage =
                "Published to the local development animation manifest.";
            var catalog = EnsureCatalog();
            catalog.Upsert(submission);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            return true;
        }

        private static string ToAbsolutePath(string assetPath)
        {
            return Path.Combine(
                Directory.GetParent(Application.dataPath)?.FullName ??
                Application.dataPath,
                assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string SanitizeFileName(string value)
        {
            foreach (var invalid in Path.GetInvalidFileNameChars())
                value = value.Replace(invalid, '_');
            return value;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var slash = path.LastIndexOf('/');
            var parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
    }
}
