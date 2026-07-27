using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Presents a hierarchy-authored selected/unselected tab state.
    /// All visual values stay serialized so designers can tune them in Inspector.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyTabVisualState : MonoBehaviour
    {
        [SerializeField] private Image background;
        [SerializeField] private Sprite selectedSprite;
        [SerializeField] private Sprite unselectedSprite;
        [SerializeField] private Graphic icon;
        [SerializeField] private TMP_Text label;
        [SerializeField] private Color selectedIconColor = Color.white;
        [SerializeField] private Color unselectedIconColor = new(0.086f, 0.208f, 0.282f, 1f);
        [SerializeField] private Color selectedLabelColor = Color.white;
        [SerializeField] private Color unselectedLabelColor = new(0.086f, 0.208f, 0.282f, 1f);

        public bool IsSelected { get; private set; }

        public void SetSelected(bool selected)
        {
            IsSelected = selected;
            if (background != null)
            {
                background.sprite = selected ? selectedSprite : unselectedSprite;
                background.type = Image.Type.Sliced;
                background.color = Color.white;
            }

            if (icon != null)
                icon.color = selected ? selectedIconColor : unselectedIconColor;
            if (label != null)
                label.color = selected ? selectedLabelColor : unselectedLabelColor;
        }
    }
}
