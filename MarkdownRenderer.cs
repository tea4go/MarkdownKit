using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
// 本文件里的 Path 是画图用的 Shapes.Path，IO 的路径操作另起别名
using IOPath = System.IO.Path;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Footnotes;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MarkdownKit.Formula;
using MarkdownKit.Mermaid;
using MarkdownKit.PlantUml;
using MdBlock = Markdig.Syntax.Block;
using MdInline = Markdig.Syntax.Inlines.Inline;
using MdMath = Markdig.Extensions.Mathematics.MathInline;
using MdMathBlock = Markdig.Extensions.Mathematics.MathBlock;
using MdTable = Markdig.Extensions.Tables.Table;
using MdTableCell = Markdig.Extensions.Tables.TableCell;
using MdTableRow = Markdig.Extensions.Tables.TableRow;
// Markdig 的 Markdown 块和 WPF 的文档块同名，两个都要用，只能给 WPF 这个起别名
using WpfBlock = System.Windows.Documents.Block;
// 我们自己的 UI 文案类跟 WPF 的 System.Windows.Localization 同名，这里显式指过去

namespace MarkdownKit
{
    /// <summary>
    /// 把 Markdown 渲染成只读的 FlowDocument，配色和字体跟随当前主题。
    /// 解析交给 Markdig，排版自己做，这样颜色、字号、间距能跟窗口的明暗主题和基准字号对齐。
    /// 认不出的块（HTML 块、引用定义等）直接跳过，不往界面上塞东西。
    /// </summary>
    public static class MarkdownRenderer
    {
        /// <summary>
        /// 只开表格、YAML 文件头、数学公式、脚注、删除线、荧光笔这几个扩展。不能用 UseAdvancedExtensions()：
        /// 它会打开字母/罗马数字的有序列表标记，而词典格式的释义行正是「n. 例子」「v. 举例」开头，
        /// 会被当成嵌套列表整个吃掉。
        /// 数学扩展只往解析器表里插一个认 $ 的行内解析器和一个认 $$ 的块解析器，不碰列表，可以单独开。
        /// 脚注同理，只认 [^x] 引用和 [^x]: 定义，同样不碰列表。
        /// 删除线和荧光笔也一样，只给 ~~ 和 == 这两个双字符记号开一条路；同一家的 ^上标^、~下标~、++插入++
        /// 都不开——单字符的 ~ 和 ^ 在正文里太常见（波浪线、LaTeX 片段），开了反而误伤。
        /// wiki 链接是自己写的扩展（Markdig 没带），同样只插一个认 [ 的行内解析器。
        /// </summary>
        private static readonly MarkdownPipeline Pipeline =
            new MarkdownPipelineBuilder().UsePipeTables().UseYamlFrontMatter().UseMathematics()
                .UseFootnotes().UseTaskLists()
                .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough | EmphasisExtraOptions.Marked)
                .Use<WikiLinkExtension>().Build();

        /// <summary>图片相对路径的基准目录（当前文档所在目录），只在一次 Render 期间有效。</summary>
        private static string _imageBaseDir;

        /// <summary>
        /// 文档库放图片的固定目录名。像云上笔记这样的工具把附件统一堆在库根的 files 下，
        /// 文档里只写一个图片文件名；左侧文件列表与搜索同样按这个名字跳过它（FolderSearch.IsIgnoredDirectory）。
        /// 名字只写在一处（MarkdownAssets.ImageDir），编辑区「粘贴图片」也照它落盘。
        /// </summary>
        private const string LibraryImageDir = MarkdownAssets.ImageDir;

        /// <summary>
        /// 往上找 files 目录最多看几级。图真在库里，库根通常就在文档上面一两层；
        /// 翻到第八级还没有，多半是这张图根本不在库中，再往上找也白搭。
        /// </summary>
        private const int LibraryImageSearchDepth = 8;

        /// <summary>
        /// 严格换行开关：为真时同一段落里的单个换行也断行。由主窗口在每次渲染前从 Settings 同步过来，
        /// 默认 false（软换行按空格，跟 Markdown 规范一致）。
        /// </summary>
        public static bool StrictLineBreak;

        /// <summary>预览区控件，图片宽度按它自适应，只在一次 Render 期间有效。</summary>
        private static FrameworkElement _imageHost;

        /// <summary>站内链接（[[name]]、相对 .md、#锚点）的点开动作，只在一次 Render 期间有效。</summary>
        private static Action<PreviewLinkTarget> _linkOpener;

        /// <summary>
        /// 本轮渲染里每个脚注编号的正文引用（同一条脚注可能被引用多次），只在一次 Render 期间有效。
        /// 引用排在正文里、条目排在文末，渲染引用时目标还没排出来，两头都先登记着，见 LinkFootnotes。
        /// </summary>
        private static Dictionary<int, List<Hyperlink>> _footnoteReferences;

        /// <summary>本轮渲染里每个脚注编号在文末的条目容器，只在一次 Render 期间有效。</summary>
        private static Dictionary<int, Section> _footnoteItems;

        /// <summary>H1~H6 相对正文字号的倍数。</summary>
        private static readonly double[] HeadingScale = { 1.7, 1.5, 1.3, 1.15, 1.05, 1.0 };

        /// <summary>把 Markdown 文本渲染成 FlowDocument。空文本返回一个空文档。</summary>
        public static FlowDocument Render(string markdown, double fontSize)
        {
            List<HeadingInfo> ignored;
            List<BlockAnchor> ignoredAnchors;
            return Render(markdown, fontSize, null, null, null, out ignored, out ignoredAnchors);
        }

        /// <summary>渲染，同时按文档顺序收出标题。outline 里的条目带渲染出的段落，预览跳转靠它定位。</summary>
        public static FlowDocument Render(string markdown, double fontSize, out List<HeadingInfo> outline)
        {
            List<BlockAnchor> ignoredAnchors;
            return Render(markdown, fontSize, null, null, null, out outline, out ignoredAnchors);
        }

        /// <summary>
        /// 渲染并收标题。baseDir 是图片相对路径的基准（当前文档所在目录，未保存的新文档传 null）；
        /// imageHost 是预览区控件，图片宽度按它的宽度自适应，传 null 则不限宽。
        /// anchors 收每个顶层块在源码里的起始行，预览按源码行定位（编辑切预览时对齐位置）靠它。
        /// linkOpener 是站内链接的点开动作（[[name]]、相对 .md、#锚点），传 null 则这类链接不可点。
        /// </summary>
        public static FlowDocument Render(string markdown, double fontSize, string baseDir,
            FrameworkElement imageHost, Action<PreviewLinkTarget> linkOpener,
            out List<HeadingInfo> outline, out List<BlockAnchor> anchors)
        {
            outline = new List<HeadingInfo>();
            anchors = new List<BlockAnchor>();
            FlowDocument document = Base(fontSize);
            if (string.IsNullOrEmpty(markdown))
                return document;

            // 图片要按当前文档目录解析相对路径、按预览区宽度定尺寸，这两个上下文一路穿到排版深处太啰嗦。
            // 渲染是 UI 线程串行的一次调用、不会重入，就暂存在字段里，出来时清掉。
            _imageBaseDir = baseDir;
            _imageHost = imageHost;
            _linkOpener = linkOpener;
            _footnoteReferences = new Dictionary<int, List<Hyperlink>>();
            _footnoteItems = new Dictionary<int, Section>();
            try
            {
                MarkdownDocument parsed = Markdig.Markdown.Parse(markdown, Pipeline);
                // 先把 `{width=50%}` 这类宽度标记摘下来挂到图片节点上，再排版
                ImageWidthAttribute.Apply(parsed);
                AddBlocks(document.Blocks, parsed, fontSize, outline, anchors);
                LinkFootnotes();
            }
            finally
            {
                _imageBaseDir = null;
                _imageHost = null;
                _linkOpener = null;
                _footnoteReferences = null;
                _footnoteItems = null;
            }
            return document;
        }

        /// <summary>
        /// 只解析标题，不排版。编辑模式下的大纲走这条路——标题不带段落引用，
        /// 跳转按源码行号定位，省得为了拿标题白渲染一份文档。
        /// </summary>
        public static List<HeadingInfo> Outline(string markdown)
        {
            var headings = new List<HeadingInfo>();
            if (string.IsNullOrEmpty(markdown))
                return headings;

            CollectHeadings(Markdig.Markdown.Parse(markdown, Pipeline), headings);
            return headings;
        }

