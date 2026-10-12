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
                KeepHeaderSlashVisible(shell);

                _tree = new LedgerTreeView(root.Q<ScrollView>("ledger-tree"));
                if (_pendingLedger != null)
                {
                    _tree.Show(_pendingLedger);
                    _pendingLedger = null;
                }
            });

            renderer.Hide();
        }

        /// <summary>
        /// AppShell draws the header's "-----/" as one label. With our long title that text overflows its box
        /// and the "/" lands under the close button. Stock headers (e.g. the Engineer's Report) keep the "/" a
        /// fixed gap before the close button, so put the dashes in a box that clips them while keeping their
        /// right end, and draw the "/" as its own label after it.
        /// </summary>
        private static void KeepHeaderSlashVisible(AppShell shell)
        {
            Label dashes = shell.Q<Label>("header-dashes");
            if (dashes == null) return;

            VisualElement header = dashes.parent;
            var clip = new VisualElement();
            clip.AddToClassList("kernel-header-dashes-clip");
            header.Insert(header.IndexOf(dashes), clip);
            clip.Add(dashes);

            shell.Dashes = shell.Dashes.TrimEnd('/');

            var slash = new Label("/");
            slash.AddToClassList("oab-window-header-dashes");
            slash.AddToClassList("kernel-header-slash");
            header.Insert(header.IndexOf(clip) + 1, slash);
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
