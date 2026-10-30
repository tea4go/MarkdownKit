using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace MarkdownKit.PlantUml
{
    /// <summary>一张状态图算好的几何。</summary>
    internal sealed class StateLayout
    {
        public double Width;
        public double Height;

        /// <summary>所有状态，按绘制顺序排（父在前，子在后）。</summary>
        public readonly List<StateGeom> Elements = new List<StateGeom>();

        public readonly List<StateEdgeGeom> Edges = new List<StateEdgeGeom>();

        /// <summary>挂在状态旁的注释（note left/right/top/bottom of X）。</summary>
        public readonly List<StateNoteGeom> Notes = new List<StateNoteGeom>();

        /// <summary>图例框（legend ... endlegend），没有图例时为 null。</summary>
        public LegendGeom Legend;

        public TitleGeom Title;
    }

    /// <summary>状态旁的注释便签。多行文本按行拆开，绘制时逐行画。</summary>
    internal sealed class StateNoteGeom
    {
        public Rect Rect;
        public readonly List<FormattedText> Lines = new List<FormattedText>();

        public void Shift(double dx, double dy)
        {
            Rect = new Rect(Rect.X + dx, Rect.Y + dy, Rect.Width, Rect.Height);
        }
    }

    internal sealed class StateGeom
    {
        public StateNode Source;
        public Rect Rect;
        public FormattedText Text;

        /// <summary>构造型（«xxx»），没有则为 null。</summary>
        public FormattedText Stereo;

        /// <summary>state X : 字段，每行一条，没有则为 null。</summary>
        public List<FormattedText> Fields;
    }

    /// <summary>一条转换。From/To 已裁到两端状态边界上，Control 是二次贝塞尔的控制点。</summary>
    internal sealed class StateEdgeGeom
    {
        public Point From;
        public Point To;
        public Point Control;
        public bool Dashed;
        public bool Bold;
        public bool CrossStart;
        public TransitionHead Head;
        public FormattedText Label;
        public Point LabelPos;

        public void Shift(double dx, double dy)
        {
            From = new Point(From.X + dx, From.Y + dy);
            To = new Point(To.X + dx, To.Y + dy);
            Control = new Point(Control.X + dx, Control.Y + dy);
            LabelPos = new Point(LabelPos.X + dx, LabelPos.Y + dy);
        }
    }

    /// <summary>
    /// 由状态图模型算出几何。叶子按种类定尺寸（普通状态是圆角矩形、[*] 是圆、伪状态是小形状）；
    /// 同一层里按转换分层（rank）——箭头下游排更靠后，同层并排；复合状态把子状态整个包住，顶部留标题区。
    /// 布局流程照 Graphviz dot 那套（官方状态图也是交给 dot/Smetana 的）。
    /// </summary>
    internal static class StateLayoutBuilder
    {
        public static StateLayout Build(StateDiagram diagram, double fontSize, double pixelsPerDip)
        {
            return new Impl(diagram, fontSize, pixelsPerDip).Build();
        }

        private sealed class Impl
        {
            private readonly StateDiagram _diagram;
            private readonly double _fs;
            private readonly double _ppd;
            private readonly Typeface _tf;
            private readonly Typeface _tfBold;
            private readonly Typeface _tfItalic;
            private readonly Brush _text;
            private readonly StateLayout _layout = new StateLayout();
            private readonly Dictionary<StateNode, FormattedText> _labels = new Dictionary<StateNode, FormattedText>();
            private readonly Dictionary<StateNode, FormattedText> _stereos = new Dictionary<StateNode, FormattedText>();
            private readonly Dictionary<StateNode, List<FormattedText>> _fields = new Dictionary<StateNode, List<FormattedText>>();
            private readonly Dictionary<Transition, FormattedText> _transitionLabels = new Dictionary<Transition, FormattedText>();

            /// <summary>同层状态间距。官方 dot 的 nodesep 下限是 35，这里取 60 更宽松（标签多时不容易挤）。</summary>
            private double Gap { get { return U(60); } }

            /// <summary>层间距。官方 dot 的 ranksep 下限是 60、Smetana 实际排出来是 56，都偏挤，这里放到 85；标签特别高时还会再加。</summary>
            private double RankGap { get { return _rankGap; } }

            private double _rankGap;

            /// <summary>
            /// PlantUML 里状态图元素的字号是 14，各种形状常量（20、25、50…）都以它为基准。
            /// 本程序字号可调，这里统一换算，保证形状比例跟 PlantUML 一致。
            /// </summary>
            private double U(double v)
            {
                return v * _fs / 14.0;
            }

            public Impl(StateDiagram diagram, double fontSize, double pixelsPerDip)
            {
                _diagram = diagram;
                _fs = fontSize;
                _ppd = pixelsPerDip <= 0 ? 1.0 : pixelsPerDip;
                _tf = new Typeface(MarkdownTheme.DiagramFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                _tfBold = MarkdownTheme.BoldTypeface(MarkdownTheme.DiagramFamily);
                _tfItalic = new Typeface(MarkdownTheme.DiagramFamily, FontStyles.Italic, FontWeights.Normal, FontStretches.Normal);
                _text = MarkdownTheme.Markdown.Text;
            }

            public StateLayout Build()
            {
                List<StateNode> roots = _diagram.Roots;
                if (roots.Count == 0)
                    return _layout;

                _rankGap = U(85);   // 官方 Smetana 实测层间隙只有 56，排出来太挤，这里放到 85
                // 转换标签的文本先算好：层间要腾出标签高度，层内要给标签宽度留位置
                foreach (Transition tr in _diagram.Transitions)
                {
                    if (string.IsNullOrEmpty(tr.Label))
                        continue;
                    FormattedText ft = Text(tr.Label, _tf, _text);
                    _transitionLabels[tr] = ft;
                    _rankGap = Math.Max(_rankGap, ft.Height + U(40));
                }

                double margin = _fs * 0.6;
                double top = margin;

                if (!string.IsNullOrEmpty(_diagram.Title))
                {
                    FormattedText title = Text(_diagram.Title, _tfBold, _text);
                    _layout.Title = new TitleGeom { Text = title, Top = top, CenterX = 0 };
                    top += title.Height + _fs * 0.8;
                }

                foreach (StateNode root in roots)
                    Size(root);
                Arrange(roots, null);

                double minX = double.MaxValue;
                double minY = double.MaxValue;
                foreach (StateNode root in roots)
                {
                    minX = Math.Min(minX, root.X);
                    minY = Math.Min(minY, root.Y);
                }
                if (minX == double.MaxValue)
                    minX = minY = 0;

                foreach (StateNode root in roots)
                    Place(root, margin + root.X - minX, top + root.Y - minY);

                Collect(roots);
                LayoutNotes();   // 先定 note：转换标签避让时要把它当障碍，顺序反了会压到便签上
                BuildEdges();

                double contentMinX = double.MaxValue;
                double contentMinY = double.MaxValue;
                double maxRight = 0;
                double maxBottom = 0;
                foreach (StateGeom geom in _layout.Elements)
                {
                    contentMinX = Math.Min(contentMinX, geom.Rect.Left);
                    contentMinY = Math.Min(contentMinY, geom.Rect.Top);
                    maxRight = Math.Max(maxRight, geom.Rect.Right);
                    maxBottom = Math.Max(maxBottom, geom.Rect.Bottom);
                }
                foreach (StateNoteGeom note in _layout.Notes)
                {
                    contentMinX = Math.Min(contentMinX, note.Rect.Left);
                    contentMinY = Math.Min(contentMinY, note.Rect.Top);
                    maxRight = Math.Max(maxRight, note.Rect.Right);
                    maxBottom = Math.Max(maxBottom, note.Rect.Bottom);
                }
                // 转换标签也可能伸到图外面，一起算进包围盒，免得被裁
                foreach (StateEdgeGeom edge in _layout.Edges)
                {
                    if (edge.Label == null)
                        continue;
                    contentMinX = Math.Min(contentMinX, edge.LabelPos.X);
                    contentMinY = Math.Min(contentMinY, edge.LabelPos.Y);
                    maxRight = Math.Max(maxRight, edge.LabelPos.X + edge.Label.Width);
                    maxBottom = Math.Max(maxBottom, edge.LabelPos.Y + edge.Label.Height);
                }
                // 曲线本身也会鼓到节点框外面（回边绕行时尤其明显），同样算进去
                foreach (StateEdgeGeom edge in _layout.Edges)
                {
                    const int steps = 24;
                    for (int k = 0; k <= steps; k++)
                    {
                        Point p = QuadCurve.PointAt(edge.From, edge.Control, edge.To, (double)k / steps);
                        contentMinX = Math.Min(contentMinX, p.X);
                        contentMinY = Math.Min(contentMinY, p.Y);
                        maxRight = Math.Max(maxRight, p.X);
                        maxBottom = Math.Max(maxBottom, p.Y);
                    }
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

                // 注释可能伸到左/上边界外，整体右移、下移补回来
                double shiftX = contentMinX < margin ? margin - contentMinX : 0;
                double shiftY = contentMinY < margin ? margin - contentMinY : 0;
                if (shiftX != 0 || shiftY != 0)
                {
                    foreach (StateGeom geom in _layout.Elements)
                        geom.Rect = new Rect(geom.Rect.X + shiftX, geom.Rect.Y + shiftY, geom.Rect.Width, geom.Rect.Height);
                    foreach (StateEdgeGeom edge in _layout.Edges)
                        edge.Shift(shiftX, shiftY);
                    foreach (StateNoteGeom note in _layout.Notes)
                        note.Shift(shiftX, shiftY);
                    if (_layout.Legend != null)
                        _layout.Legend.Shift(shiftX, shiftY);
                    // 标题也要跟着挪：note top of X 可能伸到标题上方，整体下移时标题不能留在原地
                    if (_layout.Title != null)
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

            /// <summary>把 note left/right/top/bottom of X 摆到目标状态旁边，垂直居中。</summary>
            private void LayoutNotes()
            {
                foreach (StateNote note in _diagram.Notes)
                {
                    StateNode target = _diagram.Find(note.TargetKey);
                    if (target == null)
                        continue;

                    var geom = new StateNoteGeom();
                    double textW = 0;
                    double textH = 0;
                    foreach (string raw in note.Lines)
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
                    double x, y;
                    switch (note.Placement)
                    {
                        case "left":
                            x = target.X - gap - w;
                            y = target.Y + (target.H - h) / 2;
                            break;
                        case "top":
                            x = target.X + (target.W - w) / 2;
                            y = target.Y - gap - h;
                            break;
                        case "bottom":
                            x = target.X + (target.W - w) / 2;
                            y = target.Y + target.H + gap;
                            break;
                        default:   // right
                            x = target.X + target.W + gap;
                            y = target.Y + (target.H - h) / 2;
                            break;
                    }
                    geom.Rect = new Rect(x, y, w, h);
                    _layout.Notes.Add(geom);
                }
            }

            // ── 尺寸 ────────────────────────────────────────────────

            /// <summary>算一个状态的尺寸。复合状态先递归算好子状态，再按排布结果把自己包住。</summary>
            private void Size(StateNode e)
            {
                _labels[e] = Text(e.Label ?? e.Key ?? "", _tf, _text);
                if (!string.IsNullOrEmpty(e.Stereotype))
                    _stereos[e] = Text("«" + e.Stereotype + "»", _tfItalic, _text);
                if (e.Fields.Count > 0)
                {
                    var list = new List<FormattedText>();
                    foreach (string f in e.Fields)
                        list.Add(Text(f, _tf, _text));
                    _fields[e] = list;
                }

                if (e.Kind == StateKind.Composite)
                {
                    foreach (StateNode child in e.Children)
                        Size(child);

                    Size content = Arrange(e.Children, e);
                    double titleW = _labels[e].Width;
                    double band = TitleBand(e);
                    e.W = Math.Max(content.Width + U(20), titleW + U(20));
                    e.H = band + content.Height + U(16);
                }
                else
                {
                    SizeLeaf(e);
                }

                ReserveNote(e);
                ReserveLabel(e);
            }

            /// <summary>节点右侧挂了 note 时预留出它的宽度，别让 note 压到同层别的节点上。</summary>
            private void ReserveNote(StateNode e)
            {
                foreach (StateNote note in _diagram.Notes)
                {
                    if (note.TargetKey != e.Key || note.Placement != "right")
                        continue;
                    double textW = 0;
                    foreach (string raw in note.Lines)
                        textW = Math.Max(textW, Text(raw, _tf, _text).Width);
                    // 宽度 = note 文字 + note 左右内边距 + 与节点之间的间隔（跟 LayoutNotes 里一致）
                    e.NoteW = Math.Max(e.NoteW, textW + U(12) + U(10));
                }
            }

            /// <summary>
            /// 挂在本状态上的转换标签会横向伸出节点（标签默认居中在边的中点附近），
            /// 同层要按最宽的那条给状态留出位置，否则标签会压到旁边的状态上。
            /// </summary>
            private void ReserveLabel(StateNode e)
            {
                foreach (KeyValuePair<Transition, FormattedText> kv in _transitionLabels)
                {
                    if (kv.Key.From != e && kv.Key.To != e)
                        continue;
                    e.LabelW = Math.Max(e.LabelW, kv.Value.Width);
                }
            }

            private void SizeLeaf(StateNode e)
            {
                FormattedText ft = _labels[e];
                double w;
                double h;
                switch (e.Kind)
                {
                    case StateKind.Start:
                        w = h = U(20);   // CircleStart.SIZE
                        break;
                    case StateKind.End:
                        w = h = U(22);   // CircleEnd.SIZE
                        break;
                    case StateKind.History:
                    case StateKind.DeepHistory:
                        w = h = U(22);   // EntityImagePseudoState.SIZE
                        break;
                    case StateKind.Junction:
                        w = h = U(12);
                        break;
                    case StateKind.Choice:
                        w = h = U(28);   // 菱形，留点余地
                        break;
                    case StateKind.ForkJoin:
                        w = U(70);
                        h = U(8);
                        break;
                    case StateKind.SynchroBar:
                        w = Math.Max(ft.Width + U(20), U(60));
                        h = U(12);
                        break;
                    default:   // Normal：圆角矩形，最小 50×50
                    {
                        // 对齐官方 EntityImageState：MARGIN=5、MARGIN_LINE=5，
                        // 尺寸 = 内容 + (MARGIN+MARGIN_LINE)*2，名称与描述上下相接不另留间距
                        double fieldsH = 0;
                        double fieldsW = 0;
                        List<FormattedText> fields = _fields.ContainsKey(e) ? _fields[e] : null;
                        if (fields != null)
                        {
                            foreach (FormattedText f in fields)
                            {
                                fieldsH += f.Height;
                                fieldsW = Math.Max(fieldsW, f.Width);
                            }
                        }
                        FormattedText stereo = _stereos.ContainsKey(e) ? _stereos[e] : null;
                        double stereoW = stereo == null ? 0 : stereo.Width;
                        double stereoH = stereo == null ? 0 : stereo.Height + U(2);
                        w = Math.Max(Math.Max(ft.Width, stereoW), fieldsW) + U(20);
                        h = ft.Height + stereoH + fieldsH + U(20);
                        w = Math.Max(w, U(50));
                        h = Math.Max(h, U(50));
                        break;
                    }
                }
                e.W = w;
                e.H = h;
            }

            /// <summary>复合状态顶部留给标题的高度。</summary>
            private double TitleBand(StateNode e)
            {
                return U(6) + _labels[e].Height + U(6);
            }

            /// <summary>
            /// 把一组兄弟按转换排好，写回各自的相对 X/Y，并返回整块尺寸。
            /// 流程照 Graphviz dot 那套：破环 + 最长路径分层 → mincross 交叉最小化定层内左右序 → 优先级法定坐标。
            /// </summary>
            private Size Arrange(List<StateNode> siblings, StateNode owner)
            {
                int n = siblings.Count;
                if (n == 0)
                    return new Size(0, 0);

                List<int[]> edges = SiblingEdges(siblings);
                int[] rank = AssignRanks(n, edges);
                BalanceRanks(n, edges, rank);
                rank = CompactRanks(rank, n, out int maxRank);

                var layers = new List<List<int>>();
                for (int r = 0; r <= maxRank; r++)
                    layers.Add(new List<int>());
                for (int i = 0; i < n; i++)
                    layers[rank[i]].Add(i);

                MinCross(layers, edges, n);
                double[] center = AssignCoordinates(layers, edges, siblings);

                return ToCoordinates(layers, center, siblings);
            }

            /// <summary>兄弟之间的转换。两端都在本层集合里才算，重复的边去掉。</summary>
            private List<int[]> SiblingEdges(List<StateNode> siblings)
            {
                var index = new Dictionary<StateNode, int>();
                for (int i = 0; i < siblings.Count; i++)
                    index[siblings[i]] = i;

                var seen = new HashSet<long>();
                var edges = new List<int[]>();
                foreach (Transition tr in _diagram.Transitions)
                {
                    int a = IndexOf(siblings, index, tr.From);
                    int b = IndexOf(siblings, index, tr.To);
                    if (a < 0 || b < 0 || a == b)
                        continue;
                    long key = EdgeKey(a, b);
                    if (seen.Add(key))
                        edges.Add(new[] { a, b });
                }
                return edges;
            }

            private static long EdgeKey(int a, int b)
            {
                return (long)Math.Min(a, b) * 100000 + Math.Max(a, b);
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
                        if (rank[v] < rank[u] + 1)
                            rank[v] = rank[u] + 1;
                        if (--indeg[v] == 0)
                            queue.Enqueue(v);
                    }
                }
                return rank;
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
            /// </summary>
            private void BalanceRanks(int n, List<int[]> edges, int[] rank)
            {
                if (n == 0)
                    return;
                var succ = Adjacency(n, edges, true);
                var pred = Adjacency(n, edges, false);

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
                            lower = Math.Max(lower, rank[p] + 1);
                            neighbors.Add(rank[p]);
                        }
                        foreach (int s in succ[u])
                        {
                            upper = Math.Min(upper, rank[s] - 1);
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
            /// 反向轮（每 4 轮的前 2 轮）在 median 相同时交换两节点，对齐 dot 的 mincross_step。
            /// </summary>
            private void MinCross(List<List<int>> layers, List<int[]> edges, int n)
            {
                if (layers.Count < 2)
                    return;

                var succ = Adjacency(n, edges, true);
                var pred = Adjacency(n, edges, false);

                // 有"指向更低层"的边（回边）的节点：dot 会给跨层边在中间层插虚拟节点，
                // 虚拟节点排在参考层右侧，等于把这个节点往右推。这里用参考层大小的偏移近似。
                var rankOf = new int[n];
                for (int r = 0; r < layers.Count; r++)
                    foreach (int node in layers[r])
                        rankOf[node] = r;
                var hasBack = new bool[n];
                for (int r = 1; r < layers.Count; r++)
                {
                    foreach (int node in layers[r])
                    {
                        foreach (int nb in succ[node])
                        {
                            if (rankOf[nb] < r)
                            {
                                hasBack[node] = true;
                                break;
                            }
                        }
                    }
                }

                // dot 的 mincross：每轮跑完算交叉数，为 0 立刻停下用当前顺序；
                // 否则记住交叉最少的那轮，最后恢复。初始顺序不参与比较——
                // 反向轮会让层内顺序来回振荡，不这么做就会停在最后一轮（往往是换回去的那次）
                int best = int.MaxValue;
                List<List<int>> bestOrder = null;

                for (int pass = 0; pass < 8; pass++)
                {
                    bool reverse = (pass % 4) < 2;   // dot：每 4 轮一个周期，前 2 轮反向
                    if (pass % 2 == 0)
                    {
                        for (int r = 1; r < layers.Count; r++)
                            SortByMedian(layers[r], layers[r - 1], pred, succ, pred, false, hasBack, reverse);
                    }
                    else
                    {
                        for (int r = layers.Count - 2; r >= 0; r--)
                            SortByMedian(layers[r], layers[r + 1], succ, succ, pred, true, hasBack, reverse);
                    }
                    Transpose(layers, edges);

                    int cur = TotalCrossings(layers, edges);
                    if (cur == 0)
                    {
                        bestOrder = null;
                        break;
                    }
                    if (cur < best)
                    {
                        best = cur;
                        bestOrder = CloneLayers(layers);
                    }
                }

                if (bestOrder != null)
                {
                    for (int r = 0; r < layers.Count; r++)
                    {
                        layers[r].Clear();
                        layers[r].AddRange(bestOrder[r]);
                    }
                }
            }

            private static List<List<int>> CloneLayers(List<List<int>> layers)
            {
                var copy = new List<List<int>>(layers.Count);
                foreach (List<int> l in layers)
                    copy.Add(new List<int>(l));
                return copy;
            }

            private static int TotalCrossings(List<List<int>> layers, List<int[]> edges)
            {
                int total = 0;
                for (int r = 0; r + 1 < layers.Count; r++)
                    total += Crossings(layers[r], layers[r + 1], edges);
                return total;
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

            /// <summary>
            /// 按"在参考层里的邻居位置中位数"给本层排序，没邻居的保持原位。
            /// median 按 dot 的算法取 256 * 参考层位置 + 端口序：端口序是该边在端点上的序号，
            /// 用来区分"同一参考节点的多条边"——否则它们 median 相同，会被反向轮无谓地交换。
            /// </summary>
            private static void SortByMedian(List<int> layer, List<int> reference, List<int>[] adj,
                List<int>[] succ, List<int>[] pred, bool forward, bool[] hasBack, bool reverse)
            {
                var posInRef = new Dictionary<int, int>();
                for (int i = 0; i < reference.Count; i++)
                    posInRef[reference[i]] = i;

                var median = new Dictionary<int, double>();
                var origin = new Dictionary<int, int>();
                for (int i = 0; i < layer.Count; i++)
                {
                    int node = layer[i];
                    origin[node] = i;
                    var positions = new List<int>();
                    foreach (int nb in adj[node])
                    {
                        int p;
                        if (!posInRef.TryGetValue(nb, out p))
                            continue;
                        // 下走用入边：端口序取该边在源节点出边表里的位置；上走用出边：取在目标节点入边表里的位置
                        int port = forward ? pred[nb].IndexOf(node) : succ[nb].IndexOf(node);
                        positions.Add(p * 256 + (port < 0 ? 0 : port));
                    }
                    positions.Sort();
                    double m = positions.Count == 0 ? i : Median(positions);
                    // 回边端点：dot 的虚拟节点排在参考层右侧，等效于把它整个推到本层最右
                    if (hasBack[node])
                        m += reference.Count * 256 + 256;
                    median[node] = m;
                }

                layer.Sort((a, b) =>
                {
                    int c = median[a].CompareTo(median[b]);
                    if (c != 0)
                        return c;
                    // median 相同：dot 在反向轮里交换两者，这里用倒序的"原位"来体现
                    return reverse ? origin[b].CompareTo(origin[a]) : origin[a].CompareTo(origin[b]);
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
            private double[] AssignCoordinates(List<List<int>> layers, List<int[]> edges, List<StateNode> siblings)
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
            private Size ToCoordinates(List<List<int>> layers, double[] center, List<StateNode> siblings)
            {
                bool leftToRight = _diagram.LeftToRight;

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
                        StateNode s = siblings[i];
                        double inPos = center[i] + shift;
                        double crossPos = rankOffset[r] + (rankSize[r] - CrossSize(s)) / 2;
                        if (leftToRight)
                        {
                            s.X = crossPos;
                            s.Y = inPos - s.H / 2;
                        }
                        else
                        {
                            // 竖排：节点连同转换标签、右侧 note 占一块有效宽度，整体在层内居中
                            double span = Math.Max(s.W, s.LabelW);
                            s.X = inPos - (span + s.NoteW) / 2 + (span - s.W) / 2;
                            s.Y = crossPos;
                        }
                    }
                }

                return leftToRight
                    ? new Size(crossTotal, inTotal)
                    : new Size(inTotal, crossTotal);
            }

            /// <summary>状态沿"层内轴"的尺寸：竖排分层时是宽，横排时是高。含转换标签与右侧 note 的预留。</summary>
            private double InSize(StateNode e)
            {
                if (_diagram.LeftToRight)
                    return e.H;
                return Math.Max(e.W, e.LabelW) + e.NoteW;
            }

            /// <summary>状态沿"分层轴"的尺寸：竖排分层时是高，横排时是宽。含转换标签与右侧 note 的预留。</summary>
            private double CrossSize(StateNode e)
            {
                if (_diagram.LeftToRight)
                    return Math.Max(e.W, e.LabelW) + e.NoteW;
                return e.H;
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

            /// <summary>把状态映射到它在 siblings 里的那一层祖先（本身是兄弟就返回自己）。</summary>
            private static int IndexOf(List<StateNode> siblings, Dictionary<StateNode, int> index, StateNode e)
            {
                for (StateNode p = e; p != null; p = p.Parent)
                {
                    int i;
                    if (index.TryGetValue(p, out i))
                        return i;
                }
                return -1;
            }

            // ── 绝对定位 ────────────────────────────────────────────

            /// <summary>把状态放到绝对位置，子状态跟着挪过去。</summary>
            private void Place(StateNode e, double x, double y)
            {
                e.X = x;
                e.Y = y;
                if (e.Children.Count == 0)
                    return;

                if (e.Kind != StateKind.Composite)
                {
                    foreach (StateNode child in e.Children)
                        Place(child, x + child.X, y + child.Y);
                    return;
                }

                double band = TitleBand(e);
                foreach (StateNode child in e.Children)
                    Place(child, x + U(10) + child.X, y + band + child.Y);
            }

            private void Collect(List<StateNode> nodes)
            {
                foreach (StateNode e in nodes)
                {
                    _layout.Elements.Add(new StateGeom
                    {
                        Source = e,
                        Rect = new Rect(e.X, e.Y, e.W, e.H),
                        Text = _labels[e],
                        Stereo = _stereos.ContainsKey(e) ? _stereos[e] : null,
                        Fields = _fields.ContainsKey(e) ? _fields[e] : null
                    });
                    if (e.Children.Count > 0)
                        Collect(e.Children);
                }
            }

            // ── 转换边 ──────────────────────────────────────────────

            private void BuildEdges()
            {
                foreach (Transition tr in _diagram.Transitions)
                {
                    StateNode source = tr.From;
                    StateNode target = tr.To;
                    if (source == null || target == null || source == target)
                        continue;

                    var sc = new Point(source.X + source.W / 2, source.Y + source.H / 2);
                    var tc = new Point(target.X + target.W / 2, target.Y + target.H / 2);
                    var srcRect = new Rect(source.X, source.Y, source.W, source.H);
                    var dstRect = new Rect(target.X, target.Y, target.W, target.H);
                    Point from = ClipToRect(srcRect, sc, tc);
                    Point to = ClipToRect(dstRect, tc, sc);

                    // 回边（箭头指向上游）改成从"弧鼓出去的那一侧"出框、进框，而不是朝着对方那一侧。
                    // 否则回边会顺着两个框之间的走廊走，跟正向边挤在同一条缝里——正向边的标签
                    // 被它挡住，只能退到很远的地方去。官方 dot 是把回边反向、在中间层插虚拟节点，
                    // 画出来同样是绕到旁边，这里用"换个出框边"达到同样的效果。
                    if (IsUpstream(sc, tc))
                    {
                        double nx = -(to.Y - from.Y);
                        double ny = to.X - from.X;
                        double len = Math.Sqrt(nx * nx + ny * ny);
                        if (len > 1e-6)
                        {
                            nx /= len;
                            ny /= len;
                            from = ClipToRect(srcRect, sc, new Point(sc.X + nx, sc.Y + ny));
                            to = ClipToRect(dstRect, tc, new Point(tc.X + nx, tc.Y + ny));
                        }
                    }

                    var edge = new StateEdgeGeom
                    {
                        From = from,
                        To = to,
                        Control = ControlPoint(from, to),
                        Dashed = tr.Dashed,
                        Bold = tr.Bold,
                        CrossStart = tr.CrossStart,
                        Head = tr.Head
                    };
                    FormattedText label;
                    if (_transitionLabels.TryGetValue(tr, out label))
                        edge.Label = label;
                    _layout.Edges.Add(edge);
                }

                PlaceLabels();
            }

            /// <summary>目标排在源头之前（箭头逆着分层方向指），也就是一条回边。</summary>
            private bool IsUpstream(Point sc, Point tc)
            {
                return _diagram.LeftToRight ? tc.X < sc.X : tc.Y < sc.Y;
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
            /// 线保持连续。位置从中间向两头试，同时避开所有状态框、复合状态标题区和别的标签。
            /// </summary>
            private void PlaceLabels()
            {
                // 矩形障碍：状态框 + note 便签 + 已放好的标签
                var blocked = new List<Rect>();
                foreach (StateGeom geom in _layout.Elements)
                {
                    // 复合状态容器本身是背景，不算障碍——它的矩形盖住整个框，
                    // 算进去的话框内任何位置都"冲突"，标签会被挤到框外面去；子节点照样是障碍。
                    // 但顶部那条标题带不是背景，标签压到标题上一样算重叠
                    if (geom.Source != null && geom.Source.Kind == StateKind.Composite)
                    {
                        blocked.Add(new Rect(geom.Rect.X, geom.Rect.Y, geom.Rect.Width,
                            Math.Min(TitleBand(geom.Source), geom.Rect.Height)));
                        continue;
                    }
                    blocked.Add(geom.Rect);
                }
                foreach (StateNoteGeom note in _layout.Notes)
                    blocked.Add(note.Rect);
                // 标题占着图顶那一条（元素都从它下面才开始排），整条都当障碍：
                // 回边会从图顶上绕过去，标签很容易落到标题上。标题的水平居中位置要等
                // 包围盒算完才知道，这里索性按整条封掉
                if (_layout.Title != null)
                    blocked.Add(new Rect(0, _layout.Title.Top, 1e6, _layout.Title.Text.Height));

                // 线按"采样点 + 到标签框的真实距离"判定，不用方块——方块判太粗，
                // 会把"贴着线但没压上"的位置也判成冲突，标签就被推到老远。
                // 采样间隔按边长定（约 3 一个点）：点太稀时曲线会从两个采样点之间穿过标签框，
                // 既算不准"离边多远"，也漏判"压上线"
                var lines = new List<EdgeLine>();
                var lineOf = new Dictionary<StateEdgeGeom, int>();
                foreach (StateEdgeGeom e in _layout.Edges)
                {
                    var line = new EdgeLine();
                    int steps = (int)Math.Ceiling((e.To - e.From).Length / 3.0);
                    if (steps < 16)
                        steps = 16;
                    else if (steps > 96)
                        steps = 96;
                    for (int k = 0; k <= steps; k++)
                        line.Points.Add(QuadCurve.PointAt(e.From, e.Control, e.To, (double)k / steps));
                    line.Bounds = BoundsOf(line.Points);
                    lineOf[e] = lines.Count;
                    lines.Add(line);
                }

                // 先放长边的标签：边越长，标签的可选位置越容易被节点和别的标签占掉，
                // 让它先占住；短边标签可选的余地大，后放也容易找到位置
                var ordered = new List<StateEdgeGeom>();
                foreach (StateEdgeGeom e in _layout.Edges)
                {
                    if (e.Label != null)
                        ordered.Add(e);
                }
                ordered.Sort((a, b) =>
                {
                    // 先按标签宽度降序：宽标签可选位置更少（更容易撞上节点和别的标签），
                    // 让它先占；窄标签余地大，后放也容易找到位置。
                    // 宽度相同再看边长，边越长可选位置越少。
                    int c = b.Label.Width.CompareTo(a.Label.Width);
                    if (c != 0)
                        return c;
                    double la = (a.To - a.From).Length;
                    double lb = (b.To - b.From).Length;
                    return lb.CompareTo(la);
                });

                // 复合状态框的并集：标签跑出这个范围算"离得远"。框外没有障碍，
                // 不加这条的话"冲突最少"的退路永远选框外，标签会被甩到图外面去
                Rect outer = Rect.Empty;
                foreach (StateGeom geom in _layout.Elements)
                {
                    if (geom.Source != null && geom.Source.Kind == StateKind.Composite)
                        outer = outer.IsEmpty ? geom.Rect : Rect.Union(outer, geom.Rect);
                }

                foreach (StateEdgeGeom edge in ordered)
                {
                    int ownLine = lineOf[edge];
                    // 两端都在复合状态里的边，标签就该留在框内——跑出去算重罚，
                    // 这样"冲突最少"的退路不会因为框外没障碍就选到框外
                    int outsidePenalty = 0;
                    if (!outer.IsEmpty && (outer.Contains(edge.From) || outer.Contains(edge.To)))
                        outsidePenalty = 1000;

                    // 向上的回边多半会穿过中间的节点，标签优先靠近起点端，别落在中间节点上
                    double[] tries = edge.From.Y > edge.To.Y
                        ? new[] { 0.18, 0.28, 0.38, 0.08, 0.5, 0.62, 0.75, 0.88 }
                        : new[] { 0.5, 0.4, 0.6, 0.3, 0.7, 0.22, 0.78, 0.15, 0.85, 0.1, 0.9 };

                    // 偏开线的距离：一对反向边（A→B 与 B→A）的中点几乎重合，偏移太小两个标签会叠在一起
                    double off = edge.Label.Height / 2 + _fs * 0.9;

                    // 所有候选位置一起打分，而不是"撞上第一个空位就用"：横移一档就是 1.1 个标签宽，
                    // 只要中点附近被节点或别的边占住，先到先得就会把标签甩到很远的地方去，
                    // 看着跟自己的边毫无关系。打分之后总能在所有空位里挑到离自己那条边最近的。
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
                                // 贴着自己边放：查别的线（免得压到别的边上），自己那条不用查
                                // —— 沿法线偏了 off，本来就离线有半个字高
                                Consider(blocked, lines, ownLine, outsidePenalty, outer, candidate,
                                    lines[ownLine], ref best, ref bestHits, ref bestGap);
                                // 一对反向边的中点几乎重合，只靠垂直偏移分不开，官方是把标签左右错开的。
                                // 横移会离开"贴着边"的位置，这时连自己那条边也要查，别滑到它上面去
                                foreach (double f in new[] { 0.55, -0.55, 1.1, -1.1 })
                                {
                                    var shifted = new Rect(
                                        candidate.X + f * edge.Label.Width,
                                        candidate.Y,
                                        edge.Label.Width,
                                        edge.Label.Height);
                                    Consider(blocked, lines, -1, outsidePenalty, outer, shifted,
                                        lines[ownLine], ref best, ref bestHits, ref bestGap);
                                }
                            }
                        }
                    }

                    blocked.Add(best);
                    edge.LabelPos = new Point(best.X, best.Y);
                }
            }

            /// <summary>标签框离线的安全距离：边缘离采样点小于这个值就算压上去了。</summary>
            private const double LineClear = 2.5;

            /// <summary>
            /// 一条边的采样折线，外加它的包围盒。判"标签压没压线"时先用包围盒粗筛，
            /// 不挨着的线整个跳过——逐点算距离是这个布局里最贵的一步。
            /// </summary>
            private sealed class EdgeLine
            {
                public readonly List<Point> Points = new List<Point>();

                /// <summary>采样点的包围盒，已按 <see cref="LineClear"/> 外扩。</summary>
                public Rect Bounds;
            }

            private static Rect BoundsOf(List<Point> points)
            {
                double minX = double.MaxValue;
                double minY = double.MaxValue;
                double maxX = double.MinValue;
                double maxY = double.MinValue;
                foreach (Point p in points)
                {
                    minX = Math.Min(minX, p.X);
                    minY = Math.Min(minY, p.Y);
                    maxX = Math.Max(maxX, p.X);
                    maxY = Math.Max(maxY, p.Y);
                }
                if (minX > maxX)
                    minX = minY = maxX = maxY = 0;
                var bounds = new Rect(minX, minY, maxX - minX, maxY - minY);
                bounds.Inflate(LineClear + 0.5, LineClear + 0.5);
                return bounds;
            }

            /// <summary>
            /// 给一个候选位置打分并留下当前最好的：先比冲突数，再比"离自己那条边多远"。
            /// 只按冲突数挑，会在若干同样干净的空位里随便挑一个（先到先得），
            /// 于是标签可能停在离边很远的地方；补上距离这一项，标签就总是落在最近的空位上。
            /// </summary>
            private void Consider(List<Rect> blocked, List<EdgeLine> lines, int skipLine,
                int outsidePenalty, Rect outer, Rect candidate, EdgeLine ownLine,
                ref Rect best, ref int bestHits, ref double bestGap)
            {
                // 压没压上按字形算：FormattedText 的框上下各带一截行距留白，拿整个框去撞，
                // 会把"只是边挨着"也判成冲突，标签就被推到很远的地方去
                double pad = (candidate.Height - _fs) / 2;
                Rect ink = pad > 0
                    ? new Rect(candidate.X, candidate.Y + pad, candidate.Width, candidate.Height - pad * 2)
                    : candidate;

                int hits = CountHits(blocked, lines, skipLine, ink, bestHits);
                // 跑出复合状态框算重罚：框外没有障碍，"冲突最少"的退路会永远选框外，
                // 标签就被甩到图外面去了。只在"所有位置都挤不下"时才算——
                // 真有空位时不该因为空位在框外就放弃它
                if (hits > 0 && outsidePenalty > 0 && !outer.Contains(candidate))
                    hits += outsidePenalty;
                // 已经比当前最好的差了，就不用再花时间算离边多远
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
            private static double GapToLine(EdgeLine line, Rect box)
            {
                double min = double.MaxValue;
                foreach (Point p in line.Points)
                {
                    double d = DistToBox(p, box);
                    if (d < min)
                        min = d;
                }
                return min == double.MaxValue ? 0 : min;
            }

            /// <summary>
            /// 候选框压到了几样东西。limit 是"当前最好的冲突数"——超过它就没有再算下去的必要，
            /// 直接返回，省掉后面几十个采样点的距离计算。
            /// </summary>
            private static int CountHits(List<Rect> blocked, List<EdgeLine> lines, int skipLine, Rect box, int limit)
            {
                var padded = new Rect(box.X - 1, box.Y - 1, box.Width + 2, box.Height + 2);
                int n = 0;
                foreach (Rect r in blocked)
                {
                    if (r.IntersectsWith(padded) && ++n > limit)
                        return n;
                }
                for (int i = 0; i < lines.Count; i++)
                {
                    if (i == skipLine)
                        continue;
                    if (LineHits(lines[i], box) && ++n > limit)
                        return n;
                }
                return n;
            }

            private static bool LineHits(EdgeLine line, Rect box)
            {
                if (!line.Bounds.IntersectsWith(box))
                    return false;
                foreach (Point p in line.Points)
                {
                    if (DistToBox(p, box) < LineClear)
                        return true;
                }
                return false;
            }

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

