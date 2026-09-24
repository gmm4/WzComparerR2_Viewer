using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using WzComparerR2.PluginBase;
using WzComparerR2.WzLib;

namespace WzComparerR2.CanvasViewer
{
    internal sealed class CanvasViewerPanel : UserControl
    {
        private readonly PluginContext context;
        private readonly CanvasGrid grid = new CanvasGrid { Dock = DockStyle.Fill };
        private readonly CanvasDetail detail = new CanvasDetail { Dock = DockStyle.Fill, Visible = false };
        private readonly ToolStrip toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
        private readonly ToolStripButton back = new ToolStripButton("← 返回");
        private readonly ToolStripButton cancel = new ToolStripButton("暂停加载");
        private readonly ToolStripButton fit = new ToolStripButton("适应窗口");
        private readonly ToolStripButton actual = new ToolStripButton("100%");
        private readonly ToolStripComboBox pages = new ToolStripComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AutoSize = false, Width = 95 };
        private readonly Label pathLabel = new Label { Dock = DockStyle.Top, Height = 26, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 26, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly Timer timer = new Timer { Interval = 15 };
        private CanvasWalker walker;
        private Wz_Node root;
        private CanvasEntry currentDetail;
        private Wz_Png detailPng;
        private bool paused;
        private bool saving;
        private bool changingPage;
        private ContextMenuStrip menu;
        private ToolStripMenuItem referenceMenu;
        private CanvasWalker referenceWalker;
        private Wz_Node referenceTarget;
        private bool searchAll;
        private bool foundFirst;
        private int referenceErrors;
        private int referenceCount;
        public bool Navigating { get; private set; }

        public CanvasViewerPanel(PluginContext context)
        {
            this.context = context;
            toolbar.Items.AddRange(new ToolStripItem[] { back, fit, actual, pages, new ToolStripSeparator(), cancel });
            Controls.Add(grid);
            Controls.Add(detail);
            Controls.Add(pathLabel);
            Controls.Add(toolbar);
            Controls.Add(status);
            back.Click += (s, e) => ReturnToGrid();
            fit.Click += (s, e) => detail.Fit();
            actual.Click += (s, e) => detail.ActualSize();
            cancel.Click += (s, e) => { paused = !paused; UpdateStatus(); };
            pages.SelectedIndexChanged += (s, e) => { if (!changingPage) LoadDetailPage(); };
            grid.OpenRequested += OpenDetail;
            grid.ContextRequested += ShowContext;
            grid.DemandChanged += UpdateStatus;
            detail.ContextRequested += p => ShowContext(currentDetail, p);
            detail.ScaleChanged += UpdateStatus;
            timer.Tick += Tick;
            timer.Start();
            SetDetailMode(false);
            UpdateStatus();
        }

        public void SelectRoot(Wz_Node node)
        {
            if (Navigating || node == null) return;
            Clear();
            root = node;
            pathLabel.Text = CanvasSource.PathOf(node);
            walker = new CanvasWalker(node);
            grid.SetMore(true);
            UpdateStatus();
        }

        public void Clear()
        {
            DisposeMenu();
            walker?.Dispose();
            walker = null;
            root = null;
            paused = false;
            currentDetail = null;
            detailPng = null;
            detail.SetImage(null);
            grid.Reset();
            SetDetailMode(false);
            pathLabel.Text = "请选择左侧 WZ、目录或 IMG";
            UpdateStatus();
        }

        public void OnWzClosing(Wz_Structure structure)
        {
            if (root?.GetNodeWzFile()?.WzStructure == structure
                || root?.GetNodeWzImage()?.WzFile?.WzStructure == structure) Clear();
        }

        private void Tick(object sender, EventArgs e)
        {
            if (!Visible || IsDisposed || saving) return;
            try
            {
                if (menu?.Visible == true && referenceMenu?.DropDown.Visible == true && referenceWalker != null)
                {
                    if (!foundFirst || searchAll) SearchReferences();
                    return;
                }
                if (currentDetail != null || paused) return;
                var watch = Stopwatch.StartNew();
                // Decode an already-discovered visible item before walking deeper.
                if (grid.LoadOneThumbnail()) return;
                bool changed = false;
                while (walker != null && grid.Entries.Count < grid.DesiredCount && watch.ElapsedMilliseconds < 8)
                {
                    if (!walker.MoveNext())
                    {
                        walker.Dispose();
                        walker = null;
                        grid.SetMore(false);
                        break;
                    }
                    var node = walker.Current;
                    if (walker.Error != null)
                    {
                        grid.Entries.Add(new CanvasEntry(node, walker.Error));
                        changed = true;
                    }
                    else if (CanvasSource.IsCandidate(node))
                    {
                        var entry = new CanvasEntry(node);
                        if (node.Value is Wz_Uol)
                        {
                            try { if (!(CanvasSource.Resolve(node)?.Value is Wz_Png)) continue; }
                            catch (Exception ex) { entry.Error = ex.Message; }
                        }
                        grid.Entries.Add(entry);
                        changed = true;
                    }
                }
                if (changed) grid.ItemsAdded();
                UpdateStatus();
            }
            catch (Exception ex)
            {
                paused = true;
                status.Text = "加载已暂停：" + ex.Message;
                cancel.Text = "继续加载";
            }
        }

        private void UpdateStatus()
        {
            if (currentDetail != null)
            {
                status.Text = $"{detail.ImageSize.Width} × {detail.ImageSize.Height}  ·  {detail.Zoom:P0}  ·  滚轮缩放，左键拖动，Esc / Backspace 返回";
                return;
            }
            cancel.Text = paused ? "继续加载" : "暂停加载";
            cancel.Enabled = root != null;
            string progress = root == null ? "请选择一个目录" : walker == null ? "已全部列出" : paused ? "已暂停" :
                grid.Entries.Count >= grid.DesiredCount ? "继续向下滚动以加载更多" : $"正在读取（已检查 {walker.Visited:N0} 个节点）";
            status.Text = $"{grid.Entries.Count:N0} 项  ·  {grid.CellSize}px  ·  {progress}  ·  Ctrl + 滚轮调整预览大小";
        }

        private void SetDetailMode(bool value)
        {
            detail.Visible = value;
            grid.Visible = !value;
            back.Visible = fit.Visible = actual.Visible = value;
            pages.Visible = value && detailPng?.ActualPages > 1;
            cancel.Visible = !value;
            if (value) detail.BringToFront(); else grid.BringToFront();
        }

        private void OpenDetail(CanvasEntry entry)
        {
            try
            {
                detailPng = CanvasSource.Resolve(entry.Node)?.Value as Wz_Png;
                if (detailPng == null) throw new InvalidOperationException(entry.Error ?? "此节点不是可显示的 Canvas");
                currentDetail = entry;
                changingPage = true;
                pages.Items.Clear();
                for (int i = 0; i < Math.Max(1, detailPng.ActualPages); i++) pages.Items.Add($"第 {i + 1} 页");
                pages.SelectedIndex = 0;
                changingPage = false;
                SetDetailMode(true);
                pathLabel.Text = entry.Path;
                LoadDetailPage();
                detail.Focus();
            }
            catch (Exception ex)
            {
                changingPage = false;
                ReturnToGrid();
                status.Text = "无法打开图片：" + ex.Message;
            }
        }

        private void LoadDetailPage()
        {
            if (detailPng == null || pages.SelectedIndex < 0) return;
            try
            {
                var bitmap = detailPng.ExtractPng(pages.SelectedIndex);
                if (bitmap == null) throw new InvalidOperationException("图片格式暂不支持");
                detail.SetImage(bitmap);
            }
            catch (Exception ex) { detail.SetImage(null); status.Text = "无法读取此页：" + ex.Message; }
        }

        private void ReturnToGrid()
        {
            currentDetail = null;
            detailPng = null;
            detail.SetImage(null);
            SetDetailMode(false);
            pathLabel.Text = root == null ? "请选择左侧目录" : CanvasSource.PathOf(root);
            grid.Focus();
            UpdateStatus();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (currentDetail != null && (keyData == Keys.Escape || keyData == Keys.Back) && menu?.Visible != true)
            {
                Control focused = context.MainForm;
                while (focused is ContainerControl container && container.ActiveControl != null) focused = container.ActiveControl;
                if (!(focused is TextBoxBase) && !(focused is ComboBox combo && (combo.DropDownStyle != ComboBoxStyle.DropDownList || combo.DroppedDown)))
                {
                    ReturnToGrid();
                    return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void ShowContext(CanvasEntry entry, Point screenPoint)
        {
            if (entry == null) return;
            DisposeMenu();
            menu = new ContextMenuStrip();
            if (CanvasSource.IsStorageCanvas(entry.Node))
            {
                referenceMenu = new ToolStripMenuItem("跳转到引用位置");
                referenceMenu.DropDownItems.Add(new ToolStripMenuItem("正在检索…") { Enabled = false });
                referenceMenu.DropDownOpening += (s, e) =>
                {
                    if (referenceTarget != null || referenceWalker != null) return;
                    try
                    {
                        referenceTarget = CanvasSource.Resolve(entry.Node);
                        var scope = entry.Node.GetNodeWzFile()?.WzStructure?.WzNode;
                        if (scope == null) throw new InvalidOperationException("无法确定当前 WZ 的检索范围");
                        referenceWalker = new CanvasWalker(scope);
                    }
                    catch (Exception ex)
                    {
                        referenceMenu.DropDownItems.Clear();
                        referenceMenu.DropDownItems.Add(new ToolStripMenuItem(ex.Message) { Enabled = false });
                    }
                };
                menu.Items.Add(referenceMenu);
                menu.Items.Add("跳转到原始 Canvas", null, (s, e) => Navigate(entry.Node));
            }
            else menu.Items.Add("跳转到 WzView", null, (s, e) => Navigate(entry.Node));
            menu.Items.Insert(1, new ToolStripMenuItem("另存为 PNG…", null, (s, e) => SaveAsPng(entry))
            {
                Enabled = CanvasSource.IsCandidate(entry.Node)
            });
            menu.Show(screenPoint);
        }

        private void SearchReferences()
        {
            var watch = Stopwatch.StartNew();
            while (referenceWalker != null && watch.ElapsedMilliseconds < 8)
            {
                if (!referenceWalker.MoveNext())
                {
                    referenceWalker.Dispose();
                    referenceWalker = null;
                    if (!foundFirst) referenceMenu.DropDownItems.Clear();
                    else RemoveSearchFooter();
                    string summary = referenceCount == 0 ? "未找到引用" : $"已列出全部 {referenceCount:N0} 个引用";
                    if (referenceErrors > 0) summary += $"（{referenceErrors:N0} 项读取失败，结果可能不完整）";
                    referenceMenu.DropDownItems.Add(new ToolStripMenuItem(summary) { Enabled = false });
                    return;
                }
                if (referenceWalker.Error != null) { referenceErrors++; continue; }
                var candidate = referenceWalker.Current;
                if (candidate == referenceTarget || !CanvasSource.IsCandidate(candidate)) continue;
                try
                {
                    if (!ReferenceEquals(CanvasSource.Resolve(candidate), referenceTarget)) continue;
                }
                catch { referenceErrors++; continue; }
                if (!foundFirst) referenceMenu.DropDownItems.Clear();
                else RemoveSearchFooter();
                foundFirst = true;
                referenceCount++;
                var item = new ToolStripMenuItem(CanvasSource.PathOf(candidate).Replace("&", "&&"));
                item.Click += (s, e) => Navigate(candidate);
                referenceMenu.DropDownItems.Add(item);
                AddSearchFooter();
                if (!searchAll) return;
            }
        }

        private void RemoveSearchFooter()
        {
            int count = referenceMenu.DropDownItems.Count;
            if (count > 0 && Equals(referenceMenu.DropDownItems[count - 1].Tag, "search"))
            {
                var last = referenceMenu.DropDownItems[count - 1];
                referenceMenu.DropDownItems.Remove(last);
                last.Dispose();
            }
        }

        private void AddSearchFooter()
        {
            var footer = new ToolStripMenuItem(searchAll ? $"正在检索全部…（已找到 {referenceCount:N0} 项）" : "… 查找全部引用")
            { Tag = "search", Enabled = !searchAll };
            footer.Click += (s, e) =>
            {
                searchAll = true;
                footer.Text = "正在检索全部…";
                footer.Enabled = false;
                // WinForms dismisses dropdowns before the item's Click callback.
                // Reopen both levels at their existing location after dispatch.
                var expectedMenu = menu;
                var expectedReferenceMenu = referenceMenu;
                var position = menu.Location;
                BeginInvoke((Action)(() =>
                {
                    if (IsDisposed || menu != expectedMenu || expectedMenu.IsDisposed) return;
                    if (!expectedMenu.Visible) expectedMenu.Show(position);
                    expectedReferenceMenu.ShowDropDown();
                }));
            };
            referenceMenu.DropDownItems.Add(footer);
        }

        private void SaveAsPng(CanvasEntry entry)
        {
            try
            {
                saving = true;
                var png = CanvasSource.Resolve(entry.Node)?.Value as Wz_Png;
                if (png == null) throw new InvalidOperationException(entry.Error ?? "此节点不是可保存的 Canvas");
                int page = entry == currentDetail ? Math.Max(0, pages.SelectedIndex) : 0;
                if (!(context.MainForm is MainForm main))
                    throw new InvalidOperationException("无法使用 WzView 的图片保存功能");
                main.SaveCanvasAsPng(entry.Node, png, page);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "图片保存失败：" + ex.Message, "Canvas Viewer",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            finally { saving = false; }
        }

        private void Navigate(Wz_Node node)
        {
            try
            {
                Navigating = true;
                Wz_Png png = null;
                try { png = CanvasSource.Resolve(node)?.Value as Wz_Png; }
                catch { /* Broken references must still be navigable for inspection. */ }
                if (!(context.MainForm is MainForm main) || !main.NavigateToCanvas(node, png))
                    throw new InvalidOperationException("无法在 WzView 中定位该节点");
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Canvas Viewer", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            finally { Navigating = false; }
        }

        private void DisposeMenu()
        {
            referenceWalker?.Dispose();
            referenceWalker = null;
            referenceTarget = null;
            referenceMenu = null;
            searchAll = foundFirst = false;
            referenceErrors = referenceCount = 0;
            menu?.Dispose();
            menu = null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Stop();
                timer.Dispose();
                walker?.Dispose();
                DisposeMenu();
            }
            base.Dispose(disposing);
        }
    }
}
