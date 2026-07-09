using UnityEngine;

namespace Tormia.Ontology.Core
{
    public static class OntologyCharacterCustomizationUiConfig
    {
        public const string GameCanvasName = "OntologyGameCanvas";
        public const string PanelName = "OntologyCharacterCustomizationPanel";
        public const string HeaderName = "Header";
        public const string TitleTextName = "TitleText";
        public const string CloseButtonName = "CloseButton";
        public const string CategoryAreaName = "CategoryArea";
        public const string CategoryScrollViewName = "CategoryScrollView";
        public const string CategoryViewportName = "CategoryViewport";
        public const string CategoryContentName = "CategoryContent";
        public const string PartGridAreaName = "PartGridArea";
        public const string ScrollViewName = "ScrollView";
        public const string ViewportName = "Viewport";
        public const string PartGridContentName = "PartGridContent";
        public const string DetailAreaName = "DetailArea";
        public const string SelectedIconName = "SelectedIcon";
        public const string SelectedTitleName = "SelectedTitle";
        public const string SelectedDescriptionName = "SelectedDescription";
        public const string FactPreviewName = "FactPreview";
        public const string EquipButtonName = "EquipButton";
        public const string UnequipButtonName = "UnequipButton";
        public const string StatusName = "Status";
        public const string ToggleHintName = "ToggleHint";
        public const string TemplatesName = "Templates";
        public const string CategoryButtonTemplateName = "CategoryButton_Template";
        public const string PartCardTemplateName = "PartCard_Template";
        public const string IconName = "Icon";
        public const string NoIconName = "NoIcon";
        public const string BadgeName = "Badge";
        public const string LabelName = "Label";
        public const string StateName = "State";

        public const string Title = "Character Parts";
        public const string CloseLabel = "X";
        public const string ToggleHint = "Press C to customize";
        public const string SelectPartTitle = "Select a part";
        public const string SelectPartDescription = "Choose a category and part to preview ontology effects.";
        public const string NoIconLabel = "NO ICON";
        public const string CategoryTemplateLabel = "Category";
        public const string PartTemplateLabel = "Part";
        public const string SlotTemplateLabel = "Slot";
        public const string EquipLabel = "Equip";
        public const string UnequipLabel = "Unequip";
        public const string EquippedLabel = "Equipped";
        public const string FactsHeader = "Ontology Facts";
        public const string OnBadge = "ON";
        public const string CapabilityBadge = "CAP";
        public const string ConflictBadge = "CONFLICT";

        public const string SlotBody = "Body";
        public const string SlotFace = "Face";
        public const string SlotHair = "Hair";
        public const string SlotUpperBody = "UpperBody";
        public const string SlotLowerBody = "LowerBody";
        public const string SlotFootwear = "Footwear";
        public const string SlotOuterwear = "Outerwear";
        public const string SlotFullBody = "FullBody";
        public const string SlotHeadwear = "Headwear";
        public const string SlotEyewear = "Eyewear";
        public const string SlotHandwear = "Handwear";
        public const string SlotFacialHair = "FacialHair";
        public const string SlotAccessory = "Accessory";

        public static readonly string[] CategoryOrder =
        {
            SlotBody,
            SlotFace,
            SlotHair,
            SlotUpperBody,
            SlotLowerBody,
            SlotFootwear,
            SlotOuterwear,
            SlotFullBody,
            SlotHeadwear,
            SlotEyewear,
            SlotHandwear,
            SlotFacialHair,
            SlotAccessory
        };

        public static readonly Color PanelColor = new(0.08f, 0.1f, 0.14f, 0.92f);
        public static readonly Color HeaderColor = new(0.1f, 0.13f, 0.18f, 0.95f);
        public static readonly Color SurfaceColor = new(0.13f, 0.16f, 0.22f, 0.94f);
        public static readonly Color SurfaceOpaqueColor = new(0.13f, 0.16f, 0.22f, 1f);
        public static readonly Color GridBackgroundColor = new(0.07f, 0.09f, 0.13f, 0.92f);
        public static readonly Color ScrollBackgroundColor = new(0.04f, 0.05f, 0.08f, 0.55f);
        public static readonly Color ViewportMaskColor = new(1f, 1f, 1f, 0.04f);
        public static readonly Color HintBackgroundColor = new(0.08f, 0.1f, 0.14f, 0.72f);
        public static readonly Color ActiveColor = new(0.18f, 0.32f, 0.42f, 0.96f);
        public static readonly Color EquippedColor = new(0.2f, 0.38f, 0.24f, 0.96f);
        public static readonly Color ConflictColor = new(0.32f, 0.26f, 0.12f, 0.96f);
        public static readonly Color TextColor = new(0.92f, 0.95f, 1f, 1f);
        public static readonly Color MutedTextColor = new(0.72f, 0.76f, 0.84f, 1f);
        public static readonly Color FactTextColor = new(0.76f, 0.88f, 1f, 1f);
        public static readonly Color EquippedTextColor = new(0.72f, 1f, 0.66f, 1f);
        public static readonly Color EquipButtonColor = new(0.56f, 0.92f, 0.66f, 1f);
        public static readonly Color SecondaryButtonColor = new(0.18f, 0.22f, 0.3f, 1f);
    }
}
