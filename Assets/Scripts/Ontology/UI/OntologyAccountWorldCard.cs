using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Hierarchy-authored presentation for one Authority-projected account
    /// world. This component does not filter, create, or own world data.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyAccountWorldCard : MonoBehaviour
    {
        [SerializeField] private Button selectButton;
        [SerializeField] private GameObject selectedIndicator;
        [SerializeField] private TMP_Text titleLabel;
        [SerializeField] private TMP_Text roleLabel;
        [SerializeField] private TMP_Text revisionLabel;
        private string worldId;

        private void Awake() => ResolveBindings();

        public void Bind(
            OntologyAuthorityAccountWorld world,
            bool selected,
            Action<string> select)
        {
            ResolveBindings();
            var available = world != null;
            worldId = available ? world.worldId : null;
            gameObject.SetActive(available);
            if (!available) return;

            if (titleLabel != null)
                titleLabel.text = world.title ?? world.worldId;
            if (roleLabel != null)
                roleLabel.text = L(
                    "ui.account.role_" +
                    (world.role ?? string.Empty).ToLowerInvariant(),
                    world.role ?? string.Empty);
            if (revisionLabel != null)
                revisionLabel.text = L("ui.account.revision", "Revision {0}")
                    .Replace("{0}", world.revision.ToString());
            if (selectedIndicator != null)
            {
                selectedIndicator.SetActive(selected);
                var selectedLabel =
                    selectedIndicator.GetComponentInChildren<TMP_Text>(true);
                if (selectedLabel != null)
                    selectedLabel.text = L(
                        "ui.account.world_select.selected",
                        "SELECTED");
            }
            if (selectButton != null)
            {
                selectButton.onClick.RemoveAllListeners();
                selectButton.onClick.AddListener(() => select?.Invoke(worldId));
            }
        }

        private void ResolveBindings()
        {
            if (selectButton == null) selectButton = GetComponent<Button>();
            var labels = GetComponentsInChildren<TMP_Text>(true);
            if (titleLabel == null && labels.Length > 0)
                titleLabel = labels[0];
        }

        private static string L(string key, string fallback) =>
            OntologyLanguagePackService.Text(key, fallback);
    }
}
