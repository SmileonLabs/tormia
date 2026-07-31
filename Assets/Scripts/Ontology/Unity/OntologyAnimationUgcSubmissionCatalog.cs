using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    [CreateAssetMenu(menuName = "Tormia/Ontology/Animation UGC Submission Catalog")]
    public sealed class OntologyAnimationUgcSubmissionCatalog : ScriptableObject
    {
        [SerializeField] private List<OntologyAnimationUgcSubmission> submissions =
            new();

        public IReadOnlyList<OntologyAnimationUgcSubmission> Submissions =>
            submissions;

        public void Upsert(OntologyAnimationUgcSubmission value)
        {
            if (value == null || string.IsNullOrWhiteSpace(value.submissionId))
                return;
            var index = submissions.FindIndex(candidate =>
                candidate != null &&
                string.Equals(
                    candidate.submissionId,
                    value.submissionId,
                    StringComparison.Ordinal));
            if (index >= 0) submissions[index] = value;
            else submissions.Add(value);
        }
    }

    [Serializable]
    public sealed class OntologyAnimationUgcSubmission
    {
        public string submissionId;
        public string uploaderAccountId;
        public string originalFileName;
        public string stagedAssetPath;
        public string approvedAssetPath;
        public string animationId;
        public string intent;
        public string actorType = "Player";
        public string rigType = "Humanoid";
        public OntologyActorProfile targetProfile;
        public string licenseId;
        public string attribution;
        public OntologyAnimationUgcSubmissionStatus status;
        public string validationMessage;
        public string checksum;
        public string contentVersion = "1.0.0";
    }

    public enum OntologyAnimationUgcSubmissionStatus
    {
        Draft = 0,
        Staged = 1,
        Validated = 2,
        Approved = 3,
        Rejected = 4,
        Published = 5
    }
}
