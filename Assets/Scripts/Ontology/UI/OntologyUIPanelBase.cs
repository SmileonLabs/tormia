using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Tormia.Ontology.Core
{
    public abstract class OntologyUIPanelBase : MonoBehaviour
    {
        [SerializeField] protected OntologyUITheme theme;
        [SerializeField] protected OntologyUILabels labels;
        private OntologyUILabels runtimeLabels;

        protected OntologyUITheme Theme => theme;
        protected OntologyUILabels Labels
        {
            get
            {
                var value = labels != null
                    ? labels
                    : runtimeLabels ??= ScriptableObject.CreateInstance<OntologyUILabels>();
                value.ApplyLanguagePack();
                return value;
            }
        }

        // Legacy compatibility for OntologyCharacterPartPanel. New hierarchy-authored panels
        // should bind existing templates instead of calling these helpers.
        protected TextMeshProUGUI CreateText(Transform parent, string name, string text, float fontSize, Color color, TextAlignmentOptions alignment)
        {
            var labelObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(parent, false);
            var label = labelObject.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.color = color;
            return label;
        }

        protected Button CreateButton(Transform parent, string name, string labelText, Vector2 anchoredPosition, Vector2 size, Color background, Color textColor, float fontSize)
        {
            var buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            var rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            var image = buttonObject.GetComponent<Image>();
            image.color = background;
            var label = CreateText(buttonObject.transform, "Text", labelText, fontSize, textColor, TextAlignmentOptions.Center);
            var labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            return buttonObject.GetComponent<Button>();
        }

        protected void StyleButton(Button button, string labelText, Color background, Color textColor, float fontSize)
        {
            if (button == null)
            {
                return;
            }

            var image = button.GetComponent<Image>();
            if (image != null)
            {
                image.color = button.interactable ? background : Theme.disabledButton;
            }

            var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
            {
                label.text = labelText;
                label.color = button.interactable ? textColor : Theme.disabledButtonText;
            }
        }

        protected void StyleText(TMP_Text text, string value, float fontSize, Color color, TextAlignmentOptions alignment)
        {
            if (text == null)
            {
                return;
            }

            text.text = value;
            text.color = color;
        }
    }
}
