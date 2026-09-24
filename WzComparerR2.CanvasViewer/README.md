# Canvas Viewer

独立的图片浏览插件。新 Tab 复用左侧 WZ / IMG 目录树，递归按树中节点顺序展示 Canvas；同一图片的不同引用保留为不同条目。

## 使用

- 选中左侧 WZ、目录、IMG 或 IMG 内节点，然后打开 Canvas Viewer。
- Ctrl + 滚轮统一调整预览格子大小，普通滚轮滚动列表。
- 双击进入当前 Tab 内的单图视图。滚轮以鼠标位置为中心缩放，左键拖动，工具栏提供适应窗口和 100%。
- 返回按钮、Esc、Backspace 返回网格，保留滚动位置、缩放及选中项。多页 Canvas 在详情工具栏切换页。
- 右键首项跳转到 WzView 的确切节点，同时显示解析后的图片。缺失引用仍可跳转检查。
- 右键第二项“另存为 PNG…”复用 WzView 的 PNG 导出及原生保存对话框目录记忆。网格保存原图第一页，详情保存当前页；引用条目保存解析后的真实图片。另存为始终弹出对话框，不受自动保存开关影响，也不切换 Tab 或改变选中节点。
- 对直接位于 _Canvas 中的条目，首项是引用位置子菜单。悬停查找第一条；点击“查找全部引用”继续扫描并逐步补全列表。关闭菜单暂停此次检索。
- 引用范围为该资源所属的当前 WZ 结构（含随它加载的分包和子文件），不会混入另一次打开的其他版本或自动打开磁盘上未加载的 WZ。
- 路径从逻辑 WZ 名开始，例如 UI.wz\AllianceUI.img\AllianceUI\backgmd。

## 加载与内存

网格使用单个绘制控件，不为每张图创建独立控件。先遍历约三屏条目，向下滚动时继续；可见条目优先解码，附近保留少量预读缩略图，离开缓存范围的位图立即释放。所有图片不会一次性解码。

遍历和反查在 UI 消息循环中分批执行，每批以 8 ms 为目标上限。与上游共用的 IMG 提取过程会修改节点，因而不在后台线程并发读取它们。单次 IMG 提取或单张巨大图片解码不可中途取消，极大资源首次读取仍可能短暂停顿。暂停按钮、换目录、关闭 WZ 和卸载插件均会停止相应工作。

不会擅自 Unextract 上游已经共享的 IMG。已提取的节点仍由原 WzLib 管理，关闭 WZ 后释放。损坏 IMG、缺失图片和循环引用以错误占位显示；反查遇到读取失败会标明结果可能不完整。

## 合并上游时关注的接入点

| 文件 | 作用 |
| --- | --- |
| WzComparerR2.CanvasViewer/ | 新增插件全部实现、项目配置和本说明 |
| WzComparerR2/CanvasViewer/MainForm.CanvasNavigation.cs | 新增 MainForm partial，集中提供跳转、属性树选中、解析后图片显示和 PNG 保存入口 |
| WzComparerR2/MainForm.cs | PNG 保存方法新增可选文件名与强制弹窗参数，原有调用不变 |
| WzComparerR2.sln | 在 Plugin 分组注册新项目及构建配置 |
| WzComparerR2.CanvasViewer.Tests/ | 独立验证程序，不加入产品解决方案 |

MainForm.cs 的 OnSavePngFile 仅调整 4 行：增加文件名及强制弹出保存对话框的可选参数，并释放对话框资源；原有调用保留原行为。MainForm.Designer.cs、PluginBase 和 WzLib 没有修改。插件使用原有 AddTab、SelectedNode1Changed、SelectedNode2Changed、WzClosing、FindWz API。

导航桥依赖 MainForm 现有的 OnSelectedWzNode、createNodeDetail、advTree2/3、superTabControl1、superTabItem1、pictureBoxEx1 和 historyNodeList。如果上游重命名这些成员，编译错误会集中在该桥接文件。没有用反射调用主界面的私有成员；测试程序为验证行为而使用反射。

## 构建

与上游保持 net462、net6.0-windows、net8.0-windows 三个目标。推荐用 .NET 8 SDK 构建完整解决方案，以同时生成 Avatar 等原插件：

~~~powershell
dotnet build WzComparerR2.sln -c Release -p:TargetFrameworks=net8.0-windows
~~~

只开发本插件时：

~~~powershell
dotnet build WzComparerR2.CanvasViewer/WzComparerR2.CanvasViewer.csproj -c Release -p:TargetFrameworks=net8.0-windows
~~~

插件沿用 Build/WcR2Plugin.targets，自动放入主程序输出目录的 Plugin/WzComparerR2.CanvasViewer/。从 WzComparerR2/bin/Release/net8.0-windows/WzComparerR2.exe 启动即可。部署时要同时使用包含导航桥的主程序；不能仅将插件 DLL 放进未包含桥接代码的上游二进制。

## 验证

~~~powershell
dotnet build WzComparerR2.CanvasViewer.Tests/WzComparerR2.CanvasViewer.Tests.csproj -c Release -p:TargetFrameworks=net8.0-windows
dotnet WzComparerR2.CanvasViewer.Tests/bin/Release/net8.0-windows/WzComparerR2.CanvasViewer.Tests.dll "旧版UI.wz路径" "新版UI/UI.wz路径" "截图输出目录"
~~~

不传参数仅运行内存样本和基础控件检查。传入上述两个 WZ 后追加共享目录选择、旧新版解码、延迟加载、详情返回、节点导航、反向引用菜单及 PNG 导出/取消的集成检查（导出对话框由测试接管，逐像素核对保存结果）；最后一个截图目录参数可省略。测试只读取 WZ，不修改资源文件。测试窗口位于屏幕外，自动更新检查只在测试进程中禁用。

真实样本验证使用 GMS 95 UI.wz 和 CMS 228 的 UI 分包。
