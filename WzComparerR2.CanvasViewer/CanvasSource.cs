using System;
using System.Collections.Generic;
using WzComparerR2.PluginBase;
using WzComparerR2.WzLib;

namespace WzComparerR2.CanvasViewer
{
    internal sealed class CanvasEntry
    {
        public CanvasEntry(Wz_Node node, string error = null)
        {
            Node = node;
            Error = error;
            Path = CanvasSource.PathOf(node);
        }
        public Wz_Node Node { get; }
        public string Path { get; }
        public string Error { get; set; }
    }

    internal static class CanvasSource
    {
        // All WZ access is serialized on the UI thread. Upstream's IMG extraction
        // mutates shared nodes and cannot safely race the existing tree viewers.
        public static Wz_Node Resolve(Wz_Node node)
        {
            var seen = new HashSet<Wz_Node>();
            while (node != null)
            {
                if (!seen.Add(node)) throw new InvalidOperationException("循环引用");
                if (node.Value is Wz_Uol uol)
                {
                    node = uol.HandleUol(node);
                    if (node == null) throw new InvalidOperationException("UOL 引用缺失");
                    continue;
                }
                string source = node.Nodes["source"]?.Value as string;
                string inlink = node.Nodes["_inlink"]?.Value as string;
                string outlink = node.Nodes["_outlink"]?.Value as string;
                if (!string.IsNullOrEmpty(source) || (string.IsNullOrEmpty(inlink) && !string.IsNullOrEmpty(outlink)))
                {
                    var file = node.GetNodeWzFile();
                    if (file == null) throw new InvalidOperationException("无法确定引用所属 WZ");
                    node = PluginManager.FindWz(!string.IsNullOrEmpty(source) ? source : outlink, file);
                    if (node == null) throw new InvalidOperationException("引用图片未找到，请打开所属 WZ");
                }
                else if (!string.IsNullOrEmpty(inlink))
                {
                    node = node.GetNodeWzImage()?.Node.FindNodeByPath(true, inlink.Split('/', '\\'));
                    if (node == null) throw new InvalidOperationException("IMG 内引用缺失");
                }
                else return node;
            }
            return null;
        }

        public static bool IsCandidate(Wz_Node node) => node.Value is Wz_Png || node.Value is Wz_Uol;

        public static Wz_Node ParentOf(Wz_Node node)
        {
            var image = node.GetValue<Wz_Image>();
            if (image != null && ReferenceEquals(node, image.Node))
                return image.OwnerNode?.ParentNode;
            return node.ParentNode;
        }

        public static string PathOf(Wz_Node node)
        {
            var parts = new Stack<string>();
            while (node != null)
            {
                parts.Push(node.Text);
                if (node.Value is Wz_File file && !file.IsSubDir) break;
                node = ParentOf(node);
            }
            return string.Join("\\", parts);
        }

        public static bool IsStorageCanvas(Wz_Node node)
        {
            for (; node != null; node = ParentOf(node))
                if (string.Equals(node.Text, "_Canvas", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(node.Text, "_Canvas.wz", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }

    // One MoveNext visits at most one node/IMG. Callers impose a time budget and
    // stop requesting visits after filling the viewport and its look-ahead area.
    internal sealed class CanvasWalker : IDisposable
    {
        private readonly Stack<IEnumerator<Wz_Node>> levels = new Stack<IEnumerator<Wz_Node>>();
        private Wz_Node pendingChildren;
        public CanvasWalker(Wz_Node root)
        {
            levels.Push(((IEnumerable<Wz_Node>)new[] { root }).GetEnumerator());
        }
        public Wz_Node Current { get; private set; }
        public string Error { get; private set; }
        public int Visited { get; private set; }
        public bool MoveNext()
        {
            Current = null;
            Error = null;
            if (pendingChildren != null)
            {
                levels.Push(pendingChildren.Nodes.GetEnumerator());
                pendingChildren = null;
            }
            while (levels.Count > 0)
            {
                if (!levels.Peek().MoveNext())
                {
                    levels.Pop().Dispose();
                    continue;
                }
                Current = levels.Peek().Current;
                if (Current == null) continue;
                Visited++;
                var children = Current;
                if (Current.Value is Wz_Image img && Current != img.Node)
                {
                    try
                    {
                        if (!img.TryExtract(out var error))
                            Error = error?.Message ?? "IMG 读取失败";
                        else children = img.Node;
                    }
                    catch (Exception ex) { Error = ex.Message; }
                }
                if (Error == null && children.Nodes.Count > 0) pendingChildren = children;
                return true;
            }
            return false;
        }
        public void Dispose()
        {
            pendingChildren = null;
            while (levels.Count > 0) levels.Pop().Dispose();
        }
    }
}
