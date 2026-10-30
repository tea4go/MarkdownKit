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
    /// 布局是最简单的分层法：按最长路径把节点分到第 0、1、2… 层，同层按出现顺序横排，
    /// 连线走「出来—横移—进去」的折线。够画常见的流程图，也不至于像力导向那样每次都不一样。
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

            // 2. 分层：最长路径。有环时靠遍数上限收敛，不会死循环
            var level = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (FlowNode node in chart.Nodes)
                level[node.Id] = 0;

            for (int pass = 0; pass < chart.Nodes.Count; pass++)
            {
                bool changed = false;
                foreach (FlowEdge edge in chart.Edges)
                {
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

            // 3. 摆位置：先按中心线排（会有负坐标），最后整体平移到正数
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

            // 4. 平移到正坐标
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
            canvas.Width = chart.Width;
            canvas.Height = chart.Height;

            Brush line = MarkdownTheme.Markdown.Text;
            Brush fill = MarkdownTheme.Markdown.CodeBackground;
            Brush border = MarkdownTheme.Markdown.CodeBorder;
            Brush labelBackground = MarkdownTheme.Background;

            // 连线先画，节点压在上面，线头就不会从节点里透出来
            foreach (FlowEdge edge in chart.Edges)
            {
                FlowNode from = chart.Find(edge.From);
                FlowNode to = chart.Find(edge.To);
                if (from == null || to == null)
                    continue;
                DrawEdge(canvas, chart, edge, from, to, line, labelBackground, fontSize);
            }

            foreach (FlowNode node in chart.Nodes)
                DrawNode(canvas, node, fill, border, fontSize);
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
            FlowNode from, FlowNode to, Brush line, Brush labelBackground, double fontSize)
        {
            var points = new List<Point>();

            if (chart.LeftToRight)
            {
                Point start = new Point(from.X + from.Width, from.Y + from.Height / 2);
                Point end = new Point(to.X, to.Y + to.Height / 2);
                if (end.X > start.X)
                {
                    double midX = (start.X + end.X) / 2;
                    points.Add(start);
                    points.Add(new Point(midX, start.Y));
                    points.Add(new Point(midX, end.Y));
                    points.Add(end);
                }
                else
                {
                    // 回边（目标在左边）：直接连，别绕远
                    points.Add(start);
                    points.Add(end);
                }
            }
            else
            {
                Point start = new Point(from.X + from.Width / 2, from.Y + from.Height);
                Point end = new Point(to.X + to.Width / 2, to.Y);
                if (end.Y > start.Y)
                {
                    double midY = (start.Y + end.Y) / 2;
                    points.Add(start);
                    points.Add(new Point(start.X, midY));
                    points.Add(new Point(end.X, midY));
                    points.Add(end);
                }
                else
                {
                    points.Add(start);
                    points.Add(end);
                }
            }

            var figure = new PathFigure { StartPoint = points[0], IsClosed = false, IsFilled = false };
            for (int i = 1; i < points.Count; i++)
                figure.Segments.Add(new LineSegment(points[i], true));

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);

            var path = new Path
            {
                Data = geometry,
                Stroke = line,
                StrokeThickness = 1.3,
                StrokeLineJoin = PenLineJoin.Round
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

            // 标签摆在折线的中点上，垫一层背景盖住线
            Point mid = points[points.Count / 2];
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
    }
}
