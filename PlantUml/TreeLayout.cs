using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace MarkdownKit.PlantUml
{
    /// <summary>树形图里一个节点（或 JSON/YAML 的一行）算好的几何。</summary>
    internal sealed class TreeGeom
    {
        public TreeNode Source;
        public Rect Rect;
        public FormattedText Text;

        /// <summary>JSON / YAML 的值格文字，非表格行是 null。</summary>
        public FormattedText Value;

        /// <summary>JSON / YAML 的键列宽度（含内边距），用来画中间那道竖线。</summary>
        public double KeyW;

        /// <summary>命中的高亮行底色；未高亮是 null。</summary>
        public Brush RowBackground;

        /// <summary>键格 / 值格的字体、字号、文字色。</summary>
        public TreeCellStyle KeyCell;
        public TreeCellStyle ValueCell;
    }

    /// <summary>一个格子的最终文字样式。</summary>
    internal sealed class TreeCellStyle
    {
        public Typeface Typeface;
        public double FontSize;
        public Brush TextColor;
        public bool Underline;
    }

    /// <summary>JSON / YAML 表格的最终外观。</summary>
    internal sealed class TreeTableStyle
    {
        public Brush Fill;
        public Brush KeyFill;
        public Pen Border;
        public double Corner;
        public Pen Separator;
    }

    /// <summary>父子之间的一条连线，From/To 已落在两个框的边界上。</summary>
    internal sealed class TreeEdgeGeom
    {
        public TreeNode Parent;
        public TreeNode Child;
        public Point From;
        public Point To;
    }

    /// <summary>一张树形图算好的几何。</summary>
    internal sealed class TreeLayout
    {
        public TreeKind Kind;
        public double Width;
        public double Height;
        public TitleGeom Title;

        /// <summary>mindmap / WBS 的节点框；JSON / YAML 的表格框。</summary>
        public readonly List<TreeGeom> Nodes = new List<TreeGeom>();

        /// <summary>JSON / YAML 表格里的行。</summary>
        public readonly List<TreeGeom> Rows = new List<TreeGeom>();

        public readonly List<TreeEdgeGeom> Edges = new List<TreeEdgeGeom>();

        /// <summary>JSON / YAML 表格的外观；其它图型为 null。</summary>
        public TreeTableStyle Table;

        /// <summary>JSON / YAML 表格之间连线的画笔与颜色。</summary>
        public Pen EdgePen;
        public Brush EdgeBrush;
    }

    /// <summary>
    /// 由模型算出几何。
    /// 思维导图和 WBS 是"整齐树"：先自底向上量出每棵子树在层内轴上占多宽（Band），
    /// 再自顶向下把节点摆进去；导图往右长（`+` 分支往左），WBS 往下长。
    /// JSON / YAML 不一样：一个对象整体画成一张表，嵌套对象从对应那一行往右挂出去。
    /// </summary>
    internal static class TreeLayoutBuilder
    {
        public static TreeLayout Build(TreeDiagram diagram, double fontSize, double pixelsPerDip,
            double maxKeyTextWidth, double maxValueTextWidth)
        {
            return new Impl(diagram, fontSize, pixelsPerDip, maxKeyTextWidth, maxValueTextWidth).Build();
        }

        private sealed class Impl
        {
            private readonly TreeDiagram _diagram;
            private readonly double _fs;
            private readonly double _ppd;
            private readonly Typeface _tf;
            private readonly Typeface _tfBold;
            private readonly Brush _text;
            private readonly Brush _defaultFill;
            private readonly Brush _defaultKeyFill;
            private readonly Brush _defaultBorder;
            private readonly TreeStyleSheet _styles;
            private readonly TreeLayout _layout = new TreeLayout();
            private readonly Dictionary<TreeNode, TreeGeom> _geoms = new Dictionary<TreeNode, TreeGeom>();
            private readonly Dictionary<TreeNode, TreeGeom> _rowGeoms = new Dictionary<TreeNode, TreeGeom>();

            /// <summary>官方 skin 里 yamlDiagram / jsonDiagram 的 highlight 默认底色。</summary>
            private static readonly Brush DefaultHighlight = CreateDefaultHighlight();

            /// <summary>JSON / YAML 键、值文字的最大宽度；超过就折行。PositiveInfinity 表示不限。</summary>
            private readonly double _maxKeyWidth;
            private readonly double _maxValueWidth;

            private double U(double v)
            {
                return v * _fs / 14.0;
            }

            /// <summary>层与层之间的空隙。</summary>
            private double DepthGap
            {
                get { return _diagram.Kind == TreeKind.Wbs ? U(30) : U(50); }
            }

            /// <summary>同层相邻子树之间的空隙。</summary>
            private double SiblingGap
            {
                get { return _diagram.Kind == TreeKind.Wbs ? U(28) : U(18); }
            }

            private bool Vertical
            {
                get { return _diagram.Kind == TreeKind.Wbs; }
            }

            private bool IsTable
            {
                get { return _diagram.Kind == TreeKind.Json || _diagram.Kind == TreeKind.Yaml; }
            }

            /// <summary>JSON / YAML 的行高。</summary>
            private double RowHeight
            {
                get { return _fs * 1.6; }
            }

            public Impl(TreeDiagram diagram, double fontSize, double pixelsPerDip,
                double maxKeyTextWidth, double maxValueTextWidth)
            {
                _diagram = diagram;
                _fs = fontSize;
                _ppd = pixelsPerDip <= 0 ? 1.0 : pixelsPerDip;
                _tf = new Typeface(MarkdownTheme.DiagramFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                _tfBold = MarkdownTheme.BoldTypeface(MarkdownTheme.DiagramFamily);
                _text = MarkdownTheme.Markdown.Text;
                _defaultFill = MarkdownTheme.Background;
                _defaultKeyFill = MarkdownTheme.Markdown.CodeBackground;
                _defaultBorder = MarkdownTheme.Markdown.Text;
                _styles = diagram.Styles;
                _maxKeyWidth = Cap(maxKeyTextWidth);
                _maxValueWidth = Cap(maxValueTextWidth);
            }

            private static Brush CreateDefaultHighlight()
            {
                var brush = new SolidColorBrush(Color.FromRgb(0xCC, 0xFF, 0x02));
                brush.Freeze();
                return brush;
            }

            /// <summary>非正数 / NaN / 无穷大一律当不限制宽度。</summary>
            private static double Cap(double width)
            {
                return double.IsNaN(width) || width <= 0 ? double.PositiveInfinity : width;
            }

            public TreeLayout Build()
            {
                if (_diagram.Roots.Count == 0)
                    return _layout;

                _layout.Kind = _diagram.Kind;

                double margin = _fs * 0.8;
                double top = margin;
                if (!string.IsNullOrEmpty(_diagram.Title))
                {
                    FormattedText title = Text(_diagram.Title, _tfBold, _text);
                    _layout.Title = new TitleGeom { Text = title, Top = top, CenterX = 0 };
                    top += title.Height + _fs * 0.8;
                }

                if (IsTable)
                {
                    _layout.Table = BuildTableStyle();
                    _layout.EdgePen = BuildEdgePen(out _layout.EdgeBrush);
                    foreach (TreeNode root in _diagram.Roots)
                        MeasureTable(root, _diagram.Highlights);
                    double cursor = 0;
                    foreach (TreeNode root in _diagram.Roots)
                    {
                        PlaceTable(root, 0, cursor);
                        cursor += Extent(root) + SiblingGap;
                    }
                }
                else
                {
                    foreach (TreeNode root in _diagram.Roots)
                        Measure(root);
                    foreach (TreeNode root in _diagram.Roots)
                        root.Band = Band(root);

                    double cursor = 0;
                    foreach (TreeNode root in _diagram.Roots)
                    {
                        Place(root, 0, cursor, 1);
                        cursor += root.Band + SiblingGap;
                    }
                    foreach (TreeNode root in _diagram.Roots)
                        BuildEdges(root);
                }

                double minX = double.MaxValue;
                double minY = double.MaxValue;
                double maxRight = 0;
                double maxBottom = 0;
                foreach (TreeGeom geom in _layout.Nodes)
                {
                    minX = Math.Min(minX, geom.Rect.Left);
                    minY = Math.Min(minY, geom.Rect.Top);
                    maxRight = Math.Max(maxRight, geom.Rect.Right);
                    maxBottom = Math.Max(maxBottom, geom.Rect.Bottom);
                }
                if (minX == double.MaxValue)
                    minX = minY = 0;

                double shiftX = margin - minX;
                double shiftY = top - minY;
                foreach (TreeGeom geom in _layout.Nodes)
                    geom.Rect = new Rect(geom.Rect.X + shiftX, geom.Rect.Y + shiftY, geom.Rect.Width, geom.Rect.Height);
                foreach (TreeGeom geom in _layout.Rows)
                    geom.Rect = new Rect(geom.Rect.X + shiftX, geom.Rect.Y + shiftY, geom.Rect.Width, geom.Rect.Height);
                foreach (TreeEdgeGeom edge in _layout.Edges)
                {
                    edge.From = new Point(edge.From.X + shiftX, edge.From.Y + shiftY);
                    edge.To = new Point(edge.To.X + shiftX, edge.To.Y + shiftY);
                }

                _layout.Width = maxRight + shiftX + margin;
                _layout.Height = maxBottom + shiftY + margin;
                if (_layout.Title != null)
                    _layout.Title.CenterX = _layout.Width / 2;
                return _layout;
            }

            // ── 大纲式（mindmap / wbs）────────────────────────────

            private void Measure(TreeNode node)
            {
                FormattedText text = Text(node.Text ?? "", _tf, _text);
                var geom = new TreeGeom { Source = node, Text = text };

                if (_diagram.Kind == TreeKind.MindMap)
                {
                    // 照官方实测：框宽 = 文字 + 20（左右各 10），框高 = 行高 + 22
                    node.W = text.Width + U(20);
                    node.H = text.Height + U(22);
                }
                else
                {
                    node.W = text.Width + U(13);
                    node.H = text.Height + U(18);
                }
                node.W = Math.Max(node.W, U(30));
                node.H = Math.Max(node.H, U(26));
                _geoms[node] = geom;

                foreach (TreeNode child in node.Children)
                    Measure(child);
            }

            /// <summary>子树在"层内轴"上占的尺寸。</summary>
            private double Band(TreeNode node)
            {
                double own = Vertical ? node.W : node.H;
                if (node.Children.Count == 0)
                    return own;

                double sum = 0;
                foreach (TreeNode child in node.Children)
                {
                    child.Band = Band(child);
                    sum += child.Band;
                }
                sum += SiblingGap * (node.Children.Count - 1);
                return Math.Max(own, sum);
            }

            /// <summary>depthPos 是沿分层轴的起点，bandStart 是子树在层内轴上的起点，dir 为 ±1 表示往右/往左长。</summary>
            private void Place(TreeNode node, double depthPos, double bandStart, int dir)
            {
                double own = Vertical ? node.W : node.H;
                double bandMid = bandStart + (node.Band - own) / 2;

                if (Vertical)
                {
                    node.X = bandMid;
                    node.Y = depthPos;
                }
                else
                {
                    node.X = dir > 0 ? depthPos : depthPos - node.W;
                    node.Y = bandMid;
                }

                TreeGeom geom = _geoms[node];
                geom.Rect = new Rect(node.X, node.Y, node.W, node.H);
                _layout.Nodes.Add(geom);

                if (node.Children.Count == 0)
                    return;

                double childDepth = Vertical
                    ? depthPos + node.H + DepthGap
                    : (dir > 0 ? depthPos + node.W + DepthGap : depthPos - node.W - DepthGap);

                double cursor = bandStart;
                foreach (TreeNode child in node.Children)
                {
                    Place(child, childDepth, cursor, dir);
                    cursor += child.Band + SiblingGap;
                }
            }

            /// <summary>连线端点落在两个框相对的那条边上。</summary>
            private void BuildEdges(TreeNode parent)
            {
                TreeGeom pg = _geoms[parent];
                foreach (TreeNode child in parent.Children)
                {
                    TreeGeom cg = _geoms[child];
                    Point from;
                    Point to;
                    if (Vertical)
                    {
                        from = new Point(pg.Rect.X + pg.Rect.Width / 2, pg.Rect.Bottom);
                        to = new Point(cg.Rect.X + cg.Rect.Width / 2, cg.Rect.Top);
                    }
                    else if (child.LeftSide || cg.Rect.Right <= pg.Rect.Left)
                    {
                        from = new Point(pg.Rect.Left, pg.Rect.Y + pg.Rect.Height / 2);
                        to = new Point(cg.Rect.Right, cg.Rect.Y + cg.Rect.Height / 2);
                    }
                    else
                    {
                        from = new Point(pg.Rect.Right, pg.Rect.Y + pg.Rect.Height / 2);
                        to = new Point(cg.Rect.Left, cg.Rect.Y + cg.Rect.Height / 2);
                    }
                    _layout.Edges.Add(new TreeEdgeGeom { Parent = parent, Child = child, From = from, To = to });
                    if (child.Children.Count > 0)
                        BuildEdges(child);
                }
            }

            // ── 表格式（JSON / YAML）──────────────────────────────

            private TreeTableStyle BuildTableStyle()
            {
                Brush border = _styles.Node.LineColor ?? _defaultBorder;
                Brush separator = _styles.Separator.LineColor ?? border;
                return new TreeTableStyle
                {
                    Fill = _styles.Node.Background ?? _defaultFill,
                    KeyFill = _styles.Node.Background ?? _defaultKeyFill,
                    Border = MakePen(border, _styles.Node.LineThickness ?? 1.2, _styles.Node.Dash),
                    Corner = _styles.Node.RoundCorner.HasValue
                        ? U(_styles.Node.RoundCorner.Value)
                        : U(4),
                    Separator = MakePen(separator, _styles.Separator.LineThickness ?? 1.2,
                        _styles.Separator.Dash)
                };
            }

            private Pen BuildEdgePen(out Brush edgeBrush)
            {
                edgeBrush = _styles.Arrow.LineColor ?? _defaultBorder;
                double thickness = _styles.Arrow.LineThickness ?? 1.2;
                if (_styles.Arrow.Dash == null)
                {
                    // 3 个点、2 个空：DashStyle 单位是线宽倍数，比 DashStyles.Dot 的点密一些
                    var dotted = new Pen(edgeBrush, thickness)
                    {
                        DashStyle = new DashStyle(new[] { 3.0, 2.0 }, 0)
                    };
                    dotted.Freeze();
                    return dotted;
                }
                return MakePen(edgeBrush, thickness, _styles.Arrow.Dash);
            }

            private static Pen MakePen(Brush brush, double thickness, double[] dash)
            {
                var pen = new Pen(brush, thickness);
                if (dash != null && dash.Length == 2 && dash[0] > 0)
                {
                    // WPF 的 Dash 单位是线宽的倍数，官方 LineStyle 写的是像素
                    double unit = Math.Max(thickness, 0.1);
                    pen.DashStyle = new DashStyle(new[] { dash[0] / unit, dash[1] / unit }, 0);
                }
                pen.Freeze();
                return pen;
            }

            private static TreeHighlightRule MatchRule(string key, List<TreeHighlightRule> active)
            {
                if (active == null)
                    return null;
                foreach (TreeHighlightRule rule in active)
                    if (rule.IsRowMatch(key))
                        return rule;
                return null;
            }

            private static List<TreeHighlightRule> UpOneLevel(string key, List<TreeHighlightRule> active)
            {
                if (active == null || active.Count == 0)
                    return null;
                var next = new List<TreeHighlightRule>();
                foreach (TreeHighlightRule rule in active)
                {
                    TreeHighlightRule up = rule.UpOneLevel(key);
                    if (up != null)
                        next.Add(up);
                }
                return next.Count == 0 ? null : next;
            }

            /// <summary>高亮行的底色：优先 `highlight` 全局样式，再让 `<<类名>>` 覆盖。</summary>
            private Brush ResolveRowBackground(TreeHighlightRule rule, out TreeStyleProps classProps)
            {
                classProps = null;
                if (rule == null)
                    return null;
                Brush background = _styles.Highlight.Background;
                if (rule.Stereotype != null)
                {
                    classProps = _styles.FindClass(rule.Stereotype);
                    if (classProps != null && classProps.Background != null)
                        background = classProps.Background;
                }
                return background ?? DefaultHighlight;
            }

            private TreeCellStyle ResolveCell(bool header, TreeHighlightRule rule,
                TreeStyleProps classProps, Brush rowBackground)
            {
                Brush textColor = null;
                double? fontSize = null;
                string fontName = null;
                TreeFontStyle? fontStyle = null;

                Take(_styles.Node, ref textColor, ref fontSize, ref fontName, ref fontStyle);
                if (header)
                    Take(_styles.Header, ref textColor, ref fontSize, ref fontName, ref fontStyle);
                if (rule != null)
                {
                    Take(_styles.Highlight, ref textColor, ref fontSize, ref fontName, ref fontStyle);
                    Take(classProps, ref textColor, ref fontSize, ref fontName, ref fontStyle);
                }

                TreeFontStyle spec = fontStyle
                    ?? (header ? new TreeFontStyle { Bold = true } : new TreeFontStyle());
                Typeface typeface;
                if (string.IsNullOrEmpty(fontName) && spec.Bold && !spec.Italic && !spec.Underline)
                {
                    typeface = _tfBold;
                }
                else
                {
                    string family = string.IsNullOrEmpty(fontName)
                        ? MarkdownTheme.DiagramFamily.Source
                        : fontName;
                    typeface = new Typeface(new FontFamily(family),
                        spec.Italic ? FontStyles.Italic : FontStyles.Normal,
                        spec.Bold ? FontWeights.Bold : FontWeights.Normal,
                        FontStretches.Normal);
                }

                return new TreeCellStyle
                {
                    Typeface = typeface,
                    FontSize = fontSize ?? _fs,
                    TextColor = textColor
                        ?? (rowBackground != null ? ContrastText(rowBackground) : _text),
                    Underline = spec.Underline
                };
            }

            private static void Take(TreeStyleProps props, ref Brush textColor, ref double? fontSize,
                ref string fontName, ref TreeFontStyle? fontStyle)
            {
                if (props == null)
                    return;
                if (props.TextColor != null)
                    textColor = props.TextColor;
                if (props.FontSize.HasValue)
                    fontSize = props.FontSize;
                if (!string.IsNullOrEmpty(props.FontName))
                    fontName = props.FontName;
                if (props.FontStyle.HasValue)
                    fontStyle = props.FontStyle;
            }

            /// <summary>高亮底色没指定文字色时，按亮度自动选黑或白，深浅主题都能读。</summary>
            private static Brush ContrastText(Brush background)
            {
                var solid = background as SolidColorBrush;
                if (solid == null)
                    return Brushes.White;
                Color color = solid.Color;
                double luminance = 0.299 * color.R + 0.587 * color.G + 0.114 * color.B;
                return luminance > 140 ? Brushes.Black : Brushes.White;
            }

            private void MeasureTable(TreeNode table, List<TreeHighlightRule> active)
            {
                double padX = U(8);
                double keyW = 0;
                double valueW = 0;
                double tableH = 0;
                foreach (TreeNode row in table.Rows)
                {
                    TreeHighlightRule rule = MatchRule(row.Text, active);
                    TreeStyleProps classProps;
                    Brush rowBackground = ResolveRowBackground(rule, out classProps);
                    TreeCellStyle keyCell = ResolveCell(true, rule, classProps, rowBackground);
                    TreeCellStyle valueCell = ResolveCell(false, rule, classProps, rowBackground);

                    FormattedText key = Text(row.Text ?? "", keyCell.Typeface, keyCell.TextColor,
                        keyCell.FontSize, _maxKeyWidth);
                    FormattedText value = Text(row.Value ?? "", valueCell.Typeface, valueCell.TextColor,
                        valueCell.FontSize, _maxValueWidth);
                    if (keyCell.Underline)
                        key.SetTextDecorations(TextDecorations.Underline);
                    if (valueCell.Underline)
                        value.SetTextDecorations(TextDecorations.Underline);

                    // 键靠右：折行后每一行也跟着靠右，别让末行甩到左半边
                    key.TextAlignment = TextAlignment.Right;
                    keyW = Math.Max(keyW, key.Width + padX * 2);
                    valueW = Math.Max(valueW, value.Width + padX * 2);
                    // 值里可以带换行（JSON 的 \n），行高得跟着内容长，不能一律按单行算
                    row.H = Math.Max(RowHeight, Math.Max(key.Height, value.Height) + U(4));
                    tableH += row.H;
                    _rowGeoms[row] = new TreeGeom
                    {
                        Source = row,
                        Text = key,
                        Value = value,
                        RowBackground = rowBackground,
                        KeyCell = keyCell,
                        ValueCell = valueCell
                    };
                    if (row.Nested != null)
                        MeasureTable(row.Nested, UpOneLevel(row.Text, active));
                }
                keyW = Math.Max(keyW, U(20));
                valueW = Math.Max(valueW, U(20));

                table.W = keyW + valueW;
                table.H = table.Rows.Count > 0 ? tableH : RowHeight;
                foreach (TreeNode row in table.Rows)
                    _rowGeoms[row].KeyW = keyW;
                var geom = new TreeGeom { Source = table, KeyW = keyW };
                _geoms[table] = geom;
            }

            /// <summary>这棵表子树在竖直方向占多高（相对表顶）。</summary>
            private double Extent(TreeNode table)
            {
                double h = table.H;
                double cursor = 0;
                double rowTop = 0;
                for (int i = 0; i < table.Rows.Count; i++)
                {
                    TreeNode row = table.Rows[i];
                    double rowCenter = rowTop + row.H / 2;
                    rowTop += row.H;
                    if (row.Nested == null)
                        continue;
                    double desired = rowCenter - row.Nested.H / 2;
                    double top = Math.Max(desired, cursor);
                    double bottom = top + Extent(row.Nested);
                    h = Math.Max(h, bottom);
                    cursor = bottom + U(6);
                }
                return h;
            }

            private void PlaceTable(TreeNode table, double x, double y)
            {
                TreeGeom geom = _geoms[table];
                geom.Rect = new Rect(x, y, table.W, table.H);
                _layout.Nodes.Add(geom);

                double rowTop = y;
                for (int i = 0; i < table.Rows.Count; i++)
                {
                    TreeNode row = table.Rows[i];
                    TreeGeom rg = _rowGeoms[row];
                    rg.Rect = new Rect(x, rowTop, table.W, row.H);
                    _layout.Rows.Add(rg);
                    rowTop += row.H;
                }

                double cursor = y;
                rowTop = y;
                for (int i = 0; i < table.Rows.Count; i++)
                {
                    TreeNode row = table.Rows[i];
                    double rowCenter = rowTop + row.H / 2;
                    rowTop += row.H;
                    if (row.Nested == null)
                        continue;

                    double desired = rowCenter - row.Nested.H / 2;
                    double childTop = Math.Max(desired, cursor);
                    double childX = x + table.W + DepthGap;
                    PlaceTable(row.Nested, childX, childTop);
                    _layout.Edges.Add(new TreeEdgeGeom
                    {
                        Parent = row,
                        Child = row.Nested,
                        From = new Point(x + table.W, rowCenter),
                        To = new Point(childX, childTop + row.Nested.H / 2)
                    });
                    cursor = childTop + Extent(row.Nested) + U(6);
                }
            }

            private FormattedText Text(string text, Typeface typeface, Brush brush,
                double maxWidth = double.PositiveInfinity)
            {
                return Text(text, typeface, brush, _fs, maxWidth);
            }

            private FormattedText Text(string text, Typeface typeface, Brush brush, double fontSize,
                double maxWidth)
            {
                string source = text ?? string.Empty;
                if (!double.IsInfinity(maxWidth))
                    source = Breakable(source);
                var ft = new FormattedText(source, CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight, typeface, fontSize, brush, _ppd);
                if (!double.IsInfinity(maxWidth))
                    ft.MaxTextWidth = maxWidth;
                return ft;
            }

            /// <summary>
            /// WPF 只按空白 / 标点断行，没有断点的长串（下划线连起来的长键名、base64 之类）
            /// 在限宽处会直接截断而不是折行。整串没有空白时逐字插一个零宽空格，让它能折行；
            /// 有空格就让 WPF 正常按词断，免得把普通单词从中间劈开。
            /// </summary>
            private static string Breakable(string text)
            {
                if (text.Length < 2 || text.IndexOf(' ') >= 0 || text.IndexOf('\t') >= 0)
                    return text;
                var sb = new StringBuilder(text.Length * 2);
                for (int i = 0; i < text.Length; i++)
                {
                    sb.Append(text[i]);
                    if (i < text.Length - 1)
                        sb.Append('\u200B');
                }
                return sb.ToString();
            }
        }
    }
}

