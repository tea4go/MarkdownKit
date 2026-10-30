using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace MarkdownKit.PlantUml
{
    /// <summary>一张组件/部署图算好的几何。</summary>
    internal sealed class ComponentLayout
    {
        public double Width;
        public double Height;

        /// <summary>所有元素，按绘制顺序排（父在前，子在后）。</summary>
        public readonly List<ElementGeom> Elements = new List<ElementGeom>();

        public readonly List<EdgeGeom> Edges = new List<EdgeGeom>();

        /// <summary>挂在元素旁的注释（note left/right/top/bottom of X）。</summary>
        public readonly List<DiagramNoteGeom> Notes = new List<DiagramNoteGeom>();

        /// <summary>图例框（legend ... endlegend），没有图例时为 null。</summary>
        public LegendGeom Legend;

        public TitleGeom Title;
    }

    /// <summary>元素旁的注释便签。多行文本按行拆开，绘制时逐行画。</summary>
    internal sealed class DiagramNoteGeom
    {
        public Rect Rect;
        public readonly List<FormattedText> Lines = new List<FormattedText>();

        public void Shift(double dx, double dy)
        {
            Rect = new Rect(Rect.X + dx, Rect.Y + dy, Rect.Width, Rect.Height);
        }
    }

    internal sealed class ElementGeom
    {
        public Element Source;
        public Rect Rect;
        public FormattedText Text;

        /// <summary>构造型（«xxx»），没有则为 null。画在名称上方。</summary>
        public FormattedText Stereo;

        /// <summary>类 / ER 框的隔间内容（排好版的文字），按隔间分组；空表示普通框。</summary>
        public readonly List<List<FormattedText>> Compartments = new List<List<FormattedText>>();

        /// <summary>每个隔间顶部那条分隔线的 y（相对整框顶部）。</summary>
        public readonly List<double> Separators = new List<double>();

        /// <summary>名称那一格的高度。</summary>
        public double NameBand;

        /// <summary>与 Compartments 一一对应：这一行前面要不要画圆点（ER 的 `*` 主键）。</summary>
        public readonly List<List<bool>> Bullets = new List<List<bool>>();

        /// <summary>名称左边圆标的文字（C / I / E / A），空表示不画。</summary>
        public string Icon;
    }

    /// <summary>类图 / ER 图相关的判定，布局和绘制都要用。</summary>
    internal static class ClassBox
    {
        /// <summary>这个种类是不是"名称 + 隔间"那种框。</summary>
        public static bool Is(ElementKind kind)
        {
            return kind == ElementKind.Class || kind == ElementKind.AbstractClass
                || kind == ElementKind.Enum || kind == ElementKind.Annotation
                || kind == ElementKind.Struct || kind == ElementKind.Object
                || kind == ElementKind.ErEntity;
        }
    }

    /// <summary>一条关系边。From/To 已裁到两端元素边界上，Control 是二次贝塞尔的控制点。</summary>
    internal sealed class EdgeGeom
    {
        public Point From;
        public Point To;
        public Point Control;
        public bool Dashed;
        public EndArrow FromArrow;
        public EndArrow ToArrow;
        public FormattedText Label;
        public Point LabelPos;

        /// <summary>两端的重数标签（类图 "1" / "1..*"），按 From / To 方向存，可为 null。</summary>
        public FormattedText FromMultiplicity;
        public Point FromMultiplicityPos;
        public FormattedText ToMultiplicity;
        public Point ToMultiplicityPos;

        /// <summary>ER 图两端的鸦爪记号。</summary>
        public ErEnd FromEr;
        public ErEnd ToEr;

        /// <summary>挂在边上的注释（note on link），可为 null。</summary>
        public FormattedText Note;
        public Rect NoteRect;

        /// <summary>
        /// 绕开障碍物时的折线路径。空表示走 From/Control/To 的二次贝塞尔；
        /// 有值时（比如直线上压着别的框）改画折线。
        /// </summary>
        public readonly List<Point> Route = new List<Point>();

        public bool HasRoute { get { return Route.Count >= 2; } }

        public void Shift(double dx, double dy)
        {
            From = new Point(From.X + dx, From.Y + dy);
            To = new Point(To.X + dx, To.Y + dy);
            Control = new Point(Control.X + dx, Control.Y + dy);
            LabelPos = new Point(LabelPos.X + dx, LabelPos.Y + dy);
            FromMultiplicityPos = new Point(FromMultiplicityPos.X + dx, FromMultiplicityPos.Y + dy);
            ToMultiplicityPos = new Point(ToMultiplicityPos.X + dx, ToMultiplicityPos.Y + dy);
            for (int i = 0; i < Route.Count; i++)
                Route[i] = new Point(Route[i].X + dx, Route[i].Y + dy);
            if (NoteRect.Width > 0)
                NoteRect = new Rect(NoteRect.X + dx, NoteRect.Y + dy, NoteRect.Width, NoteRect.Height);
        }
    }

    /// <summary>二次贝塞尔曲线上的取点与切线。布局放标签、绘制画线和箭头都要用。</summary>
    internal static class QuadCurve
    {
        public static Point PointAt(Point p0, Point c, Point p1, double t)
        {
            double mt = 1 - t;
            return new Point(
                mt * mt * p0.X + 2 * mt * t * c.X + t * t * p1.X,
                mt * mt * p0.Y + 2 * mt * t * c.Y + t * t * p1.Y);
        }

        /// <summary>t 处的单位切向量。</summary>
        public static void TangentAt(Point p0, Point c, Point p1, double t, out double ux, out double uy)
        {
            double dx = 2 * (1 - t) * (c.X - p0.X) + 2 * t * (p1.X - c.X);
            double dy = 2 * (1 - t) * (c.Y - p0.Y) + 2 * t * (p1.Y - c.Y);
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-9)
            {
                ux = 1;
                uy = 0;
                return;
            }
            ux = dx / len;
            uy = dy / len;
        }
    }

    /// <summary>
    /// 由模型算出几何。叶子按标签定尺寸；同一层里按关系分层（rank）——箭头下游排更靠后
    /// （竖排时更靠下、横排时更靠右），同层并排；容器把子节点整个包住，顶部留标题区。
    /// </summary>
    internal static class ComponentLayoutBuilder
    {
        public static ComponentLayout Build(ComponentDiagram diagram, double fontSize, double pixelsPerDip)
        {
            return new Impl(diagram, fontSize, pixelsPerDip).Build();
        }

        private sealed class Impl
        {
            private readonly ComponentDiagram _diagram;
            private readonly double _fs;
            private readonly double _ppd;
            private readonly Typeface _tf;
            private readonly Typeface _tfBold;
            private readonly Typeface _tfItalic;
            private readonly Brush _text;
            private readonly ComponentLayout _layout = new ComponentLayout();
            private readonly Dictionary<Element, FormattedText> _labels = new Dictionary<Element, FormattedText>();
            private readonly Dictionary<Element, List<List<FormattedText>>> _body = new Dictionary<Element, List<List<FormattedText>>>();
            private readonly Dictionary<Element, List<List<bool>>> _bullets = new Dictionary<Element, List<List<bool>>>();
            private readonly Dictionary<Element, FormattedText> _stereos = new Dictionary<Element, FormattedText>();

            /// <summary>同层节点间距，对应 dot 的 nodesep（0.35in ≈ 25 点）。</summary>
            private double Gap { get { return U(25); } }

            /// <summary>层间距，对应 dot 的 ranksep（0.8in ≈ 58 点）。有 note on link 时会加大。</summary>
            private double RankGap { get { return _rankGap; } }

            private double _rankGap;

            /// <summary>
            /// PlantUML 里组件图元素的字号是 14，各种形状常量（10、15、20…）都以它为基准。
            /// 本程序字号可调，这里统一换算，保证形状比例跟 PlantUML 一致。
            /// </summary>
            private double U(double v)
            {
                return v * _fs / 14.0;
            }

            /// <summary>各形状的四周留白（单位同 PlantUML 常量）。取自 USymbol* 的 getMargin()。</summary>
            private void Margin(ElementKind kind, out double x1, out double x2, out double y1, out double y2)
            {
                switch (kind)
                {
                    case ElementKind.Node:
                    case ElementKind.Frame:
                    case ElementKind.Component:
                        x1 = 15; x2 = 25; y1 = 20; y2 = 10;
                        return;
                    case ElementKind.Database:
                        x1 = 10; x2 = 10; y1 = 24; y2 = 5;
                        return;
                    case ElementKind.Queue:
                        x1 = 5; x2 = 15; y1 = 5; y2 = 5;
                        return;
                    case ElementKind.Package:
                    case ElementKind.Folder:
                    case ElementKind.Artifact:
                        x1 = 10; x2 = 20; y1 = 13; y2 = 10;
                        return;
                    case ElementKind.Stack:
                        x1 = 25; x2 = 25; y1 = 10; y2 = 10;
                        return;
                    case ElementKind.Card:
                        x1 = 10; x2 = 10; y1 = 3; y2 = 3;
                        return;
                    case ElementKind.Process:
                        x1 = 20; x2 = 20; y1 = 10; y2 = 10;
                        return;
                    case ElementKind.Action:
                        x1 = 10; x2 = 20; y1 = 10; y2 = 10;
                        return;
                    case ElementKind.Cloud:
                        x1 = 15; x2 = 15; y1 = 15; y2 = 15;
                        return;
                    case ElementKind.Rectangle:
                        // 照官方 rectangle 实测：左右各 10，上下各 13（比默认高一点，框才不会显得扁）
                        x1 = 10; x2 = 10; y1 = 13; y2 = 13;
                        return;
                    default:
                        x1 = 10; x2 = 10; y1 = 10; y2 = 10;
                        return;
                }
            }

            /// <summary>
            /// 容器标题实际占的那块地方（画在框内顶部），拿它当标签避让的障碍。
            /// package / folder / artifact 的标题在左上角的页签里，只封页签那一块；
            /// 其余形状的标题在框内居中，按整条封。
            /// </summary>
            private Rect TitleRect(ElementGeom geom)
            {
                Rect r = geom.Rect;
                double band = Math.Min(TitleBand(geom.Source, geom.Text.Height), r.Height);
                switch (geom.Source.Kind)
                {
                    case ElementKind.Package:
                    case ElementKind.Folder:
                    case ElementKind.Artifact:
                        double tabW = Math.Min(geom.Text.Width + U(6) + U(7), r.Width);
                        return new Rect(r.X, r.Y, tabW, band);
                    default:
                        return new Rect(r.X, r.Y, r.Width, band);
                }
            }

            /// <summary>容器顶部留给标题的高度。各形状标题位置不同（见 USymbol*.asBig）。</summary>
            private double TitleBand(Element e, double titleHeight)
            {
                switch (e.Kind)
                {
                    case ElementKind.Node:
                    case ElementKind.Component:
                        return U(13) + titleHeight + U(6);
                    case ElementKind.Package:
                    case ElementKind.Folder:
                    case ElementKind.Artifact:
                        return titleHeight + U(6) + U(3);
                    case ElementKind.Frame:
                        return titleHeight + U(3) + U(6);
                    default:
                        return U(2) + titleHeight + U(6);
                }
            }

            public Impl(ComponentDiagram diagram, double fontSize, double pixelsPerDip)
            {
                _diagram = diagram;
                _fs = fontSize;
                _ppd = pixelsPerDip <= 0 ? 1.0 : pixelsPerDip;
                _tf = new Typeface(MarkdownTheme.DiagramFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                _tfBold = MarkdownTheme.BoldTypeface(MarkdownTheme.DiagramFamily);
                _tfItalic = new Typeface(MarkdownTheme.DiagramFamily, FontStyles.Italic, FontWeights.Normal, FontStretches.Normal);
                _text = MarkdownTheme.Markdown.Text;
            }

            public ComponentLayout Build()
            {
                List<Element> roots = _diagram.Roots;
                if (roots.Count == 0)
                    return _layout;

                _rankGap = U(58);
                // 边上有注释时，层间要腾出「注释 + 边标签」的高度，否则注释和标签会压住上层元素。
                // 注释居中在线中点上，所以两层各让出一半，整体间距要两倍。
                foreach (Relation rel in _diagram.Relations)
                {
                    if (string.IsNullOrEmpty(rel.Note))
                        continue;
                    double noteH = Text(rel.Note, _tf, _text).Height + U(8);
                    double labelH = string.IsNullOrEmpty(rel.Label) ? 0 : Text(rel.Label, _tf, _text).Height;
                    _rankGap = Math.Max(_rankGap, 2 * (noteH + labelH + U(16)));
                }

                double margin = _fs * 0.6;
                double top = margin;

                if (!string.IsNullOrEmpty(_diagram.Title))
                {
                    FormattedText title = Text(_diagram.Title, _tfBold, _text);
                    _layout.Title = new TitleGeom { Text = title, Top = top, CenterX = 0 };
                    top += title.Height + _fs * 0.8;
                }

                foreach (Element root in roots)
                    Size(root);
                Arrange(roots, null);

                double minX = double.MaxValue;
                double minY = double.MaxValue;
                foreach (Element root in roots)
                {
                    minX = Math.Min(minX, root.X);
                    minY = Math.Min(minY, root.Y);
                }
                if (minX == double.MaxValue)
                    minX = minY = 0;

                foreach (Element root in roots)
                    Place(root, margin + root.X - minX, top + root.Y - minY);

                Collect(roots);
                BuildEdges();
                LayoutNotes();   // 注释先摆好：标签放置要把注释框算进障碍物
                PlaceLabels();

                double contentMinX = double.MaxValue;
                double contentMinY = double.MaxValue;
                double maxRight = 0;
                double maxBottom = 0;
                foreach (ElementGeom geom in _layout.Elements)
                {
                    contentMinX = Math.Min(contentMinX, geom.Rect.Left);
                    contentMinY = Math.Min(contentMinY, geom.Rect.Top);
                    maxRight = Math.Max(maxRight, geom.Rect.Right);
                    maxBottom = Math.Max(maxBottom, geom.Rect.Bottom);
                }
                foreach (EdgeGeom edge in _layout.Edges)
                {
                    if (edge.NoteRect.Width > 0)
                    {
                        contentMinX = Math.Min(contentMinX, edge.NoteRect.Left);
                        contentMinY = Math.Min(contentMinY, edge.NoteRect.Top);
                        maxRight = Math.Max(maxRight, edge.NoteRect.Right);
                        maxBottom = Math.Max(maxBottom, edge.NoteRect.Bottom);
                    }
                    // 标签排在注释框上面，是最靠上的一块，也要算进来
                    if (edge.Label != null)
                    {
                        contentMinX = Math.Min(contentMinX, edge.LabelPos.X);
                        contentMinY = Math.Min(contentMinY, edge.LabelPos.Y);
                        maxRight = Math.Max(maxRight, edge.LabelPos.X + edge.Label.Width);
                        maxBottom = Math.Max(maxBottom, edge.LabelPos.Y + edge.Label.Height);
                    }
                    // 绕行的折线会伸到元素外面，不算进包围盒就会被画布裁掉
                    foreach (Point p in edge.Route)
                    {
                        contentMinX = Math.Min(contentMinX, p.X);
                        contentMinY = Math.Min(contentMinY, p.Y);
                        maxRight = Math.Max(maxRight, p.X);
                        maxBottom = Math.Max(maxBottom, p.Y);
                    }
                }
                foreach (DiagramNoteGeom note in _layout.Notes)
                {
                    contentMinX = Math.Min(contentMinX, note.Rect.Left);
                    contentMinY = Math.Min(contentMinY, note.Rect.Top);
                    maxRight = Math.Max(maxRight, note.Rect.Right);
                    maxBottom = Math.Max(maxBottom, note.Rect.Bottom);
                }
                if (contentMinX == double.MaxValue)
                    contentMinX = 0;
                if (contentMinY == double.MaxValue)
                    contentMinY = 0;

                // 图例摆在正文外侧，位置依赖正文的包围盒，所以要等正文算完再放
                LayoutLegend(contentMinX, contentMinY, maxRight, maxBottom);
                if (_layout.Legend != null)
                {
                    contentMinX = Math.Min(contentMinX, _layout.Legend.Rect.Left);
                    contentMinY = Math.Min(contentMinY, _layout.Legend.Rect.Top);
                    maxRight = Math.Max(maxRight, _layout.Legend.Rect.Right);
                    maxBottom = Math.Max(maxBottom, _layout.Legend.Rect.Bottom);
                }

                // 注释可能伸到左/上边界外，整体右移、下移补回来。
                // note on link 挂在边的中点上方，标签又排在注释上面，会伸到元素上方去，
                // 所以上边界要让开图标题；但 legend top 是"故意摆在标题上方"，那种情况
                // 标题要跟着一起下移，不能把标题顶在原地。
                bool legendAboveTitle = _layout.Legend != null && _layout.Title != null
                    && _layout.Legend.Rect.Bottom <= _layout.Title.Top;
                double keepOutTop = margin;
                if (_layout.Title != null && !legendAboveTitle)
                    keepOutTop = _layout.Title.Top + _layout.Title.Text.Height + _fs * 0.5;
                double shiftX = contentMinX < margin ? margin - contentMinX : 0;
                double shiftY = contentMinY < keepOutTop ? keepOutTop - contentMinY : 0;
                if (shiftX != 0 || shiftY != 0)
                {
                    foreach (ElementGeom geom in _layout.Elements)
                        geom.Rect = new Rect(geom.Rect.X + shiftX, geom.Rect.Y + shiftY, geom.Rect.Width, geom.Rect.Height);
                    foreach (EdgeGeom edge in _layout.Edges)
                        edge.Shift(shiftX, shiftY);
                    foreach (DiagramNoteGeom note in _layout.Notes)
                        note.Shift(shiftX, shiftY);
                    if (_layout.Legend != null)
                        _layout.Legend.Shift(shiftX, shiftY);
                    // 只有 legend 摆在标题上方时，标题才需要跟着挪
                    if (_layout.Title != null && legendAboveTitle)
                        _layout.Title.Top += shiftY;
                    maxRight += shiftX;
                    maxBottom += shiftY;
                }

                _layout.Width = maxRight + margin;
                _layout.Height = maxBottom + margin;
                if (_layout.Title != null)
                    _layout.Title.CenterX = _layout.Width / 2;
                return _layout;
            }

            /// <summary>
            /// 摆图例（legend ... endlegend）。默认放在正文下方居中，left/right/top 放到对应一侧，
            /// center 跟官方一样是"图内居中"，这里按下方居中处理——压在图上不好看。
            /// 框的内边距和行距照官方实测值取。
            /// </summary>
            private void LayoutLegend(double contentMinX, double contentMinY, double contentMaxX, double contentMaxY)
            {
                LegendGeom geom = LegendBox.Build(_diagram.Legend, _tf, _text, _fs, _ppd);
                if (geom == null)
                    return;

                Size size = geom.ContentSize();
                double w = size.Width + geom.Pad * 2;
                double h = size.Height + geom.Pad * 2;
                double gap = U(12);
                double x;
                double y;
                switch (_diagram.LegendPlacement)
                {
                    case "top":
                        x = (contentMinX + contentMaxX - w) / 2;
                        // 要躲开图标题，它也在正文上方
                        y = (_layout.Title != null ? _layout.Title.Top : contentMinY) - gap - h;
                        break;
                    case "left":
                        x = contentMinX - gap - w;
                        y = (contentMinY + contentMaxY - h) / 2;
                        break;
                    case "right":
                        x = contentMaxX + gap;
                        y = (contentMinY + contentMaxY - h) / 2;
                        break;
                    default:   // bottom / center
                        x = (contentMinX + contentMaxX - w) / 2;
                        y = contentMaxY + gap;
                        break;
                }
                geom.Rect = new Rect(x, y, w, h);
                _layout.Legend = geom;
            }

            /// <summary>
            /// 注释的候选位置：先按源码写的那侧（居中），再试同侧左/右错开的版本，
            /// 最后才试其它三侧。`note bottom of X` 底下正好有连线时，错开一点就放得下，
            /// 不用一路躲到图外面（那样会把整张图撑得很宽）。
            /// </summary>
            private static string[] NoteOrder(string preferred)
            {
                switch (preferred)
                {
                    case "left": return new[] { "left", "left-top", "left-bottom", "bottom", "top", "right" };
                    case "top": return new[] { "top", "top-left", "top-right", "right", "left", "bottom" };
                    case "right": return new[] { "right", "right-top", "right-bottom", "top", "bottom", "left" };
                    default: return new[] { "bottom", "bottom-left", "bottom-right", "right", "left", "top" };
                }
            }

            private static Rect NoteRect(Element target, string side, double w, double h, double gap)
            {
                double x;
                double y;
                double cx = target.X + target.W / 2;
                double cy = target.Y + target.H / 2;
                switch (side)
                {
                    case "left":
                        x = target.X - gap - w;
                        y = cy - h / 2;
                        break;
                    case "left-top":
                        x = target.X - gap - w;
                        y = target.Y;
                        break;
                    case "left-bottom":
                        x = target.X - gap - w;
                        y = target.Y + target.H - h;
                        break;
                    case "top":
                        x = cx - w / 2;
                        y = target.Y - gap - h;
                        break;
                    case "top-left":
                        x = cx - w;
                        y = target.Y - gap - h;
                        break;
                    case "top-right":
                        x = cx;
                        y = target.Y - gap - h;
                        break;
                    case "bottom":
                        x = cx - w / 2;
                        y = target.Y + target.H + gap;
                        break;
                    case "bottom-left":
                        x = cx - w;
                        y = target.Y + target.H + gap;
                        break;
                    case "bottom-right":
                        x = cx;
                        y = target.Y + target.H + gap;
                        break;
                    case "right-top":
                        x = target.X + target.W + gap;
                        y = target.Y;
                        break;
                    case "right-bottom":
                        x = target.X + target.W + gap;
                        y = target.Y + target.H - h;
                        break;
                    default:
                        x = target.X + target.W + gap;
                        y = cy - h / 2;
                        break;
                }
                return new Rect(x, y, w, h);
            }

            /// <summary>这个位置会不会压住别的元素框或已有的连线。</summary>
            private bool NoteHits(Rect box)
            {
                foreach (ElementGeom geom in _layout.Elements)
                {
                    if (geom.Rect.IntersectsWith(box))
                        return true;
                }
                foreach (DiagramNoteGeom note in _layout.Notes)
                {
                    if (note.Rect.IntersectsWith(box))
                        return true;
                }
                foreach (EdgeGeom edge in _layout.Edges)
                {
                    // 用真实路径采样（可能有绕行折线），不能按贝塞尔算
                    foreach (Point p in EdgeSamples(edge))
                    {
                        if (box.Contains(p))
                            return true;
                    }
                }
                return false;
            }

            /// <summary>把 note left/right/top/bottom of X 摆到目标元素旁边，垂直居中。</summary>
            private void LayoutNotes()
            {
                foreach (DiagramNote note in _diagram.Notes)
                {
                    Element target = _diagram.Find(note.Target);
                    if (target == null)
                        continue;

                    var geom = new DiagramNoteGeom();
                    double textW = 0;
                    double textH = 0;
                    foreach (string raw in note.Text.Split('\n'))
                    {
                        FormattedText ft = Text(raw, _tf, _text);
                        geom.Lines.Add(ft);
                        textW = Math.Max(textW, ft.Width);
                        textH += ft.Height;
                    }

                    double pad = U(6);
                    double w = textW + pad * 2;
                    double h = textH + pad * 2;
                    double gap = U(10);
                    // 先按源码写的那一侧摆；摆下去会压住线或别的框就换一侧试
                    Rect rect = default(Rect);
                    bool placed = false;
                    foreach (string side in NoteOrder(note.Placement))
                    {
                        Rect candidate = NoteRect(target, side, w, h, gap);
                        if (placed)
                            break;
                        if (!NoteHits(candidate))
                        {
                            rect = candidate;
                            placed = true;
                        }
                    }
                    if (!placed)
                        rect = NoteRect(target, note.Placement, w, h, gap);
                    geom.Rect = rect;
                    _layout.Notes.Add(geom);
                }
            }

            // ── 尺寸 ────────────────────────────────────────────────

            /// <summary>算一个节点的尺寸。容器先递归算好子节点，再按排布结果把自己包住。</summary>
            private void Size(Element e)
            {
                _labels[e] = Text(e.Label ?? e.Key ?? "", _tf, _text);
                if (!string.IsNullOrEmpty(e.Stereotype))
                    _stereos[e] = Text("«" + e.Stereotype + "»", _tfItalic, _text);

                if (e.Children.Count == 0)
                {
                    SizeLeaf(e, _labels[e]);
                    return;
                }

                foreach (Element child in e.Children)
                    Size(child);

                Size content = Arrange(e.Children, e);
                if (e.Transparent)
                {
                    e.W = content.Width;
                    e.H = content.Height;
                    return;
                }

                double x1, x2, y1, y2;
                Margin(e.Kind, out x1, out x2, out y1, out y2);
                double titleW = _labels[e].Width;
                double band = TitleBand(e, _labels[e].Height);
                // node 容器要比标题再宽 60（suppWidthBecauseOfShape），Java 里那些宽框就是这么来的
                double suppW = e.Kind == ElementKind.Node ? U(60) : 0;
                e.W = Math.Max(content.Width, titleW + suppW) + U(x1 + x2);
                e.H = band + content.Height + U(y2);
            }

            private void SizeLeaf(Element e, FormattedText ft)
            {
                if (e.Compartments.Count > 0 || ClassBox.Is(e.Kind))
                {
                    SizeClassBox(e, ft);
                    return;
                }

                double x1, x2, y1, y2;
                Margin(e.Kind, out x1, out x2, out y1, out y2);
                FormattedText stereo = _stereos.ContainsKey(e) ? _stereos[e] : null;
                double stereoW = stereo == null ? 0 : stereo.Width;
                double stereoH = stereo == null ? 0 : stereo.Height + U(2);
                double w = Math.Max(ft.Width, stereoW) + U(x1 + x2);
                double h = ft.Height + stereoH + U(y1 + y2);

                switch (e.Kind)
                {
                    case ElementKind.Interface:
                    case ElementKind.Circle:
                        // 棒棒糖：小圆在上、名字在圆下方
                        w = Math.Max(ft.Width, U(18)) + U(20);
                        h = U(10) + U(18) + U(4) + ft.Height + U(10);
                        break;
                    case ElementKind.Actor:
                    case ElementKind.Person:
                        w = Math.Max(ft.Width, U(26)) + U(20);
                        h = U(59) + ft.Height + U(16);   // 火柴人画在标签上方
                        break;
                    case ElementKind.Usecase:
                        UsecaseSize(ft, out w, out h);
                        break;
                    case ElementKind.Hexagon:
                        w = Math.Max(w, U(30));
                        h = Math.Max(h, U(24));
                        break;
                }
                e.W = w;
                e.H = h;
            }


            /// <summary>类 / ER 框：名称一格，下面每个隔间一格，之间画分隔线。</summary>
            private void SizeClassBox(Element e, FormattedText name)
            {
                double padX = U(12);
                double padY = U(5);
                // 圆标占的宽度：左边距 8 + 直径 21 + 一点间隙
                double iconW = e.ShowIcon ? U(32) : 0;
                double nameH = Math.Max(name.Height, U(17));
                double w = name.Width + iconW;
                double h = nameH + padY * 2;

                var rows = new List<List<FormattedText>>();
                var dots = new List<List<bool>>();
                foreach (Compartment c in e.Compartments)
                {
                    var row = new List<FormattedText>();
                    var dotRow = new List<bool>();
                    double ch = padY * 2;
                    foreach (string line in c.Lines)
                    {
                        FormattedText ft = Text(BodyText(line), _tf, _text);
                        row.Add(ft);
                        dotRow.Add(line.StartsWith("* ", StringComparison.Ordinal));
                        w = Math.Max(w, ft.Width + U(18));
                        ch += ft.Height;
                    }
                    rows.Add(row);
                    dots.Add(dotRow);
                    h += ch;
                }
                _body[e] = rows;
                _bullets[e] = dots;

                e.W = w + padX * 2;
                e.H = h;
            }

            /// <summary>ER 的 `*` 前缀去掉（画的时候换成圆点）。</summary>
            private static string BodyText(string line)
            {
                if (line.StartsWith("* ", StringComparison.Ordinal))
                    return line.Substring(2).Trim();
                return line;
            }

            /// <summary>用例的椭圆要刚好包住文字：取最小外接椭圆，长短轴比钳在 [0.2, 0.8]（USymbolUsecase）。</summary>
            private void UsecaseSize(FormattedText ft, out double w, out double h)
            {
                double tw = Math.Max(ft.Width, 1);
                double th = Math.Max(ft.Height, 1);
                double alpha = th / tw;
                if (alpha < 0.2)
                    alpha = 0.2;
                else if (alpha > 0.8)
                    alpha = 0.8;
                double a = Math.Sqrt(tw / 2 * (tw / 2) + th / 2 * (th / 2) / (alpha * alpha));
                double b = alpha * a;
                w = 2 * a + U(12);
                h = 2 * b + U(12);
            }

            /// <summary>
            /// 把一组兄弟按关系排好，写回各自的相对 X/Y，并返回整块尺寸。
            /// 流程照 Graphviz dot 那套（PlantUML 的组件图就是交给 dot 的）：
            /// 破环 + 最长路径分层 → mincross 交叉最小化定层内左右序 → 优先级法定坐标。
            /// </summary>
            private Size Arrange(List<Element> siblings, Element owner)
            {
                int n = siblings.Count;
                if (n == 0)
                    return new Size(0, 0);

                List<int[]> edges = SiblingEdges(siblings);
                int[] rank = AssignRanks(n, edges);
                BalanceRanks(n, edges, rank);
                if (owner != null && owner.Kind == ElementKind.Package)
                    rank = PackageRanks(siblings, owner, edges, rank);
                rank = CompactRanks(rank, n, out int maxRank);

                var layers = new List<List<int>>();
                for (int r = 0; r <= maxRank; r++)
                    layers.Add(new List<int>());
                for (int i = 0; i < n; i++)
                    layers[rank[i]].Add(i);

                MinCross(layers, edges, n);
                if (Environment.GetEnvironmentVariable("MDVIEW_DBG") == "1")
                {
                    var sb = new System.Text.StringBuilder("RANKS ");
                    for (int i = 0; i < n; i++)
                        sb.Append(siblings[i].Label).Append("=").Append(rank[i]).Append(" ");
                    sb.Append("| EDGES ");
                    foreach (int[] e in edges)
                        sb.Append(siblings[e[0]].Label).Append("->").Append(siblings[e[1]].Label)
                          .Append("(ml=").Append(e.Length > 2 ? e[2] : -1).Append(") ");
                    Console.WriteLine(sb.ToString());
                }
                double[] center = AssignCoordinates(layers, edges, siblings);

                return ToCoordinates(layers, center, siblings);
            }

            /// <summary>同一 package 内直接相连的短边按 Graphviz rank=same 放到同一层。</summary>
            private int[] PackageRanks(List<Element> siblings, Element owner, List<int[]> edges, int[] baseline)
            {
                int n = siblings.Count;
                var index = new Dictionary<Element, int>();
                for (int i = 0; i < n; i++)
                    index[siblings[i]] = i;

                var parent = new int[n];
                for (int i = 0; i < n; i++)
                    parent[i] = i;
                var same = new HashSet<long>();
                foreach (Relation rel in _diagram.Relations)
                {
                    int a, b;
                    if (rel.Source.Parent != owner || rel.Target.Parent != owner
                        || !index.TryGetValue(rel.Source, out a) || !index.TryGetValue(rel.Target, out b)
                        || a == b || Math.Abs(baseline[a] - baseline[b]) > 1)
                        continue;
                    same.Add(EdgeKey(a, b));
                    int rootA = Root(parent, a);
                    int rootB = Root(parent, b);
                    parent[rootB] = rootA;
                }
                if (same.Count == 0)
                    return baseline;

                var groups = new Dictionary<int, int>();
                var groupOf = new int[n];
                for (int i = 0; i < n; i++)
                {
                    int root = Root(parent, i);
                    int group;
                    if (!groups.TryGetValue(root, out group))
                        groups[root] = group = groups.Count;
                    groupOf[i] = group;
                }

                var rankEdges = new List<int[]>();
                var seen = new HashSet<long>();
                foreach (int[] edge in edges)
                {
                    int a = groupOf[edge[0]];
                    int b = groupOf[edge[1]];
                    if (a == b)
                    {
                        if (!same.Contains(EdgeKey(edge[0], edge[1])))
                            return baseline; // 长边与同层约束冲突，沿用原来的分层
                    }
                    else if (seen.Add(EdgeKey(a, b)))
                        rankEdges.Add(new[] { a, b });
                }

                int[] groupRanks = AssignRanks(groups.Count, rankEdges);
                BalanceRanks(groups.Count, rankEdges, groupRanks);
                var rank = new int[n];
                for (int i = 0; i < n; i++)
                    rank[i] = groupRanks[groupOf[i]];
                return rank;
            }

            private static int Root(int[] parent, int i)
            {
                while (parent[i] != i)
                {
                    parent[i] = parent[parent[i]];
                    i = parent[i];
                }
                return i;
            }

            private static long EdgeKey(int a, int b)
            {
                return (long)Math.Min(a, b) * 100000 + Math.Max(a, b);
            }

            /// <summary>兄弟之间的关系。两端都在本层集合里才算，重复的边去掉。</summary>
            private List<int[]> SiblingEdges(List<Element> siblings)
            {
                var index = new Dictionary<Element, int>();
                for (int i = 0; i < siblings.Count; i++)
                    index[siblings[i]] = i;

                var seen = new HashSet<long>();
                var edges = new List<int[]>();
                foreach (Relation rel in _diagram.Relations)
                {
                    int a = IndexOf(siblings, index, rel.Source);
                    int b = IndexOf(siblings, index, rel.Target);
                    if (a < 0 || b < 0 || a == b)
                        continue;
                    // 标签会横向伸出节点，同层排布要按它留位，否则会压到隔壁节点上
                    if (!string.IsNullOrEmpty(rel.Label))
                    {
                        double lw = Text(rel.Label, _tf, _text).Width;
                        siblings[a].LabelW = Math.Max(siblings[a].LabelW, lw);
                        siblings[b].LabelW = Math.Max(siblings[b].LabelW, lw);
                    }
                    // note on link 也挂在这条边上，比标签宽得多，同样要留位
                    if (!string.IsNullOrEmpty(rel.Note))
                    {
                        double nw = NoteSize(rel.Note).Width + U(12);
                        siblings[a].LabelW = Math.Max(siblings[a].LabelW, nw);
                        siblings[b].LabelW = Math.Max(siblings[b].LabelW, nw);
                    }
                    long key = EdgeKey(a, b);
                    // 第三个元素是 dot 的 minlen：官方取 Link.getLength() - 1，
                    // 所以 -> 是 0（两端同层）、--> / -- / ..> 是 1（目标下沉一层）
                    int minlen = Math.Max(0, rel.Length - 1);
                    if (seen.Contains(key))
                    {
                        // 一对反向边（A->B 与 B->A）是无向同 key，只留一条即可；
                        // 但要取更严格的那个 minlen，否则 --> 的分层要求会被 -> 覆盖掉
                        for (int i = 0; i < edges.Count; i++)
                        {
                            if (EdgeKey(edges[i][0], edges[i][1]) == key && edges[i][2] < minlen)
                                edges[i][2] = minlen;
                        }
                        continue;
                    }
                    seen.Add(key);
                    edges.Add(new[] { a, b, minlen });
                }
                return edges;
            }

            // ── 分层 ────────────────────────────────────────────────

            /// <summary>
            /// 破环 + 最长路径分层。先用 DFS 找出回边（指向栈上节点的边），把回边反向得到 DAG，
            /// 再按拓扑序做最长路径——保证每条边都从低层指向高层。
            /// </summary>
            private int[] AssignRanks(int n, List<int[]> edges)
            {
                var succ = new List<int>[n];
                for (int i = 0; i < n; i++)
                    succ[i] = new List<int>();
                foreach (int[] e in edges)
                    succ[e[0]].Add(e[1]);

                // 每条边的最小层距（dot 的 minlen）。-> 是 0，两端可以同层；--> 是 1，目标下沉一层。
                // 回边反向时仍用同一个值，所以按无向存。
                var minlen = MinLen(n, edges);

                var state = new int[n];          // 0 未访问 1 在栈上 2 完成
                var isBack = new bool[n, n];
                for (int i = 0; i < n; i++)
                {
                    if (state[i] == 0)
                        Dfs(i, succ, state, isBack);
                }

                var dag = new List<int>[n];
                var indeg = new int[n];
                for (int i = 0; i < n; i++)
                    dag[i] = new List<int>();
                foreach (int[] e in edges)
                {
                    if (isBack[e[0], e[1]])
                    {
                        dag[e[1]].Add(e[0]);
                        indeg[e[0]]++;
                    }
                    else
                    {
                        dag[e[0]].Add(e[1]);
                        indeg[e[1]]++;
                    }
                }

                var rank = new int[n];
                var queue = new Queue<int>();
                for (int i = 0; i < n; i++)
                {
                    if (indeg[i] == 0)
                        queue.Enqueue(i);
                }
                while (queue.Count > 0)
                {
                    int u = queue.Dequeue();
                    foreach (int v in dag[u])
                    {
                        int w = minlen[u, v];
                        if (rank[v] < rank[u] + w)
                            rank[v] = rank[u] + w;
                        if (--indeg[v] == 0)
                            queue.Enqueue(v);
                    }
                }
                return rank;
            }

            /// <summary>
            /// 每条边的最小层距（dot 的 minlen）二维表。edges 的第三个元素就是它，
            /// 没有时按 1 处理。回边反向时用同一个值，所以两个方向都填。
            /// </summary>
            private static int[,] MinLen(int n, List<int[]> edges)
            {
                var minlen = new int[n, n];
                foreach (int[] e in edges)
                {
                    int w = e.Length > 2 ? e[2] : 1;
                    minlen[e[0], e[1]] = w;
                    minlen[e[1], e[0]] = w;
                }
                return minlen;
            }

            private void Dfs(int u, List<int>[] succ, int[] state, bool[,] isBack)
            {
                state[u] = 1;
                foreach (int v in succ[u])
                {
                    if (state[v] == 1)
                        isBack[u, v] = true;
                    else if (state[v] == 0)
                        Dfs(v, succ, state, isBack);
                }
                state[u] = 2;
            }

            /// <summary>
            /// 最长路径分层只管"不违反先后"，不管边有多长。这里再做一轮平衡：在不违反前驱/后继
            /// 约束的前提下，把节点往下挪到"邻居层号的中位数"上，缩短跨层边的总长。
            /// dot 的 network simplex 就是干这个的，这里用简单的迭代近似。
            /// </summary>
            private void BalanceRanks(int n, List<int[]> edges, int[] rank)
            {
                if (n == 0)
                    return;
                var succ = Adjacency(n, edges, true);
                var pred = Adjacency(n, edges, false);
                var minlen = MinLen(n, edges);

                for (int iter = 0; iter < 8; iter++)
                {
                    bool changed = false;
                    for (int u = 0; u < n; u++)
                    {
                        int lower = int.MinValue;
                        int upper = int.MaxValue;
                        var neighbors = new List<int>();
                        foreach (int p in pred[u])
                        {
                            lower = Math.Max(lower, rank[p] + minlen[p, u]);
                            neighbors.Add(rank[p]);
                        }
                        foreach (int s in succ[u])
                        {
                            upper = Math.Min(upper, rank[s] - minlen[u, s]);
                            neighbors.Add(rank[s]);
                        }
                        if (neighbors.Count == 0)
                            continue;

                        neighbors.Sort();
                        int target = (int)Math.Round(Median(neighbors));
                        if (lower != int.MinValue)
                            target = Math.Max(target, lower);
                        if (upper != int.MaxValue)
                            target = Math.Min(target, upper);
                        if (target > rank[u])
                        {
                            rank[u] = target;
                            changed = true;
                        }
                    }
                    if (!changed)
                        break;
                }
            }

            /// <summary>平衡后层号可能中间空出来，压紧成 0..k。</summary>
            private static int[] CompactRanks(int[] rank, int n, out int maxRank)
            {
                var used = new List<int>();
                var seen = new HashSet<int>();
                for (int i = 0; i < n; i++)
                {
                    if (seen.Add(rank[i]))
                        used.Add(rank[i]);
                }
                used.Sort();

                var map = new Dictionary<int, int>();
                for (int i = 0; i < used.Count; i++)
                    map[used[i]] = i;

                var result = new int[n];
                for (int i = 0; i < n; i++)
                    result[i] = map[rank[i]];
                maxRank = used.Count - 1;
                return result;
            }

            // ── 交叉最小化（mincross）────────────────────────────────

            /// <summary>
            /// dot 的 mincross 简化版：反复"下走按上一层邻居中位数排序、上走按下一层排序"，
            /// 中间穿插相邻交换（交换后交叉数变小才保留）。跑几轮后层内顺序基本稳定。
            /// </summary>
            private void MinCross(List<List<int>> layers, List<int[]> edges, int n)
            {
                if (layers.Count < 2)
                    return;

                var succ = Adjacency(n, edges, true);
                var pred = Adjacency(n, edges, false);

                for (int pass = 0; pass < 8; pass++)
                {
                    if (pass % 2 == 0)
                    {
                        for (int r = 1; r < layers.Count; r++)
                            SortByMedian(layers[r], layers[r - 1], pred);
                    }
                    else
                    {
                        for (int r = layers.Count - 2; r >= 0; r--)
                            SortByMedian(layers[r], layers[r + 1], succ);
                    }
                    Transpose(layers, edges);
                }
            }

            private static List<int>[] Adjacency(int n, List<int[]> edges, bool forward)
            {
                var adj = new List<int>[n];
                for (int i = 0; i < n; i++)
                    adj[i] = new List<int>();
                foreach (int[] e in edges)
                    adj[forward ? e[0] : e[1]].Add(forward ? e[1] : e[0]);
                return adj;
            }

            /// <summary>按"在参考层里的邻居位置中位数"给本层排序，没邻居的保持原位。</summary>
            private static void SortByMedian(List<int> layer, List<int> reference, List<int>[] adj)
            {
                var posInRef = new Dictionary<int, int>();
                for (int i = 0; i < reference.Count; i++)
                    posInRef[reference[i]] = i;

                var median = new Dictionary<int, double>();
                var origin = new Dictionary<int, int>();
                for (int i = 0; i < layer.Count; i++)
                {
                    origin[layer[i]] = i;
                    var positions = new List<int>();
                    foreach (int nb in adj[layer[i]])
                    {
                        int p;
                        if (posInRef.TryGetValue(nb, out p))
                            positions.Add(p);
                    }
                    positions.Sort();
                    median[layer[i]] = positions.Count == 0 ? i : Median(positions);
                }

                layer.Sort((a, b) =>
                {
                    int c = median[a].CompareTo(median[b]);
                    return c != 0 ? c : origin[a].CompareTo(origin[b]);
                });
            }

            /// <summary>相邻交换：逐层试交换相邻两个节点，交叉数变小就留下。</summary>
            private void Transpose(List<List<int>> layers, List<int[]> edges)
            {
                for (int guard = 0; guard < 8; guard++)
                {
                    bool improved = false;
                    for (int r = 0; r < layers.Count; r++)
                    {
                        List<int> layer = layers[r];
                        for (int i = 0; i + 1 < layer.Count; i++)
                        {
                            int before = CrossingsAround(layers, r, edges);
                            int t = layer[i];
                            layer[i] = layer[i + 1];
                            layer[i + 1] = t;
                            int after = CrossingsAround(layers, r, edges);
                            if (after < before)
                            {
                                improved = true;
                            }
                            else
                            {
                                t = layer[i];
                                layer[i] = layer[i + 1];
                                layer[i + 1] = t;
                            }
                        }
                    }
                    if (!improved)
                        break;
                }
            }

            private static int CrossingsAround(List<List<int>> layers, int r, List<int[]> edges)
            {
                int total = 0;
                if (r > 0)
                    total += Crossings(layers[r - 1], layers[r], edges);
                if (r + 1 < layers.Count)
                    total += Crossings(layers[r], layers[r + 1], edges);
                return total;
            }

            /// <summary>相邻两层的交叉数：按上层顺序列出各自在下层的邻居位置，数逆序对。</summary>
            private static int Crossings(List<int> upper, List<int> lower, List<int[]> edges)
            {
                if (upper.Count == 0 || lower.Count == 0)
                    return 0;

                var upperPos = new Dictionary<int, int>();
                for (int i = 0; i < upper.Count; i++)
                    upperPos[upper[i]] = i;
                var lowerPos = new Dictionary<int, int>();
                for (int i = 0; i < lower.Count; i++)
                    lowerPos[lower[i]] = i;

                var seq = new List<int>[upper.Count];
                for (int i = 0; i < upper.Count; i++)
                    seq[i] = new List<int>();
                foreach (int[] e in edges)
                {
                    int a, b;
                    if (upperPos.ContainsKey(e[0]) && lowerPos.ContainsKey(e[1]))
                    {
                        a = upperPos[e[0]];
                        b = lowerPos[e[1]];
                    }
                    else if (upperPos.ContainsKey(e[1]) && lowerPos.ContainsKey(e[0]))
                    {
                        a = upperPos[e[1]];
                        b = lowerPos[e[0]];
                    }
                    else
                    {
                        continue;
                    }
                    seq[a].Add(b);
                }

                var flat = new List<int>();
                for (int i = 0; i < seq.Length; i++)
                {
                    seq[i].Sort();
                    flat.AddRange(seq[i]);
                }

                int count = 0;
                for (int i = 0; i < flat.Count; i++)
                {
                    for (int j = i + 1; j < flat.Count; j++)
                    {
                        if (flat[i] > flat[j])
                            count++;
                    }
                }
                return count;
            }

            // ── 层内坐标 ────────────────────────────────────────────

            /// <summary>
            /// 优先级法：先按上一层邻居的中心中位数定"理想位置"，再从左往右、从右往左各压一遍
            /// （保证同层不重叠），最后取两次的中间值。再补一遍压紧兜底。
            /// </summary>
            private double[] AssignCoordinates(List<List<int>> layers, List<int[]> edges, List<Element> siblings)
            {
                int n = siblings.Count;
                var center = new double[n];
                var placed = new bool[n];
                var pred = Adjacency(n, edges, false);

                for (int r = 0; r < layers.Count; r++)
                {
                    List<int> layer = layers[r];
                    int m = layer.Count;
                    if (m == 0)
                        continue;

                    var desired = new double[m];
                    var has = new bool[m];
                    for (int i = 0; i < m; i++)
                    {
                        var values = new List<double>();
                        foreach (int p in pred[layer[i]])
                        {
                            if (placed[p])
                                values.Add(center[p]);
                        }
                        if (values.Count > 0)
                        {
                            values.Sort();
                            desired[i] = Median(values);
                            has[i] = true;
                        }
                    }

                    var left = new double[m];
                    for (int i = 0; i < m; i++)
                    {
                        double half = InSize(siblings[layer[i]]) / 2;
                        double minPos = i == 0
                            ? (has[i] ? desired[i] : 0)
                            : left[i - 1] + InSize(siblings[layer[i - 1]]) / 2 + Gap + half;
                        left[i] = has[i] ? Math.Max(desired[i], minPos) : minPos;
                    }

                    var right = new double[m];
                    for (int i = m - 1; i >= 0; i--)
                    {
                        double half = InSize(siblings[layer[i]]) / 2;
                        double maxPos = i == m - 1
                            ? (has[i] ? desired[i] : left[i])
                            : right[i + 1] - InSize(siblings[layer[i + 1]]) / 2 - Gap - half;
                        right[i] = has[i] ? Math.Min(desired[i], maxPos) : maxPos;
                    }

                    for (int i = 0; i < m; i++)
                    {
                        center[layer[i]] = (left[i] + right[i]) / 2;
                        placed[layer[i]] = true;
                    }

                    // 取中间值可能又挤到一起，再从左往右压紧一遍
                    for (int i = 1; i < m; i++)
                    {
                        double minPos = center[layer[i - 1]] + InSize(siblings[layer[i - 1]]) / 2
                            + Gap + InSize(siblings[layer[i]]) / 2;
                        if (center[layer[i]] < minPos)
                            center[layer[i]] = minPos;
                    }
                }

                return center;
            }

            /// <summary>把层内坐标（沿"层内轴"的一维值）落成 X/Y，并让各层在层内轴上居中。</summary>
            private Size ToCoordinates(List<List<int>> layers, double[] center, List<Element> siblings)
            {
                bool leftToRight = _diagram.LeftToRight;

                // 各层沿"分层轴"的尺寸与起点
                var rankSize = new double[layers.Count];
                for (int r = 0; r < layers.Count; r++)
                {
                    foreach (int i in layers[r])
                        rankSize[r] = Math.Max(rankSize[r], CrossSize(siblings[i]));
                }
                var rankOffset = new double[layers.Count];
                double acc = 0;
                for (int r = 0; r < layers.Count; r++)
                {
                    rankOffset[r] = acc;
                    acc += rankSize[r] + RankGap;
                }
                double crossTotal = layers.Count == 0 ? 0 : acc - RankGap;

                // 层内轴的整体范围，用来把各层居中
                double minIn = double.MaxValue;
                double maxIn = double.MinValue;
                for (int i = 0; i < siblings.Count; i++)
                {
                    double half = InSize(siblings[i]) / 2;
                    minIn = Math.Min(minIn, center[i] - half);
                    maxIn = Math.Max(maxIn, center[i] + half);
                }
                if (minIn > maxIn)
                    minIn = maxIn = 0;
                double inTotal = maxIn - minIn;

                for (int r = 0; r < layers.Count; r++)
                {
                    List<int> layer = layers[r];
                    if (layer.Count == 0)
                        continue;

                    double layerMin = double.MaxValue;
                    double layerMax = double.MinValue;
                    foreach (int i in layer)
                    {
                        double half = InSize(siblings[i]) / 2;
                        layerMin = Math.Min(layerMin, center[i] - half);
                        layerMax = Math.Max(layerMax, center[i] + half);
                    }
                    double shift = (inTotal - (layerMax - layerMin)) / 2 - layerMin;

                    foreach (int i in layer)
                    {
                        Element s = siblings[i];
                        double inPos = center[i] + shift;                 // 层内轴上的中心
                        double crossPos = rankOffset[r] + (rankSize[r] - CrossSize(s)) / 2;
                        if (leftToRight)
                        {
                            s.X = crossPos;
                            s.Y = inPos - s.H / 2;
                        }
                        else
                        {
                            s.X = inPos - s.W / 2;
                            s.Y = crossPos;
                        }
                    }
                }

                return leftToRight
                    ? new Size(crossTotal, inTotal)
                    : new Size(inTotal, crossTotal);
            }

            /// <summary>元素沿"层内轴"的尺寸：竖排分层时是宽，横排时是高。</summary>
            private double InSize(Element e)
            {
                // 同层的两个元素之间也可能有边，它的标签居中在边上、横着铺开，
                // 要"框宽 + 标签宽"才够（两端各让出半个标签宽）。只取 max 的话，
                // 框比标签窄时留的缝就不够，标签会压到框和容器标题上
                return _diagram.LeftToRight ? e.H : e.W + e.LabelW;
            }

            /// <summary>元素沿"分层轴"的尺寸：竖排分层时是高，横排时是宽。</summary>
            private double CrossSize(Element e)
            {
                // 横排时标签横躺在两层之间的空隙里，两端各得让出半个标签宽，
                // 合起来才够一个标签宽——不然标签必然压到两端的元素上。
                // 竖排时标签在层内轴上，这里不用管（见 InSize）。
                return _diagram.LeftToRight ? e.W + e.LabelW : e.H;
            }

            private static double Median(List<int> sorted)
            {
                int c = sorted.Count;
                if (c == 0)
                    return 0;
                return c % 2 == 1 ? sorted[c / 2] : (sorted[c / 2 - 1] + sorted[c / 2]) / 2.0;
            }

            private static double Median(List<double> sorted)
            {
                int c = sorted.Count;
                if (c == 0)
                    return 0;
                return c % 2 == 1 ? sorted[c / 2] : (sorted[c / 2 - 1] + sorted[c / 2]) / 2.0;
            }

            /// <summary>把元素映射到它在 siblings 里的那一层祖先（本身是兄弟就返回自己）。</summary>
            private static int IndexOf(List<Element> siblings, Dictionary<Element, int> index, Element e)
            {
                for (Element p = e; p != null; p = p.Parent)
                {
                    int i;
                    if (index.TryGetValue(p, out i))
                        return i;
                }
                return -1;
            }

            // ── 绝对定位 ────────────────────────────────────────────

            /// <summary>把节点放到绝对位置，子节点跟着挪过去。</summary>
            private void Place(Element e, double x, double y)
            {
                e.X = x;
                e.Y = y;
                if (e.Children.Count == 0)
                    return;

                if (e.Transparent)
                {
                    foreach (Element child in e.Children)
                        Place(child, x + child.X, y + child.Y);
                    return;
                }

                double x1, x2, y1, y2;
                Margin(e.Kind, out x1, out x2, out y1, out y2);
                double band = TitleBand(e, _labels[e].Height);
                foreach (Element child in e.Children)
                    Place(child, x + U(x1) + child.X, y + band + child.Y);
            }

            private void Collect(List<Element> nodes)
            {
                foreach (Element e in nodes)
                {
                    var geom = new ElementGeom
                    {
                        Source = e,
                        Rect = new Rect(e.X, e.Y, e.W, e.H),
                        Text = _labels[e],
                        Stereo = _stereos.ContainsKey(e) ? _stereos[e] : null
                    };
                    if (_body.ContainsKey(e))
                    {
                        // 类 / ER 框：把隔间和分隔线的位置算出来，绘制直接用
                        double padY = U(5);
                        double band = Math.Max(geom.Text.Height, U(17)) + padY * 2;
                        geom.NameBand = band;
                        geom.Icon = e.ShowIcon ? IconOf(e.Kind) : null;
                        double y = band;
                        int ci = 0;
                        foreach (List<FormattedText> row in _body[e])
                        {
                            geom.Separators.Add(y);
                            geom.Compartments.Add(row);
                            geom.Bullets.Add(_bullets.ContainsKey(e) && ci < _bullets[e].Count
                                ? _bullets[e][ci] : new List<bool>());
                            ci++;
                            double h = padY * 2;
                            foreach (FormattedText ft in row)
                                h += ft.Height;
                            y += h;
                        }
                    }
                    _layout.Elements.Add(geom);
                    if (e.Children.Count > 0)
                        Collect(e.Children);
                }
            }

            /// <summary>类图左上角圆标里的字母。</summary>
            internal static string IconOf(ElementKind kind)
            {
                switch (kind)
                {
                    case ElementKind.Interface: return "I";
                    case ElementKind.Enum: return "E";
                    case ElementKind.AbstractClass: return "A";
                    case ElementKind.Annotation: return "@";
                    case ElementKind.ErEntity: return "E";
                    case ElementKind.Object: return "O";
                    default: return "C";
                }
            }

            // ── 关系边 ──────────────────────────────────────────────

            private void BuildEdges()
            {
                foreach (Relation rel in _diagram.Relations)
                {
                    Element source = rel.Source;
                    Element target = rel.Target;
                    if (source == null || target == null || source == target)
                        continue;

                    var sc = new Point(source.X + source.W / 2, source.Y + source.H / 2);
                    var tc = new Point(target.X + target.W / 2, target.Y + target.H / 2);
                    Point from = ClipToRect(new Rect(source.X, source.Y, source.W, source.H), sc, tc);
                    Point to = ClipToRect(new Rect(target.X, target.Y, target.W, target.H), tc, sc);

                    var edge = new EdgeGeom
                    {
                        From = from,
                        To = to,
                        // note on link 挂在线上，线鼓起来的话注释就没法贴着它摆；
                        // 类图 / ER 图官方画的就是直线，也走直线
                        Control = string.IsNullOrEmpty(rel.Note) && !_diagram.IsClassDiagram
                            ? ControlPoint(from, to) : MidPoint(from, to),
                        Dashed = rel.Dashed,
                        ToArrow = rel.Target == rel.Right ? rel.RightArrow : rel.LeftArrow,
                        FromArrow = rel.Target == rel.Right ? rel.LeftArrow : rel.RightArrow,
                        FromEr = rel.Target == rel.Right ? rel.LeftEr : rel.RightEr,
                        ToEr = rel.Target == rel.Right ? rel.RightEr : rel.LeftEr
                    };
                    // 类图的重数标签贴在两端靠里的位置。注意边的 From/To 是按 Target 定的，
                    // 跟源码里的 Left/Right 不一定同向，得跟着翻一下
                    bool toIsRight = rel.Target == rel.Right;
                    string fromMult = toIsRight ? rel.LeftMultiplicity : rel.RightMultiplicity;
                    string toMult = toIsRight ? rel.RightMultiplicity : rel.LeftMultiplicity;
                    if (!string.IsNullOrEmpty(fromMult))
                        edge.FromMultiplicity = Text(fromMult, _tf, _text);
                    if (!string.IsNullOrEmpty(toMult))
                        edge.ToMultiplicity = Text(toMult, _tf, _text);
                    PlaceMultiplicities(edge);
                    Detour(edge, source, target);
                    if (!string.IsNullOrEmpty(rel.Label))
                        edge.Label = Text(rel.Label, _tf, _text);
                    if (!string.IsNullOrEmpty(rel.Note))
                    {
                        // note on link：贴在边中点上方，线在它下面穿过
                        edge.Note = Text(rel.Note, _tf, _text);
                        Point mid = QuadCurve.PointAt(edge.From, edge.Control, edge.To, 0.5);
                        Size noteSize = NoteSize(rel.Note);
                        edge.NoteRect = new Rect(mid.X - noteSize.Width / 2,
                            mid.Y - U(4) - noteSize.Height, noteSize.Width, noteSize.Height);
                    }
                    _layout.Edges.Add(edge);
                }
            }

            /// <summary>重数标签贴在边的两端、稍微靠里一点。</summary>
            private void PlaceMultiplicities(EdgeGeom edge)
            {
                if (edge.FromMultiplicity != null)
                    edge.FromMultiplicityPos = MultiplicityPos(edge, 0.25, edge.FromMultiplicity);
                if (edge.ToMultiplicity != null)
                    edge.ToMultiplicityPos = MultiplicityPos(edge, 0.75, edge.ToMultiplicity);
            }

            /// <summary>重数贴在线的旁边：沿曲线取一点，再往切线垂直方向偏开半个字。</summary>
            private Point MultiplicityPos(EdgeGeom edge, double t, FormattedText ft)
            {
                // 沿线找几个位置，避开注释框和元素框；都躲不开就用第一个
                double[] tries = { t, 0.35, 0.15, 0.5, 0.65, 0.1 };
                Point first = default(Point);
                for (int i = 0; i < tries.Length; i++)
                {
                    Point p = MultiplicityAt(edge, tries[i], ft);
                    if (i == 0)
                        first = p;
                    if (!MultiplicityHits(p, ft))
                        return p;
                }
                return first;
            }

            private Point MultiplicityAt(EdgeGeom edge, double t, FormattedText ft)
            {
                Point p = QuadCurve.PointAt(edge.From, edge.Control, edge.To, t);
                double ux, uy;
                QuadCurve.TangentAt(edge.From, edge.Control, edge.To, t, out ux, out uy);
                double off = U(7);
                return new Point(p.X - uy * off - ft.Width / 2, p.Y + ux * off - ft.Height / 2);
            }

            private bool MultiplicityHits(Point p, FormattedText ft)
            {
                var box = new Rect(p.X, p.Y, ft.Width, ft.Height);
                foreach (DiagramNoteGeom note in _layout.Notes)
                {
                    if (note.Rect.IntersectsWith(box))
                        return true;
                }
                foreach (ElementGeom geom in _layout.Elements)
                {
                    if (geom.Rect.IntersectsWith(box))
                        return true;
                }
                return false;
            }

            /// <summary>
            /// 直线的路径上压着别的元素时，改成从旁边绕过去。
            /// 跨层的长边（比如 A→C 而 B 夹在中间）会正好穿过 B，绕一下才不会糊成一团。
            /// </summary>
            private void Detour(EdgeGeom edge, Element source, Element target)
            {
                ElementGeom blocker = null;
                double best = double.MaxValue;
                foreach (ElementGeom g in _layout.Elements)
                {
                    if (g.Source == source || g.Source == target)
                        continue;
                    if (g.Source.Inside(source) || g.Source.Inside(target)
                        || source.Inside(g.Source) || target.Inside(g.Source))
                        continue;
                    double t = SegmentRect(edge.From, edge.To, g.Rect);
                    if (t >= 0 && t < best)
                    {
                        best = t;
                        blocker = g;
                    }
                }
                if (blocker == null)
                    return;

                double gap = U(14);
                Rect r = blocker.Rect;
                double x = ClearSide(r.Right + gap, r, source, target) ? r.Right + gap : r.Left - gap;
                double top = r.Top - U(10);
                double bottom = r.Bottom + U(10);

                edge.Route.Add(edge.From);
                edge.Route.Add(new Point(edge.From.X, top));
                edge.Route.Add(new Point(x, top));
                edge.Route.Add(new Point(x, bottom));
                edge.Route.Add(new Point(edge.To.X, bottom));
                edge.Route.Add(edge.To);
            }

            /// <summary>线段先碰到矩形的那个参数 t（0..1），没碰到返回 -1。</summary>
            private static double SegmentRect(Point a, Point b, Rect r)
            {
                const int steps = 40;
                for (int i = 1; i < steps; i++)
                {
                    double t = (double)i / steps;
                    var p = new Point(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
                    if (r.Contains(p))
                        return t;
                }
                return -1;
            }

            /// <summary>竖着走 x 这条线（绕过 blocker 的那一段）会不会撞上别的元素。</summary>
            private bool ClearSide(double x, Rect blocker, Element source, Element target)
            {
                var band = new Rect(x - U(4), blocker.Top - U(10), U(8), blocker.Height + U(20));
                foreach (ElementGeom g in _layout.Elements)
                {
                    if (g.Source == source || g.Source == target)
                        continue;
                    if (g.Rect.IntersectsWith(band))
                        return false;
                }
                return true;
            }

            /// <summary>把一条边采样成折线点列：有绕行路径就按路径均匀取点，否则取贝塞尔。</summary>
            internal static List<Point> EdgeSamples(EdgeGeom e)
            {
                var pts = new List<Point>();
                if (e.HasRoute)
                {
                    // 折线：每段按长度比例取点，保证点距大致均匀
                    double total = 0;
                    for (int i = 1; i < e.Route.Count; i++)
                        total += (e.Route[i] - e.Route[i - 1]).Length;
                    int steps = (int)Math.Ceiling(total / 3.0);
                    if (steps < 16)
                        steps = 16;
                    else if (steps > 160)
                        steps = 160;
                    for (int k = 0; k <= steps; k++)
                    {
                        double want = total * k / steps;
                        double acc = 0;
                        for (int i = 1; i < e.Route.Count; i++)
                        {
                            double seg = (e.Route[i] - e.Route[i - 1]).Length;
                            if (acc + seg >= want || i == e.Route.Count - 1)
                            {
                                double t = seg < 1e-6 ? 0 : (want - acc) / seg;
                                t = Math.Max(0, Math.Min(1, t));
                                pts.Add(new Point(
                                    e.Route[i - 1].X + (e.Route[i].X - e.Route[i - 1].X) * t,
                                    e.Route[i - 1].Y + (e.Route[i].Y - e.Route[i - 1].Y) * t));
                                break;
                            }
                            acc += seg;
                        }
                    }
                    return pts;
                }

                int n = (int)Math.Ceiling((e.To - e.From).Length / 3.0);
                if (n < 16)
                    n = 16;
                else if (n > 96)
                    n = 96;
                for (int k = 0; k <= n; k++)
                    pts.Add(QuadCurve.PointAt(e.From, e.Control, e.To, (double)k / n));
                return pts;
            }

            /// <summary>note on link 的框大小：按最宽一行算，再加内边距。</summary>
            private Size NoteSize(string note)
            {
                double w = 0;
                double h = 0;
                string[] lines = note.Replace("\r\n", "\n").Split('\n');
                foreach (string line in lines)
                {
                    FormattedText ft = Text(line, _tf, _text);
                    w = Math.Max(w, ft.Width);
                    h += ft.Height;
                }
                return new Size(w + U(12), h + U(8));
            }

            /// <summary>两点中点，用作直线边的控制点。</summary>
            private static Point MidPoint(Point from, Point to)
            {
                return new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2);
            }

            /// <summary>
            /// 边的控制点取中点并沿垂直方向偏出一点，画成一段有弧度的曲线，标签贴着弧线走。
            /// 弧高随边长成比例（短边弧也短）；上下两条反向边会向相反两侧鼓，不会叠在一起。
            /// </summary>
            private Point ControlPoint(Point from, Point to)
            {
                double dx = to.X - from.X;
                double dy = to.Y - from.Y;
                double len = Math.Sqrt(dx * dx + dy * dy);
                Point mid = new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2);
                if (len < 1e-6)
                    return mid;
                double nx = -dy / len;
                double ny = dx / len;
                double bulge = len * 0.18;
                return new Point(mid.X + nx * bulge, mid.Y + ny * bulge);
            }

            /// <summary>
            /// 边标签默认贴在中点，会盖住线。改成挪到线的旁边（沿曲线切线的垂直方向偏开半个字高），
            /// 线保持连续。位置从中间向两头试、从贴着线往外试，同时避开所有元素框和别的标签。
            /// </summary>
            private void PlaceLabels()
            {
                double[] tries = { 0.5, 0.4, 0.6, 0.3, 0.7, 0.22, 0.78, 0.15, 0.85 };

                // 矩形障碍：元素框 + 便签 + 已放好的标签。
                // 容器（package / node / folder 之类）本身是背景，不算障碍——它的矩形盖住整个框，
                // 算进去的话框内任何位置都"冲突"，画在框里那条边上的标签就只能退到压框的位置；
                // 框里的子元素照样是障碍
                var blocked = new List<Rect>();
                foreach (ElementGeom geom in _layout.Elements)
                {
                    if (geom.Source != null && geom.Source.IsContainer && !geom.Source.Transparent)
                    {
                        // 容器本身是背景，但它的标题不是——标签压到标题上一样算重叠
                        if (geom.Text != null)
                            blocked.Add(TitleRect(geom));
                        continue;
                    }
                    blocked.Add(geom.Rect);
                }
                foreach (EdgeGeom e in _layout.Edges)
                {
                    if (e.NoteRect.Width > 0)
                        blocked.Add(e.NoteRect);
                }
                foreach (DiagramNoteGeom note in _layout.Notes)
                    blocked.Add(note.Rect);
                // 标题占着图顶那一条（元素都从它下面才开始排），整条都当障碍：
                // 回边会从图顶上绕过去，标签很容易落到标题上。标题的水平居中位置要等
                // 包围盒算完才知道，这里索性按整条封掉
                if (_layout.Title != null)
                    blocked.Add(new Rect(0, _layout.Title.Top, 1e6, _layout.Title.Text.Height));

                // 线按"采样点到标签框的真实距离"判定，不用方块——方块判太粗，
                // 会把"贴着线但没压上"的位置也判成冲突，标签就被推到老远
                var lines = new List<List<Point>>();
                var lineOf = new Dictionary<EdgeGeom, int>();
                foreach (EdgeGeom e in _layout.Edges)
                {
                    lineOf[e] = lines.Count;
                    lines.Add(EdgeSamples(e));
                }

                foreach (EdgeGeom edge in _layout.Edges)
                {
                    if (edge.Label == null)
                        continue;

                    // 有注释的边：标签排在注释框正上方（跟 Java 版一致）
                    if (edge.Note != null)
                    {
                        var above = new Rect(
                            edge.NoteRect.X + (edge.NoteRect.Width - edge.Label.Width) / 2,
                            edge.NoteRect.Y - edge.Label.Height - U(4),
                            edge.Label.Width,
                            edge.Label.Height);
                        edge.LabelPos = new Point(above.X, above.Y);
                        blocked.Add(above);
                        continue;
                    }

                    int ownLine = lineOf[edge];
                    // 偏开线的距离：一对反向边（A->B 与 B->A）的中点几乎重合，偏移太小两个标签会叠在一起
                    double off = edge.Label.Height / 2 + _fs * 0.25;

                    // 所有候选位置一起打分，而不是"撞上第一个空位就用"：横移/外挪一档都不小，
                    // 先到先得会把标签甩到离自己那条边很远的地方。打分后总挑离边最近的那个空位
                    Rect best = Rect.Empty;
                    int bestHits = int.MaxValue;
                    double bestGap = double.MaxValue;

                    foreach (double t in tries)
                    {
                        Point basePoint = QuadCurve.PointAt(edge.From, edge.Control, edge.To, t);
                        double ux;
                        double uy;
                        QuadCurve.TangentAt(edge.From, edge.Control, edge.To, t, out ux, out uy);
                        // 先从贴着线的地方试，挤不下就再往外挪一点
                        foreach (double k in new[] { 1.0, 2.0, 3.5, 5.0 })
                        {
                            foreach (int sign in new[] { 1, -1 })
                            {
                                double d = off * k;
                                var candidate = new Rect(
                                    basePoint.X - uy * d * sign - edge.Label.Width / 2,
                                    basePoint.Y + ux * d * sign - edge.Label.Height / 2,
                                    edge.Label.Width,
                                    edge.Label.Height);
                                Consider(blocked, lines, ownLine, candidate, lines[ownLine],
                                    ref best, ref bestHits, ref bestGap);
                            }
                        }
                    }

                    blocked.Add(best);
                    edge.LabelPos = new Point(best.X, best.Y);
                }
            }

            /// <summary>
            /// 给一个候选位置打分并留下当前最好的：先比冲突数，再比"离自己那条边多远"。
            /// 只按冲突数挑，会在若干同样干净的空位里随便挑一个（先到先得），
            /// 标签可能停在离边很远的地方；补上距离这一项，标签就总是落在最近的空位上。
            /// </summary>
            private void Consider(List<Rect> blocked, List<List<Point>> lines, int skipLine,
                Rect candidate, List<Point> ownLine,
                ref Rect best, ref int bestHits, ref double bestGap)
            {
                // 压没压上按字形算：FormattedText 的框上下各带一截行距留白，
                // 拿整个框去撞会把"只是边挨着"也判成冲突
                double pad = (candidate.Height - _fs) / 2;
                Rect ink = pad > 0
                    ? new Rect(candidate.X, candidate.Y + pad, candidate.Width, candidate.Height - pad * 2)
                    : candidate;

                int hits = CountHits(blocked, lines, skipLine, ink, bestHits);
                if (hits > bestHits)
                    return;

                double gap = GapToLine(ownLine, ink);
                if (hits < bestHits || gap < bestGap)
                {
                    bestHits = hits;
                    bestGap = gap;
                    best = candidate;
                }
            }

            /// <summary>标签框离自己那条边最近的距离（按采样点算）。</summary>
            private static double GapToLine(List<Point> line, Rect box)
            {
                double min = double.MaxValue;
                foreach (Point p in line)
                {
                    double d = DistToBox(p, box);
                    if (d < min)
                        min = d;
                }
                return min == double.MaxValue ? 0 : min;
            }

            /// <summary>候选框压到了几样东西。超过当前最优就没必要再算下去。</summary>
            private static int CountHits(List<Rect> blocked, List<List<Point>> lines, int skipLine,
                Rect box, int limit)
            {
                int n = 0;
                foreach (Rect r in blocked)
                {
                    if (r.IntersectsWith(box) && ++n > limit)
                        return n;
                }
                for (int i = 0; i < lines.Count; i++)
                {
                    if (i == skipLine)
                        continue;
                    bool hit = false;
                    foreach (Point p in lines[i])
                    {
                        if (DistToBox(p, box) < LineClear)
                        {
                            hit = true;
                            break;
                        }
                    }
                    if (hit && ++n > limit)
                        return n;
                }
                return n;
            }

            /// <summary>标签框离线的安全距离：边缘离采样点小于这个值就算压上去了。</summary>
            private const double LineClear = 2.5;

            /// <summary>点到矩形的最短距离（点在框内时为 0）。</summary>
            private static double DistToBox(Point p, Rect box)
            {
                double dx = Math.Max(Math.Max(box.Left - p.X, 0), p.X - box.Right);
                double dy = Math.Max(Math.Max(box.Top - p.Y, 0), p.Y - box.Bottom);
                return Math.Sqrt(dx * dx + dy * dy);
            }

            /// <summary>把从 inside 指向 outside 的线裁到矩形边界，得到箭头落点。</summary>
            private static Point ClipToRect(Rect rect, Point inside, Point outside)
            {
                double dx = outside.X - inside.X;
                double dy = outside.Y - inside.Y;
                double t = double.MaxValue;
                if (dx > 1e-9)
                    t = Math.Min(t, (rect.Right - inside.X) / dx);
                else if (dx < -1e-9)
                    t = Math.Min(t, (rect.Left - inside.X) / dx);
                if (dy > 1e-9)
                    t = Math.Min(t, (rect.Bottom - inside.Y) / dy);
                else if (dy < -1e-9)
                    t = Math.Min(t, (rect.Top - inside.Y) / dy);
                if (t == double.MaxValue || t < 0)
                    t = 0;
                return new Point(inside.X + dx * t, inside.Y + dy * t);
            }

            private FormattedText Text(string text, Typeface typeface, Brush brush)
            {
                return new FormattedText(
                    text ?? string.Empty,
                    CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    _fs,
                    brush,
                    _ppd);
            }
        }
    }
}

