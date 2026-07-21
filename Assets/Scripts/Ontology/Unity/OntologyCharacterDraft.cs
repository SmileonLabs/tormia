using UnityEngine;

namespace Tormia.Ontology.Core
{
    public enum OntologyCharacterCreationStage
    {
        Brief,
        ConceptReview,
        ConceptApproved,
        ModelGeneration,
        ModelReview,
        UnitySetup
    }

    [CreateAssetMenu(menuName = "Tormia/Ontology/Character Draft")]
    public sealed class OntologyCharacterDraft : ScriptableObject
    {
        public string characterId = "NewCharacter";

        [TextArea(4, 12)]
        public string userPrompt;

        [TextArea(4, 12)]
        public string designBrief;

        public OntologyCharacterCreationStage stage = OntologyCharacterCreationStage.Brief;

        // Candidate image is reviewed before it is copied to approvedConceptImage.
        public Texture2D conceptImage;

        // Populated by concept approval and used as Meshy's image-to-3D input.
        public Texture2D approvedConceptImage;

        [TextArea(2, 8)]
        public string conceptFeedback;

        public string meshyTaskId;
        public string meshyStatus;
        public int meshyProgress;
        public string meshyGlbUrl;
        public string meshyFbxUrl;
        public string meshyThumbnailUrl;

        [TextArea(2, 8)]
        public string meshyError;

        public string importedModelAssetPath;
        public bool hasHumanoidRig;

        [TextArea(2, 8)]
        public string rigValidationMessage;

        public GameObject generatedModelPrefab;
        public OntologyActorProfile actorProfile;
    }
}
