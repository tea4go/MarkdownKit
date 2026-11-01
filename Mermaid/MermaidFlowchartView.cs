using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MarkdownKit.Mermaid
{
    /// <summary>
    /// 画一张 Mermaid 流程图（flowchart / graph）。
    ///
    /// 布局是最简单的分层法：先用 DFS 找出回边、按无环部分把节点分到第 0、1、2… 层，
    /// 再用重心法（barycenter）调整每层顺序把交叉压到最少；回边从侧面绕行。
    /// 连线走圆角折线（拐角用二次贝塞尔），不做直角硬拐。
    /// 绘制用 Canvas + 矢量形状，整体缩放到预览区宽度以内（只缩不放）。
    /// </summary>
    internal sealed class MermaidFlowchartView : Viewbox
    {
        /// <summary>节点内边距：左右 / 上下。</summary>
        private const double NodePadX = 13;
        private const double NodePadY = 8;

        /// <summary>层与层之间、同层节点之间的距离。</summary>
        private const double LayerGap = 48;
        private const double NodeGap = 30;

        /// <summary>四周留白，免得箭头贴在边框上。</summary>
        private const double Pad = 24;

        /// <summary>折线拐角的圆弧半径。线段太短时会自动缩小。</summary>
        private const double CornerRadius = 16;

        public MermaidFlowchartView(Flowchart chart, double fontSize, FrameworkElement host)
        {
            Stretch = Stretch.Uniform;
            StretchDirection = StretchDirection.DownOnly;
            HorizontalAlignment = HorizontalAlignment.Center;

            var canvas = new Canvas();
            Child = canvas;

            try
            {
                Layout(chart, fontSize);
                Draw(canvas, chart, fontSize);
            }
            catch (Exception)
            {
                // 画不出来就留白，别把整份预览拖垮
                canvas.Children.Clear();
                canvas.Width = 0;
                canvas.Height = 0;
            }
        }

        // ── 布局 ────────────────────────────────────────────────────

        private static void Layout(Flowchart chart, double fontSize)
        {
            if (chart.Nodes.Count == 0)
            {
                chart.Width = 0;
                chart.Height = 0;
                return;
            }

            // 1. 先量尺寸
            foreach (FlowNode node in chart.Nodes)
            {
                Size text = MeasureText(node.Text, fontSize);
                node.Width = Math.Max(text.Width + NodePadX * 2, fontSize * 3);
                node.Height = Math.Max(text.Height + NodePadY * 2, fontSize * 1.9);

                // 菱形和圆要把文字包在里面，得多留些余地
                if (node.Shape == FlowShape.Diamond)
                {
                    node.Width = node.Width * 1.35;
                    node.Height = node.Height * 1.35;
                }
                else if (node.Shape == FlowShape.Circle)
                {
                    double side = Math.Max(node.Width, node.Height) * 1.15;
                    node.Width = side;
                    node.Height = side;
                }
            }

            // 2. 标回边，分层只看无环部分，否则有环时最长路径会把层号越推越大
            MarkBackEdges(chart);

            var level = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (FlowNode node in chart.Nodes)
                level[node.Id] = 0;

            for (int pass = 0; pass < chart.Nodes.Count; pass++)
            {
                bool changed = false;
                foreach (FlowEdge edge in chart.Edges)
                {
                    if (edge.Back)
                        continue;
                    int want = level[edge.From] + 1;
                    if (level[edge.To] < want)
                    {
                        level[edge.To] = want;
                        changed = true;
                    }
                }
                if (!changed)
                    break;
            }

            int maxLayer = 0;
            foreach (FlowNode node in chart.Nodes)
            {
                node.Layer = level[node.Id];
                maxLayer = Math.Max(maxLayer, node.Layer);
            }

            var layers = new List<List<FlowNode>>();
            for (int i = 0; i <= maxLayer; i++)
                layers.Add(new List<FlowNode>());
            foreach (FlowNode node in chart.Nodes)
                layers[node.Layer].Add(node);

            // 3. 重心法调层内顺序，减少连线交叉
            OrderLayers(chart, layers);

            // 4. 摆位置：先按中心线排（会有负坐标），最后整体平移到正数
            double offset = 0;
            foreach (List<FlowNode> group in layers)
            {
                if (group.Count == 0)
                    continue;

                if (chart.LeftToRight)
                {
                    double total = NodeGap * (group.Count - 1);
                    double thickest = 0;
                    foreach (FlowNode node in group)
                    {
                        total += node.Height;
                        thickest = Math.Max(thickest, node.Width);
                    }

                    double y = -total / 2;
                    foreach (FlowNode node in group)
                    {
                        node.X = offset;
                        node.Y = y;
                        y += node.Height + NodeGap;
                    }
                    offset += thickest + LayerGap;
                }
                else
                {
                    double total = NodeGap * (group.Count - 1);
                    double thickest = 0;
                    foreach (FlowNode node in group)
                    {
                        total += node.Width;
                        thickest = Math.Max(thickest, node.Height);
                    }

                    double x = -total / 2;
                    foreach (FlowNode node in group)
                    {
                        node.X = x;
                        node.Y = offset;
                        x += node.Width + NodeGap;
                    }
                    offset += thickest + LayerGap;
                }
            }

            // 5. 平移到正坐标
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            foreach (FlowNode node in chart.Nodes)
            {
                minX = Math.Min(minX, node.X);
                minY = Math.Min(minY, node.Y);
                maxX = Math.Max(maxX, node.X + node.Width);
                maxY = Math.Max(maxY, node.Y + node.Height);
            }
            if (minX == double.MaxValue)
            {
                chart.Width = 0;
                chart.Height = 0;
                return;
            }

            foreach (FlowNode node in chart.Nodes)
            {
                node.X += Pad - minX;
                node.Y += Pad - minY;
            }
            chart.Width = maxX - minX + Pad * 2;
            chart.Height = maxY - minY + Pad * 2;
        }

        /// <summary>DFS 遍历，指向栈上祖先的边记作回边；自环也算回边。</summary>
        private static void MarkBackEdges(Flowchart chart)
        {
            var outgoing = new Dictionary<string, List<FlowEdge>>(StringComparer.Ordinal);
            foreach (FlowNode node in chart.Nodes)
                outgoing[node.Id] = new List<FlowEdge>();

            foreach (FlowEdge edge in chart.Edges)
            {
                edge.Back = false;
                if (string.Equals(edge.From, edge.To))
                {
                    edge.Back = true;
                    continue;
                }
                List<FlowEdge> list;
                if (outgoing.TryGetValue(edge.From, out list))
                    list.Add(edge);
            }

            // 0 未访问 / 1 在栈上 / 2 已完成
            var state = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (FlowNode node in chart.Nodes)
                state[node.Id] = 0;

            foreach (FlowNode root in chart.Nodes)
            {
                if (state[root.Id] != 0)
                    continue;

                state[root.Id] = 1;
                var stack = new Stack<DfsFrame>();
                stack.Push(new DfsFrame { Id = root.Id, Index = 0 });

                while (stack.Count > 0)
                {
                    DfsFrame frame = stack.Peek();
                    List<FlowEdge> edges = outgoing[frame.Id];
                    if (frame.Index >= edges.Count)
                    {
                        state[frame.Id] = 2;
                        stack.Pop();
                        continue;
                    }

                    FlowEdge edge = edges[frame.Index];
                    frame.Index++;

                    int target;
                    if (!state.TryGetValue(edge.To, out target))
                        continue;

                    if (target == 1)
                    {
                        edge.Back = true;
                    }
                    else if (target == 0)
                    {
                        state[edge.To] = 1;
                        stack.Push(new DfsFrame { Id = edge.To, Index = 0 });
                    }
                }
            }
        }

        private sealed class DfsFrame
        {
            public string Id;
            public int Index;
        }

        /// <summary>重心法排序：反复自上而下、自下而上按邻居平均位置重排每层，留交叉最少的一次。</summary>
        private static void OrderLayers(Flowchart chart, List<List<FlowNode>> layers)
        {
            if (layers.Count < 2)
                return;

            var pred = new Dictionary<string, List<FlowNode>>(StringComparer.Ordinal);
            var succ = new Dictionary<string, List<FlowNode>>(StringComparer.Ordinal);
            foreach (FlowNode node in chart.Nodes)
            {
                pred[node.Id] = new List<FlowNode>();
                succ[node.Id] = new List<FlowNode>();
            }

            foreach (FlowEdge edge in chart.Edges)
            {
                if (edge.Back)
                    continue;
                FlowNode from = chart.Find(edge.From);
                FlowNode to = chart.Find(edge.To);
                if (from == null || to == null || from.Layer == to.Layer)
                    continue;
                succ[from.Id].Add(to);
                pred[to.Id].Add(from);
            }

            var pos = new Dictionary<string, int>(StringComparer.Ordinal);
            RebuildPositions(layers, pos);

            int best = CountCrossings(chart, pos);
            if (best == 0)
                return;
            List<List<string>> bestOrder = Snapshot(layers);

            for (int sweep = 0; sweep < 6 && best > 0; sweep++)
            {
                if ((sweep % 2) == 0)
                {
                    for (int l = 1; l < layers.Count; l++)
                    {
                        SortLayer(layers[l], pred, pos);
                        RebuildPositions(layers, pos);
                    }
                }
                else
                {
                    for (int l = layers.Count - 2; l >= 0; l--)
                    {
                        SortLayer(layers[l], succ, pos);
                        RebuildPositions(layers, pos);
                    }
                }

                int crossings = CountCrossings(chart, pos);
                if (crossings < best)
                {
                    best = crossings;
                    bestOrder = Snapshot(layers);
                }
            }

            ApplyOrder(chart, layers, bestOrder);
        }

        private static void SortLayer(List<FlowNode> layer, Dictionary<string, List<FlowNode>> neighbors,
            Dictionary<string, int> pos)
        {
            if (layer.Count < 2)
                return;

            var key = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (FlowNode node in layer)
            {
                List<FlowNode> adjacent;
                int current;
                pos.TryGetValue(node.Id, out current);

                if (neighbors.TryGetValue(node.Id, out adjacent) && adjacent.Count > 0)
                {
                    double sum = 0;
                    foreach (FlowNode other in adjacent)
                    {
                        int p;
                        if (pos.TryGetValue(other.Id, out p))
                            sum += p;
                    }
                    key[node.Id] = sum / adjacent.Count;
                }
                else
                {
                    key[node.Id] = current;
                }
            }

            layer.Sort((a, b) =>
            {
                double ka, kb;
                key.TryGetValue(a.Id, out ka);
                key.TryGetValue(b.Id, out kb);
                int cmp = ka.CompareTo(kb);
                if (cmp != 0)
                    return cmp;

                int pa, pb;
                pos.TryGetValue(a.Id, out pa);
                pos.TryGetValue(b.Id, out pb);
                return pa.CompareTo(pb);
            });
        }

        private static void RebuildPositions(List<List<FlowNode>> layers, Dictionary<string, int> pos)
        {
            foreach (List<FlowNode> layer in layers)
            {
                for (int i = 0; i < layer.Count; i++)
                    pos[layer[i].Id] = i;
            }
        }

        /// <summary>数相邻两层之间的连线交叉数（只看跨一层的边，长边暂不计）。</summary>
        private static int CountCrossings(Flowchart chart, Dictionary<string, int> pos)
        {
            int total = 0;
            int maxLayer = 0;
            foreach (FlowNode node in chart.Nodes)
                maxLayer = Math.Max(maxLayer, node.Layer);

            var froms = new List<int>();
            var tos = new List<int>();

            for (int l = 0; l < maxLayer; l++)
            {
                froms.Clear();
                tos.Clear();
                foreach (FlowEdge edge in chart.Edges)
                {
                    if (edge.Back)
                        continue;
                    FlowNode from = chart.Find(edge.From);
                    FlowNode to = chart.Find(edge.To);
                    if (from == null || to == null)
                        continue;
                    if (from.Layer != l || to.Layer != l + 1)
                        continue;

                    int pf, pt;
                    if (pos.TryGetValue(from.Id, out pf) && pos.TryGetValue(to.Id, out pt))
                    {
                        froms.Add(pf);
                        tos.Add(pt);
                    }
                }

                for (int i = 0; i < froms.Count; i++)
                {
                    for (int j = i + 1; j < froms.Count; j++)
                    {
                        int d1 = froms[i] - froms[j];
                        int d2 = tos[i] - tos[j];
                        if ((long)d1 * d2 < 0)
                            total++;
                    }
                }
            }
            return total;
        }

        private static List<List<string>> Snapshot(List<List<FlowNode>> layers)
        {
            var copy = new List<List<string>>();
            foreach (List<FlowNode> layer in layers)
            {
                var ids = new List<string>();
                foreach (FlowNode node in layer)
                    ids.Add(node.Id);
                copy.Add(ids);
            }
            return copy;
        }

        private static void ApplyOrder(Flowchart chart, List<List<FlowNode>> layers, List<List<string>> order)
        {
            for (int i = 0; i < layers.Count && i < order.Count; i++)
            {
                var byId = new Dictionary<string, FlowNode>(StringComparer.Ordinal);
                foreach (FlowNode node in layers[i])
                    byId[node.Id] = node;

                var ordered = new List<FlowNode>();
                foreach (string id in order[i])
                {
                    FlowNode node;
                    if (byId.TryGetValue(id, out node))
                        ordered.Add(node);
                }

                layers[i].Clear();
                layers[i].AddRange(ordered);
            }
        }

        /// <summary>量一段文字排出来多大（换行符按多行算）。</summary>
        private static Size MeasureText(string text, double fontSize)
        {
            var block = new TextBlock
            {
                Text = text ?? string.Empty,
                FontFamily = MarkdownTheme.DiagramFamily,
                FontSize = fontSize,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = fontSize * 14
            };
            block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return block.DesiredSize;
        }

        // ── 绘制 ────────────────────────────────────────────────────

        private static void Draw(Canvas canvas, Flowchart chart, double fontSize)
        {
            // 回边从右侧（LR 时从下方）绕行，给画布留出槽位
            int wrapEdges = 0;
            foreach (FlowEdge edge in chart.Edges)
            {
                if (edge.Back && !string.Equals(edge.From, edge.To))
                    wrapEdges++;
            }

            double extra = wrapEdges > 0 ? 18 + (wrapEdges - 1) * 12 : 0;
            if (chart.LeftToRight)
            {
                canvas.Width = chart.Width;
                canvas.Height = chart.Height + extra;
            }
            else
            {
                canvas.Width = chart.Width + extra;
                canvas.Height = chart.Height;
            }

            Brush line = MarkdownTheme.Markdown.Text;
            Brush fill = MarkdownTheme.Markdown.CodeBackground;
            Brush border = MarkdownTheme.Markdown.CodeBorder;
            Brush labelBackground = MarkdownTheme.Background;

            // 同一节点上有多条出边/入边时，沿节点边沿分散端口，避免线段重叠
            var outFrac = new Dictionary<FlowEdge, double>();
            var inFrac = new Dictionary<FlowEdge, double>();
            AssignPorts(chart, outFrac, inFrac);

            // 连线先画，节点压在上面，线头就不会从节点里透出来
            int slot = 0;
            foreach (FlowEdge edge in chart.Edges)
            {
                FlowNode from = chart.Find(edge.From);
                FlowNode to = chart.Find(edge.To);
                if (from == null || to == null)
                    continue;

                double startFrac;
                if (!outFrac.TryGetValue(edge, out startFrac))
                    startFrac = 0.5;
                double endFrac;
                if (!inFrac.TryGetValue(edge, out endFrac))
                    endFrac = 0.5;

                int wrapSlot = -1;
                if (edge.Back && !string.Equals(edge.From, edge.To))
                    wrapSlot = slot++;

                DrawEdge(canvas, chart, edge, from, to, line, labelBackground, fontSize,
                    startFrac, endFrac, wrapSlot);
            }

            foreach (FlowNode node in chart.Nodes)
                DrawNode(canvas, node, fill, border, fontSize);
        }

        private static void AssignPorts(Flowchart chart,
            Dictionary<FlowEdge, double> outFrac, Dictionary<FlowEdge, double> inFrac)
        {
            var outs = new Dictionary<string, List<FlowEdge>>(StringComparer.Ordinal);
            var ins = new Dictionary<string, List<FlowEdge>>(StringComparer.Ordinal);
            foreach (FlowNode node in chart.Nodes)
            {
                outs[node.Id] = new List<FlowEdge>();
                ins[node.Id] = new List<FlowEdge>();
            }

            foreach (FlowEdge edge in chart.Edges)
            {
                if (edge.Back || string.Equals(edge.From, edge.To))
                    continue;
                List<FlowEdge> list;
                if (outs.TryGetValue(edge.From, out list))
                    list.Add(edge);
                if (ins.TryGetValue(edge.To, out list))
                    list.Add(edge);
            }

            foreach (FlowNode node in chart.Nodes)
            {
                List<FlowEdge> leaving = outs[node.Id];
                if (leaving.Count > 0)
                {
                    leaving.Sort((a, b) => CompareAlong(chart, chart.Find(a.To), chart.Find(b.To)));
                    for (int i = 0; i < leaving.Count; i++)
                        outFrac[leaving[i]] = PortFraction(i, leaving.Count);
                }

                List<FlowEdge> arriving = ins[node.Id];
                if (arriving.Count > 0)
                {
                    arriving.Sort((a, b) => CompareAlong(chart, chart.Find(a.From), chart.Find(b.From)));
                    for (int i = 0; i < arriving.Count; i++)
                        inFrac[arriving[i]] = PortFraction(i, arriving.Count);
                }
            }
        }

        /// <summary>按目标在层内的位置排序，让端口顺序和线的走向一致。</summary>
        private static int CompareAlong(Flowchart chart, FlowNode a, FlowNode b)
        {
            if (a == null || b == null)
                return 0;
            double ca = chart.LeftToRight ? a.Y + a.Height / 2 : a.X + a.Width / 2;
            double cb = chart.LeftToRight ? b.Y + b.Height / 2 : b.X + b.Width / 2;
            return ca.CompareTo(cb);
        }

        private static double PortFraction(int index, int count)
        {
            if (count <= 1)
                return 0.5;
            return 0.3 + 0.4 * index / (count - 1);
        }

        private static void DrawNode(Canvas canvas, FlowNode node, Brush fill, Brush border, double fontSize)
        {
            if (node.Shape == FlowShape.Diamond)
            {
                var diamond = new Polygon
                {
                    Points = new PointCollection
                    {
                        new Point(node.X + node.Width / 2, node.Y),
                        new Point(node.X + node.Width, node.Y + node.Height / 2),
                        new Point(node.X + node.Width / 2, node.Y + node.Height),
                        new Point(node.X, node.Y + node.Height / 2)
                    },
                    Fill = fill,
                    Stroke = border,
                    StrokeThickness = 1.3
                };
                canvas.Children.Add(diamond);
            }
            else if (node.Shape == FlowShape.Circle)
            {
                var circle = new Ellipse
                {
                    Width = node.Width,
                    Height = node.Height,
                    Fill = fill,
                    Stroke = border,
                    StrokeThickness = 1.3
                };
                Canvas.SetLeft(circle, node.X);
                Canvas.SetTop(circle, node.Y);
                canvas.Children.Add(circle);
            }
            else
            {
                var box = new Border
                {
                    Width = node.Width,
                    Height = node.Height,
                    Background = fill,
                    BorderBrush = border,
                    BorderThickness = new Thickness(1.3),
                    CornerRadius = new CornerRadius(node.Shape == FlowShape.Rounded ? node.Height / 2 : 7)
                };
                Canvas.SetLeft(box, node.X);
                Canvas.SetTop(box, node.Y);
                canvas.Children.Add(box);
            }

            var text = new TextBlock
            {
                Text = node.Text ?? string.Empty,
                FontFamily = MarkdownTheme.DiagramFamily,
                FontSize = fontSize,
                Foreground = MarkdownTheme.Markdown.Text,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = Math.Max(10, node.Width - NodePadX * 2)
            };
            text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            Canvas.SetLeft(text, node.X + Math.Max(0, (node.Width - text.DesiredSize.Width) / 2));
            Canvas.SetTop(text, node.Y + Math.Max(0, (node.Height - text.DesiredSize.Height) / 2));
            canvas.Children.Add(text);
        }

        private static void DrawEdge(Canvas canvas, Flowchart chart, FlowEdge edge,
            FlowNode from, FlowNode to, Brush line, Brush labelBackground, double fontSize,
            double startFrac, double endFrac, int wrapSlot)
        {
            List<Point> points = BuildPoints(chart, edge, from, to, startFrac, endFrac, wrapSlot);

            var path = new Path
            {
                Data = BuildRoundedGeometry(points),
                Stroke = line,
                StrokeThickness = 1.3,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            if (edge.Dashed)
                path.StrokeDashArray = new DoubleCollection { 4, 3 };
            canvas.Children.Add(path);

            // 箭头：按最后一段的方向画一个实心三角
            Point last = points[points.Count - 1];
            Point prev = points[points.Count - 2];
            double dx = last.X - prev.X;
            double dy = last.Y - prev.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length > 0.01)
            {
                double ux = dx / length;
                double uy = dy / length;
                const double head = 9;
                const double half = 4.5;
                var arrow = new Polygon
                {
                    Points = new PointCollection
                    {
                        last,
                        new Point(last.X - ux * head - uy * half, last.Y - uy * head + ux * half),
                        new Point(last.X - ux * head + uy * half, last.Y - uy * head - ux * half)
                    },
                    Fill = line,
                    Stroke = line,
                    StrokeThickness = 0.5
                };
                canvas.Children.Add(arrow);
            }

            if (string.IsNullOrEmpty(edge.Label))
                return;

            // 标签摆在整条折线的中点，垫一层背景盖住线
            Point mid = PolylineMidpoint(points);
            var label = new TextBlock
            {
                Text = edge.Label,
                FontFamily = MarkdownTheme.DiagramFamily,
                FontSize = fontSize * 0.88,
                Foreground = MarkdownTheme.Markdown.Text,
                Background = labelBackground,
                Padding = new Thickness(4, 1, 4, 1)
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, mid.X - label.DesiredSize.Width / 2);
            Canvas.SetTop(label, mid.Y - label.DesiredSize.Height / 2);
            canvas.Children.Add(label);
        }

        /// <summary>折线的顶点序列。正向边走「出来—横移—进去」，回边从侧面绕。</summary>
        private static List<Point> BuildPoints(Flowchart chart, FlowEdge edge,
            FlowNode from, FlowNode to, double startFrac, double endFrac, int wrapSlot)
        {
            var points = new List<Point>();

            if (string.Equals(edge.From, edge.To))
            {
                if (chart.LeftToRight)
                {
                    Point s = new Point(from.X + from.Width * 0.6, from.Y + from.Height);
                    Point e = new Point(from.X + from.Width * 0.25, from.Y + from.Height);
                    double y = from.Y + from.Height + 22;
                    points.Add(s);
                    points.Add(new Point(s.X, y));
                    points.Add(new Point(e.X, y));
                    points.Add(e);
                }
                else
                {
                    Point s = new Point(from.X + from.Width, from.Y + from.Height * 0.45);
                    Point e = new Point(from.X + from.Width, from.Y + from.Height * 0.15);
                    double x = from.X + from.Width + 22;
                    points.Add(s);
                    points.Add(new Point(x, s.Y));
                    points.Add(new Point(x, e.Y));
                    points.Add(e);
                }
                return points;
            }

            if (edge.Back)
            {
                if (chart.LeftToRight)
                {
                    // 从下方绕过所有节点再折回
                    Point s = new Point(from.X + from.Width / 2, from.Y + from.Height);
                    Point e = new Point(to.X + to.Width / 2, to.Y + to.Height);
                    double gutter = chart.Height - Pad + 18 + wrapSlot * 12;
                    points.Add(s);
                    points.Add(new Point(s.X, gutter));
                    points.Add(new Point(e.X, gutter));
                    points.Add(e);
                }
                else
                {
                    // 从右侧绕过所有节点再折回
                    Point s = new Point(from.X + from.Width, from.Y + from.Height / 2);
                    Point e = new Point(to.X + to.Width, to.Y + to.Height / 2);
                    double gutter = chart.Width - Pad + 18 + wrapSlot * 12;
                    points.Add(s);
                    points.Add(new Point(gutter, s.Y));
                    points.Add(new Point(gutter, e.Y));
                    points.Add(e);
                }
                return points;
            }

            if (chart.LeftToRight)
            {
                Point s = new Point(from.X + from.Width, from.Y + from.Height * startFrac);
                Point e = new Point(to.X, to.Y + to.Height * endFrac);
                if (Math.Abs(s.Y - e.Y) < 0.5)
                {
                    points.Add(s);
                    points.Add(e);
                    return points;
                }

                double midX = (s.X + e.X) / 2;
                if (to.Layer - from.Layer > 1)
                    midX = from.X + from.Width + LayerGap / 2;
                points.Add(s);
                points.Add(new Point(midX, s.Y));
                points.Add(new Point(midX, e.Y));
                points.Add(e);
            }
            else
            {
                Point s = new Point(from.X + from.Width * startFrac, from.Y + from.Height);
                Point e = new Point(to.X + to.Width * endFrac, to.Y);
                if (Math.Abs(s.X - e.X) < 0.5)
                {
                    points.Add(s);
                    points.Add(e);
                    return points;
                }

                double midY = (s.Y + e.Y) / 2;
                if (to.Layer - from.Layer > 1)
                    midY = from.Y + from.Height + LayerGap / 2;
                points.Add(s);
                points.Add(new Point(s.X, midY));
                points.Add(new Point(e.X, midY));
                points.Add(e);
            }

            return points;
        }

        /// <summary>把折线的每个直角拐点换成一段二次贝塞尔圆弧。</summary>
        private static PathGeometry BuildRoundedGeometry(IList<Point> points)
        {
            var figure = new PathFigure
            {
                StartPoint = points[0],
                IsClosed = false,
                IsFilled = false
            };

            int last = points.Count - 1;
            for (int i = 1; i < last; i++)
            {
                Point prev = points[i - 1];
                Point corner = points[i];
                Point next = points[i + 1];

                double d1 = Distance(prev, corner);
                double d2 = Distance(corner, next);
                double radius = Math.Min(CornerRadius, Math.Min(d1, d2) / 2);

                if (radius < 1)
                {
                    figure.Segments.Add(new LineSegment(corner, true));
                    continue;
                }

                var entry = new Point(corner.X + (prev.X - corner.X) * radius / d1,
                                      corner.Y + (prev.Y - corner.Y) * radius / d1);
                var exit = new Point(corner.X + (next.X - corner.X) * radius / d2,
                                     corner.Y + (next.Y - corner.Y) * radius / d2);

                figure.Segments.Add(new LineSegment(entry, true));
                figure.Segments.Add(new QuadraticBezierSegment(corner, exit, true));
            }

            figure.Segments.Add(new LineSegment(points[last], true));

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            return geometry;
        }

        private static Point PolylineMidpoint(IList<Point> points)
        {
            double total = 0;
            for (int i = 1; i < points.Count; i++)
                total += Distance(points[i - 1], points[i]);
            if (total < 0.001)
                return points[0];

            double half = total / 2;
            double walked = 0;
            for (int i = 1; i < points.Count; i++)
            {
                double d = Distance(points[i - 1], points[i]);
                if (walked + d >= half && d > 0.001)
                {
                    double t = (half - walked) / d;
                    return new Point(points[i - 1].X + (points[i].X - points[i - 1].X) * t,
                                     points[i - 1].Y + (points[i].Y - points[i - 1].Y) * t);
                }
                walked += d;
            }
            return points[points.Count - 1];
        }

        private static double Distance(Point a, Point b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
