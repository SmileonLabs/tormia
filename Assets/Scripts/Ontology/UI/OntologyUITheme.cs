using UnityEngine;

namespace Tormia.Ontology.Core
{
    [CreateAssetMenu(menuName = "Tormia/Ontology/UI Theme", fileName = "OntologyUITheme")]
    public sealed class OntologyUITheme : ScriptableObject
    {
        [Header("Panel")]
        public Color panelBackground = new Color(0.035f, 0.045f, 0.065f, 0.90f);
        public Color titleText = new Color(0.86f, 0.94f, 1f, 1f);
        public Color statusText = new Color(0.70f, 0.82f, 0.92f, 1f);
        public Color outputText = new Color(0.82f, 0.88f, 0.94f, 1f);
        public Color hudBackground = new Color(0.035f, 0.045f, 0.065f, 0.82f);
        public Color hudText = new Color(0.88f, 0.96f, 1f, 1f);
        public Color actorToastBackground = new Color(0.03f, 0.04f, 0.06f, 0.82f);
        public Color actorToastText = new Color(0.82f, 1f, 0.86f, 1f);
        public Color actorToastInfoText = new Color(0.82f, 0.94f, 1f, 1f);
        public Color actorToastPositiveText = new Color(0.72f, 1f, 0.72f, 1f);
        public Color actorToastWarningText = new Color(1f, 0.88f, 0.42f, 1f);
        public Color actorToastNegativeText = new Color(1f, 0.62f, 0.58f, 1f);

        [Header("Rows")]
        public Color rowEven = new Color(0.08f, 0.10f, 0.14f, 0.75f);
        public Color rowOdd = new Color(0.05f, 0.07f, 0.10f, 0.75f);
        public Color rowEquipped = new Color(0.10f, 0.24f, 0.16f, 0.88f);
        public Color rowConflict = new Color(0.36f, 0.16f, 0.10f, 0.90f);
        public Color rowConflictText = new Color(1f, 0.74f, 0.55f, 1f);
        public Color rowText = new Color(0.92f, 0.96f, 1f, 1f);
        public Color rowEquippedText = new Color(0.70f, 1f, 0.78f, 1f);

        [Header("Buttons")]
        public Color equipButton = new Color(0.16f, 0.56f, 0.34f, 0.95f);
        public Color unequipButton = new Color(0.58f, 0.22f, 0.22f, 0.95f);
        public Color fixedButton = new Color(0.18f, 0.24f, 0.34f, 0.95f);
        public Color actionButton = new Color(0.16f, 0.32f, 0.62f, 0.95f);
        public Color buttonHover = new Color(0.26f, 0.44f, 0.70f, 0.98f);
        public Color disabledButton = new Color(0.18f, 0.18f, 0.20f, 0.55f);
        public Color buttonText = Color.white;
        public Color disabledButtonText = new Color(0.56f, 0.58f, 0.62f, 1f);
        public Color questActionText = new Color(1f, 0.92f, 0.35f, 1f);

        [Header("Typography")]
        public float titleFontSize = 22f;
        public float statusFontSize = 14f;
        public float outputFontSize = 12f;
        public float hudFontSize = 13f;
        public float actorToastFontSize = 16f;
        public float rowFontSize = 14f;
        public float buttonFontSize = 12f;
        public float actionButtonFontSize = 15f;

        [Header("Layout")]
        public Vector2 panelSize = new Vector2(430f, 640f);
        public Vector2 panelAnchoredPosition = new Vector2(-32f, -32f);
        public Vector2 debugPanelSize = new Vector2(760f, 640f);
        public Vector2 debugPanelAnchoredPosition = new Vector2(32f, -32f);
        public Vector2 hudPanelSize = new Vector2(360f, 220f);
        public Vector2 hudPanelAnchoredPosition = new Vector2(24f, 24f);
        public Vector2 actorToastSize = new Vector2(280f, 42f);
        public Vector3 actorToastWorldOffset = new Vector3(0f, 2.35f, 0f);
        public Vector3 actorToastFloatOffset = new Vector3(0f, 0.35f, 0f);
        public float actorToastDuration = 1.35f;
        public float actorToastFadeDuration = 0.35f;
        public float actorToastQueueGap = 0.12f;
        public float actorToastWorldScale = 0.01f;
        public float actorToastReferenceDistance = 8f;
        public float actorToastMinScale = 0.0075f;
        public float actorToastMaxScale = 0.018f;
        public Vector2 titleAnchoredPosition = new Vector2(0f, -16f);
        public Vector2 statusAnchoredPosition = new Vector2(0f, -50f);
        public Vector2 rowsOffsetMin = new Vector2(16f, 18f);
        public Vector2 rowsOffsetMax = new Vector2(-16f, -86f);
        public Vector2 fixedButtonSize = new Vector2(140f, 32f);
        public float fixedButtonSpacing = 38f;
        public float rowHeight = 30f;
        public float rowSpacing = 34f;
        public Vector2 rowLabelOffsetMin = new Vector2(8f, 0f);
        public Vector2 rowLabelOffsetMax = new Vector2(-176f, 0f);
        public Vector2 buttonSize = new Vector2(76f, 24f);
        public Vector2 equipButtonPosition = new Vector2(-122f, 0f);
        public Vector2 unequipButtonPosition = new Vector2(-40f, 0f);
        public Vector2 detailPanelSize = new Vector2(398f, 68f);
        public float actionButtonHeight = 36f;
        public float actionButtonSpacing = 42f;
        public Vector2 actionButtonTextPadding = new Vector2(10f, 0f);
        public Vector2 scrollBarSize = new Vector2(10f, 0f);
    }
}
