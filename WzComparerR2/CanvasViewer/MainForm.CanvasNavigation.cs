using WzComparerR2.WzLib;

namespace WzComparerR2
{
    // The only host-side integration for the optional Canvas Viewer plugin.
    // Keep this partial separate from upstream's MainForm and its designer.
    public partial class MainForm
    {
        // Share WzView's PNG exporter and native dialog history without selecting
        // another node, changing tabs, or temporarily changing global settings.
        public void SaveCanvasAsPng(Wz_Node node, Wz_Png png, int page = 0)
        {
            if (node == null) throw new System.ArgumentNullException(nameof(node));
            if (png == null) throw new System.ArgumentNullException(nameof(png));
            if (page < 0 || page >= System.Math.Max(1, png.ActualPages))
                throw new System.ArgumentOutOfRangeException(nameof(page));

            string name;
            switch (Config.ImageHandlerConfig.Default.ImageNameMethod.Value)
            {
                case Config.ImageNameMethod.PathToImage:
                    name = node.FullPath.Replace('\\', '.');
                    break;
                case Config.ImageNameMethod.PathToWz:
                    name = node.FullPathToFile.Replace('\\', '.');
                    break;
                default:
                    name = node.Text;
                    break;
            }
            name = string.Join("_", name.Split(System.IO.Path.GetInvalidFileNameChars()));
            OnSavePngFile(new Animation.Frame { Png = png, Page = page }, name, true);
        }

        public bool NavigateToCanvas(Wz_Node node, Wz_Png resolvedImage)
        {
            if (node == null || !OnSelectedWzNode(node))
                return false;

            superTabControl1.SelectedTab = superTabItem1;
            var selected = advTree2.SelectedNode;
            selected?.EnsureVisible();
            advTree3.BeginUpdate();
            try
            {
                historyNodeList.Clear();
                advTree3.Nodes.Clear();
                var detail = createNodeDetail(selected);
                advTree3.Nodes.Add(detail);
                detail.ExpandAll();
                advTree3.SelectedNode = detail;
            }
            finally
            {
                advTree3.EndUpdate();
            }

            // Linked canvases often store a 1x1 placeholder in the logical node.
            // Keep that node selected, but display the resolved pixels immediately.
            if (resolvedImage != null)
            {
                pictureBoxEx1.PictureName = node.FullPathToFile;
                pictureBoxEx1.ShowImage(resolvedImage);
            }
            return true;
        }
    }
}
