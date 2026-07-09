using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Tormia.Ontology.Core
{
    public sealed class OntologyCharacterPartButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private OntologyCharacterPartPanel panel;
        [SerializeField] private string partId;
        [SerializeField] private bool equip;

        private Button button;
        private Image image;
        private Color normalColor;
        private Color hoverColor;

        private void Awake()
        {
            button = GetComponent<Button>();
            image = GetComponent<Image>();
            if (panel == null)
            {
                panel = GetComponentInParent<OntologyCharacterPartPanel>();
            }
        }

        private void OnEnable()
        {
            if (button == null)
            {
                button = GetComponent<Button>();
            }

            if (image == null)
            {
                image = GetComponent<Image>();
            }

            if (button != null)
            {
                button.onClick.AddListener(InvokePartAction);
            }
        }

        private void OnDisable()
        {
            if (button != null)
            {
                button.onClick.RemoveListener(InvokePartAction);
            }
        }

        public void Configure(OntologyCharacterPartPanel targetPanel, string targetPartId, bool shouldEquip)
        {
            panel = targetPanel;
            partId = targetPartId;
            equip = shouldEquip;
        }

        public void ConfigureStyle(Color normal, Color hover)
        {
            normalColor = normal;
            hoverColor = hover;
            if (image == null)
            {
                image = GetComponent<Image>();
            }

            if (image != null)
            {
                image.color = normalColor;
            }
        }

        public void SetInteractable(bool value, Color enabledColor, Color disabledColor)
        {
            if (button == null)
            {
                button = GetComponent<Button>();
            }

            if (image == null)
            {
                image = GetComponent<Image>();
            }

            if (button != null)
            {
                button.interactable = value;
            }

            normalColor = value ? enabledColor : disabledColor;
            if (image != null)
            {
                image.color = normalColor;
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (button != null && button.interactable && image != null)
            {
                image.color = hoverColor;
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (image != null)
            {
                image.color = normalColor;
            }
        }

        private void InvokePartAction()
        {
            if (panel != null && !string.IsNullOrWhiteSpace(partId))
            {
                panel.SetPartEquipped(partId, equip);
            }
        }
    }
}
