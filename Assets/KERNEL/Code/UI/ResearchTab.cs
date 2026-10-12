using System.Collections;
using KSP.UI;
using PatchManager.CSharpPatching;
using PatchManager.PrefabPatching;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KERNEL.UI
{
    /// <summary>
    /// K.E.R.N.E.L's tab on the R&D screen, after the tech tier tabs. A prefab patch adds this component to the
    /// tier tab bar, so it's created and destroyed with the screen. The tier tabs themselves are only created
    /// when the screen starts, so ours is a copy of one of them, in the same toggle group: it looks, sounds and
    /// switches like the stock tabs.
    /// </summary>
    public class ResearchTab : MonoBehaviour
    {
        private const string PrefabAddress = "RDCenterUI.prefab";
        private const string TabBarPath = "Container/TechTree/TierScroll/TierSelectionContainer";
        private const string TabName = "TierSelection-KERNEL";

        private ToggleExtended _tab;

        /// <summary>
        /// Registers the R&D screen patch. Patch Manager only accepts prefab patches before the game starts
        /// loading, so this runs from the plugin's OnPreInitialized.
        /// </summary>
        public static void RegisterPatch(PmScope pm)
        {
            pm.PatchPrefab("ResearchTab", PrefabAddress)
                .AddComponent(
                    "research-tab",
                    PrefabPatchBuilder.GameObjectAt(TabBarPath),
                    PrefabPatchComponentBuilder.For<ResearchTab>("research-tab").Build())
                .Register();
        }

        private IEnumerator Start()
        {
            // The R&D controller creates the tier tabs in its own Start; they exist from the next frame
            yield return null;

            // Copy a tab that isn't selected: a selected copy would take the selection from it on joining the group
            ToggleExtended template = null;
            foreach (ToggleExtended tier in GetComponentsInChildren<ToggleExtended>())
            {
                if (tier.isOn) continue;
                template = tier;
                break;
            }

            if (template == null) yield break;

            _tab = Instantiate(template, transform);
            _tab.name = TabName;
            _tab.transform.SetAsLastSibling();

            string title = Localization.Translate(Localization.Title);
            foreach (TextMeshProUGUI text in _tab.GetComponentsInChildren<TextMeshProUGUI>(true))
                text.text = title;

            _tab.onValueChanged.AddListener(OnSelected);
        }

        private void OnSelected(bool selected)
        {
            // The ledger page slides in here in the next step
        }
    }
}
