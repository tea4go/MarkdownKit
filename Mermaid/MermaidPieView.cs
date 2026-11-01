using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MarkdownKit.Mermaid
{
    /// <summary>
    /// 画一张 Mermaid 饼图（pie）。
    ///
    /// 左边是饼，从 12 点钟方向顺时针排；扇形上标百分比（太窄的瓣省略，免得叠字），
    /// 右边是图例，色块加标签，showData 时再带上数值。
    /// 和流程图一样整体缩放到预览区宽度以内（只缩不放）。
    /// </summary>
    internal sealed class MermaidPieView : Viewbox
    {
        /// <summary>四周留白。</summary>
        private const double Pad = 18;

        /// <summary>饼半径相对字号的比例。</summary>
        private const double RadiusFactor = 6;

        /// <summary>标题与内容之间、饼与图例之间的距离。</summary>
        private const double TitleGap = 12;
        private const double PieLegendGap = 30;

        /// <summary>图例色块尺寸、色块与文字间距、行距。</summary>
        private const double LegendSwatch = 12;
        private const double LegendSwatchGap = 8;
        private const double LegendRowGap = 7;

        /// <summary>百分比标签放在半径的这个比例处。</summary>
        private const double PercentInner = 0.62;

        /// <summary>扇形小于这个角度就不标百分比了。</summary>
        private const double MinPercentSweep = 12;

        private static readonly Color[] PaletteColors =
        {
            Color.FromRgb(0x4E, 0x79, 0xA7),
            Color.FromRgb(0xF2, 0x8E, 0x2B),
            Color.FromRgb(0xE1, 0x57, 0x59),
            Color.FromRgb(0x76, 0xB7, 0xB2),
            Color.FromRgb(0x59, 0xA1, 0x4F),
            Color.FromRgb(0xED, 0xC9, 0x48),
            Color.FromRgb(0xB0, 0x7A, 0xA1),
            Color.FromRgb(0xFF, 0x9D, 0xA7),
            Color.FromRgb(0x9C, 0x75, 0x5F),
            Color.FromRgb(0xBA, 0xB0, 0xAC),
            Color.FromRgb(0x86, 0xBC, 0xB6),
            Color.FromRgb(0xD3, 0x72, 0x95)
        };

        private static readonly Brush[] Palette = BuildPalette();

        public MermaidPieView(PieChart chart, double fontSize, FrameworkElement host)
        {
            Stretch = Stretch.Uniform;
            StretchDirection = StretchDirection.DownOnly;
            HorizontalAlignment = HorizontalAlignment.Center;

            var canvas = new Canvas();
            Child = canvas;

            try
            {
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

        private static void Draw(Canvas canvas, PieChart chart, double fontSize)
        {
            double total = 0;
            foreach (PieSlice slice in chart.Slices)
                total += slice.Value;

            double radius = fontSize * RadiusFactor;
            double pieSize = radius * 2;

            // 先量图例，宽度决定整张图的宽度
            var legendItems = new List<string>();
            double legendWidth = 0;
            double legendRowHeight = 0;
            foreach (PieSlice slice in chart.Slices)
            {
                string text = chart.ShowData
                    ? slice.Label + ": " + FormatNumber(slice.Value)
                    : slice.Label;
                legendItems.Add(text);

                Size size = MeasureText(text, fontSize * 0.92, FontWeights.Normal);
                legendWidth = Math.Max(legendWidth, LegendSwatch + LegendSwatchGap + size.Width);
                legendRowHeight = Math.Max(legendRowHeight, Math.Max(size.Height, LegendSwatch));
            }
            legendRowHeight += LegendRowGap;
            bool hasLegend = chart.Slices.Count > 0;
            double legendHeight = hasLegend
                ? chart.Slices.Count * legendRowHeight - LegendRowGap
                : 0;

            // 标题：有就占顶部一行
            string title = chart.Title;
            bool hasTitle = !string.IsNullOrEmpty(title);
            Size titleSize = hasTitle
                ? MeasureText(title, fontSize * 1.15, FontWeights.Bold)
                : new Size(0, 0);
            double titleBlock = hasTitle ? titleSize.Height + TitleGap : 0;

            double contentWidth = pieSize + (hasLegend ? PieLegendGap + legendWidth : 0);
            double contentHeight = Math.Max(pieSize, legendHeight);
            double width = Math.Max(contentWidth, titleSize.Width) + Pad * 2;
            double height = titleBlock + contentHeight + Pad * 2;

            canvas.Width = width;
            canvas.Height = height;

            if (hasTitle)
            {
                var block = new TextBlock
                {
                    Text = title,
                    FontFamily = MarkdownTheme.DiagramFamily,
                    FontSize = fontSize * 1.15,
                    FontWeight = FontWeights.Bold,
                    Foreground = MarkdownTheme.Markdown.Text
                };
                Canvas.SetLeft(block, (width - titleSize.Width) / 2);
                Canvas.SetTop(block, Pad);
                canvas.Children.Add(block);
            }

            double cx = Pad + radius;
            double cy = Pad + titleBlock + contentHeight / 2;
            Brush separator = MarkdownTheme.Background;

            // 扇形：从顶部开始顺时针
            double start = -90;
            for (int i = 0; i < chart.Slices.Count; i++)
            {
                PieSlice slice = chart.Slices[i];
                double sweep = total > 0 ? slice.Value / total * 360 : 0;
                if (sweep <= 0.0001)
                    continue;

                Brush fill = Palette[i % Palette.Length];
                if (sweep >= 359.999)
                {
                    // ArcSegment 画不了整圆，单独用 Ellipse
                    var circle = new Ellipse
                    {
                        Width = pieSize,
                        Height = pieSize,
                        Fill = fill,
                        Stroke = separator,
                        StrokeThickness = 1.4
                    };
                    Canvas.SetLeft(circle, cx - radius);
                    Canvas.SetTop(circle, cy - radius);
                    canvas.Children.Add(circle);
                }
                else
                {
                    var figure = new PathFigure
                    {
                        StartPoint = new Point(cx, cy),
                        IsClosed = true,
                        IsFilled = true
                    };
                    figure.Segments.Add(new LineSegment(PointOn(cx, cy, radius, start), true));
                    figure.Segments.Add(new ArcSegment(
                        PointOn(cx, cy, radius, start + sweep),
                        new Size(radius, radius), 0, sweep > 180,
                        SweepDirection.Clockwise, true));

                    var geometry = new PathGeometry();
                    geometry.Figures.Add(figure);
                    canvas.Children.Add(new Path
                    {
                        Data = geometry,
                        Fill = fill,
                        Stroke = separator,
                        StrokeThickness = 1.4,
                        StrokeLineJoin = PenLineJoin.Round
                    });
                }

                if (sweep >= MinPercentSweep)
                {
                    string percent = Math.Round(slice.Value / total * 100)
                        .ToString("0", CultureInfo.InvariantCulture) + "%";
                    Size size = MeasureText(percent, fontSize * 0.85, FontWeights.SemiBold);
                    Point at = PointOn(cx, cy, radius * PercentInner, start + sweep / 2);

                    var label = new TextBlock
                    {
                        Text = percent,
                        FontFamily = MarkdownTheme.DiagramFamily,
                        FontSize = fontSize * 0.85,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = ContrastText(PaletteColors[i % PaletteColors.Length])
                    };
                    Canvas.SetLeft(label, at.X - size.Width / 2);
                    Canvas.SetTop(label, at.Y - size.Height / 2);
                    canvas.Children.Add(label);
                }

                start += sweep;
            }

            if (hasLegend)
            {
                double x = Pad + pieSize + PieLegendGap;
                double y = cy - legendHeight / 2;
                for (int i = 0; i < legendItems.Count; i++)
                {
                    var swatch = new Rectangle
                    {
                        Width = LegendSwatch,
                        Height = LegendSwatch,
                        Fill = Palette[i % Palette.Length],
                        RadiusX = 2.5,
                        RadiusY = 2.5
                    };
                    Canvas.SetLeft(swatch, x);
                    Canvas.SetTop(swatch, y + (legendRowHeight - LegendSwatch) / 2);
                    canvas.Children.Add(swatch);

                    Size size = MeasureText(legendItems[i], fontSize * 0.92, FontWeights.Normal);
                    var text = new TextBlock
                    {
                        Text = legendItems[i],
                        FontFamily = MarkdownTheme.DiagramFamily,
                        FontSize = fontSize * 0.92,
                        Foreground = MarkdownTheme.Markdown.Text
                    };
                    Canvas.SetLeft(text, x + LegendSwatch + LegendSwatchGap);
                    Canvas.SetTop(text, y + (legendRowHeight - size.Height) / 2);
                    canvas.Children.Add(text);

                    y += legendRowHeight;
                }
            }
        }

        private static Point PointOn(double cx, double cy, double radius, double degrees)
        {
            double rad = degrees * Math.PI / 180;
            return new Point(cx + radius * Math.Cos(rad), cy + radius * Math.Sin(rad));
        }

        private static Size MeasureText(string text, double fontSize, FontWeight weight)
        {
            var block = new TextBlock
            {
                Text = text ?? string.Empty,
                FontFamily = MarkdownTheme.DiagramFamily,
                FontSize = fontSize,
                FontWeight = weight,
                TextWrapping = TextWrapping.NoWrap
            };
            block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return block.DesiredSize;
        }

        /// <summary>按填充色的亮度挑黑字还是白字，浅黄上用白字看不清。</summary>
        private static Brush ContrastText(Color color)
        {
            double luminance = 0.299 * color.R + 0.587 * color.G + 0.114 * color.B;
            return luminance > 150 ? Brushes.Black : Brushes.White;
        }

        private static string FormatNumber(double value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static Brush[] BuildPalette()
        {
            var brushes = new Brush[PaletteColors.Length];
            for (int i = 0; i < PaletteColors.Length; i++)
            {
                var brush = new SolidColorBrush(PaletteColors[i]);
                brush.Freeze();
                brushes[i] = brush;
            }
            return brushes;
        }
    }
}
