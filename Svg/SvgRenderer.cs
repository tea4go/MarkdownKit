using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Xml;
using System.Xml.Linq;

namespace MarkdownKit.Svg
{
    /// <summary>
    /// 把 SVG 画成 WPF 的矢量 DrawingImage：Image 控件直接显示，缩放不糊。
    /// 认的是手写示意图常用的那一套——svg/g/rect/circle/ellipse/line/polyline/polygon/path/text/tspan，
    /// 样式沿树继承（fill/stroke/线宽/线帽/线接/透明度）、viewBox 等比缩放、transform。
    /// 脚本、外部引用、渐变、滤镜、裁剪、use 一律不碰：文档里的 SVG 只当静态图看。
    /// </summary>
    internal static class SvgRenderer
    {
        private const string SvgNamespace = "http://www.w3.org/2000/svg";

        private static readonly Regex NumberPattern = new Regex(
            @"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?", RegexOptions.Compiled);

        private static readonly Regex TransformPattern = new Regex(
            @"([a-zA-Z]+)\s*\(([^)]*)\)", RegexOptions.Compiled);

        private static readonly Regex WhitespacePattern = new Regex(@"\s+", RegexOptions.Compiled);

        /// <summary>解析并绘制。不是 SVG、XML 坏了、尺寸无效都返回 null，调用方据此退回占位。</summary>
        public static ImageSource TryCreate(string svgText)
        {
            if (string.IsNullOrWhiteSpace(svgText))
                return null;

            try
            {
                XDocument document = Parse(svgText);
                XElement root = document == null ? null : document.Root;
                if (root == null || root.Name.LocalName != "svg" || !IsSvgNamespace(root))
                    return null;
                return Render(root);
            }
            catch (Exception)
            {
                // SVG 来自文档、内容不可控，画不出来就当没有，别拖垮整份预览
                return null;
            }
        }