        /// <summary>
        /// 把文本原样铺成纯文本，不解析 Markdown。流式翻译边收边显示时用这个：
        /// 每来一段都重新解析一遍 Markdown 既费时，又会在半截语法上抖（表格没写完、
        /// 代码围栏没闭合），等整段回来再换成 Render 出来的 Markdown 版。
        /// </summary>
        public static FlowDocument RenderPlain(string text, double fontSize)
        {
            FlowDocument document = Base(fontSize);
            if (string.IsNullOrEmpty(text))
                return document;

            var paragraph = new Paragraph { Margin = new Thickness(0) };
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                    paragraph.Inlines.Add(new LineBreak());
                paragraph.Inlines.Add(new Run(lines[i]));
            }
            document.Blocks.Add(paragraph);
            return document;
        }

        /// <summary>文档外壳：主题字体、正文字号与颜色。纯文本和 Markdown 两条路共用，切过去时才不会跳版。</summary>
        private static FlowDocument Base(double fontSize)
        {
            return new FlowDocument
            {
                FontFamily = MarkdownTheme.PreviewFamily,
                FontSize = fontSize,
                // 恒为 Normal：正文要是跟「预览粗体」走，**加粗** 就跟普通文字一个样了。
                // 钉死也是防 WPF 的默认值跟随系统「消息字体」——系统设成粗体时整份文档都会变粗。
                FontWeight = FontWeights.Normal,
                Foreground = MarkdownTheme.Markdown.Text,
                Background = Brushes.Transparent,
                PagePadding = new Thickness(0),
                // 不设的话 FlowDocument 会按默认列宽分成好几栏
                ColumnWidth = double.PositiveInfinity,
                TextAlignment = TextAlignment.Left
            };
        }

        // depth 是列表嵌套层数：0 是顶层，进一层 ListItem 的嵌套列表加一。项目符号按层轮换，
        // 跟浏览器/Word 的默认一样（一级实心 ●、二级空心 ○、三级方块 ■），三级以后又从 ● 开始。
        // 不轮换的话各级都是实心圆点，缩进一挤就分不清哪一行属于哪一级。
        private static void AddBlocks(BlockCollection target, ContainerBlock source, double fontSize,
            List<HeadingInfo> outline, List<BlockAnchor> anchors = null, int depth = 0)
        {
            // 脚注定义那组在源码里可能夹在正文中间，但显示上要归到文末当参考文献。
            // Markdig 把它插在第一条定义所在的位置，这里先攒着，等这一层其余块排完再补到末尾。
            WpfBlock deferredFootnotes = null;
            foreach (MdBlock block in source)
            {
                WpfBlock rendered = ToBlock(block, fontSize, outline, depth);
                if (rendered == null)
                    continue;
                if (block is FootnoteGroup)
                {
                    deferredFootnotes = rendered;
                    continue;
                }
                target.Add(rendered);
                // 只有顶层那次调用带 anchors（嵌套的引用、列表项不传），粒度到段落，够定位用
                if (anchors != null)
                    anchors.Add(new BlockAnchor { SourceLine = block.Line, Target = rendered });
            }
            // 这组被挪到了末尾，行号却还停在定义处，收进 anchors 会把后面的定位带偏，索性不收
            if (deferredFootnotes != null)
                target.Add(deferredFootnotes);
        }

        private static WpfBlock ToBlock(MdBlock block, double fontSize, List<HeadingInfo> outline, int depth = 0)
        {
            // 文件头要排在 CodeBlock 前面：Markdig 的 YamlFrontMatterBlock 也归在代码块那一支，
            // 放到后面会被 Code() 抢去渲染成带复制按钮的代码块
            var frontMatter = block as Markdig.Extensions.Yaml.YamlFrontMatterBlock;
            if (frontMatter != null)
                return FrontMatter(frontMatter, fontSize);

            // 数学块和文件头一样也归在 FencedCodeBlock 那一支，同样得排在 CodeBlock 前面，
            // 否则会被 Code() 渲染成带复制按钮的代码块
            var mathBlock = block as MdMathBlock;
            if (mathBlock != null)
                return FormulaBlock(mathBlock, fontSize);

            var heading = block as HeadingBlock;
            if (heading != null)
            {
                WpfBlock rendered = Heading(heading, fontSize);
                if (outline != null)
                {
                    HeadingInfo info = Describe(heading);
                    info.Target = rendered as Paragraph;
                    outline.Add(info);
                }
                return rendered;
            }

            var paragraph = block as ParagraphBlock;
            if (paragraph != null)
                return Paragraph(paragraph, fontSize);

            var list = block as ListBlock;
            if (list != null)
                return List(list, fontSize, outline, depth);

            var quote = block as QuoteBlock;
            if (quote != null)
                return Quote(quote, fontSize, outline, depth);

            // FencedCodeBlock 也是 CodeBlock，围栏和缩进两种代码块一起接
            var code = block as CodeBlock;
            if (code != null)
            {
                WpfBlock chart = MermaidBlock(code, fontSize);
                if (chart != null)
                    return chart;

                WpfBlock diagram = PlantUmlBlock(code, fontSize);
                return diagram ?? Code(code, fontSize);
            }

            var rule = block as ThematicBreakBlock;
            if (rule != null)
                return Rule(fontSize);

            var table = block as MdTable;
            if (table != null)
                return Table(table, fontSize);

            // 文末那组脚注定义，排成一节「[n] 正文」
            var footnotes = block as FootnoteGroup;
            if (footnotes != null)
                return FootnoteList(footnotes, fontSize);

            // 原始 HTML 块：只有 <table> 认，其余照旧跳过（返回 null 由调用方丢弃）
            var htmlBlock = block as HtmlBlock;
            if (htmlBlock != null)
                return HtmlTable(htmlBlock, fontSize);

            return null;
        }

        private static WpfBlock Heading(HeadingBlock heading, double fontSize)
        {
            int level = Math.Max(1, Math.Min(6, heading.Level));
            var paragraph = new Paragraph
            {
                FontSize = fontSize * HeadingScale[level - 1],
                FontWeight = FontWeights.Bold,
                Foreground = MarkdownTheme.Markdown.Heading,
                Margin = new Thickness(0, fontSize * (level <= 2 ? 0.9 : 0.6), 0, fontSize * 0.35)
            };
            // H1/H2 底下压一条通栏横线：H1 粗、用标题色，H2 细、用浅灰，H3 往下不画
            if (level == 1)
            {
                paragraph.BorderBrush = MarkdownTheme.Markdown.HeadingRule1;
                paragraph.BorderThickness = new Thickness(0, 0, 0, Math.Round(fontSize * 0.12));
                paragraph.Padding = new Thickness(0, 0, 0, fontSize * 0.3);
            }
            else if (level == 2)
            {
                paragraph.BorderBrush = MarkdownTheme.Markdown.HeadingRule2;
                paragraph.BorderThickness = new Thickness(0, 0, 0, 1);
                paragraph.Padding = new Thickness(0, 0, 0, fontSize * 0.2);
            }
            AddInlines(paragraph.Inlines, heading.Inline, paragraph.FontSize);
            // 标题字号比正文大，公式也跟着大，行高一样要撑开
            FormulaLineHeight(paragraph, paragraph.FontSize);
            return paragraph;
        }

        /// <summary>标题的层级、纯文本与源码行号。渲染路径下再补上段落引用。</summary>
        private static HeadingInfo Describe(HeadingBlock heading)
        {
            return new HeadingInfo
            {
                Level = Math.Max(1, Math.Min(6, heading.Level)),
                Text = InlineText(heading.Inline),
                SourceLine = heading.Line
            };
        }

        /// <summary>按文档顺序收标题，供编辑模式用（不排版，所以没有段落引用）。</summary>
        private static void CollectHeadings(ContainerBlock source, List<HeadingInfo> target)
        {
            foreach (MdBlock block in source)
            {
                var heading = block as HeadingBlock;
                if (heading != null)
                {
                    target.Add(Describe(heading));
                    continue;
                }
                var container = block as ContainerBlock;
                if (container != null)
                    CollectHeadings(container, target);
            }
        }

        /// <summary>把标题的行内内容拼成纯文本，丢掉 #、**、` 这些标记。</summary>
        private static string InlineText(ContainerInline inline)
        {
            var text = new StringBuilder();
            AppendInlineText(inline, text);
            return text.ToString().Trim();
        }

        private static void AppendInlineText(ContainerInline container, StringBuilder target)
        {
            if (container == null)
                return;
            for (MdInline inline = container.FirstChild; inline != null; inline = inline.NextSibling)
            {
                var literal = inline as LiteralInline;
                if (literal != null)
                {
                    target.Append(literal.Content.ToString());
                    continue;
                }
                var code = inline as CodeInline;
                if (code != null)
                {
                    target.Append(code.Content);
                    continue;
                }
                // 大纲条目和图片 alt 里出现公式时取裸源码，不带 $ 定界符。
                // 不接这一支的话标题会静默丢字
                var math = inline as MdMath;
                if (math != null)
                {
                    target.Append(math.Content.ToString());
                    continue;
                }
                // wiki 链接在标题里取显示文本，别把 [[ ]] 带进大纲
                var wiki = inline as WikiLinkInline;
                if (wiki != null)
                {
                    target.Append(wiki.Label);
                    continue;
                }
                var nested = inline as ContainerInline;
                if (nested != null)
                    AppendInlineText(nested, target);
            }
        }

        private static WpfBlock Paragraph(ParagraphBlock source, double fontSize)
        {
            // 整段只有一个图片时排成独立一块：既贴合「图片独占一行」的观感，
            // 也让宽度自适应走块级布局（行内的 InlineUIContainer 拿不到栏宽）
            LinkInline image = SoleImage(source.Inline);
            if (image != null)
                return ImageBlock(image, fontSize);

            var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, fontSize * 0.4) };
            AddInlines(paragraph.Inlines, source.Inline, fontSize);
            FormulaLineHeight(paragraph, fontSize);
            return paragraph;
        }

        /// <summary>
        /// 段落里有行内公式时把行高撑开。
        ///
        /// 公式的降部是画在控件外面的（见 FormulaElement 的说明），而行盒只按控件高度留位：
        /// 实测 BaselineAlignment.Bottom 只把元素底边贴到行盒下沿，撑不大行盒，所以降部会压到
        /// 下一行 / 表格下一行的字上。LineHeight 是纯布局手段，给多少行盒就至少多高，
        /// 所以按「最高那个公式的完整墨迹高度 + 正文字降部」撑开。代价是这一段的每一行都变高，
        /// 表格单元格里本来就只有一行，正好；正文段落里公式多的时候会松一点，但不会压字。
        /// </summary>
        private static void FormulaLineHeight(Paragraph paragraph, double fontSize)
        {
            double ink = MaxFormulaInk(paragraph.Inlines);
            if (ink <= 0)
                return;
            paragraph.LineHeight = ink + FormulaElement.TextDescent(fontSize);
        }

        /// <summary>取行内最高的公式的完整墨迹高度；没有公式返回 0。链接、强调这些容器要递归进去。</summary>
        private static double MaxFormulaInk(InlineCollection inlines)
        {
            double max = 0;
            foreach (System.Windows.Documents.Inline inline in inlines)
            {
                var container = inline as InlineUIContainer;
                if (container != null)
                {
                    var formula = container.Child as FormulaElement;
                    if (formula != null)
                        max = Math.Max(max, formula.InkHeight);
                    continue;
                }
                var span = inline as Span;
                if (span != null)
                    max = Math.Max(max, MaxFormulaInk(span.Inlines));
            }
            return max;
        }

        private static WpfBlock List(ListBlock source, double fontSize, List<HeadingInfo> outline, int depth = 0)
        {
            // 序号/项目符号画在条目文字左边，占的宽度由这段左缩进让出来：缩进不够，序号会伸到
            // 文档左边缘之外被预览区裁掉半截。一位数序号要 1.55 倍字宽上下，这里给 1.6 倍；
            // 位数越多序号越宽，两位数以上再多让半个字宽，免得首位被裁掉。
            int count = 0;
            foreach (MdBlock child in source)
            {
                if (child is ListItemBlock)
                    count++;
            }
            int digits = count.ToString().Length;

            // 任务列表（- [x] / - [ ]）自带勾选框，别再画一个圆点/序号在前面，
            // 否则会变成「• ☑ 文字」。整条列表只要有一条是任务项就按任务列表排。
            bool taskList = false;
            foreach (MdBlock child in source)
            {
                var item = child as ListItemBlock;
                if (item != null && IsTaskItem(item))
                {
                    taskList = true;
                    break;
                }
            }

            // WPF 把项目符号/序号画在「内容起点」的左边，而任务列表的勾选框正好压在内容起点上，
            // 于是普通列表看着比任务列表靠左一整列。这里给它多让出一个标记宽度，
            // 符号就会落到勾选框那一列，正文也跟着对齐。
            double extra = taskList ? 0 : fontSize * 1.0;
            double indent = fontSize * (source.IsOrdered ? 1.6 + 0.5 * (digits - 1) : 1.6) + extra;

            // 无序列表的符号不用 WPF 自带的那套 TextMarkerStyle：它取的字形跟着正文字体走，
            // 换个主题/字体，三个符号的粗细和大小就变样（比如 ■ 在某些字体里是空心细框）。
            // 这里固定用微软雅黑，字号比正文小 2 号，三个符号的墨迹大小才稳定、互相配套。
            bool bullets = !taskList && !source.IsOrdered;
            double column = bullets ? BulletColumn(fontSize) : 0;

            var list = new System.Windows.Documents.List
            {
                // 有符号的列表整条交给 BulletGlyph 画，MarkerStyle 关掉，缩进全靠上面的 Margin。
                // 有序列表和任务列表还是走原来的路子（序号/勾选框不用自己画）
                MarkerStyle = source.IsOrdered && !taskList ? TextMarkerStyle.Decimal : TextMarkerStyle.None,
                Margin = new Thickness(indent, 0, 0, fontSize * 0.4),
                Padding = new Thickness(0)
            };
            foreach (MdBlock child in source)
            {
                var item = child as ListItemBlock;
                if (item == null)
                    continue;
                var listItem = new ListItem();
                // 条目里的内容整体降一级：条目里再嵌的列表就是二级、三级……
                AddBlocks(listItem.Blocks, item, fontSize, outline, null, depth + 1);
                if (bullets)
                    AddBullet(listItem, depth, fontSize, column);
                list.ListItems.Add(listItem);
            }
            return list;
        }

        /// <summary>项目符号固定用微软雅黑取字形：三个符号从同一个字体出，粗细、大小才配套。</summary>
        private const string BulletFontName = "Microsoft YaHei";

        /// <summary>符号字号比正文小几号（DIP）。字形版的墨迹偏粗，跟正文同号就压过正文了。</summary>
        private const double BulletFontDelta = 2;

        /// <summary>符号字号：正文小两号；正文本来就小的时候托个底，免得缩成看不清的一点。</summary>
        private static double BulletFontSize(double fontSize)
        {
            return Math.Max(8, fontSize - BulletFontDelta);
        }

        /// <summary>符号列宽：一个符号的字宽 + 与正文的留白，正文从列宽之后起排。</summary>
        private static double BulletColumn(double fontSize)
        {
            return BulletFontSize(fontSize) + Math.Round(fontSize * 0.4);
        }

        /// <summary>
        /// 给列表项的第一段挂上项目符号：一级实心圆、二级空心圆、三级实心方块，再往下循环。
        /// 靠负的 TextIndent 把符号伸到正文左边，换行后的文字仍退回正文那一列（悬挂缩进）。
        /// 条目开头不是段落（直接就是代码块、表格）时不画——为个符号硬补一行空段落更碍眼。
        /// </summary>
        private static void AddBullet(ListItem item, int depth, double fontSize, double column)
        {
            var first = item.Blocks.FirstBlock as Paragraph;
            if (first == null)
                return;

            // 段落盒从正文列开始，首行缩进成负数，第一行就伸到正文左边去放符号
            first.TextIndent = -column;
            var marker = new InlineUIContainer(BulletGlyph(depth, fontSize, column))
            {
                BaselineAlignment = BaselineAlignment.Center
            };
            if (first.Inlines.FirstInline == null)
                first.Inlines.Add(marker);
            else
                first.Inlines.InsertBefore(first.Inlines.FirstInline, marker);
        }

        /// <summary>
        /// 排一格符号列：格子定宽 <paramref name="column"/>，符号贴左放，右边自然空出与正文的间隔。
        /// 做成定宽容器是为了让「符号 + 留白」整体算第一行的行首前导，后面的文字才不会叠到符号上。
        /// </summary>
        private static FrameworkElement BulletGlyph(int depth, double fontSize, double column)
        {
            string glyph;
            switch (depth % 3)
            {
                case 1:
                    glyph = "○";
                    break;
                case 2:
                    glyph = "■";
                    break;
                default:
                    glyph = "●";
                    break;
            }

            var text = new TextBlock
            {
                Text = glyph,
                // 字号比正文小两号，圆点/方块的墨迹才不会压过正文；字体写死在这里，
                // 预览区换字体（等宽、宋体）也不影响三个符号的观感
                FontFamily = new FontFamily(BulletFontName),
                FontSize = BulletFontSize(fontSize),
                Foreground = MarkdownTheme.Markdown.Text,
                VerticalAlignment = VerticalAlignment.Center
            };

            // 格子高度压到一行正文那么高：字形盒子比行高还高一个边，不压的话
            // 带符号的行会比普通行高出 1–2px，整段列表的行距看着忽松忽紧
            var host = new Grid { Width = column, Height = fontSize };
            host.Children.Add(text);
            return host;
        }

        /// <summary>这条列表项的正文是不是以任务勾选框开头（- [x] / - [ ]）。</summary>
        private static bool IsTaskItem(ListItemBlock item)
        {
            foreach (MdBlock block in item)
            {
                var paragraph = block as ParagraphBlock;
                if (paragraph == null || paragraph.Inline == null)
                    continue;
                return paragraph.Inline.FirstChild is TaskList;
            }
            return false;
        }

        /// <summary>
        /// 引用块和 GFM 提示块共用一套版式：左边一条粗色带 + 一层淡底，正文比预览字号小两号。
        /// 这个值是「小几号」，跟着预览配置字号走，用户调大调小预览字体时引用块保持同样的落差。
        /// </summary>
        private const double QuoteFontDelta = 2;

        /// <summary>引用块 / 提示块的正文：预览字号小两号，掉到 10 号以下就托底，嵌套再多也不至于看不清。</summary>
        private static double QuoteBodySize(double fontSize)
        {
            return Math.Max(10, fontSize - QuoteFontDelta);
        }

        /// <summary>
        /// 左侧那条色带的宽度（DIP）。跟字号成比例（15 号字约 6px），比原来那条细线粗一档——
        /// 引用块和提示块用同一个宽度，两种块的左边缘才对得齐。
        /// </summary>
        private static double QuoteBarWidth(double fontSize)
        {
            return Math.Max(4, Math.Round(fontSize * 0.4));
        }

        private static WpfBlock Quote(QuoteBlock source, double fontSize, List<HeadingInfo> outline, int depth = 0)
        {
            // GFM 提示块（> [!NOTE] / [!TIP] / [!WARNING] / [!IMPORTANT] / [!CAUTION]）单独排版
            string kind = AlertKind(source);
            if (kind != null)
                return Alert(source, kind, fontSize, outline, depth);

            // 普通引用跟提示块一个版式：粗色带 + 淡底 + 小两号的正文，只是配色取中性灰那一档
            var section = new Section
            {
                FontSize = QuoteBodySize(fontSize),
                BorderBrush = MarkdownTheme.Markdown.QuoteBorder,
                BorderThickness = new Thickness(QuoteBarWidth(fontSize), 0, 0, 0),
                // 上下跟提示块一样留 5px，左右也取同一个比例：两种块挨着排的时候，
                // 块高和正文的起始位置都对得齐
                Padding = new Thickness(fontSize * 0.7, AlertVerticalPadding, fontSize * 0.7, AlertVerticalPadding),
                Margin = new Thickness(0, 0, 0, fontSize * 0.4),
                Background = MarkdownTheme.Markdown.QuoteBackground,
                Foreground = MarkdownTheme.Markdown.QuoteText
            };
            AddBlocks(section.Blocks, source, section.FontSize, outline, null, depth);
            TrimLastBlockMargin(section);
            return section;
        }

        /// <summary>
        /// 认出 GFM 提示块：引用块第一段以 [!NOTE] / [!TIP] / [!WARNING] / [!IMPORTANT] / [!CAUTION] 开头。
        /// 返回规范化后的类型名；不是提示块返回 null，交回普通引用的排版。
        /// </summary>
        private static string AlertKind(QuoteBlock source)
        {
            ParagraphBlock first = null;
            foreach (MdBlock block in source)
            {
                first = block as ParagraphBlock;
                break;
            }
            if (first == null || first.Inline == null)
                return null;

            string text = InlineText(first.Inline);
            if (text.Length < 4 || text[0] != '[' || text[1] != '!')
                return null;
            int close = text.IndexOf(']');
            if (close < 3)
                return null;

            string kind = text.Substring(2, close - 2).Trim().ToUpperInvariant();
            Color color;
            string icon;
            return AlertStyle(kind, out color, out icon) ? kind : null;
        }

        /// <summary>提示块的图标与主色。主色只用来画左边条和标题，底色是它压到 10% 的淡色。</summary>
        private static bool AlertStyle(string kind, out Color color, out string icon)
        {
            switch (kind)
            {
                case "NOTE":
                    color = Color.FromRgb(0x4C, 0x8D, 0xF6); icon = "ℹ"; return true;
                case "TIP":
                    color = Color.FromRgb(0x3F, 0xB9, 0x50); icon = "✔"; return true;
                case "WARNING":
                    color = Color.FromRgb(0xD2, 0x99, 0x22); icon = "⚠"; return true;
                case "IMPORTANT":
                    color = Color.FromRgb(0xA3, 0x71, 0xF7); icon = "❗"; return true;
                case "CAUTION":
                    color = Color.FromRgb(0xF8, 0x51, 0x49); icon = "⛔"; return true;
                default:
                    color = Colors.Gray; icon = null; return false;
            }
        }

        /// <summary>提示卡片上下留白（DIP）：固定 5px，不跟字号缩放。</summary>
        private const double AlertVerticalPadding = 5;

        /// <summary>
        /// 排成一张提示卡片：左侧粗色条 + 10% 主色底 + 一行「图标 类型」标题，正文照常排。
        /// 底色用主色加透明度而不是写死的浅色，深浅两种主题下都不刺眼。
        /// 正文比预览字号小两号（跟普通引用对齐），标题仍按预览字号排，当卡片的小标题用。
        /// </summary>
        private static WpfBlock Alert(QuoteBlock source, string kind, double fontSize, List<HeadingInfo> outline,
            int depth = 0)
        {
            Color color;
            string icon;
            AlertStyle(kind, out color, out icon);

            double bodySize = QuoteBodySize(fontSize);
            var section = new Section
            {
                FontSize = bodySize,
                BorderThickness = new Thickness(QuoteBarWidth(fontSize), 0, 0, 0),
                BorderBrush = new SolidColorBrush(color),
                Background = Tint(color, 0.10),
                // 上下只留 5px：提示卡片本身就不高，按字号比例给（22 号字要留近 10px）
                // 会显得上下一大截空，跟贴着正文的观感对不上；左右仍按字号走，长句子不顶到色条
                Padding = new Thickness(fontSize * 0.7, AlertVerticalPadding, fontSize * 0.7, AlertVerticalPadding),
                Margin = new Thickness(0, 0, 0, fontSize * 0.6)
            };

            // 标题按预览字号排：正文小了两号，标题保持原样就自然高出一档，跟正文区分开
            var title = new Paragraph
            {
                FontSize = fontSize,
                Margin = new Thickness(0, 0, 0, fontSize * 0.15)
            };
            title.Inlines.Add(new Run(icon + "  " + kind)
            {
                Foreground = new SolidColorBrush(color),
                FontWeight = FontWeights.Bold
            });
            section.Blocks.Add(title);

            AddBlocks(section.Blocks, source, bodySize, outline, null, depth);

            // 第一段开头的 [!KIND] 标记去掉，剩下的就是正文（标记后面那个空格也一并吃掉）
            Paragraph body = null;
            foreach (System.Windows.Documents.Block block in section.Blocks)
            {
                var paragraph = block as Paragraph;
                if (paragraph != null && !ReferenceEquals(paragraph, title))
                {
                    body = paragraph;
                    break;
                }
            }
            if (body != null)
            {
                StripParagraphPrefix(body, kind.Length + 3);   // "[!" + kind + "]"
                TrimAlertBodyTop(section, body);
            }

            TrimLastBlockMargin(section);
            return section;
        }

        /// <summary>
        /// 卡片里最后一块自带的下边距（段落是 0.4 字号）是多余的：卡片自己的内边距已经留了 5px，
        /// 留着就成了「5 + 将近 10」，正文下面又空出一大截。引用块和提示块共用这套收尾。
        /// </summary>
        private static void TrimLastBlockMargin(Section section)
        {
            System.Windows.Documents.Block last = null;
            foreach (System.Windows.Documents.Block block in section.Blocks)
                last = block;
            if (last == null)
                return;
            Thickness margin = last.Margin;
            last.Margin = new Thickness(margin.Left, margin.Top, margin.Right, 0);
        }

        /// <summary>
        /// 啃掉提示块正文开头多出来的那一行。两种写法都会在标题和正文之间留一整行空白：
        /// 一是 `&gt; [!TIP]` 后面空一行（标记自成一个段落，去掉标记后剩个空段落），
        /// 二是开了「严格换行」，标记后面那个软换行排成了断行。
        /// 空白段落整段删掉，正文段首的断行 / 空 Run 也一并去掉，让正文紧贴标题
        /// ——跟 GitHub 的提示块排版一致，不然卡片中间会空一大截。
        /// </summary>
        private static void TrimAlertBodyTop(Section section, Paragraph body)
        {
            // 标记独占一行时空出来的段落：连着删，直到撞上第一段真正有内容的
            Paragraph first = body;
            while (first != null && IsBlankParagraph(first))
            {
                Paragraph next = null;
                for (System.Windows.Documents.Block block = first.NextBlock; block != null; block = block.NextBlock)
                {
                    next = block as Paragraph;
                    if (next != null)
                        break;
                }
                section.Blocks.Remove(first);
                first = next;
            }

            // 正文段首残留的断行、或标记后面那个空格留下的空 Run，也没有排版意义
            while (first != null && first.Inlines.FirstInline != null)
            {
                System.Windows.Documents.Inline head = first.Inlines.FirstInline;
                var run = head as Run;
                if (!(head is LineBreak) && (run == null || run.Text.Length > 0))
                    break;
                first.Inlines.Remove(head);
            }
        }

        /// <summary>段落里除了空白和断行什么都没画（当空行处理）。图片、公式、链接这些不算空。</summary>
        private static bool IsBlankParagraph(Paragraph paragraph)
        {
            for (System.Windows.Documents.Inline inline = paragraph.Inlines.FirstInline; inline != null; inline = inline.NextInline)
            {
                if (inline is LineBreak)
                    continue;
                var run = inline as Run;
                if (run == null || run.Text.Trim().Length > 0)
                    return false;
            }
            return true;
        }

        /// <summary>把主色压成很淡的一层底色。</summary>
        private static Brush Tint(Color color, double opacity)
        {
            var brush = new SolidColorBrush(color) { Opacity = opacity };
            brush.Freeze();
            return brush;
        }

        /// <summary>从段落开头删掉 count 个字符（可能跨好几个 Run），顺带吃掉后面的空格。</summary>
        private static void StripParagraphPrefix(Paragraph paragraph, int count)
        {
            while (count > 0 && paragraph.Inlines.Count > 0)
            {
                var run = paragraph.Inlines.FirstInline as Run;
                if (run == null)
                {
                    paragraph.Inlines.Remove(paragraph.Inlines.FirstInline);
                    continue;
                }
                if (run.Text.Length <= count)
                {
                    count -= run.Text.Length;
                    paragraph.Inlines.Remove(run);
                    continue;
                }
                run.Text = run.Text.Substring(count);
                count = 0;
            }

            var first = paragraph.Inlines.FirstInline as Run;
            if (first != null && first.Text.StartsWith(" ", StringComparison.Ordinal))
                first.Text = first.Text.Substring(1);
        }

        /// <summary>
        /// 代码块。WPF 的 Paragraph 画不出圆角（Block 没有 CornerRadius），
        /// 只能换成 BlockUIContainer 包一层 Border，再塞个 TextBlock 装代码。
        /// 代价是里面的文字不参与 FlowDocument 的选择（选择只覆盖 TextElement），
        /// 所以右上角配一个复制按钮作为取用出口。
        /// </summary>
        private static WpfBlock Code(CodeBlock source, double fontSize)
        {
            string code = CodeText(source);

            // 代码文字得能选中、能复制，所以不能再用 TextBlock —— 它是 UIElement，
            // 压根不参与 FlowDocument 的选择（跟行内代码那段注释说的是同一个限制）。
            // 这里嵌一个只读 RichTextBox：外面的圆角 Border 不动，文字走它自己的文档，
            // 选择和复制由它负责。关掉滚动条让它按内容长高，光标和 Tab 焦点都关掉，
            // 免得抢走预览自己的交互。
            var paragraph = new Paragraph { Margin = new Thickness(0) };
            Highlight(paragraph.Inlines, code, FenceLanguage(source));

            var document = new FlowDocument(paragraph)
            {
                FontFamily = MarkdownTheme.EditorFamily,
                FontSize = fontSize * 0.95,
                Foreground = MarkdownTheme.Markdown.Text,
                // 不设的话 FlowDocument 默认 400 宽，会把代码排成好几栏
                ColumnWidth = double.PositiveInfinity,
                PagePadding = new Thickness(0)
            };
            // FlowDocument 没有“不折行”开关：页宽给多少就在哪儿断行。这里按最长一行量出页宽钉死，
            // 超出可视宽度的部分交给横向滚动条。
            document.PageWidth = CodePageWidth(code, document.FontSize);

            var text = new RichTextBox
            {
                IsReadOnly = true,
                IsReadOnlyCaretVisible = false,
                IsTabStop = false,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Padding = new Thickness(0),
                // 行号列收起时靠这 5px 补足：外框左边已有 5px 内边距，合计 10px 才跟行号那列的
                // 起始位置对齐；点开行号后由 LineNumberButton 把这里清成 0（行号自己带 5px 边距）
                Margin = new Thickness(5, 0, 0, 0),
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Document = document
            };

            // 滚动条平时隐形，鼠标进到这个代码块上才浮现；挂个只作用于自家滚动条的隐式样式，
            // 用透明度而不是 Visibility，免得显隐时把内容宽度挤一下
            var scrollBarStyle = new Style(typeof(ScrollBar));
            var scrollBarIdle = new DataTrigger
            {
                Binding = new Binding("IsMouseOver")
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(RichTextBox), 1)
                },
                Value = false
            };
            scrollBarIdle.Setters.Add(new Setter(UIElement.OpacityProperty, 0.0));
            scrollBarStyle.Triggers.Add(scrollBarIdle);
            text.Resources.Add(typeof(ScrollBar), scrollBarStyle);

            // 行号列：默认收起，点右上角那个「行号」按钮才显示。单独占一列而不是塞进代码里，
            // 这样在预览里手动选中复制拿到的仍然只有代码。
            var numbers = new TextBlock
            {
                Text = LineNumberText(code),
                FontFamily = MarkdownTheme.EditorFamily,
                FontSize = fontSize * 0.95,
                Foreground = MarkdownTheme.Muted,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                // 左边 5：加上外框的 5px 内边距，行号离左边框 10px；右边 5：行号与代码之间留 5px
                Margin = new Thickness(5, 0, 5, 0),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };

            // 复制按钮和行号按钮单独占一列，正文少占这点宽度，换来按钮永远压不到代码上
            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Grid.SetColumn(numbers, 0);
            content.Children.Add(numbers);

            Grid.SetColumn(text, 1);
            content.Children.Add(text);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top
            };
            buttons.Children.Add(LineNumberButton(numbers, paragraph, text, fontSize));
            buttons.Children.Add(CopyButton(code, fontSize));

            Grid.SetColumn(buttons, 2);
            content.Children.Add(buttons);

            return new BlockUIContainer(new Border
            {
                Background = MarkdownTheme.Markdown.CodeBackground,
                BorderBrush = MarkdownTheme.Markdown.CodeBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                // 左边固定 5px：行号（开时）和代码（关时）都离左边框这么远，
                // 不会因为行号那列收起来就把这块留白变大
                Padding = new Thickness(5, fontSize * 0.5, fontSize * 0.5, fontSize * 0.5),
                Margin = new Thickness(0, 0, 0, fontSize * 0.5),
                Child = content
            });
        }

        /// <summary>
        /// 代码块按多宽排：量出最长一行（含行尾空白）的宽度，再留一个字号的余量。
        /// 页宽小于它会被折行，大于它就没必要——滚动条的范围也是按这个宽度算的。
        /// </summary>
        private static double CodePageWidth(string code, double fontSize)
        {
            var typeface = new Typeface(MarkdownTheme.EditorFamily,
                FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            double widest = 0;
            int start = 0;
            for (int i = 0; i <= code.Length; i++)
            {
                if (i < code.Length && code[i] != '\n' && code[i] != '\r')
                    continue;
                if (i > start)
                {
                    string line = code.Substring(start, i - start);
                    var formatted = new FormattedText(line, CultureInfo.CurrentUICulture,
                        FlowDirection.LeftToRight, typeface, fontSize, Brushes.Black, 1.0);
                    if (formatted.WidthIncludingTrailingWhitespace > widest)
                        widest = formatted.WidthIncludingTrailingWhitespace;
                }
                start = i + 1;
            }
            return Math.Ceiling(widest + fontSize);
        }

        /// <summary>围栏上的语言标识（```python 里的 python）。缩进式代码块没有，返回 null。</summary>
        private static string FenceLanguage(CodeBlock source)
        {
            var fenced = source as FencedCodeBlock;
            return fenced == null ? null : fenced.Info;
        }

        /// <summary>
        /// 独占一段的 $$...$$ 排成一块居中的独立公式。认不出来就退回普通代码块，
        /// 把原始源码显示出来，别让一个坏公式吞掉内容。
        /// </summary>
        private static WpfBlock FormulaBlock(CodeBlock source, double fontSize)
        {
            FrameworkElement formula = FormulaRenderer.TryCreateBlock(CodeText(source).Trim(), fontSize);
            if (formula == null)
                return Code(source, fontSize);
            return new BlockUIContainer(formula)
            {
                Margin = new Thickness(0, fontSize * 0.3, 0, fontSize * 0.5)
            };
        }

        /// <summary>
        /// 代码块标了 mermaid，就用原生渲染器画出来（时序图 / 流程图）；画不出来返回 null，退回普通代码块。
        /// </summary>
        private static WpfBlock MermaidBlock(CodeBlock source, double fontSize)
        {
            string language = FenceLanguage(source) ?? string.Empty;
            if (!language.Trim().Equals("mermaid", StringComparison.OrdinalIgnoreCase))
                return null;

            FrameworkElement view = MermaidRenderer.TryCreate(CodeText(source), fontSize, _imageHost);
            if (view == null)
                return null;

            return new BlockUIContainer(view)
            {
                Margin = new Thickness(0, fontSize * 0.2, 0, fontSize * 0.6)
            };
        }

        /// <summary>
        /// 代码块标了 plantuml / puml，或内容本身就是一张 @startuml 图，就用原生时序图渲染器画出来。
        /// 画不出来（不是时序图语法、或没有可画的参与者）返回 null，调用方退回普通代码块。
        /// </summary>
        private static WpfBlock PlantUmlBlock(CodeBlock source, double fontSize)
        {
            string code = CodeText(source);
            if (!IsPlantUml(FenceLanguage(source), code))
                return null;
            FrameworkElement diagram = PlantUmlRenderer.TryCreate(code, fontSize, _imageHost);
            if (diagram == null)
                return null;
            return new BlockUIContainer(diagram)
            {
                Margin = new Thickness(0, fontSize * 0.2, 0, fontSize * 0.6)
            };
        }

        private static bool IsPlantUml(string language, string code)
        {
            string lang = language == null ? "" : language.Trim().ToLowerInvariant();
            int space = lang.IndexOf(' ');
            if (space > 0)
                lang = lang.Substring(0, space);
            if (lang == "plantuml" || lang == "puml" || lang == "uml")
                return true;
            // 没标语言，但内容就是一张 @startuml 图
            return lang.Length == 0 && code.TrimStart().StartsWith("@startuml", StringComparison.Ordinal);
        }

        /// <summary>
        /// 按语言把代码切成带色的 Run 填进行内集合。认不出的语言整段按正文色铺，等同于原来的单色。
        /// 换行符直接留在 Run 里，容器（TextBlock / Paragraph）自己会断行。
        /// </summary>
        private static void Highlight(InlineCollection target, string code, string language)
        {
            IReadOnlyList<CodeToken> tokens = SyntaxHighlighter.Highlight(code, language);
            for (int i = 0; i < tokens.Count; i++)
            {
                CodeToken token = tokens[i];
                var run = new Run(token.Text);
                if (token.Kind != TokenKind.Plain)
                    run.Foreground = TokenBrush(token.Kind);
                target.Add(run);
            }
        }

        /// <summary>高亮片段类型对应的颜色。</summary>
        private static Brush TokenBrush(TokenKind kind)
        {
            MarkdownPalette palette = MarkdownTheme.Markdown;
            switch (kind)
            {
                case TokenKind.Keyword: return palette.CodeKeyword;
                case TokenKind.String: return palette.CodeString;
                case TokenKind.Comment: return palette.CodeComment;
                case TokenKind.Number: return palette.CodeNumber;
                case TokenKind.Function: return palette.CodeFunction;
                case TokenKind.Type: return palette.CodeType;
                case TokenKind.Tag: return palette.CodeTag;
                case TokenKind.Attribute: return palette.CodeAttribute;
                case TokenKind.Property: return palette.CodeProperty;
                case TokenKind.Section: return palette.CodeSection;
                case TokenKind.Variable: return palette.CodeVariable;
                default: return palette.Text;
            }
        }

        /// <summary>
        /// 代码块右上角的复制按钮。图标用 Path 画两张叠着的纸，免得受用户自定义字体影响。
        /// 点击区包一层透明 Border：Transparent 参与命中测试而 null 不参与。
        /// </summary>
        private static Border CopyButton(string code, double fontSize)
        {
            var glyph = new Path
            {
                // 后面那张纸只画被前面那张挡不住的三条边，否则两张纸的轮廓会在里面交叉
                Data = Geometry.Parse("M 0.6,0.6 H 7.4 V 4.6 M 0.6,0.6 V 7.4 H 4.6 M 4.6,4.6 H 11.4 V 11.4 H 4.6 Z"),
                Stroke = MarkdownTheme.Muted,
                StrokeThickness = 1.2,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Width = fontSize * 0.62,
                Height = fontSize * 0.62,
                Stretch = Stretch.Uniform
            };
            var button = new Border
            {
                Padding = new Thickness(fontSize * 0.2),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = MarkdownResources.T("copyCode"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Child = glyph
            };
            button.MouseEnter += (s, e) => glyph.Stroke = MarkdownTheme.Foreground;
            button.MouseLeave += (s, e) => glyph.Stroke = MarkdownTheme.Muted;
            // FlowDocumentScrollViewer 的文本选择逻辑会在 MouseLeftButtonDown 冒泡到这里之后
            // 捕获鼠标，随后 MouseLeftButtonUp 被它吃掉，按钮永远收不到。先一步把按下标成
            // 已处理，选择逻辑就不启动，抬起事件才能正常回到按钮上。
            button.MouseLeftButtonDown += (s, e) => e.Handled = true;
            button.MouseLeftButtonUp += (s, e) =>
            {
                if (!TryCopy(code))
                    return;
                // 复制成功图标短暂变绿；鼠标还停在按钮上时恢复成悬停色而不是常态色
                glyph.Stroke = MarkdownTheme.Copied;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
                timer.Tick += (t, args) =>
                {
                    timer.Stop();
                    glyph.Stroke = button.IsMouseOver ? MarkdownTheme.Foreground : MarkdownTheme.Muted;
                };
                timer.Start();
            };
            return button;
        }

        /// <summary>「1\n2\n3…」——行号列的文字，行数跟代码行数一致。</summary>
        private static string LineNumberText(string code)
        {
            string normalized = code.Replace("\r\n", "\n").Replace('\r', '\n');
            int count = 0;
            for (int i = 0; i < normalized.Length; i++)
            {
                if (normalized[i] == '\n')
                    count++;
            }
            count++;   // 最后一行（除非整段为空）

            var sb = new StringBuilder(count * 4);
            for (int i = 1; i <= count; i++)
            {
                if (i > 1)
                    sb.Append('\n');
                sb.Append(i);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 代码块右上角的「显示行号」开关。图标是一条编号线加三条横线，点一下把左边那列行号显示 / 收起。
        /// 行号列和代码行要逐行对齐，所以打开时两边都钉死同一个行高（默认行高按字体度量算，容易错开）。
        /// </summary>
        private static Border LineNumberButton(TextBlock numbers, Paragraph paragraph, RichTextBox text,
            double fontSize)
        {
            var glyph = new Path
            {
                Data = Geometry.Parse("M 1.2,0.6 L 2.4,0 V 3.6 M 5.2,0.6 H 11.4 M 1.2,6.2 H 11.4 M 1.2,9.8 H 11.4"),
                Stroke = MarkdownTheme.Muted,
                StrokeThickness = 1.2,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Width = fontSize * 0.62,
                Height = fontSize * 0.62,
                Stretch = Stretch.Uniform
            };
            var button = new Border
            {
                Padding = new Thickness(fontSize * 0.2),
                Margin = new Thickness(0, 0, fontSize * 0.2, 0),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = MarkdownResources.T("codeLineNumbers"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Child = glyph
            };

            // 开着的时候图标常亮，悬停时也亮，其余时候是弱化色
            bool on = false;
            Action refresh = () =>
            {
                glyph.Stroke = on || button.IsMouseOver ? MarkdownTheme.Foreground : MarkdownTheme.Muted;
            };
            button.MouseEnter += (s, e) => refresh();
            button.MouseLeave += (s, e) => refresh();

            // 跟复制按钮一样：先把按下标成已处理，免得预览的选择逻辑把抬起事件吃掉
            button.MouseLeftButtonDown += (s, e) => e.Handled = true;
            button.MouseLeftButtonUp += (s, e) =>
            {
                on = !on;
                numbers.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                // 收起行号时把代码左边补到 10px（外框 5px + 这里 5px），跟行号那列的起点对齐
                text.Margin = on ? new Thickness(0) : new Thickness(5, 0, 0, 0);
                ApplyCodeLineHeight(paragraph, numbers, fontSize, on);
                refresh();
            };
            return button;
        }

        /// <summary>行号打开时给代码段落和行号列定同一个行高，收起时恢复默认。</summary>
        private static void ApplyCodeLineHeight(Paragraph paragraph, TextBlock numbers, double fontSize, bool on)
        {
            if (on)
            {
                double height = Math.Round(fontSize * 0.95 * 1.45);
                paragraph.LineHeight = height;
                paragraph.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                numbers.LineHeight = height;
                numbers.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            }
            else
            {
                // 这里有 using MdBlock = Markdig.Syntax.Block 的别名，裸写 Block 会和它冲突，所以写全名
                paragraph.ClearValue(System.Windows.Documents.Block.LineHeightProperty);
                paragraph.ClearValue(System.Windows.Documents.Block.LineStackingStrategyProperty);
                numbers.ClearValue(TextBlock.LineHeightProperty);
                numbers.ClearValue(TextBlock.LineStackingStrategyProperty);
            }
        }

        /// <summary>剪贴板常被其他程序短暂占用，失败时重试几次再放弃。</summary>
        private static bool TryCopy(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    // 用 WinForms 的 Win32 实现而非 System.Windows 的 OLE 实现：
                    // 后者在某些受限的启动上下文里会抛 CLIPBRD_E_CANT_OPEN，前者不会
                    System.Windows.Forms.Clipboard.SetText(text);
                    return true;
                }
                catch (ExternalException)
                {
                    Thread.Sleep(60);
                }
            }
            return false;
        }

        /// <summary>把代码块的多行拼成一段文本。</summary>
        private static string CodeText(LeafBlock source)
        {
            var text = new StringBuilder();
            // 只能按 Count 取：Lines 是 Markdig 内部的扩容数组，长度大于实际行数，
            // 直接 foreach 会把数组尾巴上的空槽也拼进来，代码块底下多出几行空白
            for (int i = 0; i < source.Lines.Count; i++)
            {
                if (text.Length > 0)
                    text.Append('\n');
                text.Append(source.Lines.Lines[i].ToString());
            }
            return text.ToString();
        }

        private static WpfBlock Rule(double fontSize)
        {
            return new BlockUIContainer(new Border
            {
                Height = 1,
                Background = MarkdownTheme.Border,
                Margin = new Thickness(0, fontSize * 0.3, 0, fontSize * 0.6)
            });
        }

        /// <summary>
        /// 文档开头的 YAML 文件头（--- 包起来那段）单独排成一张两列键值表，不混进正文。
        /// 只影响预览，编辑区仍是原文，保存不会丢字段。按行取「键: 值」，认不出的行跳过。
        /// </summary>
        private static WpfBlock FrontMatter(LeafBlock source, double fontSize)
        {
            var pairs = new List<KeyValuePair<string, string>>();
            string[] lines = CodeText(source).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0)
                    continue;
                int colon = line.IndexOf(':');
                if (colon <= 0)
                    continue;
                string key = line.Substring(0, colon).Trim();
                string value = line.Substring(colon + 1).Trim();
                if (key.Length == 0)
                    continue;
                if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[value.Length - 1] == value[0])
                    value = value.Substring(1, value.Length - 2);
                pairs.Add(new KeyValuePair<string, string>(key, value));
            }
            if (pairs.Count == 0)
                return null;

            // 用 Grid 而不是 Table：Table 的 Auto 列在 FlowDocument 里量不准，
            // 会把值那列压到几乎没宽度、逐字换行。信息栏是元数据，不图选中，Grid 排得更稳。
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < pairs.Count; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                TextBlock key = FrontMatterText(pairs[i].Key, MarkdownTheme.Muted, fontSize,
                    new Thickness(0, fontSize * 0.1, fontSize * 1.2, fontSize * 0.1), false);
                Grid.SetRow(key, i);
                Grid.SetColumn(key, 0);
                grid.Children.Add(key);

                TextBlock value = FrontMatterText(pairs[i].Value, MarkdownTheme.Markdown.Text, fontSize,
                    new Thickness(0, fontSize * 0.1, 0, fontSize * 0.1), true);
                Grid.SetRow(value, i);
                Grid.SetColumn(value, 1);
                grid.Children.Add(value);
            }

            var section = new Section();
            section.Blocks.Add(new BlockUIContainer(grid));
            section.Blocks.Add(Rule(fontSize));
            return section;
        }

        private static TextBlock FrontMatterText(string text, Brush foreground, double fontSize,
            Thickness margin, bool wrap)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                FontFamily = MarkdownTheme.PreviewFamily,
                Foreground = foreground,
                Margin = margin,
                TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap
            };
        }

        /// <summary>
        /// 表格。用 WPF 原生的 Table 排，而不是把 Grid 塞进 BlockUIContainer：
        /// BlockUIContainer 里装的是 UIElement，里面的文字不参与 FlowDocument 的选择
        /// （选择只覆盖 TextElement），表格内容就既选不中、也复制不了。
        /// 原生 Table 的单元格是 TextElement，选中和 Ctrl+C 跟正文一样好使。
        /// 代价是 Block 没有 CornerRadius，外框只能是直角，拿不到圆角卡片那圈效果。
        /// </summary>
        private static WpfBlock Table(MdTable source, double fontSize)
        {
            var rows = new List<MdTableRow>();
            foreach (MdBlock rowBlock in source)
            {
                var row = rowBlock as MdTableRow;
                if (row != null)
                    rows.Add(row);
            }

            int columnCount = 0;
            foreach (MdTableRow row in rows)
            {
                int count = 0;
                foreach (MdBlock cellBlock in row)
                {
                    if (cellBlock is MdTableCell)
                        count++;
                }
                columnCount = Math.Max(columnCount, count);
            }
            if (columnCount == 0)
                return null;

            var table = new Table
            {
                // 单元格紧贴，隔行底色连成整条，网格线也由格子自己画。
                // 这里**不能**再给 Table 设 Background：实测整表一层不透明底色会把 WPF 的选区
                // 高亮整个吞掉（选区里含表格时，连选区里的段落部分都不再画高亮），
                // 于是「表格里的文字选不中」——其实选中了、也复制得到，只是完全看不见。
                // 表格的底色改由每个格子自己铺，见下面 rowBackground。
                CellSpacing = 0,
                BorderBrush = MarkdownTheme.Markdown.TableBorder,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, fontSize * 0.5)
            };
            // 各列宽度按内容长短加权分配：# / ID 这类短列收窄，长文本列占大头。
            // 全用 Auto 的话，长文本列会按不换行的原宽撑出去（预览区没有横向滚动条，会被裁掉），
            // 所以统一用星号列，只是权重按内容算。
            int[] widths = new int[columnCount];
            bool hasLong = false;
            foreach (MdTableRow row in rows)
            {
                int index = 0;
                foreach (MdBlock cellBlock in row)
                {
                    var cell = cellBlock as MdTableCell;
                    if (cell != null && index < columnCount)
                        widths[index] = Math.Max(widths[index], CellTextWidth(cell));
                    index++;
                }
            }
            for (int i = 0; i < columnCount; i++)
                if (widths[i] > 6)
                    hasLong = true;
            for (int i = 0; i < columnCount; i++)
            {
                // 短列（#、ID、是否…）直接按内容宽度定，不参与比例分配：
                // 用星号权重的话，旁边两列一长，它按比例分到的宽度连 "01" 都放不下，只能折行。
                if (hasLong && widths[i] <= 6)
                {
                    table.Columns.Add(new TableColumn { Width = GridLength.Auto });
                    continue;
                }
                // 长列仍用加权星号：既体现内容比例，又不会像 Auto 那样按不换行的原宽撑出页面。
                // 全是短列时也走这里（权重按内容比例），否则表会被等分成几大块。
                double weight = Math.Min(Math.Max(widths[i], 1), 60);
                table.Columns.Add(new TableColumn { Width = new GridLength(weight, GridUnitType.Star) });
            }

            var group = new TableRowGroup();
            table.RowGroups.Add(group);

            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                MdTableRow sourceRow = rows[rowIndex];
                // 表头算第 1 行，往下第 2、4、6… 行（1 起数）铺一层浅色底，方便横向读。
                // 不隔行的那些格子也要给底色（用表格底色），不能留 null —— Table 上那层
                // 整表底色已经去掉了，留 null 就会透出正文底色。
                Brush rowBackground = sourceRow.IsHeader
                    ? MarkdownTheme.Markdown.TableHeaderBackground
                    : (rowIndex % 2 == 1 ? MarkdownTheme.Markdown.TableEvenRowBackground : MarkdownTheme.Markdown.TableBackground);
                bool lastRow = rowIndex == rows.Count - 1;

                var row = new TableRow();
                int cellIndex = 0;
                foreach (MdBlock cellBlock in sourceRow)
                {
                    if (cellIndex >= columnCount)
                        break;
                    var sourceCell = cellBlock as MdTableCell;
                    TableCell cell = NewCell(rowBackground, fontSize, cellIndex, columnCount, lastRow);
                    if (sourceCell != null)
                        CellContent(cell.Blocks, sourceCell, fontSize, sourceRow.IsHeader);
                    // 分隔行里的 :-- / :-: / --: 决定这一列的对齐
                    ApplyColumnAlignment(cell, source.ColumnDefinitions, cellIndex);
                    row.Cells.Add(cell);
                    cellIndex++;
                }
                // 行短了补空格子，不然后面的列会整体左移串位
                while (cellIndex < columnCount)
                {
                    row.Cells.Add(NewCell(rowBackground, fontSize, cellIndex, columnCount, lastRow));
                    cellIndex++;
                }
                group.Rows.Add(row);
            }

            return table;
        }

        /// <summary>
        /// 按分隔行的冒号给整列定对齐方式（`:--` 左、`:-:` 中、`--:` 右，没写冒号就还是左）。
        /// Markdig 把它放在 TableColumnDefinition.Alignment 里，之前一直没用上。
        /// </summary>
        private static void ApplyColumnAlignment(TableCell cell,
            System.Collections.Generic.List<Markdig.Extensions.Tables.TableColumnDefinition> columns, int index)
        {
            if (columns == null || index < 0 || index >= columns.Count)
                return;
            Markdig.Extensions.Tables.TableColumnDefinition definition = columns[index];
            if (definition == null || !definition.Alignment.HasValue)
                return;

            TextAlignment alignment;
            switch (definition.Alignment.Value)
            {
                case Markdig.Extensions.Tables.TableColumnAlign.Center:
                    alignment = TextAlignment.Center;
                    break;
                case Markdig.Extensions.Tables.TableColumnAlign.Right:
                    alignment = TextAlignment.Right;
                    break;
                default:
                    alignment = TextAlignment.Left;
                    break;
            }

            foreach (System.Windows.Documents.Block block in cell.Blocks)
            {
                var paragraph = block as Paragraph;
                if (paragraph != null)
                    paragraph.TextAlignment = alignment;
            }
        }

        // ── 原始 HTML 表格 ──────────────────────────────────────────

        /// <summary>一个 HTML 表格单元格的解析结果。</summary>
        private sealed class HtmlCell
        {
            public string Html;
            public bool Header;
            public int ColSpan = 1;
            public TextAlignment Alignment = TextAlignment.Left;
        }

        /// <summary>HtmlBlock 的原始文本（Markdig 按行存，这里拼回去）。</summary>
        private static string HtmlBlockText(HtmlBlock block)
        {
            var sb = new StringBuilder();
            if (block.Lines.Lines != null)
            {
                foreach (var line in block.Lines.Lines)
                    sb.Append(line.Slice.ToString()).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>
        /// 原始 HTML 的 &lt;table&gt;。Markdown 标准里没有列宽语法，要让列宽可控只能写 HTML 表格
        /// （Typora / GitHub 也支持），所以这里把它解析成和管道表格同一套外观。
        /// 支持：thead/tbody/tfoot、tr、th/td、colgroup/col 的宽度、colspan、
        /// 单元格里的 b/strong、i/em、code、br、sub/sup 和 &amp;xxx; 实体。
        /// 不支持 rowspan（按普通单元格排）。其它 HTML 块仍按原样跳过。
        /// </summary>
        private static WpfBlock HtmlTable(HtmlBlock source, double fontSize)
        {
            string html = HtmlBlockText(source);
            int open = html.IndexOf("<table", StringComparison.OrdinalIgnoreCase);
            if (open < 0)
                return null;
            int close = html.IndexOf("</table", open, StringComparison.OrdinalIgnoreCase);
            string body = close < 0 ? html.Substring(open) : html.Substring(open, close - open);

            // 列宽：<col style="width:20%"> / <col width="120">。没写的列按内容长度补。
            var declared = new List<GridLength?>();
            foreach (Match m in Regex.Matches(body, "<col(?!group)\\b[^>]*>", RegexOptions.IgnoreCase))
                declared.Add(ParseHtmlWidth(m.Value));

            var rows = new List<List<HtmlCell>>();
            foreach (Match tr in Regex.Matches(body, "<tr\\b[^>]*>(.*?)(?=<tr\\b|</table\\b|$)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                var cells = new List<HtmlCell>();
                foreach (Match cell in Regex.Matches(tr.Groups[1].Value,
                    "<(t[hd])\\b([^>]*)>(.*?)(?=</t[hd]\\s*>|<t[hd]\\b|$)",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline))
                {
                    string attrs = cell.Groups[2].Value;
                    cells.Add(new HtmlCell
                    {
                        Html = Regex.Replace(cell.Groups[3].Value, "</t[hd]\\s*$", "",
                            RegexOptions.IgnoreCase),
                        Header = cell.Groups[1].Value.Equals("th", StringComparison.OrdinalIgnoreCase),
                        ColSpan = ParseHtmlSpan(attrs, "colspan"),
                        Alignment = ParseHtmlAlignment(attrs)
                    });
                }
                if (cells.Count > 0)
                    rows.Add(cells);
            }
            if (rows.Count == 0)
                return null;

            int columnCount = declared.Count;
            foreach (List<HtmlCell> row in rows)
            {
                int count = 0;
                foreach (HtmlCell cell in row)
                    count += cell.ColSpan;
                columnCount = Math.Max(columnCount, count);
            }
            if (columnCount == 0)
                return null;

            // 没写宽度的列，按内容长度补一个星号权重
            var weights = new int[columnCount];
            foreach (List<HtmlCell> row in rows)
            {
                int column = 0;
                foreach (HtmlCell cell in row)
                {
                    int width = DisplayWidth(HtmlPlainText(cell.Html));
                    for (int k = 0; k < cell.ColSpan && column + k < columnCount; k++)
                        weights[column + k] = Math.Max(weights[column + k], Math.Max(1, width / cell.ColSpan));
                    column += cell.ColSpan;
                }
            }

            var table = new Table
            {
                CellSpacing = 0,
                BorderBrush = MarkdownTheme.Markdown.TableBorder,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, fontSize * 0.5)
            };
            for (int i = 0; i < columnCount; i++)
            {
                GridLength? declaredWidth = i < declared.Count ? declared[i] : null;
                if (declaredWidth.HasValue)
                {
                    table.Columns.Add(new TableColumn { Width = declaredWidth.Value });
                    continue;
                }
                double weight = Math.Min(Math.Max(weights[i], 3), 60);
                table.Columns.Add(new TableColumn { Width = new GridLength(weight, GridUnitType.Star) });
            }

            var group = new TableRowGroup();
            table.RowGroups.Add(group);
            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                List<HtmlCell> cells = rows[rowIndex];
                bool headerRow = false;
                foreach (HtmlCell cell in cells)
                    if (cell.Header)
                        headerRow = true;

                Brush background = headerRow
                    ? MarkdownTheme.Markdown.TableHeaderBackground
                    : (rowIndex % 2 == 1 ? MarkdownTheme.Markdown.TableEvenRowBackground : MarkdownTheme.Markdown.TableBackground);
                bool lastRow = rowIndex == rows.Count - 1;

                var row = new TableRow();
                int column2 = 0;
                foreach (HtmlCell cell in cells)
                {
                    if (column2 >= columnCount)
                        break;
                    TableCell rendered = NewCell(background, fontSize, column2, columnCount, lastRow);
                    if (cell.ColSpan > 1)
                        rendered.ColumnSpan = Math.Min(cell.ColSpan, columnCount - column2);
                    var paragraph = new Paragraph
                    {
                        Margin = new Thickness(0),
                        TextAlignment = cell.Alignment,
                        FontWeight = cell.Header ? FontWeights.Bold : FontWeights.Normal
                    };
                    AddHtmlInlines(paragraph.Inlines, cell.Html, fontSize);
                    rendered.Blocks.Add(paragraph);
                    row.Cells.Add(rendered);
                    column2 += rendered.ColumnSpan;
                }
                // 行短了补空格子（colspan 占过的位置也要算进去）
                while (column2 < columnCount)
                {
                    row.Cells.Add(NewCell(background, fontSize, column2, columnCount, lastRow));
                    column2++;
                }
                group.Rows.Add(row);
            }
            return table;
        }

        /// <summary>单元格 HTML 的纯文字（去标签、还原实体），只用来估列宽。</summary>
        private static string HtmlPlainText(string html)
        {
            if (string.IsNullOrEmpty(html))
                return "";
            return WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]*>", ""));
        }

        /// <summary>
        /// 把单元格里的 HTML 片段转成 WPF 内联：b/strong 粗体、i/em 斜体、code 等宽、
        /// br 换行、sub/sup 上下标；其它标签（a、span、font…）只丢标签、文字保留。
        /// </summary>
        private static void AddHtmlInlines(InlineCollection target, string html, double fontSize)
        {
            // 不能先整体解实体再切标签：&lt;环境&gt; 解开后会变成「<环境>」被当成标签丢掉。
            // 正确顺序是「按原始文本切标签 → 只在文字片段上解实体」。
            string text = html ?? "";
            var opened = new List<KeyValuePair<string, Span>>();
            InlineCollection sink = target;
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '<')
                {
                    int end = text.IndexOf('>', i);
                    if (end < 0)
                        break;
                    string raw = text.Substring(i + 1, end - i - 1).Trim();
                    i = end + 1;

                    bool closing = raw.StartsWith("/", StringComparison.Ordinal);
                    string name = (closing ? raw.Substring(1) : raw).ToLowerInvariant();
                    int cut = name.IndexOfAny(new[] { ' ', '\t', '/' });
                    if (cut >= 0)
                        name = name.Substring(0, cut);

                    if (name == "br")
                    {
                        sink.Add(new LineBreak());
                        continue;
                    }

                    bool styled = name == "b" || name == "strong" || name == "i" || name == "em"
                        || name == "code" || name == "sub" || name == "sup";
                    if (!styled)
                        continue;

                    if (!closing)
                    {
                        var span = new Span();
                        if (name == "b" || name == "strong")
                            span.FontWeight = FontWeights.Bold;
                        else if (name == "i" || name == "em")
                            span.FontStyle = FontStyles.Italic;
                        else if (name == "code")
                            span.FontFamily = MarkdownTheme.EditorFamily;
                        else
                            span.FontSize = fontSize * 0.75;
                        sink.Add(span);
                        opened.Add(new KeyValuePair<string, Span>(name, span));
                        sink = span.Inlines;
                        continue;
                    }

                    // 闭合：找到同名的那一层收掉（HTML 写歪了也不至于把整段吞掉）
                    for (int k = opened.Count - 1; k >= 0; k--)
                    {
                        if (opened[k].Key != name)
                            continue;
                        Span span = opened[k].Value;
                        opened.RemoveRange(k, opened.Count - k);
                        if (name == "sub")
                            ApplyBaseline(span.Inlines, BaselineAlignment.Subscript);
                        else if (name == "sup")
                            ApplyBaseline(span.Inlines, BaselineAlignment.Superscript);
                        sink = opened.Count > 0 ? opened[opened.Count - 1].Value.Inlines : target;
                        break;
                    }
                    continue;
                }

                int next = text.IndexOf('<', i);
                if (next < 0)
                    next = text.Length;
                string chunk = text.Substring(i, next - i);
                if (chunk.Length > 0)
                    sink.Add(new Run(WebUtility.HtmlDecode(chunk)));
                i = next;
            }
        }

        /// <summary>col 标签上的宽度：width="120" / width="20%" / style="width:20%"。</summary>
        private static GridLength? ParseHtmlWidth(string tag)
        {
            Match m = Regex.Match(tag, "width\\s*=\\s*[\"']?\\s*([\\d.]+)\\s*(%|px)?", RegexOptions.IgnoreCase);
            if (!m.Success)
                m = Regex.Match(tag, "style\\s*=\\s*[\"'][^\"']*width\\s*:\\s*([\\d.]+)\\s*(%|px)?",
                    RegexOptions.IgnoreCase);
            if (!m.Success)
                return null;

            double value;
            if (!double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || value <= 0)
                return null;
            bool percent = m.Groups[2].Success && m.Groups[2].Value == "%";
            return percent ? new GridLength(value, GridUnitType.Star) : new GridLength(value);
        }

        /// <summary>colspan="2" 这类跨列数，非法值一律按 1。</summary>
        private static int ParseHtmlSpan(string attributes, string name)
        {
            Match m = Regex.Match(attributes, name + "\\s*=\\s*[\"']?(\\d+)", RegexOptions.IgnoreCase);
            int span;
            if (!m.Success || !int.TryParse(m.Groups[1].Value, out span) || span < 2)
                return 1;
            return Math.Min(span, 10);
        }

        /// <summary>align="center" 或 style="text-align:center"。</summary>
        private static TextAlignment ParseHtmlAlignment(string attributes)
        {
            Match m = Regex.Match(attributes, "text-align\\s*:\\s*(left|center|right)", RegexOptions.IgnoreCase);
            if (!m.Success)
                m = Regex.Match(attributes, "align\\s*=\\s*[\"']?(left|center|right)", RegexOptions.IgnoreCase);
            if (!m.Success)
                return TextAlignment.Left;
            string value = m.Groups[1].Value.ToLowerInvariant();
            if (value == "center")
                return TextAlignment.Center;
            return value == "right" ? TextAlignment.Right : TextAlignment.Left;
        }

        /// <summary>单元格内容的大致显示宽度（半角=1、全角=2），只用来给列宽定权重。</summary>
        private static int CellTextWidth(MdTableCell cell)
        {
            int widest = 0;
            foreach (MdBlock block in cell)
            {
                var paragraph = block as ParagraphBlock;
                if (paragraph != null)
                {
                    widest = Math.Max(widest, InlineTextWidth(paragraph.Inline));
                    continue;
                }
                // 代码块、列表、表格这类没细算，给个中等权重，别让它们把列压成一条缝
                widest = Math.Max(widest, 10);
            }
            return widest;
        }

        /// <summary>把一段内联内容按显示宽度累加（跳过图片、脚注这类没有文字宽度的）。</summary>
        private static int InlineTextWidth(ContainerInline container)
        {
            if (container == null)
                return 0;
            int width = 0;
            for (MdInline child = container.FirstChild; child != null; child = child.NextSibling)
            {
                var literal = child as LiteralInline;
                if (literal != null)
                {
                    width += DisplayWidth(literal.Content.ToString());
                    continue;
                }
                var code = child as CodeInline;
                if (code != null)
                {
                    width += DisplayWidth(code.Content);
                    continue;
                }
                var nested = child as ContainerInline;
                if (nested != null)
                    width += InlineTextWidth(nested);
            }
            return width;
        }

        /// <summary>半角算 1、全角算 2，够列宽排序用。</summary>
        private static int DisplayWidth(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;
            int width = 0;
            for (int i = 0; i < text.Length; i++)
                width += text[i] < 128 ? 1 : 2;
            return width;
        }

        /// <summary>一个单元格的底色、网格线、内边距。只画右边和下边：外框交给 Table 自己。</summary>
        private static TableCell NewCell(Brush background, double fontSize,
            int cellIndex, int columnCount, bool lastRow)
        {
            return new TableCell
            {
                Background = background,
                BorderBrush = MarkdownTheme.Markdown.TableBorder,
                BorderThickness = new Thickness(
                    0, 0, cellIndex < columnCount - 1 ? 1 : 0, lastRow ? 0 : 1),
                Padding = new Thickness(fontSize * 0.4, fontSize * 0.15, fontSize * 0.4, fontSize * 0.15)
            };
        }

        /// <summary>把单元格里的块内容填进 TableCell。管道表格的格子里正常只有一个段落。</summary>
        private static void CellContent(BlockCollection target, MdTableCell cell, double fontSize, bool bold)
        {
            foreach (MdBlock block in cell)
            {
                var paragraph = block as ParagraphBlock;
                if (paragraph == null)
                    continue;
                var rendered = new Paragraph { Margin = new Thickness(0) };
                if (bold)
                    rendered.FontWeight = FontWeights.Bold;
                AddInlines(rendered.Inlines, paragraph.Inline, fontSize);
                // 格子里的公式也要撑开，不然表格行不会跟着长高，分母会压到下一行
                FormulaLineHeight(rendered, fontSize);
                target.Add(rendered);
            }
        }

        private static void AddInlines(InlineCollection target, ContainerInline source, double fontSize)
        {
            if (source == null)
                return;

            // <sub>/<sup> 要成对处理：中间的内容先攒进一个 Span，遇到闭合标签再整体贴上行内位置。
            // 其它 HTML 标签照旧忽略（Markdig 给的是 HtmlInline，AddInline 里没有分支就丢掉了）。
            InlineCollection sink = target;
            Span mark = null;
            BaselineAlignment markAlign = BaselineAlignment.Baseline;

            for (MdInline inline = source.FirstChild; inline != null; inline = inline.NextSibling)
            {
                var html = inline as HtmlInline;
                if (html != null)
                {
                    string name = HtmlTagName(html.Tag, out bool closing);
                    if (name == "sub" || name == "sup")
                    {
                        if (mark != null)
                        {
                            ApplyBaseline(mark.Inlines, markAlign);
                            target.Add(mark);
                            mark = null;
                            sink = target;
                        }
                        if (!closing)
                        {
                            markAlign = name == "sub" ? BaselineAlignment.Subscript : BaselineAlignment.Superscript;
                            mark = new Span { FontSize = fontSize * 0.75 };
                            sink = mark.Inlines;
                        }
                        continue;
                    }
                }
                AddInline(sink, inline, fontSize);
            }

            if (mark != null)
            {
                ApplyBaseline(mark.Inlines, markAlign);
                target.Add(mark);
            }
        }

        /// <summary>从 &lt;sub&gt; / &lt;/sub&gt; 里取标签名，并判断是不是闭合标签。</summary>
        private static string HtmlTagName(string tag, out bool closing)
        {
            closing = false;
            if (string.IsNullOrEmpty(tag))
                return null;

            string text = tag.Trim();
            if (text.StartsWith("</", StringComparison.Ordinal))
            {
                closing = true;
                text = text.Substring(2);
            }
            else if (text.StartsWith("<", StringComparison.Ordinal))
            {
                text = text.Substring(1);
            }

            int end = text.IndexOfAny(new[] { '>', ' ', '\t', '/' });
            if (end >= 0)
                text = text.Substring(0, end);
            return text.ToLowerInvariant();
        }

        /// <summary>
        /// 把下标/上标位置贴到里面每一个 Run 上。BaselineAlignment 不是继承属性，设在 Span 上不生效，
        /// 只能递归到叶子。字号走 Span 继承，不用逐层设。
        /// </summary>
        private static void ApplyBaseline(InlineCollection inlines, BaselineAlignment alignment)
        {
            foreach (System.Windows.Documents.Inline inline in inlines)
            {
                var span = inline as Span;
                if (span != null)
                {
                    ApplyBaseline(span.Inlines, alignment);
                    continue;
                }
                inline.BaselineAlignment = alignment;
            }
        }

        private static void AddInline(InlineCollection target, MdInline inline, double fontSize)
        {
            var literal = inline as LiteralInline;
            if (literal != null)
            {
                target.Add(new Run(literal.Content.ToString()));
                return;
            }

            var code = inline as CodeInline;
            if (code != null)
            {
                // 行内代码用 Span 而不是 InlineUIContainer 包一层 Border：后者装的是 UIElement，
                // 里面的文字不参与 FlowDocument 的选择（选择只覆盖 TextElement），
                // 行内代码就既选不中也复制不了。代价是 Inline 没有 Padding / CornerRadius，
                // 底色只能紧贴文字、四个角是直角，拿不到原来那个小圆角块。
                var span = new Span
                {
                    Background = MarkdownTheme.Markdown.InlineCodeBackground,
                    Foreground = MarkdownTheme.Markdown.InlineCodeText,
                    FontFamily = MarkdownTheme.EditorFamily,
                    FontSize = fontSize * 0.95
                };
                span.Inlines.Add(new Run(code.Content));
                target.Add(span);
                return;
            }

            var emphasis = inline as EmphasisInline;
            if (emphasis != null)
            {
                var span = new Span();
                if (emphasis.DelimiterChar == '~')
                    // ~~删除线~~：只压一条横线，字号、字重、颜色都不动
                    span.TextDecorations = TextDecorations.Strikethrough;
                else if (emphasis.DelimiterChar == '=')
                    // ==荧光笔==：只铺底色，文字颜色不动，跟记号笔划过一样
                    span.Background = MarkdownTheme.Markdown.MarkBackground;
                else if (emphasis.DelimiterCount >= 2)
                    span.FontWeight = FontWeights.Bold;
                else
                    span.FontStyle = FontStyles.Italic;
                AddInlines(span.Inlines, emphasis, fontSize);
                target.Add(span);
                return;
            }

            var lineBreak = inline as LineBreakInline;
            if (lineBreak != null)
            {
                // 软换行按空格处理，硬换行才真的断行；开了「严格换行」就一律断行
                if (lineBreak.IsHard || StrictLineBreak)
                    target.Add(new LineBreak());
                else
                    target.Add(new Run(" "));
                return;
            }

            var wiki = inline as WikiLinkInline;
            if (wiki != null)
            {
                target.Add(MakeWikiLink(wiki, fontSize));
                return;
            }

            // 脚注引用 [^1]：排成上标小号数字，得排在 LinkInline 之前——
            // 它本身就派生自 LinkInline，落到下面就成了一条没有文字的链接
            var footnote = inline as FootnoteLink;
            if (footnote != null)
            {
                // Markdig 会在每条脚注末尾补一串回链（IsBackLink，每条引用一个），
                // 那是它自己渲染 HTML 用的。回链样式自己排（见 AppendBackLink），这里跳过，
                // 否则条文末尾会多出「1」这样的上标数字，跟左边那个编号重复
                if (!footnote.IsBackLink)
                    target.Add(BuildFootnoteReference(footnote, fontSize));
                return;
            }

            var link = inline as LinkInline;
            if (link != null)
            {
                if (link.IsImage)
                {
                    Uri uri;
                    bool remote;
                    BitmapSource local;
                    if (!TryPrepareImage(link, out uri, out remote, out local))
                    {
                        // 占位排成行内文本（图标是个小的行内元素），位置跟真图片一样听段落的：
                        // 软换行就当空格、独占一段就单独一行，别自己另外插换行
                        target.Add(ImageFallback(InlineText(link), link.Url, fontSize));
                        return;
                    }
                    // 夹在文字里的图片：InlineUIContainer 拿不到栏宽，靠 MarkdownImage 自己按预览区宽度封顶
                    target.Add(new InlineUIContainer(MakeImage(link, uri, remote, local, fontSize))
                    {
                        BaselineAlignment = BaselineAlignment.Bottom
                    });
                    return;
                }
                target.Add(MakeHyperlink(link.Url, link, fontSize));
                return;
            }

            var autolink = inline as AutolinkInline;
            if (autolink != null)
            {
                target.Add(MakeHyperlink(autolink.Url, null, fontSize));
                return;
            }

            var math = inline as MdMath;
            if (math != null)
            {
                string latex = math.Content.ToString();
                FrameworkElement formula = FormulaRenderer.TryCreateInline(latex, fontSize);
                if (formula == null)
                {
                    // 认不出的公式原样显示源码。Content 不含定界符，补回来
                    string mark = math.DelimiterCount >= 2 ? "$$" : "$";
                    target.Add(new Run(mark + latex + mark));
                    return;
                }
                // 行内公式是 UIElement，不参与文本选择（跟行内图片一个道理）。
                // 基线为什么用 Baseline、降部怎么让位，见 FormulaElement 的说明
                target.Add(new InlineUIContainer(formula)
                {
                    BaselineAlignment = BaselineAlignment.Baseline
                });
                return;
            }

            // 任务项前面的 [x] / [ ]：画一个固定边长的方框，勾上就在里面补个白勾。
            // 不能再用 ☐ / ☑ 这两个字符——它们来自不同的字形，字宽、墨迹大小都不一样，
            // 勾不勾选整行文字就会左右跳；方框自己定宽高，两种状态占位完全一致
            var task = inline as TaskList;
            if (task != null)
            {
                target.Add(new InlineUIContainer(TaskBox(task.Checked, fontSize))
                {
                    BaselineAlignment = BaselineAlignment.Center
                });
                target.Add(new Run(" "));
                return;
            }

            var container = inline as ContainerInline;
            if (container != null)
                AddInlines(target, container, fontSize);
        }

        /// <summary>
        /// 任务项的勾选框：边长固定的小方框，勾上就填成实心、压一个白勾。
        /// 两种状态走同一个方框、同一套尺寸——勾不勾选占位一样宽，后面那截文字才不会左右跳。
        /// 尺寸按字号算，缩放预览时跟着一起变。
        /// </summary>
        private static FrameworkElement TaskBox(bool isChecked, double fontSize)
        {
            double side = Math.Round(fontSize * 0.95);
            var border = new Border
            {
                Width = side,
                Height = side,
                CornerRadius = new CornerRadius(Math.Round(side * 0.18)),
                BorderThickness = new Thickness(Math.Max(1, Math.Round(side * 0.09))),
                BorderBrush = isChecked ? MarkdownTheme.Markdown.Link : MarkdownTheme.Muted,
                Background = isChecked ? MarkdownTheme.Markdown.Link : Brushes.Transparent
            };
            if (!isChecked)
                return border;

            border.Child = new TextBlock
            {
                Text = "✓",
                // 实心底上压白勾：浅色、深色主题都不用另配颜色
                Foreground = Brushes.White,
                FontSize = side * 0.8,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            return border;
        }

        /// <summary>
        /// 链接用主题的链接色，不加下划线：中文的下划线会把笔画切碎，只靠颜色区分就够。
        /// 绝对地址（http/https 等）交给 WPF 的默认行为；
        /// 站内目标——相对路径的 md 文档、本文件内的 #锚点——自己接管点击，交给主窗口去解析并打开。
        /// </summary>
        private static Hyperlink MakeHyperlink(string url, ContainerInline children, double fontSize)
        {
            var hyperlink = new Hyperlink
            {
                Foreground = MarkdownTheme.Markdown.Link,
                TextDecorations = null
            };
            if (children != null)
                AddInlines(hyperlink.Inlines, children, fontSize);
            else
                hyperlink.Inlines.Add(new Run(url));

            if (IsInternalLink(url))
            {
                AttachPreviewLink(hyperlink, PreviewLinkTarget.Relative(url));
                return hyperlink;
            }

            Uri uri;
            if (Uri.TryCreate(url, UriKind.Absolute, out uri))
                hyperlink.NavigateUri = uri;
            return hyperlink;
        }

        /// <summary>
        /// 脚注引用 [^1]：排成上标小号数字，颜色跟链接一致，点一下跳到文末那一条。
        /// 编号取 Footnote.Order —— Markdig 按引用出现的先后定的，跟文末列表一致。
        ///
        /// 目标块这会儿还没排出来（条目在文末，最后才轮到），所以这里只登记引用，
        /// 等整篇排完由 LinkFootnotes 把两头接上。
        /// </summary>
        private static System.Windows.Documents.Inline BuildFootnoteReference(FootnoteLink link, double fontSize)
        {
            int number = link.Footnote == null ? 0 : link.Footnote.Order;
            // 只占正文一半大，用 SuperscriptMark 排：BaselineAlignment.Superscript / TextTop 都是
            // 按父级字号抬高，数字顶会戳出正文上沿，只有自己包一个 TextBlock 才压得住。
            double size = Math.Max(8, fontSize * 0.5);
            if (number <= 0)
            {
                // 引用了没定义过的脚注：留个记号，不给点
                return SuperscriptMark("[?]", size, MarkdownTheme.Markdown.Link);
            }

            // 上标里的方括号看着乱，只留数字；下划线也不要，免得像条下角标
            var reference = new Hyperlink
            {
                Foreground = MarkdownTheme.Markdown.Link,
                TextDecorations = null,
                ToolTip = string.Format(MarkdownResources.T("footnoteJump"), number)
            };
            reference.Inlines.Add(SuperscriptMark(number.ToString(), size, MarkdownTheme.Markdown.Link));

            if (_footnoteReferences != null)
            {
                List<Hyperlink> list;
                if (!_footnoteReferences.TryGetValue(number, out list))
                {
                    list = new List<Hyperlink>();
                    _footnoteReferences.Add(number, list);
                }
                list.Add(reference);
            }
            return reference;
        }

        /// <summary>
        /// 排一个「半个字高、落在文字上半部分」的上标记号。
        /// 不用 BaselineAlignment.Superscript / TextTop：它们按父级字号抬高，数字顶会戳出正文上沿。
        /// 这里用 InlineUIContainer 包一个 TextBlock（底边贴基线），盒子高度只有半个字号的 1.3 倍左右，
        /// 天然落在正文高度以内；再用纯渲染的负位移把它抬到上半部分（RenderTransform 不参与排版，
        /// 所以不会把行高撑开）。
        /// </summary>
        private static System.Windows.Documents.Inline SuperscriptMark(string text, double size, Brush color)
        {
            var block = new TextBlock
            {
                Text = text,
                FontSize = size,
                Foreground = color,
                // 这几个字是独立控件，会自己吃掉鼠标事件；点击动作由 AttachPreviewLink 单独接一份
                Cursor = Cursors.Hand,
                RenderTransform = new TranslateTransform(0, -size * 0.45)
            };
            return new InlineUIContainer(block) { BaselineAlignment = BaselineAlignment.Baseline };
        }

        /// <summary>
        /// 文末的脚注列表，当参考文献看：上方压一条通栏细线，每条一段，排成「1 正文 ↩」。
        /// 整条用引用块那个灰字，比正文轻一档；序号上标、行首悬挂缩进——
        /// 正文多行时续行跟正文左边缘对齐，不钻到序号底下。
        /// 回链 ↩ 由 LinkFootnotes 补在条目末尾，只有一个箭头，点一下跳回正文里对应的引用处。
        /// 脚注本身是个容器块（里面可能有多段、列表），内容照样走 AddBlocks。
        /// </summary>
        private static WpfBlock FootnoteList(FootnoteGroup group, double fontSize)
        {
            // 脚注的字号比预览字号小 2 号（下限 9，免得缩到看不清）。
            // 注意必须把 FontSize 显式设在条目容器上：Paragraph() 不会给段落设字号，
            // 正文平时是直接继承文档默认值的，这里不设就会继承成正文那么大的字。
            double itemFont = Math.Max(9, fontSize - 2);
            var section = new Section
            {
                BorderBrush = MarkdownTheme.Markdown.HeadingRule2,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Margin = new Thickness(0, fontSize * 1.2, 0, 0),
                Padding = new Thickness(0, fontSize * 0.5, 0, 0)
            };

            int fallback = 0;
            foreach (MdBlock child in group)
            {
                var footnote = child as Footnote;
                if (footnote == null)
                    continue;
                fallback++;
                int number = footnote.Order > 0 ? footnote.Order : fallback;

                var item = new Section
                {
                    FontSize = itemFont,   // 条目里的一切（正文、行内代码、上标序号）都按这个字号走
                    Margin = new Thickness(0, 0, 0, itemFont * 0.35),
                    // 脚注是「注解」，比正文轻一档：跟引用块同一个文字色，不抢正文的注意力
                    Foreground = MarkdownTheme.Markdown.QuoteText
                };
                AddBlocks(item.Blocks, footnote, itemFont, null);

                // 第一条段落接序号；整条脚注没有段落（只有代码块、表格）时补一个空的放在最前
                Paragraph first = FirstParagraph(item);
                if (first == null)
                {
                    first = new Paragraph();
                    if (item.Blocks.FirstBlock == null)
                        item.Blocks.Add(first);
                    else
                        item.Blocks.InsertBefore(item.Blocks.FirstBlock, first);
                }

                double indent = itemFont * 1.1;
                first.Margin = new Thickness(indent, 0, 0, itemFont * 0.3);
                first.TextIndent = -indent;

                // 序号照正文里那个上标数字的样子排：半个字高、落在上半部分，不戳出文字上沿
                System.Windows.Documents.Inline mark = SuperscriptMark(number.ToString(),
                    Math.Max(8, itemFont * 0.5), MarkdownTheme.Markdown.QuoteText);
                if (first.Inlines.FirstInline == null)
                {
                    first.Inlines.Add(mark);
                    first.Inlines.Add(new Run(" "));
                }
                else
                {
                    first.Inlines.InsertBefore(first.Inlines.FirstInline, new Run(" "));
                    first.Inlines.InsertBefore(first.Inlines.FirstInline, mark);
                }

                if (_footnoteItems != null)
                    _footnoteItems[number] = item;

                section.Blocks.Add(item);
            }
            return section;
        }

        /// <summary>
        /// 把脚注两头接上：正文里每个引用点一下滚到文末那一条，条目末尾的 ↩ 点一下滚回引用处。
        /// 同一条脚注被引用多次时，每个引用都跳到条目，回链只认第一次——跳回最早那处就够了。
        /// 只定义没引用的脚注没有回链可给，也就不挂。
        /// </summary>
        private static void LinkFootnotes()
        {
            if (_footnoteReferences == null || _footnoteItems == null)
                return;

            foreach (KeyValuePair<int, Section> pair in _footnoteItems)
            {
                List<Hyperlink> references;
                if (!_footnoteReferences.TryGetValue(pair.Key, out references) || references.Count == 0)
                    continue;

                foreach (Hyperlink reference in references)
                    AttachPreviewLink(reference, PreviewLinkTarget.Footnote(pair.Value));

                Hyperlink back = AppendBackLink(pair.Value);
                if (back != null)
                    AttachPreviewLink(back, PreviewLinkTarget.Footnote(references[0]));
            }
        }

        /// <summary>
        /// 在条目最后一段末尾补一个 ↩ 回链。多段脚注挂在最后一段后面，读起来才顺；
        /// 条目里一个段落都没有就不补，返回 null。
        /// </summary>
        private static Hyperlink AppendBackLink(Section item)
        {
            Paragraph last = null;
            foreach (WpfBlock block in item.Blocks)
            {
                var paragraph = block as Paragraph;
                if (paragraph != null)
                    last = paragraph;
            }
            if (last == null)
                return null;

            var link = new Hyperlink
            {
                Foreground = MarkdownTheme.Markdown.Link,
                TextDecorations = null,
                ToolTip = MarkdownResources.T("footnoteBack"),
                // 回链只放一个箭头：左边已经有编号了，右边再带一个就是重复。
                // 大小跟正文里的上标数字一致，但不上标——上下居中，落在行中腰上
                FontSize = Math.Max(9, last.FontSize * 0.72),
                BaselineAlignment = BaselineAlignment.Center
            };
            link.Inlines.Add(new Run("↩"));
            last.Inlines.Add(new Run(" "));
            last.Inlines.Add(link);
            return link;
        }

        /// <summary>取容器里的第一段正文；第一块就是表格、代码块时返回 null。</summary>
        private static Paragraph FirstParagraph(Section section)
        {
            foreach (WpfBlock block in section.Blocks)
            {
                var paragraph = block as Paragraph;
                if (paragraph != null)
                    return paragraph;
            }
            return null;
        }

        /// <summary>[[...]] 排成普通链接的样子，显示文本用别名或目标名；跟普通链接一样不加下划线。</summary>
        private static Hyperlink MakeWikiLink(WikiLinkInline wiki, double fontSize)
        {
            var hyperlink = new Hyperlink
            {
                Foreground = MarkdownTheme.Markdown.Link,
                TextDecorations = null
            };
            hyperlink.Inlines.Add(new Run(wiki.Label));
            AttachPreviewLink(hyperlink, PreviewLinkTarget.Wiki(wiki.Target, wiki.Anchor, wiki.Raw));
            return hyperlink;
        }

        /// <summary>
        /// 是不是该由预览自己打开的目标：相对路径的 md 文档（含子目录），或本文件内的 #锚点。
        /// 绝对地址（http、https、mailto 等）不算，仍旧走原来的路子。
        /// </summary>
        private static bool IsInternalLink(string url)
        {
            if (string.IsNullOrEmpty(url))
                return false;
            if (url[0] == '#')
                return true;

            Uri absolute;
            if (Uri.TryCreate(url, UriKind.Absolute, out absolute))
                return false;

            string path = url;
            int hash = path.IndexOf('#');
            if (hash >= 0)
                path = path.Substring(0, hash);
            return IsMarkdownDoc(path);
        }

        /// <summary>只有 Markdown 文档才由预览自己打开，跟窗口那边认的扩展名一致。</summary>
        private static bool IsMarkdownDoc(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;
            string ext = IOPath.GetExtension(path).ToLowerInvariant();
            return ext == ".md" || ext == ".markdown" || ext == ".mdown";
        }

        /// <summary>
        /// 预览里的链接统一走 Click 交回主窗口：站内链接由它解析路径并打开，脚注由它滚到目标处。
        /// 点开动作要在渲染期就攥住：_linkOpener 是渲染期的暂存字段，Render 一结束就清空，
        /// 等点击时再读只会拿到 null。
        /// </summary>
        private static void AttachPreviewLink(Hyperlink hyperlink, PreviewLinkTarget target)
        {
            Action<PreviewLinkTarget> opener = _linkOpener;
            hyperlink.Click += (sender, e) =>
            {
                // 拦下来，别再往上冒泡触发 WPF 自带的导航
                e.Handled = true;
                if (opener != null)
                    opener(target);
            };

            // 上标数字是用 InlineUIContainer 排的，那块 UI 自己会吃掉鼠标事件、也不会替父级
            // 触发 Click，所以给里面的每个控件单独接一份同样的动作
            foreach (System.Windows.Documents.Inline inline in hyperlink.Inlines)
            {
                var container = inline as InlineUIContainer;
                if (container == null)
                    continue;
                var child = container.Child as UIElement;
                if (child == null)
                    continue;
                // 跟复制按钮同一个坑：不先把按下标成已处理，FlowDocument 的文本选择逻辑会把鼠标
                // 抢过去，抬起事件根本到不了这里
                child.MouseLeftButtonDown += (sender, e) => e.Handled = true;
                child.MouseLeftButtonUp += (sender, e) =>
                {
                    e.Handled = true;
                    if (opener != null)
                        opener(target);
                };
            }
        }

        /// <summary>段落里若只有一个图片（前后允许空白和换行）就返回它，否则返回 null。</summary>
        private static LinkInline SoleImage(ContainerInline inline)
        {
            if (inline == null)
                return null;
            LinkInline image = null;
            for (MdInline child = inline.FirstChild; child != null; child = child.NextSibling)
            {
                var literal = child as LiteralInline;
                if (literal != null)
                {
                    if (literal.Content.ToString().Trim().Length == 0)
                        continue;
                    return null;
                }
                if (child is LineBreakInline)
                    continue;
                var link = child as LinkInline;
                if (link != null && link.IsImage && image == null)
                {
                    image = link;
                    continue;
                }
                return null;
            }
            return image;
        }

        /// <summary>独占一段的图片，排成块级，宽度按预览区自适应。</summary>
        private static WpfBlock ImageBlock(LinkInline image, double fontSize)
        {
            Uri uri;
            bool remote;
            BitmapSource local;
            if (!TryPrepareImage(image, out uri, out remote, out local))
            {
                // 占位是一行字，排成段落比塞进 BlockUIContainer 更听话：缩进、折行都由段落自己管
                var paragraph = new Paragraph { Margin = new Thickness(0, fontSize * 0.2, 0, fontSize * 0.6) };
                paragraph.Inlines.Add(ImageFallback(InlineText(image), image.Url, fontSize));
                return paragraph;
            }

            return new BlockUIContainer(MakeImage(image, uri, remote, local, fontSize))
            {
                Margin = new Thickness(0, fontSize * 0.2, 0, fontSize * 0.6)
            };
        }

        /// <summary>
        /// 解析图片地址并（本地图）当场解码。返回 false 表示这张图显示不出来——路径不可用、
        /// 本地文件不存在或根本不是图片——调用方改排占位。
        ///
        /// 本地图必须现在解码完：量尺寸时图片尺寸得立刻可用，否则首次会按栏宽排版，
        /// 小图被当成大图放大到铺满。
        /// </summary>
        private static bool TryPrepareImage(LinkInline link, out Uri uri, out bool remote, out BitmapSource local)
        {
            local = null;
            if (!ResolveImageUri(link.Url, _imageBaseDir, out uri, out remote))
                return false;
            if (remote)
                return true;
            local = TryLoadLocalImage(uri);
            return local != null;
        }

        /// <summary>把一张能加载的图片排成控件：本地图直接带位图，网络图异步拉取。</summary>
        private static MarkdownImage MakeImage(LinkInline link, Uri uri, bool remote, BitmapSource local,
            double fontSize)
        {
            string alt = InlineText(link);
            var image = new MarkdownImage
            {
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Left,
                Source = local
            };
            // `{width=50%}` 这类标记在解析后被摘下挂在图片节点上，这里取来当宽度上限
            double ratio;
            if (ImageWidthAttribute.TryGetWidthRatio(link, out ratio))
                image.WidthRatio = ratio;
            if (!string.IsNullOrEmpty(alt))
                image.ToolTip = alt;
            FollowImageHost(image, MarkdownImage.AvailableWidthProperty);

            if (remote)
                LoadRemoteImage(image, uri, alt, link.Url, fontSize);
            return image;
        }

        /// <summary>把控件宽度盯在预览区宽度上：渲染时先取当前宽度，窗口缩放时由绑定刷新。</summary>
        private static void FollowImageHost(DependencyObject target, DependencyProperty property)
        {
            if (_imageHost == null)
                return;
            if (_imageHost.ActualWidth > 0)
                target.SetValue(property, _imageHost.ActualWidth);
            BindingOperations.SetBinding(target, property, new Binding("ActualWidth") { Source = _imageHost });
        }

        /// <summary>本地图片同步解码。文件不存在、内容不是图片、文件损坏都算解不出来，返回 null。</summary>
        private static BitmapSource TryLoadLocalImage(Uri uri)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = uri;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 网络图片异步下载。不用 BitmapImage 自带的 UriSource 下载：它走 WIC 的网络栈，
        /// 在部分环境（如本机）连得上却拿不到内容，只会得到一张空图；改用 WebClient 拉字节流再解码。
        /// </summary>
        private static void LoadRemoteImage(MarkdownImage image, Uri uri, string alt, string url, double fontSize)
        {
            var client = new ImageWebClient();
            client.Headers[HttpRequestHeader.UserAgent] =
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) MarkdownKit";

            // 私有仓库 / 私有 WebDAV 的图要带认证才取得到：是否带、是否换成 raw 地址交给宿主决定。
            MarkdownImageRequest access = MarkdownResources.ResolveImage == null
                ? null
                : MarkdownResources.ResolveImage(url);
            if (access != null)
            {
                client.Authorization = access.Authorization;
                if (!string.IsNullOrEmpty(access.Url) && !string.Equals(access.Url, url, StringComparison.Ordinal))
                {
                    Uri authorized;
                    if (Uri.TryCreate(access.Url, UriKind.Absolute, out authorized))
                        uri = authorized;
                }
            }

            client.DownloadDataCompleted += (s, e) =>
            {
                client.Dispose();
                if (e.Cancelled || e.Error != null)
                {
                    ReplaceWithFallback(image, alt, url, fontSize);
                    return;
                }
                try
                {
                    using (var stream = new System.IO.MemoryStream(e.Result))
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = stream;
                        bitmap.EndInit();
                        bitmap.Freeze();
                        image.Source = bitmap;
                    }
                }
                catch (Exception)
                {
                    ReplaceWithFallback(image, alt, url, fontSize);
                }
            };
            try
            {
                client.DownloadDataAsync(uri);
            }
            catch (Exception)
            {
                client.Dispose();
                ReplaceWithFallback(image, alt, url, fontSize);
            }
        }

        /// <summary>给图片下载加超时的 WebClient：默认实现没有超时，网络卡住会一直挂着。</summary>
        private sealed class ImageWebClient : WebClient
        {
            /// <summary>私有仓库取图要带的 Authorization 头；null 表示匿名取。</summary>
            public string Authorization;

            protected override WebRequest GetWebRequest(Uri address)
            {
                WebRequest request = base.GetWebRequest(address);
                request.Timeout = 15000;
                var http = request as HttpWebRequest;
                if (http != null)
                {
                    http.ReadWriteTimeout = 15000;
                    // 令牌挂在真正的请求上，而不是丢给 WebClient 那层去转抄——
                    // Gitee 只认这个头（放查询串是 403），302 到签名地址之后就不再需要它了
                    if (!string.IsNullOrEmpty(Authorization))
                        http.Headers[HttpRequestHeader.Authorization] = Authorization;
                    // 自签名 WebDAV 服务器：是否放宽证书由宿主决定
                    if (MarkdownResources.AcceptCertificate != null)
                        http.ServerCertificateValidationCallback = MarkdownResources.AcceptCertificate;
                }
                return request;
            }
        }

        /// <summary>
        /// 把加载失败的图片换成占位。
        ///
        /// 下载回调到达时图片早就挂进文档了，就地把它换成占位那段行内文本。但 DownloadDataAsync 也可能
        /// 同步抛（地址不被支持之类），那一刻图片还在构造途中、还没挂上父级——查 Parent 只会查到 null，
        /// 占位就丢了，界面上留下一块空白。所以查不到父级时把这次替换推到布局阶段再做。
        /// </summary>
        private static void ReplaceWithFallback(MarkdownImage image, string alt, string url, double fontSize)
        {
            if (SwapInFallback(image, alt, url, fontSize))
                return;
            image.Dispatcher.BeginInvoke(new Action(() => SwapInFallback(image, alt, url, fontSize)),
                DispatcherPriority.Loaded);
        }

        /// <summary>父级是行内/块级图片容器就把内容换成占位，换成功返回 true。</summary>
        private static bool SwapInFallback(MarkdownImage image, string alt, string url, double fontSize)
        {
            var inline = image.Parent as InlineUIContainer;
            if (inline != null)
            {
                InlineCollection inlines = SiblingInlines(inline);
                if (inlines == null)
                    return false;
                // 占位是一段行内文本，直接顶掉那个图片容器：位置照旧，别自己另外插换行
                inlines.InsertBefore(inline, ImageFallback(alt, url, fontSize));
                inlines.Remove(inline);
                return true;
            }
            var block = image.Parent as BlockUIContainer;
            if (block != null)
            {
                block.Child = BlockFallback(alt, url, fontSize);
                return true;
            }
            return false;
        }

        /// <summary>取某个行内元素所在的兄弟集合（段落或超链接里）；都挂不上返回 null。</summary>
        private static InlineCollection SiblingInlines(System.Windows.Documents.Inline inline)
        {
            var paragraph = inline.Parent as Paragraph;
            if (paragraph != null)
                return paragraph.Inlines;
            // Hyperlink 也是 Span，一起接住
            var span = inline.Parent as Span;
            return span == null ? null : span.Inlines;
        }

        /// <summary>
        /// 图片显示不出来时的替代：破图图标 + alt 文字（没有 alt 就退回 URL），跟浏览器里加载失败的样子一致。
        ///
        /// 早先这里只吐一段灰色斜体的 alt 文字，看着像作者额外写的一句注释，读者根本意识不到
        /// 「这儿本该有一张图、只是没加载出来」；图还在、只是地址坏了这件事得摆在明面上。
        /// 文字按正文的样式走（不缩号、不用斜体、不用灰色），和旁边的正文连起来读才顺。
        ///
        /// 排成纯行内文本（Span）而不是塞一个 TextBlock 控件：行内控件拿到的是无限宽度约束，
        /// 长说明会一路顶出预览区右边；而段落里的文本自己会按栏宽、按列表缩进折行。
        /// </summary>
        private static Span ImageFallback(string alt, string url, double fontSize)
        {
            double iconSize = fontSize * 1.1;
            var span = new Span
            {
                FontSize = fontSize,
                Foreground = MarkdownTheme.Markdown.Text,
                // 鼠标停上去能看见到底想加载哪个地址，坏链排查方便
                ToolTip = string.IsNullOrWhiteSpace(url) ? null : url
            };
            span.Inlines.Add(new InlineUIContainer(new Image
            {
                Source = BrokenImageIcon.Get(),
                Width = iconSize,
                Height = iconSize,
                Stretch = Stretch.Uniform
            })
            {
                // 图标比字高一点，按行心摆，别往上飘
                BaselineAlignment = BaselineAlignment.Center
            });
            span.Inlines.Add(new Run(" " + (!string.IsNullOrWhiteSpace(alt) ? alt : url)));
            return span;
        }

        /// <summary>块级容器里的占位（独占一段的图）。块里拿到的是有限宽度，普通 TextBlock 就会自己折行。</summary>
        private static TextBlock BlockFallback(string alt, string url, double fontSize)
        {
            double iconSize = fontSize * 1.1;
            var text = new TextBlock
            {
                FontSize = fontSize,
                Foreground = MarkdownTheme.Markdown.Text,
                TextWrapping = TextWrapping.Wrap,
                ToolTip = string.IsNullOrWhiteSpace(url) ? null : url
            };
            text.Inlines.Add(new InlineUIContainer(new Image
            {
                Source = BrokenImageIcon.Get(),
                Width = iconSize,
                Height = iconSize,
                Stretch = Stretch.Uniform
            })
            {
                BaselineAlignment = BaselineAlignment.Center
            });
            text.Inlines.Add(new Run(" " + (!string.IsNullOrWhiteSpace(alt) ? alt : url)));
            return text;
        }

        /// <summary>
        /// 把 Markdown 里的图片地址解析成可加载的 URI。http/https 走网络，其余按本地路径处理，
        /// 依次试四种写法：
        /// 绝对路径原样用；相对路径拼当前文档所在目录；再试文档目录下的 files 子目录；
        /// 最后逐级往上，看各层的 files 目录（文档库把图片集中收在库根的 files 下）。
        /// 后两步是给文档库用的——图和文档不在同一个目录、文档里只写图片文件名时靠它们找到，
        /// 而且由近及远，文档自己那份 files 优先于上层库根的。
        /// 四种都落空时仍旧拼出相对文档目录的路径，交给调用方排破图，鼠标停上去能看到原始地址。
        /// 解析不出来返回 false。
        /// </summary>
        private static bool ResolveImageUri(string url, string baseDir, out Uri uri, out bool remote)
        {
            uri = null;
            remote = false;
            if (string.IsNullOrWhiteSpace(url))
                return false;

            string text = url.Trim();
            Uri absolute;
            if (Uri.TryCreate(text, UriKind.Absolute, out absolute))
            {
                if (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps)
                {
                    uri = absolute;
                    remote = true;
                    return true;
                }
                if (absolute.IsFile)
                {
                    uri = absolute;
                    return true;
                }
                // 其它协议（data:、ftp: 等）暂不支持
                return false;
            }

            try
            {
                // Markdig 会把路径里的空格等转义成 %20，本地路径要先还原，再统一成反斜杠
                string path = Uri.UnescapeDataString(text).Replace('/', '\\');
                if (IOPath.IsPathRooted(path))
                {
                    // 1) 绝对路径：原样用，不再猜别的目录
                    uri = new Uri(IOPath.GetFullPath(path));
                    return true;
                }
                if (string.IsNullOrEmpty(baseDir))
                    return false;   // 未保存的新文档没有基准目录，相对路径无处可依

                // 2) 相对文档所在目录：图就放在文档旁边
                string local = IOPath.GetFullPath(IOPath.Combine(baseDir, path));
                if (System.IO.File.Exists(local))
                {
                    uri = new Uri(local);
                    return true;
                }

                // 3) 文档目录下的 files 子目录：图跟文档放在一起，只是另外收进 files 里
                string nearby = IOPath.GetFullPath(IOPath.Combine(baseDir, LibraryImageDir, path));
                if (System.IO.File.Exists(nearby))
                {
                    uri = new Uri(nearby);
                    return true;
                }

                // 4) 再逐级往上：文档库根的 files，文档放在库的二级、三级子目录里也找得到
                string inLibrary = FindLibraryImage(IOPath.GetDirectoryName(baseDir), path);
                if (inLibrary != null)
                {
                    uri = new Uri(inLibrary);
                    return true;
                }

                // 都没有：仍按文档目录排一个，由调用方显示破图
                uri = new Uri(local);
                return true;
            }
            catch (Exception)
            {
                return false;   // 含非法字符等，当作解析不出来
            }
        }

        /// <summary>
        /// 从给定目录开始逐级往上找 files\&lt;图片路径&gt;，返回第一个存在的完整路径；一个都没有返回 null。
        /// 调用方从文档目录的上一级开始传，因为文档自己目录下的 files 归上一步管——那一步更近，得先试。
        /// 由近及远地找，这样文档库根下的 files 能被子目录里的文档认出来，外层也不会顶掉更近的那份。
        /// </summary>
        private static string FindLibraryImage(string dir, string path)
        {
            for (int depth = 0; depth < LibraryImageSearchDepth && !string.IsNullOrEmpty(dir); depth++)
            {
                string candidate = IOPath.GetFullPath(IOPath.Combine(dir, LibraryImageDir, path));
                if (System.IO.File.Exists(candidate))
                    return candidate;
                dir = IOPath.GetDirectoryName(dir);
            }
            return null;
        }
    }

    /// <summary>大纲里的一条标题。</summary>
    public sealed class HeadingInfo
    {
        /// <summary>标题层级，1~6。</summary>
        public int Level { get; set; }

        /// <summary>标题的纯文本，已去掉 #、加粗等标记。</summary>
        public string Text { get; set; }

        /// <summary>源码里的行号，0 起。编辑模式按它定位。</summary>
        public int SourceLine { get; set; }

        /// <summary>渲染出的段落。只有预览模式渲染时才填，编辑模式为 null。</summary>
        public Paragraph Target { get; set; }

        /// <summary>PDF 书签指向的页码，0 起；Markdown / 代码大纲不用，保持 -1。</summary>
        public int Page { get; set; } = -1;
    }

    /// <summary>渲染出的顶层块与它在源码里的起始行，供预览按源码行定位。</summary>
    public sealed class BlockAnchor
    {
        /// <summary>源码里的行号，0 起。</summary>
        public int SourceLine { get; set; }

        /// <summary>这一行起渲染出的块。</summary>
        public WpfBlock Target { get; set; }
    }
}
