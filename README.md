# MarkdownKit

MarkdownKit 是一个面向 WPF 的 Markdown 渲染类库：输入 Markdown 文本，输出只读的 `FlowDocument`。解析交给 [Markdig](https://github.com/xoofx/markdig)，排版、配色、图表绘制全部在本地完成，不依赖浏览器、Node 或 Java。

本库是 [MdViewPro](https://gitee.com/tea4go/mdview) 的渲染核心，也可以被其他 WPF 应用直接引用。

## 基于本项目开发的软件

[MdViewPro](https://gitee.com/tea4go/mdview) 是 Windows 上的 Markdown 查看器与编辑器，编辑、分屏和整屏预览一体，也能查看 PDF、Office 文档与 JSON / YAML。它的 Markdown 预览由 MarkdownKit 渲染，编辑、文件管理、PDF / Office 预览和云端图床等能力则由宿主实现。

## 主要特性

- 基于 Markdig 的 Markdown 解析，自绘 WPF 排版，颜色与字号跟随宿主主题。
- 代码块语法高亮、复制按钮、可选行号；长行不折行，超宽横向滚动。
- 表格、脚注双向跳转、任务列表、删除线、荧光笔、YAML front matter。
- LaTeX 数学公式：行内 `$...$` 与块级 `$$...$$`，解析交给 WpfMath。
- 图片按预览区宽度自适应（只缩不放），支持按设备档位设置宽度；附件可从文档目录或 `files` 目录查找。
- 网络图片支持宿主注入认证信息与证书校验策略；加载失败时绘制矢量破图占位。
- Obsidian 风格的 wiki 链接：`[[目标]]`、`[[目标|别名]]`、`[[目标#标题]]`。
- Mermaid：`sequenceDiagram`、`flowchart` / `graph`、`pie`。
- PlantUML：时序图、组件/部署图、状态图、活动图、类图/ER 图；思维导图、WBS、JSON/YAML/INI/TOML/XML 树形图、甘特图。
- 内嵌 SVG 与 `.svg` 图片文件矢量渲染，缩放不糊。
- 结构化文本（JSON、YAML、INI、TOML、XML）可画成可缩放、可选文字的树形图；JSON 解析兼容 JSON5 的单引号、裸键与续行字符串。
- 渲染失败时回退显示源码，不让一张坏图或坏公式拖垮整篇预览。

## 环境要求

| 项目 | 要求 |
| --- | --- |
| 运行时 | .NET Framework 4.7.2 |
| UI 框架 | WPF（`PresentationCore` / `PresentationFramework` / `WindowsBase`） |
| 语言版本 | C# 7.3 |
| 构建工具 | Visual Studio 2022 及以上（使用自带的 MSBuild） |
| 构建期依赖 | Markdig 0.37.0、WpfMath 2.1.0、Microsoft.NETFramework.ReferenceAssemblies 1.0.3 |

`Microsoft.NETFramework.ReferenceAssemblies` 通过 NuGet 提供 .NET Framework 引用程序集，构建时不必额外安装 Developer Pack。

## 构建

在 Visual Studio 中打开 `MarkdownKit.sln` 直接生成即可。命令行构建使用 Visual Studio 自带的 MSBuild，在 Developer Command Prompt 中执行：

```bat
:: 还原 NuGet 包
msbuild MarkdownKit.sln -t:Restore -p:Configuration=Release

:: 编译
msbuild MarkdownKit.sln -p:Configuration=Release
```

产物为 `bin\Release\MarkdownKit.dll`。

> 不要用 `dotnet build` 构建本项目。工程是旧式 csproj，`dotnet build` 虽能还原 NuGet 包，但不会把 PackageReference 解析成编译器引用，会报找不到 Markdig / WpfMath 类型。请使用 Visual Studio 的 MSBuild。

## 快速上手

```csharp
using System.Collections.Generic;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using MarkdownKit;

// 1. 配置主题快照。未配置时使用内置浅色主题，可直接调用 Render。
MarkdownTheme.Configure(new MarkdownStyle
{
    Markdown = MarkdownPalette.Light(),          // 或 MarkdownPalette.Dark()
    PreviewFamily = new FontFamily("Microsoft YaHei"),
    PreviewFontWeight = FontWeights.Normal,
    EditorFamily = new FontFamily("Consolas"),
    EditorFontSize = 0,                          // 0 表示按正文字号缩放
    EditorFontWeight = FontWeights.Normal,
    DiagramFamily = new FontFamily("Microsoft YaHei"),
    DiagramFontSize = 16,
    NoteFold = 14,
    IsDark = false,
    DefaultFont = "Microsoft YaHei",
    Background = Brushes.White,
    Foreground = Brushes.Black,
    Muted = Brushes.Gray,
    Border = Brushes.LightGray,
    Copied = Brushes.SeaGreen
});

// 2. 按需注入宿主能力（不设置则用内置中文文案、匿名下载、严格校验证书）。
MarkdownResources.Translate = key =>
{
    switch (key)
    {
        case "copyCode": return "复制代码";
        default: return null;                    // 返回空则回退内置文案
    }
};
MarkdownResources.ResolveImage = url =>
    new MarkdownImageRequest { Url = url, Authorization = null };
// 只用自签名图床时才注入 AcceptCertificate；不需要就保持 null，按系统规则校验证书。

// 3. 渲染。baseDir 是当前文档目录（未保存的新文档传 null）。
List<HeadingInfo> outline;
List<BlockAnchor> anchors;
FlowDocument document = MarkdownRenderer.Render(
    markdown,
    16,                                          // 正文字号
    @"C:\notes",                                 // 图片相对路径基准目录
    previewBox,                                  // 预览区控件，图片按它的宽度自适应
    target => { /* 处理站内链接，见 PreviewLinkTarget */ },
    out outline,
    out anchors);

previewBox.Document = document;

// 4. 结构化树形图：查找可缩放 / 可选文字的视图（没有则返回 null）。
IZoomableView zoom = MarkdownZoom.Find(document);
ISelectableTextView selectable = zoom as ISelectableTextView;
```

## 公共 API

### 渲染入口

`MarkdownRenderer`（静态类）

| 成员 | 说明 |
| --- | --- |
| `Render(string markdown, double fontSize)` | 最简渲染，返回空文档或渲染结果。 |
| `Render(string markdown, double fontSize, out List<HeadingInfo> outline)` | 渲染并收集标题大纲。 |
| `Render(string markdown, double fontSize, string baseDir, FrameworkElement imageHost, Action<PreviewLinkTarget> linkOpener, out List<HeadingInfo> outline, out List<BlockAnchor> anchors)` | 完整渲染。`baseDir` 用于解析图片相对路径；`imageHost` 用于图片自适应宽度；`linkOpener` 处理站内链接；`anchors` 收集顶层块的源码起始行。 |
| `Outline(string markdown)` | 只解析标题，不排版。适合编辑模式的大纲。 |
| `RenderPlain(string text, double fontSize)` | 把纯文本原样铺开，不解析 Markdown。适合流式输出的中间态。 |
| `StrictLineBreak`（静态字段） | 为真时段落内单个换行也断行。默认 false。 |
| `SynchronousRemoteImages`（静态字段） | 为真时在 `Render` 返回前同步下载网络图片。导出等一次性排版场景使用；预览保持 false。 |

`HeadingInfo`：`Level`（1~6）、`Text`（纯文本）、`SourceLine`（0 起）、`Target`（预览渲染时的 `Paragraph`，编辑模式为 null）、`Page`（宿主 PDF 书签预留，默认 -1）。

`BlockAnchor`：`SourceLine`（0 起）与对应的 WPF `Block`，用于编辑区与预览区的按行滚动联动。

### 主题

- `MarkdownPalette`：整套配色。内置 `Light()` / `Dark()`；也可自行构造。
- `MarkdownStyle`：主题输入，包含配色、正文字体、代码字体与字号、图内字体与字号、明暗标记及通用画刷。
- `MarkdownTheme`：渲染层读取的静态主题快照。主题或字号变化时调用 `Configure(style)` 刷新；`BoldTypeface(family)` 会挑选一套真正带粗体字形的字体。

### 宿主钩子

`MarkdownResources`（静态类）：

| 成员 | 说明 |
| --- | --- |
| `Translate` | 文案翻译函数。内置 key 有 `copyCode`、`codeLineNumbers`、`footnoteJump`、`footnoteBack`。返回空则回退内置中文。 |
| `ResolveImage` | 按图片地址返回 `MarkdownImageRequest`，用于私有图床的 URL 改写与 `Authorization`。返回 null 表示匿名原样下载。 |
| `AcceptCertificate` | 下载网络图片时的证书校验回调。null 表示按系统规则校验。 |

### 其他公共类型

| 类型 | 用途 |
| --- | --- |
| `MarkdownAssets` | 附件目录约定，`ImageDir = "files"`。 |
| `PreviewLinkTarget` | 站内链接目标：wiki 名称、相对路径、锚点、脚注目标等。 |
| `WikiLinkParser` / `WikiLinkExtension` / `WikiLinkInline` | wiki 链接的 Markdig 解析扩展。 |
| `SyntaxHighlighter` | 可复用的代码高亮器：`Highlight(code, language)` 返回 `IReadOnlyList<CodeToken>`；`Resolve(language)` 取词法规则；`FamilyOf(language)` 取语言族。 |
| `TokenKind` / `CodeToken` / `SyntaxFamily` | 高亮片段类型、片段结构、语言词法族。 |
| `MarkdownZoom` / `IZoomableView` / `ISelectableTextView` | 从渲染结果中查找可缩放、可选文字的视图（目前是树形图）。 |
| `MarkdownStructured` | 命名空间 `MarkdownKit.PlantUml`；`CanDrawTree(text, format)` 判断结构化文本能否画成树。 |

## 支持的 Markdown 语法与扩展

### 基础语法

表格（管道表）、YAML front matter（以键值形式展示）、脚注、任务列表、删除线 `~~...~~`、荧光笔 `==...==`。

渲染管线只启用上述扩展，刻意不使用 Markdig 的 `UseAdvancedExtensions()`：它会打开字母/罗马数字有序列表标记，把「n. 例子」「v. 举例」这类词典释义行拆成嵌套列表。

### 图片

```markdown
![说明](图.jpg)
![说明](files/图.jpg)

<!-- 旧写法：按预览区宽度百分比封顶 -->
![说明](图.jpg){width=50%}

<!-- 新写法：一次给出多档宽度，按预览区物理宽度选档 -->
![说明](图.jpg)<!--{width=50% phone_width=80% pad_width=30% pc_width=10%}-->
```

- 图片查找顺序：绝对路径原样使用；相对路径先按 `baseDir`（当前文档目录）解析；再找 `baseDir/files/`；最后从上一级目录开始最多向上 8 级查找 `files/`。因此文档放在库的二级、三级子目录里时，只写文件名也能找到库根的附件。
- 未保存的新文档没有 `baseDir`，相对图片路径无法解析。
- 设备档位按预览区内容宽度划分：小于 7 英寸为手机，7~14 英寸为平板，14 英寸及以上为 2in1 电脑。未写的档位回退到 `width`。
- 所有宽度都是上限：图片本身比上限窄时保持原始尺寸，不放大。
- 网络图片通过 `MarkdownResources.ResolveImage` 注入认证；下载失败会显示矢量破图图标。

### Wiki 链接

```markdown
[[目标]]
[[目标|显示文本]]
[[目标#标题]]
```

与 Obsidian 的 wiki 链接语法一致。只识别 `[[ ]]`，普通 `[文字](地址)` 链接不受影响。点击行为交给宿主提供的 `linkOpener`。

### 代码块

- 围栏语言标识决定高亮规则，例如 ` ```csharp `、` ```json5 `。
- 支持的语言族：C/C++/C#、Java、JavaScript/TypeScript、Go、Rust、PHP、Swift、Kotlin、Scala、Dart、Groovy、Objective-C、Python、Shell/Bash/批处理、Ruby、Perl、YAML、INI、TOML、PowerShell、Dockerfile、Makefile、Pascal/Delphi、Visual Basic、CSS/SCSS/Less、R、Lua、SQL 家族、JSON/JSONC/JSON5、HTML/XML/XAML/SVG/Vue。
- 代码块带复制按钮；行号默认收起，点代码块上的「行号」按钮才展开；长行不折行，超宽时横向滚动，滚动条悬停才显示。
- 语言标识为 `mermaid` 或 `plantuml` / `puml` / `uml` 时，会尝试画成图；画不出来就退回普通代码块。

### 数学公式

- 行内公式：`$E=mc^2$`
- 块级公式：独占一段的 `$$...$$`

认不出的公式会原样显示源码。常见别名 `\dfrac`、`\tfrac`、`\ `、`\quad`、`\qquad` 会在解析前做等价替换。

### Mermaid

| 图型 | 说明 |
| --- | --- |
| `sequenceDiagram` | 时序图，翻译成 PlantUML 时序模型后复用同一套布局与绘制。 |
| `flowchart` / `graph` | 分层布局的流程图。支持矩形、圆角、菱形、圆形节点，实线/虚线、箭头与连线标签。不支持子图、样式类、一条线串三个以上节点。 |
| `pie` | 饼图，扇形加图例。 |

其他 Mermaid 图型返回 null，回退显示源码。

### PlantUML

代码块语言写 `plantuml` / `puml` / `uml`，或内容本身以 `@startuml` 开头即可识别。全部由本库解析、布局和矢量绘制，不依赖 Java，也不访问网络。

| 图型 | 写法 |
| --- | --- |
| 时序图 | `@startuml ... @enduml` |
| 组件/部署图 | `@startuml` 内使用 `[组件]`、`component`、`package` 等 |
| 状态图 | `@startuml` 内使用 `state`、`[*]` 等 |
| 活动图 | `@startuml` 内使用 `start`、`stop`、`:动作;`、`if ... then` 等 |
| 类图 / ER 图 | `@startuml` 内使用 `class`、`interface`、`entity`、`<<PK>>` 等 |
| 思维导图 | `@startmindmap` |
| WBS | `@startwbs` |
| JSON 树 | `@startjson` |
| YAML 树 | `@startyaml` |
| INI 树 | `@startini` |
| TOML 树 | `@starttoml` |
| XML 树 | `@startxml` |
| 甘特图 | `@startgantt` |

同一份 `@startuml` 源码会按多种图型分别解析并打分，选择最匹配的一种；都不像时返回 null，回退代码块。

树形图支持缩放（放大、缩小、1:1）以及图上文字拖选复制，宿主通过 `MarkdownZoom.Find` 与 `IZoomableView` / `ISelectableTextView` 接入。甘特图只做静态渲染，不走这套接口。

### SVG

- 原始 HTML 块里的内嵌 `<svg>...</svg>` 会画成矢量图，宽度按预览区自适应。
- `.svg` 图片文件同样走矢量渲染。
- 支持元素：`svg`、`g`、`rect`、`circle`、`ellipse`、`line`、`polyline`、`polygon`、`path`、`text`、`tspan`。
- 支持样式继承、`viewBox` 等比缩放与 `transform`。
- 不支持脚本、外部引用、渐变、滤镜、裁剪与 `use`。

### 结构化文本树形图

`MarkdownStructured.CanDrawTree(text, format)` 接受 `json`、`yaml`/`yml`、`ini`/`conf`、`toml`、`xml` 等格式标识。判定可以画之后，宿主把原文包成对应的 `@start<kind>` 代码块交给 `MarkdownRenderer.Render`，即可得到树形图。顶层标量、空对象等画不出树的内容会返回 false。

## 渲染管线

一次 `Render` 的主流程：

1. `Markdig.Markdown.Parse(markdown, Pipeline)` 解析成 Markdig 语法树。
2. `ImageWidthAttribute.Apply` 把图片后面的 `{width=...}` / HTML 注释宽度标记摘下来，挂到图片节点上。
3. `AddBlocks` 逐块转换为 WPF 排版元素：
   - 标题、段落、列表、引用、表格、脚注；
   - 代码块走语法高亮，并尝试 Mermaid / PlantUML；
   - 数学公式走 WpfMath；
   - 图片走 `MarkdownImage`，SVG 走 `SvgRenderer`。
4. `LinkFootnotes` 把正文引用与文末脚注条目互相挂接，得到双向跳转。
5. 返回 `FlowDocument`，同时填充 `outline` 与 `anchors`。

渲染期间的图片目录、宿主控件、链接回调、脚注登记表暂存在静态字段里，`try/finally` 中清理。因此：

> `MarkdownRenderer.Render` 必须在 UI 线程上串行调用，不能并发重入。

## 主题与配色

`MarkdownPalette` 的颜色按用途分组：

- 正文与标题：`Text`、`Heading`、`HeadingRule1`、`HeadingRule2`、`Link`、`MarkBackground`。
- 引用：`QuoteText`、`QuoteBorder`、`QuoteBackground`。
- 代码：`CodeBackground`、`CodeBorder` 及各类 token 颜色（关键字、字符串、注释、数字、函数、类型、标签、属性、键名、段落名、变量）。
- 行内代码：`InlineCodeBackground`、`InlineCodeText`。
- 表格：`TableBackground`、`TableBorder`、`TableHeaderBackground`、`TableEvenRowBackground`。
- 其他：`SegmentFill`。

`MarkdownStyle` 还可单独设置代码字号（`EditorFontSize`），0 表示按正文字号缩放；图内字号固定使用 `DiagramFontSize`，保证形状常量与布局比例稳定。

主题切换后需要重新 `Configure` 并重新 `Render`，已生成的 `FlowDocument` 不会自动换色。

## 目录结构

```
MarkdownKit/
├─ MarkdownKit.sln
├─ MarkdownKit.csproj
├─ MarkdownRenderer.cs         渲染入口与各类块的 WPF 排版（核心文件）
├─ MarkdownTheme.cs            渲染层读取的主题静态快照
├─ MarkdownStyle.cs            宿主传入的主题配置
├─ MarkdownPalette.cs          明暗两套配色与全部颜色字段
├─ MarkdownResources.cs        文案、图片认证、证书校验钩子
├─ MarkdownAssets.cs           附件目录名约定（files）
├─ MarkdownImage.cs            图片控件：宽度上限与设备档位缩放
├─ ImageWidthAttribute.cs      图片宽度标记解析
├─ WikiLink.cs                 wiki 链接 Markdig 扩展
├─ SyntaxHighlighter.cs        代码语法高亮
├─ BrokenImageIcon.cs          破图占位矢量图标
├─ Formula/
│  ├─ FormulaRenderer.cs       LaTeX 公式渲染入口（WpfMath）
│  └─ FormulaElement.cs        公式元素排版与基线计算
├─ Mermaid/
│  ├─ MermaidRenderer.cs       Mermaid 渲染入口
│  ├─ MermaidSequenceParser.cs 时序图解析
│  ├─ MermaidFlowchart.cs      流程图解析
│  ├─ MermaidFlowchartView.cs  流程图布局与绘制
│  ├─ MermaidPie.cs            饼图解析
│  └─ MermaidPieView.cs        饼图绘制
├─ PlantUml/
│  ├─ PlantUmlRenderer.cs      PlantUML 渲染入口与图型判定
│  ├─ PlantUmlPreprocessor.cs  预处理
│  ├─ Sequence*.cs             时序图模型、解析、布局
│  ├─ PlantUmlSequenceView.cs  时序图视图
│  ├─ Component*.cs            组件/部署图与类图/ER 图
│  ├─ State*.cs                状态图
│  ├─ Activity*.cs             活动图
│  ├─ Gantt*.cs                甘特图
│  ├─ Tree*.cs                 思维导图 / WBS / JSON / YAML / INI / TOML / XML 树
│  ├─ ConfigTreeReaders.cs     结构化文本读取
│  ├─ DiagramZoom.cs           缩放与文字选择接口
│  ├─ LegendBox.cs             图例
│  └─ StructuredTreeCheck.cs   结构化文本能否画树的判定
└─ Svg/
   └─ SvgRenderer.cs           SVG 转 WPF DrawingImage
```

## 开发约定

1. **新增源文件要手动登记**。工程是旧式 csproj，没有通配符包含；新增 `.cs` 文件必须同时写进 `MarkdownKit.csproj` 的 `<Compile Include="..."/>`，否则不会参与编译。
2. **语言版本是 C# 7.3**。不要使用 C# 8 及以上的语法（如 switch expression、可空引用类型、`using` 声明、范围运算符等）。
3. **渲染失败要回退，不要抛异常**。图表、公式、SVG 无法识别时返回 null，由上层退回代码块或原始文本。
4. **颜色从主题取**。新增渲染元素时通过 `MarkdownTheme` / `MarkdownPalette` 取色，不要在渲染代码里写死颜色，否则深色主题会失效。
5. **保持 UI 线程串行**。渲染上下文是静态字段，不支持并发调用；异步图片解码等操作必须回到 UI 线程再更新文档。
6. **扩展 Markdig 管线要谨慎**。优先单独启用需要的扩展，避免 `UseAdvancedExtensions()` 改变普通正文的解析行为。
7. **图内尺寸使用固定基准字号**。形状常量按 `DiagramFontSize` 量出来，调整字号时布局按比例缩放，不要为某一张图单独改常量。

### 常见扩展点

| 需求 | 入口 |
| --- | --- |
| 增加代码高亮语言 | `SyntaxHighlighter` 中的语言别名 `switch` 与 `Syntax` 定义 |
| 增加 Mermaid 图型 | `MermaidRenderer.TryCreate` 增加分派，并新增解析/视图 |
| 增加 PlantUML 图型 | `PlantUmlRenderer.TryCreate` 与对应 Parser / Layout / View |
| 调整图片尺寸策略 | `MarkdownImage` 与 `ImageWidthAttribute` |
| 增加 Markdown 语法 | `MarkdownRenderer` 顶部的 `Pipeline` 与 `AddBlocks` 分派 |
| 暴露新的宿主能力 | `MarkdownResources` 增加静态钩子，或在 `MarkdownStyle` 增加配置项 |

## 已知边界

- 本库只负责渲染成只读 `FlowDocument`，不含编辑器、文件管理、PDF 导出；这些由宿主实现。
- HTML 块只识别内嵌 `<svg>`，其余 HTML 不渲染。
- Mermaid / PlantUML 支持的是常用子集，复杂语法会回退成代码块显示。
- 图片宽度标记只认「紧跟在图片后面」的位置，不会误伤公式或代码里的花括号。
- 远程图片下载、认证与证书策略依赖宿主注入；默认匿名下载并严格校验证书。
- 主题、字号变化后需重新渲染；已有文档不会自动更新配色。

## 开源协议

本项目采用 [MIT License](LICENSE) 发布。复制、修改、分发本项目代码或二进制时，请保留 `LICENSE` 中的版权声明和许可声明。