        private static XDocument Parse(string text)
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true
            };
            using (var reader = XmlReader.Create(new System.IO.StringReader(text), settings))
                return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }

        private static ImageSource Render(XElement root)
        {
            double viewX = 0, viewY = 0, viewWidth = 0, viewHeight = 0;
            bool hasViewBox = TryViewBox(root, out viewX, out viewY, out viewWidth, out viewHeight);

            double width = ViewportSize(root.Attribute("width"));
            double height = ViewportSize(root.Attribute("height"));
            if (!hasViewBox)
            {
                // 没有 viewBox：用户坐标就是像素坐标，缺尺寸按 SVG 规范的默认 300x150
                if (double.IsNaN(width)) width = 300;
                if (double.IsNaN(height)) height = 150;
                viewX = 0;
                viewY = 0;
                viewWidth = width;
                viewHeight = height;
            }
            else
            {
                if (double.IsNaN(width)) width = viewWidth;
                if (double.IsNaN(height)) height = viewHeight;
            }
            if (width <= 0 || height <= 0 || viewWidth <= 0 || viewHeight <= 0)
                return null;

            // preserveAspectRatio 默认 xMidYMid meet：等比缩到装得下，剩下的一分为二
            double scale = Math.Min(width / viewWidth, height / viewHeight);
            double offsetX = (width - viewWidth * scale) / 2 - viewX * scale;
            double offsetY = (height - viewHeight * scale) / 2 - viewY * scale;

            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                // 透明底撑满视口，Drawing 的 Bounds 才等于整张图的尺寸（Image 量自然尺寸靠它）
                dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));
                dc.PushClip(new RectangleGeometry(new Rect(0, 0, width, height)));
                dc.PushTransform(new MatrixTransform(scale, 0, 0, scale, offsetX, offsetY));
                DrawChildren(dc, root, SvgStyle.Root());
                dc.Pop();
                dc.Pop();
            }

            var image = new DrawingImage(visual.Drawing);
            image.Freeze();
            return image;
        }

        private static bool TryViewBox(XElement root, out double x, out double y, out double width,
            out double height)
        {
            x = y = width = height = 0;
            XAttribute attribute = root.Attribute("viewBox");
            if (attribute == null)
                return false;
            List<double> values = Numbers(attribute.Value);
            if (values.Count != 4)
                return false;
            x = values[0];
            y = values[1];
            width = values[2];
            height = values[3];
            return width > 0 && height > 0;
        }

        /// <summary>svg 的 width/height：纯数字或 px 才当有效，百分比得看外面容器，交给 viewBox 兜底。</summary>
        private static double ViewportSize(XAttribute attribute)
        {
            if (attribute == null)
                return double.NaN;
            string text = attribute.Value.Trim();
            if (text.EndsWith("%", StringComparison.Ordinal))
                return double.NaN;
            return Length(text, 0, double.NaN);
        }

        private static void DrawChildren(DrawingContext dc, XElement parent, SvgStyle style)
        {
            foreach (XElement child in parent.Elements())
                DrawElement(dc, child, style);
        }

        private static void DrawElement(DrawingContext dc, XElement element, SvgStyle inherited)
        {
            if (!IsSvgNamespace(element))
                return;

            string name = element.Name.LocalName;
            switch (name)
            {
                case "defs":
                case "title":
                case "desc":
                case "metadata":
                case "style":
                case "script":
                case "clipPath":
                case "mask":
                case "pattern":
                case "marker":
                case "symbol":
                case "linearGradient":
                case "radialGradient":
                case "filter":
                    return;
            }

            SvgStyle style = ApplyStyle(element, inherited);
            Matrix transform = ParseTransform(element.Attribute("transform") == null
                ? null
                : element.Attribute("transform").Value);

            bool pushTransform = !transform.IsIdentity;
            bool pushOpacity = style.LocalOpacity < 1;
            if (pushTransform)
                dc.PushTransform(new MatrixTransform(transform));
            if (pushOpacity)
                dc.PushOpacity(style.LocalOpacity);

            switch (name)
            {
                case "svg":
                case "g":
                case "a":
                    DrawChildren(dc, element, style);
                    break;
                case "line":
                    DrawLine(dc, element, style);
                    break;
                case "rect":
                    DrawRect(dc, element, style);
                    break;
                case "circle":
                    DrawCircle(dc, element, style);
                    break;
                case "ellipse":
                    DrawEllipse(dc, element, style);
                    break;
                case "polyline":
                    DrawPolyline(dc, element, style, false);
                    break;
                case "polygon":
                    DrawPolyline(dc, element, style, true);
                    break;
                case "path":
                    DrawPath(dc, element, style);
                    break;
                case "text":
                    DrawText(dc, element, style);
                    break;
            }

            if (pushOpacity)
                dc.Pop();
            if (pushTransform)
                dc.Pop();
        }

        private static void DrawLine(DrawingContext dc, XElement element, SvgStyle style)
        {
            Pen pen = CreatePen(style);
            if (pen == null)
                return;
            dc.DrawLine(pen, new Point(Number(element, "x1"), Number(element, "y1")),
                new Point(Number(element, "x2"), Number(element, "y2")));
        }

        private static void DrawRect(DrawingContext dc, XElement element, SvgStyle style)
        {
            double width = Number(element, "width");
            double height = Number(element, "height");
            if (width <= 0 || height <= 0)
                return;

            Brush fill = CreateFill(style);
            Pen pen = CreatePen(style);
            if (fill == null && pen == null)
                return;

            double rx = Number(element, "rx", -1);
            double ry = Number(element, "ry", -1);
            if (rx < 0 && ry < 0)
                rx = ry = 0;
            else if (rx < 0)
                rx = ry;
            else if (ry < 0)
                ry = rx;

            var rect = new Rect(Number(element, "x"), Number(element, "y"), width, height);
            if (rx > 0 || ry > 0)
                dc.DrawGeometry(fill, pen, new RectangleGeometry(rect, rx, ry));
            else
                dc.DrawRectangle(fill, pen, rect);
        }

        private static void DrawCircle(DrawingContext dc, XElement element, SvgStyle style)
        {
            double radius = Number(element, "r");
            if (radius <= 0)
                return;
            Brush fill = CreateFill(style);
            Pen pen = CreatePen(style);
            if (fill == null && pen == null)
                return;
            dc.DrawEllipse(fill, pen, new Point(Number(element, "cx"), Number(element, "cy")), radius, radius);
        }

        private static void DrawEllipse(DrawingContext dc, XElement element, SvgStyle style)
        {
            double rx = Number(element, "rx", -1);
            double ry = Number(element, "ry", -1);
            // 只写了一个半径时，另一个照抄
            if (rx < 0 && ry < 0)
                return;
            if (rx < 0)
                rx = ry;
            if (ry < 0)
                ry = rx;
            if (rx <= 0 || ry <= 0)
                return;
            Brush fill = CreateFill(style);
            Pen pen = CreatePen(style);
            if (fill == null && pen == null)
                return;
            dc.DrawEllipse(fill, pen, new Point(Number(element, "cx"), Number(element, "cy")), rx, ry);
        }

        private static void DrawPolyline(DrawingContext dc, XElement element, SvgStyle style, bool closed)
        {
            XAttribute attribute = element.Attribute("points");
            if (attribute == null)
                return;
            List<double> values = Numbers(attribute.Value);
            if (values.Count < 4)
                return;

            Brush fill = CreateFill(style);
            Pen pen = CreatePen(style);
            if (fill == null && pen == null)
                return;

            var points = new List<Point>(values.Count / 2);
            for (int i = 0; i + 1 < values.Count; i += 2)
                points.Add(new Point(values[i], values[i + 1]));

            var geometry = new StreamGeometry();
            using (StreamGeometryContext context = geometry.Open())
            {
                context.BeginFigure(points[0], fill != null, closed);
                points.RemoveAt(0);
                context.PolyLineTo(points, pen != null, false);
            }
            geometry.Freeze();
            dc.DrawGeometry(fill, pen, geometry);
        }

        private static void DrawPath(DrawingContext dc, XElement element, SvgStyle style)
        {
            XAttribute data = element.Attribute("d");
            if (data == null)
                return;
            Brush fill = CreateFill(style);
            Pen pen = CreatePen(style);
            if (fill == null && pen == null)
                return;

            PathGeometry geometry = ParsePath(data.Value, style.FillRule);
            if (geometry == null)
                return;
            dc.DrawGeometry(fill, pen, geometry);
        }

        // ---------- 样式 ----------

        private sealed class SvgStyle
        {
            public bool HasFill = true;
            public Color FillColor = Colors.Black;
            public double FillOpacity = 1;
            public bool HasStroke;
            public Color StrokeColor = Colors.Black;
            public double StrokeOpacity = 1;
            public double StrokeWidth = 1;
            public PenLineCap LineCap = PenLineCap.Flat;
            public PenLineJoin LineJoin = PenLineJoin.Miter;
            public FillRule FillRule = FillRule.Nonzero;
            public Color CurrentColor = Colors.Black;
            public double FontSize = 16;
            public FontFamily FontFamily = MarkdownTheme.DiagramFamily;
            public FontWeight FontWeight = FontWeights.Normal;
            public FontStyle FontStyle = FontStyles.Normal;
            public SvgTextAnchor Anchor = SvgTextAnchor.Start;

            /// <summary>元素自身的 opacity，不继承；绘制完这一层就作废。</summary>
            public double LocalOpacity = 1;

            public static SvgStyle Root()
            {
                return new SvgStyle();
            }

            public SvgStyle Clone()
            {
                var copy = (SvgStyle)MemberwiseClone();
                copy.LocalOpacity = 1;
                return copy;
            }
        }

        private enum SvgTextAnchor
        {
            Start,
            Middle,
            End
        }

        private static SvgStyle ApplyStyle(XElement element, SvgStyle parent)
        {
            SvgStyle style = parent.Clone();
            foreach (XAttribute attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration)
                    continue;
                ApplyProperty(style, attribute.Name.LocalName, attribute.Value);
            }

            // style="fill:red;stroke:blue" 覆盖 presentation attribute（CSS 优先）
            XAttribute css = element.Attribute("style");
            if (css != null)
            {
                foreach (string part in css.Value.Split(';'))
                {
                    int colon = part.IndexOf(':');
                    if (colon <= 0)
                        continue;
                    ApplyProperty(style, part.Substring(0, colon).Trim(), part.Substring(colon + 1).Trim());
                }
            }
            return style;
        }

        private static void ApplyProperty(SvgStyle style, string name, string value)
        {
            if (string.IsNullOrEmpty(name) || value == null)
                return;

            switch (name.ToLowerInvariant())
            {
                case "fill":
                    ApplyPaint(value, style.CurrentColor, out style.HasFill, out style.FillColor);
                    break;
                case "fill-opacity":
                    style.FillOpacity = Clamp(OpacityValue(value, 1), 0, 1);
                    break;
                case "stroke":
                    ApplyPaint(value, style.CurrentColor, out style.HasStroke, out style.StrokeColor);
                    break;
                case "stroke-opacity":
                    style.StrokeOpacity = Clamp(OpacityValue(value, 1), 0, 1);
                    break;
                case "stroke-width":
                    style.StrokeWidth = Math.Max(0, Length(value, style.FontSize, 1));
                    break;
                case "stroke-linecap":
                    style.LineCap = ParseLineCap(value);
                    break;
                case "stroke-linejoin":
                    style.LineJoin = ParseLineJoin(value);
                    break;
                case "fill-rule":
                    style.FillRule = value.Trim().Equals("evenodd", StringComparison.OrdinalIgnoreCase)
                        ? FillRule.EvenOdd
                        : FillRule.Nonzero;
                    break;
                case "color":
                    Color color;
                    if (TryParseColor(value, style.CurrentColor, out color))
                        style.CurrentColor = color;
                    break;
                case "font-size":
                    style.FontSize = Math.Max(0, Length(value, style.FontSize, style.FontSize));
                    break;
                case "font-family":
                    style.FontFamily = ResolveFontFamily(value, style.FontFamily);
                    break;
                case "font-weight":
                    style.FontWeight = ParseFontWeight(value);
                    break;
                case "font-style":
                    style.FontStyle = value.Trim().Equals("italic", StringComparison.OrdinalIgnoreCase)
                                      || value.Trim().Equals("oblique", StringComparison.OrdinalIgnoreCase)
                        ? FontStyles.Italic
                        : FontStyles.Normal;
                    break;
                case "text-anchor":
                    style.Anchor = ParseAnchor(value);
                    break;
                case "opacity":
                    style.LocalOpacity = Clamp(OpacityValue(value, 1), 0, 1);
                    break;
            }
        }

        private static void ApplyPaint(string value, Color current, out bool hasPaint, out Color color)
        {
            hasPaint = false;
            color = current;
            if (string.IsNullOrWhiteSpace(value))
                return;

            string text = value.Trim();
            if (text.Equals("none", StringComparison.OrdinalIgnoreCase))
                return;
            if (text.Equals("currentColor", StringComparison.OrdinalIgnoreCase))
            {
                hasPaint = true;
                color = current;
                return;
            }
            if (TryParseColor(text, current, out color))
                hasPaint = true;
        }

        private static PenLineCap ParseLineCap(string value)
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case "round":
                    return PenLineCap.Round;
                case "square":
                    return PenLineCap.Square;
                default:
                    return PenLineCap.Flat;
            }
        }

        private static PenLineJoin ParseLineJoin(string value)
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case "round":
                    return PenLineJoin.Round;
                case "bevel":
                    return PenLineJoin.Bevel;
                default:
                    return PenLineJoin.Miter;
            }
        }

        private static FontWeight ParseFontWeight(string value)
        {
            string text = value.Trim().ToLowerInvariant();
            if (text == "bold" || text == "bolder")
                return FontWeights.Bold;
            int number;
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
                return number >= 600 ? FontWeights.Bold : FontWeights.Normal;
            return FontWeights.Normal;
        }

        private static SvgTextAnchor ParseAnchor(string value)
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case "middle":
                    return SvgTextAnchor.Middle;
                case "end":
                    return SvgTextAnchor.End;
                default:
                    return SvgTextAnchor.Start;
            }
        }

        private static FontFamily ResolveFontFamily(string value, FontFamily fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
                return fallback;

            var names = new List<string>();
            foreach (string raw in value.Split(','))
            {
                string name = raw.Trim().Trim('\'', '"');
                if (name.Length == 0)
                    continue;
                switch (name.ToLowerInvariant())
                {
                    case "sans-serif":
                    case "system-ui":
                        name = "Segoe UI";
                        break;
                    case "serif":
                        name = "Times New Roman";
                        break;
                    case "monospace":
                        name = "Consolas";
                        break;
                    case "cursive":
                        name = "Comic Sans MS";
                        break;
                    case "fantasy":
                        name = "Impact";
                        break;
                }
                names.Add(name);
            }
            return names.Count == 0 ? fallback : new FontFamily(string.Join(",", names));
        }

        private static Brush CreateFill(SvgStyle style)
        {
            if (!style.HasFill)
                return null;
            return CreateBrush(style.FillColor, style.FillOpacity);
        }

        private static Pen CreatePen(SvgStyle style)
        {
            if (!style.HasStroke || style.StrokeWidth <= 0)
                return null;
            Brush brush = CreateBrush(style.StrokeColor, style.StrokeOpacity);
            if (brush == null)
                return null;
            var pen = new Pen(brush, style.StrokeWidth)
            {
                StartLineCap = style.LineCap,
                EndLineCap = style.LineCap,
                DashCap = style.LineCap,
                LineJoin = style.LineJoin,
                MiterLimit = 4
            };
            pen.Freeze();
            return pen;
        }

        private static Brush CreateBrush(Color color, double opacity)
        {
            if (opacity <= 0)
                return null;
            byte alpha = (byte)Math.Round(color.A * Clamp(opacity, 0, 1));
            if (alpha == 0)
                return null;
            var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
            brush.Freeze();
            return brush;
        }

        // ---------- 文本 ----------

        private static void DrawText(DrawingContext dc, XElement element, SvgStyle style)
        {
            if (!style.HasFill || style.FontSize <= 0)
                return;
            Brush brush = CreateFill(style);
            if (brush == null)
                return;

            var typeface = new Typeface(style.FontFamily, style.FontStyle, style.FontWeight,
                FontStretches.Normal);
            double x = Number(element, "x");
            double y = Number(element, "y");
            DrawTextNodes(dc, element, style, typeface, brush, ref x, ref y);
        }

        private static void DrawTextNodes(DrawingContext dc, XElement element, SvgStyle style,
            Typeface typeface, Brush brush, ref double x, ref double y)
        {
            foreach (XNode node in element.Nodes())
            {
                var text = node as XText;
                if (text != null)
                {
                    string content = CollapseWhitespace(text.Value);
                    if (content.Length == 0)
                        continue;

                    var formatted = new FormattedText(content, CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight, typeface, style.FontSize, brush, 1.0);
                    double width = formatted.WidthIncludingTrailingWhitespace;
                    double drawX = x;
                    if (style.Anchor == SvgTextAnchor.Middle)
                        drawX -= width / 2;
                    else if (style.Anchor == SvgTextAnchor.End)
                        drawX -= width;
                    dc.DrawText(formatted, new Point(drawX, y - formatted.Baseline));
                    x = drawX + width;
                    continue;
                }

                var child = node as XElement;
                if (child == null || child.Name.LocalName != "tspan" || !IsSvgNamespace(child))
                    continue;

                SvgStyle childStyle = ApplyStyle(child, style);
                double childX = child.Attribute("x") != null ? Number(child, "x") : x;
                double childY = child.Attribute("y") != null ? Number(child, "y") : y;
                childX += Number(child, "dx");
                childY += Number(child, "dy");
                if (!childStyle.HasFill || childStyle.FontSize <= 0)
                    continue;
                var childTypeface = new Typeface(childStyle.FontFamily, childStyle.FontStyle,
                    childStyle.FontWeight, FontStretches.Normal);
                Brush childBrush = CreateFill(childStyle);
                if (childBrush == null)
                    continue;
                DrawTextNodes(dc, child, childStyle, childTypeface, childBrush, ref childX, ref childY);
                x = childX;
                y = childY;
            }
        }

        /// <summary>XML 里的缩进和换行不是要画的内容，按 SVG 默认规则折成一个空格。</summary>
        private static string CollapseWhitespace(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            return WhitespacePattern.Replace(value, " ").Trim();
        }

        // ---------- 路径 ----------

        private static PathGeometry ParsePath(string data, FillRule fillRule)
        {
            if (string.IsNullOrWhiteSpace(data))
                return null;

            var geometry = new PathGeometry { FillRule = fillRule };
            var cursor = new PathCursor(data);
            PathFigure figure = null;
            Point current = new Point(0, 0);
            Point start = current;
            Point lastCubicControl = current;
            Point lastQuadControl = current;
            char lastCubicCommand = '\0';
            char lastQuadCommand = '\0';
            char command = '\0';

            while (true)
            {
                cursor.SkipSeparators();
                if (!cursor.HasMore)
                    break;

                char next = cursor.Peek;
                if (char.IsLetter(next))
                {
                    command = next;
                    cursor.Advance();
                }
                else if (command == '\0')
                {
                    break;
                }

                char upper = char.ToUpperInvariant(command);
                bool relative = char.IsLower(command);
                int before = cursor.Index;

                switch (upper)
                {
                    case 'M':
                    {
                        double px = cursor.ReadNumber();
                        double py = cursor.ReadNumber();
                        Point point = relative ? new Point(current.X + px, current.Y + py) : new Point(px, py);
                        if (figure == null || figure.Segments.Count > 0)
                        {
                            figure = new PathFigure { StartPoint = point, IsClosed = false };
                            geometry.Figures.Add(figure);
                        }
                        else
                        {
                            figure.StartPoint = point;
                        }
                        current = point;
                        start = point;
                        command = relative ? 'l' : 'L';
                        break;
                    }
                    case 'L':
                    {
                        double px = cursor.ReadNumber();
                        double py = cursor.ReadNumber();
                        Point point = relative ? new Point(current.X + px, current.Y + py) : new Point(px, py);
                        EnsureFigure(geometry, ref figure, current);
                        figure.Segments.Add(new LineSegment(point, true));
                        current = point;
                        break;
                    }
                    case 'H':
                    {
                        double px = cursor.ReadNumber();
                        double x = relative ? current.X + px : px;
                        Point point = new Point(x, current.Y);
                        EnsureFigure(geometry, ref figure, current);
                        figure.Segments.Add(new LineSegment(point, true));
                        current = point;
                        break;
                    }
                    case 'V':
                    {
                        double py = cursor.ReadNumber();
                        double y = relative ? current.Y + py : py;
                        Point point = new Point(current.X, y);
                        EnsureFigure(geometry, ref figure, current);
                        figure.Segments.Add(new LineSegment(point, true));
                        current = point;
                        break;
                    }
                    case 'C':
                    {
                        double x1 = cursor.ReadNumber();
                        double y1 = cursor.ReadNumber();
                        double x2 = cursor.ReadNumber();
                        double y2 = cursor.ReadNumber();
                        double x = cursor.ReadNumber();
                        double y = cursor.ReadNumber();
                        Point c1 = relative ? new Point(current.X + x1, current.Y + y1) : new Point(x1, y1);
                        Point c2 = relative ? new Point(current.X + x2, current.Y + y2) : new Point(x2, y2);
                        Point end = relative ? new Point(current.X + x, current.Y + y) : new Point(x, y);
                        EnsureFigure(geometry, ref figure, current);
                        figure.Segments.Add(new BezierSegment(c1, c2, end, true));
                        lastCubicControl = c2;
                        lastCubicCommand = upper;
                        current = end;
                        break;
                    }
                    case 'S':
                    {
                        double x2 = cursor.ReadNumber();
                        double y2 = cursor.ReadNumber();
                        double x = cursor.ReadNumber();
                        double y = cursor.ReadNumber();
                        Point c1 = lastCubicCommand == 'C' || lastCubicCommand == 'S'
                            ? new Point(2 * current.X - lastCubicControl.X, 2 * current.Y - lastCubicControl.Y)
                            : current;
                        Point c2 = relative ? new Point(current.X + x2, current.Y + y2) : new Point(x2, y2);
                        Point end = relative ? new Point(current.X + x, current.Y + y) : new Point(x, y);
                        EnsureFigure(geometry, ref figure, current);
                        figure.Segments.Add(new BezierSegment(c1, c2, end, true));
                        lastCubicControl = c2;
                        lastCubicCommand = upper;
                        current = end;
                        break;
                    }
                    case 'Q':
                    {
                        double x1 = cursor.ReadNumber();
                        double y1 = cursor.ReadNumber();
                        double x = cursor.ReadNumber();
                        double y = cursor.ReadNumber();
                        Point c1 = relative ? new Point(current.X + x1, current.Y + y1) : new Point(x1, y1);
                        Point end = relative ? new Point(current.X + x, current.Y + y) : new Point(x, y);
                        EnsureFigure(geometry, ref figure, current);
                        figure.Segments.Add(new QuadraticBezierSegment(c1, end, true));
                        lastQuadControl = c1;
                        lastQuadCommand = upper;
                        current = end;
                        break;
                    }
                    case 'T':
                    {
                        double x = cursor.ReadNumber();
                        double y = cursor.ReadNumber();
                        Point c1 = lastQuadCommand == 'Q' || lastQuadCommand == 'T'
                            ? new Point(2 * current.X - lastQuadControl.X, 2 * current.Y - lastQuadControl.Y)
                            : current;
                        Point end = relative ? new Point(current.X + x, current.Y + y) : new Point(x, y);
                        EnsureFigure(geometry, ref figure, current);
                        figure.Segments.Add(new QuadraticBezierSegment(c1, end, true));
                        lastQuadControl = c1;
                        lastQuadCommand = upper;
                        current = end;
                        break;
                    }
                    case 'A':
                    {
                        double rx = cursor.ReadNumber();
                        double ry = cursor.ReadNumber();
                        double rotation = cursor.ReadNumber();
                        bool largeArc = cursor.ReadNumber() != 0;
                        bool sweep = cursor.ReadNumber() != 0;
                        double x = cursor.ReadNumber();
                        double y = cursor.ReadNumber();
                        Point end = relative ? new Point(current.X + x, current.Y + y) : new Point(x, y);
                        EnsureFigure(geometry, ref figure, current);
                        if (rx <= 0 || ry <= 0 || end == current)
                        {
                            figure.Segments.Add(new LineSegment(end, true));
                        }
                        else
                        {
                            figure.Segments.Add(new ArcSegment(end, new Size(rx, ry), rotation, largeArc,
                                sweep ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true));
                        }
                        current = end;
                        break;
                    }
                    case 'Z':
                    {
                        if (figure != null)
                        {
                            figure.IsClosed = true;
                            figure = null;
                        }
                        current = start;
                        break;
                    }
                    default:
                        // 认不出的命令整段放弃，免得读错数字后画出乱七八糟的线
                        return geometry;
                }

                // C/S、Q/T 之外命令都打断平滑曲线的镜像控制点
                lastCubicCommand = upper == 'C' || upper == 'S' ? upper : '\0';
                lastQuadCommand = upper == 'Q' || upper == 'T' ? upper : '\0';

                if (cursor.Index == before)
                    break;
            }

            geometry.Freeze();
            return geometry;
        }

        private static void EnsureFigure(PathGeometry geometry, ref PathFigure figure, Point current)
        {
            if (figure != null)
                return;
            figure = new PathFigure { StartPoint = current, IsClosed = false };
            geometry.Figures.Add(figure);
        }

        private sealed class PathCursor
        {
            private readonly string _text;

            public PathCursor(string text)
            {
                _text = text;
            }

            public int Index;

            public bool HasMore
            {
                get { return Index < _text.Length; }
            }

            public char Peek
            {
                get { return _text[Index]; }
            }

            public void Advance()
            {
                Index++;
            }

            public void SkipSeparators()
            {
                while (Index < _text.Length &&
                       (char.IsWhiteSpace(_text[Index]) || _text[Index] == ','))
                    Index++;
            }

            public double ReadNumber()
            {
                SkipSeparators();
                int start = Index;
                if (Index < _text.Length && (_text[Index] == '+' || _text[Index] == '-'))
                    Index++;

                bool dot = false;
                while (Index < _text.Length)
                {
                    char c = _text[Index];
                    if (c >= '0' && c <= '9')
                    {
                        Index++;
                        continue;
                    }
                    if (c == '.' && !dot)
                    {
                        dot = true;
                        Index++;
                        continue;
                    }
                    if ((c == 'e' || c == 'E') && Index + 1 < _text.Length)
                    {
                        int save = Index;
                        Index++;
                        if (Index < _text.Length && (_text[Index] == '+' || _text[Index] == '-'))
                            Index++;
                        if (Index < _text.Length && char.IsDigit(_text[Index]))
                            continue;
                        Index = save;
                    }
                    break;
                }

                string token = _text.Substring(start, Index - start);
                double value;
                return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                    ? value
                    : 0;
            }
        }

        // ---------- transform ----------

        private static Matrix ParseTransform(string value)
        {
            Matrix matrix = Matrix.Identity;
            if (string.IsNullOrWhiteSpace(value))
                return matrix;

            var operations = new List<Matrix>();
            foreach (Match match in TransformPattern.Matches(value))
            {
                string name = match.Groups[1].Value.ToLowerInvariant();
                List<double> args = Numbers(match.Groups[2].Value);
                switch (name)
                {
                    case "translate":
                        if (args.Count >= 1)
                            operations.Add(new Matrix(1, 0, 0, 1, args[0], args.Count > 1 ? args[1] : 0));
                        break;
                    case "scale":
                        if (args.Count >= 1)
                        {
                            double sy = args.Count > 1 ? args[1] : args[0];
                            operations.Add(new Matrix(args[0], 0, 0, sy, 0, 0));
                        }
                        break;
                    case "rotate":
                        if (args.Count >= 1)
                        {
                            double angle = args[0] * Math.PI / 180;
                            double cos = Math.Cos(angle);
                            double sin = Math.Sin(angle);
                            double cx = args.Count > 2 ? args[1] : 0;
                            double cy = args.Count > 2 ? args[2] : 0;
                            double dx = cx - cx * cos + cy * sin;
                            double dy = cy - cx * sin - cy * cos;
                            operations.Add(new Matrix(cos, sin, -sin, cos, dx, dy));
                        }
                        break;
                    case "skewx":
                        if (args.Count >= 1)
                            operations.Add(new Matrix(1, 0, Math.Tan(args[0] * Math.PI / 180), 1, 0, 0));
                        break;
                    case "skewy":
                        if (args.Count >= 1)
                            operations.Add(new Matrix(1, Math.Tan(args[0] * Math.PI / 180), 0, 1, 0, 0));
                        break;
                    case "matrix":
                        if (args.Count >= 6)
                            operations.Add(new Matrix(args[0], args[1], args[2], args[3], args[4], args[5]));
                        break;
                }
            }

            // SVG 的变换列表从右往左作用到点上，WPF 矩阵是行向量（左乘），所以倒着累乘
            for (int i = operations.Count - 1; i >= 0; i--)
                matrix = Matrix.Multiply(matrix, operations[i]);
            return matrix;
        }

        // ---------- 数值与颜色 ----------

        private static double Number(XElement element, string name, double fallback = 0)
        {
            XAttribute attribute = element.Attribute(name);
            return attribute == null ? fallback : Number(attribute.Value, fallback);
        }

        private static double Number(string value, double fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
                return fallback;
            // x 可以是 "12 24" 这样的列表，取第一个就够（少见的多字形定位按起点画）
            Match match = NumberPattern.Match(value);
            if (!match.Success)
                return fallback;
            double result;
            return double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
                ? result
                : fallback;
        }

        /// <summary>带单位的长度（px/pt/em/...）。reference 是百分数和 em 的基准。</summary>
        private static double Length(string value, double reference, double fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
                return fallback;
            string text = value.Trim();
            if (text.EndsWith("%", StringComparison.Ordinal))
            {
                double percent = Number(text.Substring(0, text.Length - 1), double.NaN);
                return double.IsNaN(percent) ? fallback : percent / 100 * reference;
            }

            Match match = NumberPattern.Match(text);
            if (!match.Success)
                return fallback;
            double number = Number(match.Value, fallback);
            string unit = text.Substring(match.Index + match.Length).Trim().ToLowerInvariant();
            switch (unit)
            {
                case "":
                case "px":
                    return number;
                case "pt":
                    return number * 96 / 72;
                case "pc":
                    return number * 16;
                case "in":
                    return number * 96;
                case "cm":
                    return number * 96 / 2.54;
                case "mm":
                    return number * 96 / 25.4;
                case "em":
                case "rem":
                    return number * reference;
                case "ex":
                    return number * reference * 0.5;
                default:
                    return number;
            }
        }

        private static List<double> Numbers(string value)
        {
            var result = new List<double>();
            if (string.IsNullOrWhiteSpace(value))
                return result;
            foreach (Match match in NumberPattern.Matches(value))
            {
                double number;
                if (double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out number))
                    result.Add(number);
            }
            return result;
        }

        private static bool TryParseColor(string value, Color current, out Color color)
        {
            color = Colors.Black;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string text = value.Trim();
            if (text.Equals("currentColor", StringComparison.OrdinalIgnoreCase))
            {
                color = current;
                return true;
            }
            if (text.Equals("transparent", StringComparison.OrdinalIgnoreCase))
            {
                color = Colors.Transparent;
                return true;
            }

            if (text.StartsWith("#", StringComparison.Ordinal))
                return TryParseHexColor(text, out color);

            if (text.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
                return TryParseRgbColor(text, out color);

            try
            {
                object parsed = ColorConverter.ConvertFromString(text);
                if (parsed is Color)
                {
                    color = (Color)parsed;
                    return true;
                }
            }
            catch (Exception)
            {
            }
            return false;
        }

        private static bool TryParseHexColor(string text, out Color color)
        {
            color = Colors.Black;
            string hex = text.Substring(1);
            if (hex.Length == 3 || hex.Length == 4)
            {
                // #RGB / #RGBA：每位复制一遍
                var expanded = new char[hex.Length * 2];
                for (int i = 0; i < hex.Length; i++)
                {
                    expanded[i * 2] = hex[i];
                    expanded[i * 2 + 1] = hex[i];
                }
                hex = new string(expanded);
            }
            if (hex.Length != 6 && hex.Length != 8)
                return false;

            uint value;
            if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
                return false;

            byte alpha = hex.Length == 8 ? (byte)(value & 0xFF) : (byte)0xFF;
            byte red = (byte)((value >> (hex.Length == 8 ? 24 : 16)) & 0xFF);
            byte green = (byte)((value >> (hex.Length == 8 ? 16 : 8)) & 0xFF);
            byte blue = (byte)((value >> (hex.Length == 8 ? 8 : 0)) & 0xFF);
            color = Color.FromArgb(alpha, red, green, blue);
            return true;
        }

        private static bool TryParseRgbColor(string text, out Color color)
        {
            color = Colors.Black;
            List<double> values = Numbers(text);
            if (values.Count < 3)
                return false;

            // rgb(100%, 0%, 0%) 的数值是百分比，不是 0~255
            bool percent = text.IndexOf('%') >= 0;
            if (percent)
            {
                for (int i = 0; i < values.Count; i++)
                    values[i] = values[i] / 100 * (i == 3 ? 1 : 255);
            }

            byte alpha = values.Count >= 4
                ? (byte)Math.Round(Clamp(values[3], 0, 1) * 255)
                : (byte)255;
            color = Color.FromArgb(alpha,
                Channel(values[0]), Channel(values[1]), Channel(values[2]));
            return true;
        }

        private static byte Channel(double value)
        {
            return (byte)Math.Round(Clamp(value, 0, 255));
        }

        /// <summary>opacity 可以是 0~1 的小数，也可以是百分比。</summary>
        private static double OpacityValue(string value, double fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
                return fallback;
            string text = value.Trim();
            if (text.EndsWith("%", StringComparison.Ordinal))
            {
                double percent = Number(text.Substring(0, text.Length - 1), double.NaN);
                return double.IsNaN(percent) ? fallback : percent / 100;
            }
            return Number(text, fallback);
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }

        private static bool IsSvgNamespace(XElement element)
        {
            string ns = element.Name.NamespaceName;
            return ns.Length == 0 || ns == SvgNamespace;
        }
    }
}
