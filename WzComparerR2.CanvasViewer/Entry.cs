using System.Windows.Forms;
using DevComponents.DotNetBar;
using WzComparerR2.PluginBase;

namespace WzComparerR2.CanvasViewer
{
    public sealed class Entry : PluginEntry
    {
        private CanvasViewerPanel viewer;
        private SuperTabControlPanel panel;
        public Entry(PluginContext context) : base(context) { }
        protected override void OnLoad()
        {
            viewer = new CanvasViewerPanel(Context) { Dock = DockStyle.Fill };
            panel = new SuperTabControlPanel();
            panel.Controls.Add(viewer);
            Context.AddTab("Canvas Viewer", panel);
            Context.SelectedNode1Changed += SelectionChanged;
            Context.SelectedNode2Changed += SelectionChanged;
            Context.WzClosing += WzClosing;
            viewer.SelectRoot(Context.SelectedNode2 ?? Context.SelectedNode1);
        }
        private void SelectionChanged(object sender, WzNodeEventArgs e) => viewer.SelectRoot(e.Node);
        private void WzClosing(object sender, WzStructureEventArgs e) => viewer.OnWzClosing(e.WzStructure);
        protected override void OnUnload()
        {
            Context.SelectedNode1Changed -= SelectionChanged;
            Context.SelectedNode2Changed -= SelectionChanged;
            Context.WzClosing -= WzClosing;
            if (panel?.Parent is SuperTabControl tabs) tabs.Tabs.Remove(panel.TabItem);
            panel?.Dispose();
            panel = null;
            viewer = null;
        }
    }
}
