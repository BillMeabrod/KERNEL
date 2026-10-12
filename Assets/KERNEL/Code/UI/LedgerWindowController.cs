using System.Collections.Generic;
using KERNEL.Ledger;
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
        // The full name (Kerbal Experimental Research & Navigation Electronic Ledger) goes on the R&D page
        private const string Title = "K.E.R.N.E.L";

        private PanelRenderer _renderer;
        private LedgerTreeView _tree;

        // Ledger received before the window's root existed
        private IEnumerable<BodyEntry> _pendingLedger;

        public bool IsOpen { get; private set; }

        public void Initialize(PanelRenderer renderer)
        {
            _renderer = renderer;

            renderer.OnWindowRoot(root =>
            {
                var shell = root.Q<AppShell>("ledger-window");
                shell.Title = Title;
                shell.CloseClicked += () => SetOpen(false);
                _tree = new LedgerTreeView(root.Q<ScrollView>("ledger-tree"));
                if (_pendingLedger != null)
                {
                    _tree.Show(_pendingLedger);
                    _pendingLedger = null;
                }
            });

            renderer.Hide();
        }

        public void ShowLedger(IEnumerable<BodyEntry> roots)
        {
            if (_tree == null)
                _pendingLedger = roots;
            else
                _tree.Show(roots);
        }

        public void SetOpen(bool open)
        {
            // The toolbar buttons call back into here when we update them below
            if (open == IsOpen) return;
            IsOpen = open;

            if (open) _renderer.Show();
            else _renderer.Hide();

            ToolbarButtons.SetPressed(open);
        }
    }
}
