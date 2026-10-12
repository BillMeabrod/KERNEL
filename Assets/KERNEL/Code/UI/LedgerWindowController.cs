using KSP.UI.Binding;
using UitkForKsp2.API;
using UitkForKsp2.Controls;
using UnityEngine;
using UnityEngine.UIElements;

namespace KERNEL.UI
{
    /// <summary>
    /// Shows and hides the ledger window and keeps the App Bar buttons in sync with it.
    /// </summary>
    public class LedgerWindowController : MonoBehaviour
    {
        // Bold initials spell out K.E.R.N.E.L
        private const string Title =
            "<b>K</b>ERBAL <b>E</b>XPERIMENTAL <b>R</b>ESEARCH & <b>N</b>AVIGATION <b>E</b>LECTRONIC <b>L</b>EDGER";

        private PanelRenderer _renderer;
        private string[] _appBarButtonIds;

        public bool IsOpen { get; private set; }

        public void Initialize(PanelRenderer renderer, params string[] appBarButtonIds)
        {
            _renderer = renderer;
            _appBarButtonIds = appBarButtonIds;

            renderer.OnWindowRoot(root =>
            {
                var shell = root.Q<AppShell>("ledger-window");
                shell.Title = Title;
                shell.CloseClicked += () => SetOpen(false);
            });

            renderer.Hide();
        }

        public void SetOpen(bool open)
        {
            // The App Bar toggles call back into here when we update them below
            if (open == IsOpen) return;
            IsOpen = open;

            if (open) _renderer.Show();
            else _renderer.Hide();

            foreach (string id in _appBarButtonIds)
                GameObject.Find(id)?.GetComponent<UIValue_WriteBool_Toggle>()?.SetValue(open);
        }
    }
}
