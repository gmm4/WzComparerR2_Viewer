using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using DevComponents.AdvTree;
using DevComponents.DotNetBar;
using WzComparerR2;
using WzComparerR2.CanvasViewer;
using WzComparerR2.PluginBase;
using WzComparerR2.WzLib;

internal static class Program
{
    private static int passed;
    private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name); passed++;
    }
    private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Flags).Invoke(target, args);
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Flags).GetValue(target);
    private static Wz_Node Add(Wz_Node parent, string name, object value = null)
    {
        var node = parent.Nodes.Add(name); node.Value = value; return node;
    }
    private static void Pump(int milliseconds)
    {
        var watch = Stopwatch.StartNew();
        do { Application.DoEvents(); System.Threading.Thread.Sleep(2); } while (watch.ElapsedMilliseconds < milliseconds);
    }
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Application.EnableVisualStyles();
            typeof(MainForm).Assembly.GetType("WzComparerR2.Dotnet6Patch").GetMethod("Patch").Invoke(null, null);
            typeof(WzComparerR2.Program).GetMethod("SetDllDirectory", BindingFlags.NonPublic | BindingFlags.Static, null, Type.EmptyTypes, null).Invoke(null, null);
            if (args.Length == 2 && args[0] == "--startup-smoke") { PluginLoaderSmoke(args[1]); return 0; }
            SourceTests();
            DetailTests();
            if (args.Length >= 2) IntegrationTests(args[0], args[1], args.Length > 2 ? args[2] : null);
            Console.WriteLine($"All {passed} checks passed."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void PluginLoaderSmoke(string pluginPath)
    {
        using (var main = new MainForm())
        {
            Call(main, "PluginOnLoad");
            var context = (PluginContext)Activator.CreateInstance(typeof(PluginContext), Flags, null, new object[] { main }, null);
            var loaderType = typeof(MainForm).Assembly.GetType("WzComparerR2.PluginLoadContext");
            var loader = (System.Runtime.Loader.AssemblyLoadContext)Activator.CreateInstance(loaderType,
                new object[] { WzComparerR2.Program.LibPath, Path.GetFullPath(pluginPath) });
            var assembly = loader.LoadFromAssemblyPath(Path.GetFullPath(pluginPath));
            var pluginType = assembly.GetType("WzComparerR2.CanvasViewer.Entry");
            Check(pluginType.IsSubclassOf(typeof(PluginEntry)), "deployed plugin shares host PluginBase types");
            var plugin = (PluginEntry)Activator.CreateInstance(pluginType, context);
            Call(plugin, "OnLoad");
            var tabs = Field<SuperTabControl>(main, "superTabControl1");
            Check(tabs.Tabs.Cast<BaseItem>().OfType<SuperTabItem>().Any(t => t.Text == "Canvas Viewer"), "deployed DLL loads through original plugin load context");
            Call(plugin, "OnUnload");
        }
    }

    private static void SourceTests()
    {
        var root = new Wz_Node("Test.wz");
        var owner = Add(root, "test.img");
        var img = new Wz_Image("test.img", 0, 0, 0, 0, null) { OwnerNode = owner };
        owner.Value = img; typeof(Wz_Image).GetField("extr", Flags).SetValue(img, true);
        var a = Add(img.Node, "a", new Wz_Png(1, 1, 0, Wz_TextureFormat.ARGB8888, 0, 1, 0, 0, img));
        var nested = Add(img.Node, "nested");
        var alias = Add(nested, "alias", new Wz_Uol("../a"));
        var alias2 = Add(img.Node, "alias2", new Wz_Uol("nested/alias"));
        Check(CanvasSource.Resolve(alias) == a && CanvasSource.Resolve(alias2) == a, "direct and chained UOL references");
        Check(CanvasSource.PathOf(alias) == "Test.wz\\test.img\\nested\\alias", "absolute path includes WZ and IMG names");
        var order = new List<string>();
        using (var walker = new CanvasWalker(img.Node))
            while (walker.MoveNext()) if (CanvasSource.IsCandidate(walker.Current)) order.Add(walker.Current.Text);
        Check(string.Join(",", order) == "a,alias,alias2", "tree order retains duplicate logical references");
        var link = Add(img.Node, "linked", new Wz_Png(1, 1, 0, Wz_TextureFormat.ARGB8888, 0, 1, 0, 0, img));
        Add(link, "_inlink", "a");
        Check(CanvasSource.Resolve(link) == a, "inlink resolves within owning IMG");
        var cycle = Add(img.Node, "cycle1", new Wz_Uol("cycle2"));
        Add(img.Node, "cycle2", new Wz_Uol("cycle1"));
        try { CanvasSource.Resolve(cycle); throw new Exception("cycle accepted"); }
        catch (InvalidOperationException) { Check(true, "cyclic reference terminates with error"); }
        var missing = Add(img.Node, "missing", new Wz_Uol("absent"));
        try { CanvasSource.Resolve(missing); throw new Exception("missing reference accepted"); }
        catch (InvalidOperationException) { Check(true, "broken reference produces error"); }
        Check(CanvasSource.IsStorageCanvas(Add(Add(root, "_Canvas"), "0")), "storage directory detection");
        typeof(Wz_Image).GetField("extr", Flags).SetValue(img, false); using (var walker = new CanvasWalker(owner))
            Check(walker.MoveNext() && walker.Error != null, "unreadable IMG reported without terminating traversal");
    }
    private static void DetailTests()
    {
        using (var detail = new CanvasDetail { Size = new Size(400, 300) })
        {
            detail.SetImage(new Bitmap(800, 600));
            Check(Math.Abs(detail.Zoom - .5f) < .001, "detail fits large image");
            Call(detail, "OnMouseWheel", new MouseEventArgs(MouseButtons.None, 0, 200, 150, 120));
            Check(detail.Zoom > .5f, "detail wheel zoom");
            var beforeDrag = Field<PointF>(detail, "origin");
            Call(detail, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 50, 50, 0));
            Call(detail, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 85, 70, 0));
            Check(Field<PointF>(detail, "origin").X == beforeDrag.X + 35, "detail left-button pan");
            Call(detail, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 85, 70, 0));
            detail.ActualSize();
            Check(detail.Zoom == 1, "detail original-pixel size");
            detail.SetImage(null);
            Check(detail.ImageSize.IsEmpty, "detail releases image on return");
        }
    }
    private static Wz_Structure LoadIntoHost(MainForm main, string file)
    {
        var wz = new Wz_Structure();
        if (wz.IsKMST1125WzFormat(file)) wz.LoadKMST1125DataWz(file); else wz.Load(file, true);
        var tree = Field<AdvTree>(main, "advTree1");
        tree.Nodes.Add((Node)Call(main, "createNode", wz.WzNode));
        Field<List<Wz_Structure>>(main, "openedWz").Add(wz);
        return wz;
    }
    private static string saveDestination;
    private static DialogResult saveResult;
    private static int saveDialogCalls;
    private static bool InterceptSaveDialog(CommonDialog __instance, ref DialogResult __result)
    {
        if (!(__instance is SaveFileDialog dialog)) return true;
        saveDialogCalls++;
        Check(dialog.Filter.Contains("*.png"), "Save As reuses the PNG dialog");
        dialog.FileName = saveDestination;
        __result = saveResult;
        return false;
    }

    private static void SaveAsChecks(MainForm main, PluginContext context, CanvasViewerPanel viewer, Wz_Node logical)
    {
        // Intercept only dialog interaction; exercise the real exporter and decoder.
        // Unique output paths avoid overwrite prompts and any user-owned files.
        var directory = Path.Combine(AppContext.BaseDirectory, "save-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var selected1 = context.SelectedNode1;
        var selected2 = context.SelectedNode2;
        var selected3 = context.SelectedNode3;
        var selectedTab = context.SelectedTab;
        var grid = Field<CanvasGrid>(viewer, "grid");
        int scroll = Field<VScrollBar>(grid, "scroll").Value;
        var config = WzComparerR2.Config.ImageHandlerConfig.Default;
        bool autoSave = config.AutoSaveEnabled;
        var harmony = new HarmonyLib.Harmony("CanvasViewer.SaveAs.Tests");
        var dialogMethod = typeof(CommonDialog).GetMethod("ShowDialog", new[] { typeof(IWin32Window) });
        var prefix = typeof(Program).GetMethod(nameof(InterceptSaveDialog), BindingFlags.NonPublic | BindingFlags.Static);
        harmony.Patch(dialogMethod, prefix: new HarmonyLib.HarmonyMethod(prefix));
        try
        {
            config.AutoSaveEnabled = true;
            saveDestination = Path.Combine(directory, "linked.png");
            saveResult = DialogResult.OK;
            saveDialogCalls = 0;
            Call(viewer, "ShowContext", new CanvasEntry(logical), new Point(-20000, -20000));
            ((ToolStripMenuItem)Field<ContextMenuStrip>(viewer, "menu").Items[1]).PerformClick();
            Check(saveDialogCalls == 1 && File.Exists(saveDestination), "Save As prompts even with automatic saving enabled");
            using (var expected = ((Wz_Png)CanvasSource.Resolve(logical).Value).ExtractPng())
            using (var saved = new Bitmap(saveDestination))
            {
                bool same = expected.Size == saved.Size;
                for (int y = 0; same && y < expected.Height; y++)
                    for (int x = 0; same && x < expected.Width; x++)
                        same = expected.GetPixel(x, y) == saved.GetPixel(x, y);
                Check(same, "linked Canvas export preserves every original pixel and alpha");
            }
            saveDestination = Path.Combine(directory, "cancelled.png");
            saveResult = DialogResult.Cancel;
            Call(viewer, "ShowContext", new CanvasEntry(logical), new Point(-20000, -20000));
            ((ToolStripMenuItem)Field<ContextMenuStrip>(viewer, "menu").Items[1]).PerformClick();
            Check(saveDialogCalls == 2 && !File.Exists(saveDestination), "cancel creates no file");
            Check(context.SelectedNode1 == selected1 && context.SelectedNode2 == selected2 && context.SelectedNode3 == selected3 &&
                context.SelectedTab == selectedTab && Field<VScrollBar>(grid, "scroll").Value == scroll,
                "save and cancel preserve selected nodes, tab and gallery position");
            Check(config.AutoSaveEnabled.Value, "Save As leaves global automatic-save setting unchanged");
        }
        finally
        {
            config.AutoSaveEnabled = autoSave;
            harmony.Unpatch(dialogMethod, prefix);
        }
    }

    private static void IntegrationTests(string oldPath, string modernPath, string screenshotDir)
    {
        using (var main = new MainForm { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), Size = new Size(1200, 850) })
        {
            Call(main, "PluginOnLoad");
            var context = (PluginContext)Activator.CreateInstance(typeof(PluginContext), Flags, null, new object[] { main }, null);
            var entry = new Entry(context);
            Call(entry, "OnLoad");
            WzComparerR2.Config.WcR2Config.Default.EnableAutoUpdate = false; main.Show();
            var tabs = Field<SuperTabControl>(main, "superTabControl1");
            var tab = tabs.Tabs.Cast<BaseItem>().OfType<SuperTabItem>().Single(t => t.Text == "Canvas Viewer");
            tabs.SelectedTab = tab;
            var viewer = Field<CanvasViewerPanel>(entry, "viewer");
            var grid = Field<CanvasGrid>(viewer, "grid");
            var old = LoadIntoHost(main, oldPath);
            Field<AdvTree>(main, "advTree1").SelectedNode = Field<AdvTree>(main, "advTree1").Nodes[0];
            Pump(2200);
            Check(grid.Entries.Count > 0, "shared left tree selection populates gallery");
            Check(grid.Entries.Count <= grid.DesiredCount && Field<CanvasWalker>(viewer, "walker") != null, "large directory stops after viewport look-ahead");
            Check(Field<Dictionary<int, Bitmap>>(grid, "thumbnails").Count > 0, "real old WZ thumbnails decode");
            int oldCellSize = grid.CellSize;
            grid.ResizeTiles(120);
            Check(grid.CellSize > oldCellSize, "uniform tile enlargement");
            grid.ResizeTiles(-120);
            Check(grid.CellSize == oldCellSize, "uniform tile reduction");
            Pump(500);
            int initial = grid.Entries.Count;
            var scroll = Field<VScrollBar>(grid, "scroll");
            scroll.Value = Math.Max(0, scroll.Maximum - scroll.LargeChange + 1);
            Pump(1000);
            Check(grid.Entries.Count > initial, "scrolling resumes discovery");
            int savedScroll = scroll.Value;
            var first = grid.Entries.First(e => e.Error == null && e.Node.Value is Wz_Png);
            Call(viewer, "OpenDetail", first);
            Check(Field<CanvasEntry>(viewer, "currentDetail") == first && context.SelectedTab == tab, "detail stays in Canvas Viewer");
            object[] escapeArgs = { Message.Create(IntPtr.Zero, 0x100, (IntPtr)Keys.Escape, IntPtr.Zero), Keys.Escape };
            Check((bool)viewer.GetType().GetMethod("ProcessCmdKey", Flags).Invoke(viewer, escapeArgs), "Escape returns from detail");
            Call(viewer, "OpenDetail", first);
            object[] backArgs = { Message.Create(IntPtr.Zero, 0x100, (IntPtr)Keys.Back, IntPtr.Zero), Keys.Back };
            Check((bool)viewer.GetType().GetMethod("ProcessCmdKey", Flags).Invoke(viewer, backArgs), "Backspace returns from detail");
            Call(viewer, "OpenDetail", first);
            Field<ToolStripButton>(viewer, "back").PerformClick();
            Check(scroll.Value == savedScroll, "return preserves scroll position");
            Call(viewer, "Navigate", first.Node);
            Check(context.SelectedTab.Text == "WzView" && context.SelectedNode2 == first.Node && context.SelectedNode3 == first.Node, "navigation selects exact canvas in both WzView trees");
            Check(grid.Entries.Count > 0 && scroll.Value == savedScroll, "navigation preserves gallery state");
            tabs.SelectedTab = tab;
            var modern = LoadIntoHost(main, modernPath);
            Wz_Node logical = null;
            using (var walker = new CanvasWalker(modern.WzNode))
                while (walker.MoveNext())
                {
                    var n = walker.Current;
                    if (n.Value is Wz_Png && n.Nodes["_outlink"] != null)
                    {
                        var target = CanvasSource.Resolve(n);
                        if (target?.Value is Wz_Png && CanvasSource.IsStorageCanvas(target)) { logical = n; break; }
                    }
                }
            Check(logical != null, "modern split WZ Canvas resolves within current structure");
            var stored = CanvasSource.Resolve(logical);
            Check(CanvasSource.PathOf(logical).StartsWith("UI.wz\\"), "modern reference includes WZ name");
            viewer.SelectRoot(logical.GetNodeWzImage().Node);
            Pump(900);
            Check(Field<Dictionary<int, Bitmap>>(grid, "thumbnails").Count > 0, "modern linked thumbnails decode");
            SaveAsChecks(main, context, viewer, logical);
            Call(viewer, "ShowContext", new CanvasEntry(stored), new Point(-20000, -20000));
            var referenceMenu = Field<ToolStripMenuItem>(viewer, "referenceMenu");
            referenceMenu.ShowDropDown();
            var timeout = Stopwatch.StartNew();
            while (!Field<bool>(viewer, "foundFirst") && timeout.ElapsedMilliseconds < 30000) Pump(20);
            Check(Field<bool>(viewer, "foundFirst"), "reverse search finds first match");
            Check(referenceMenu.DropDownItems.Count == 2 && Field<CanvasWalker>(viewer, "referenceWalker") != null, "first match pauses scan and offers find-all");
            ((ToolStripMenuItem)referenceMenu.DropDownItems[1]).PerformClick();
            Pump(200);
            Check(Field<bool>(viewer, "searchAll"), "find-all resumes reference traversal");
            Check(Field<ContextMenuStrip>(viewer, "menu").Visible, "find-all keeps context menu open");
            timeout.Restart();
            while (Field<CanvasWalker>(viewer, "referenceWalker") != null && timeout.ElapsedMilliseconds < 60000) Pump(20);
            Check(Field<CanvasWalker>(viewer, "referenceWalker") == null, "full reference scan completes");
            Check(Field<int>(viewer, "referenceCount") >= 1, "full scan retains first match");
            ((ToolStripMenuItem)referenceMenu.DropDownItems[0]).PerformClick();
            Check(context.SelectedTab.Text == "WzView" && CanvasSource.Resolve(context.SelectedNode2) == stored, "reference submenu click navigates to matching logical canvas");
            Field<ContextMenuStrip>(viewer, "menu").Close();
            Call(viewer, "Navigate", logical);
            Check(context.SelectedNode2 == logical && context.SelectedNode3 == logical, "linked jump selects logical reference");
            tabs.SelectedTab = tab;
            if (screenshotDir != null)
            {
                Directory.CreateDirectory(screenshotDir);
                viewer.SelectRoot(logical.GetNodeWzImage().Node);
                Pump(1200);
                using (var bitmap = new Bitmap(viewer.Width, viewer.Height))
                {
                    viewer.DrawToBitmap(bitmap, viewer.ClientRectangle);
                    bitmap.Save(Path.Combine(screenshotDir, "canvas-gallery.png"));
                }
                Call(viewer, "OpenDetail", new CanvasEntry(logical));
                using (var bitmap = new Bitmap(viewer.Width, viewer.Height))
                {
                    viewer.DrawToBitmap(bitmap, viewer.ClientRectangle);
                    bitmap.Save(Path.Combine(screenshotDir, "canvas-detail.png"));
                }
            }
            viewer.OnWzClosing(modern);
            Check(grid.Entries.Count == 0 && Field<CanvasWalker>(viewer, "walker") == null, "closing WZ clears preview and traversal");
            Call(entry, "OnUnload");
            Check(!tabs.Tabs.Cast<BaseItem>().OfType<SuperTabItem>().Any(t => t.Text == "Canvas Viewer"), "unload removes plugin tab");
            old.Clear(); modern.Clear();
            main.Hide();
        }
    }
}
