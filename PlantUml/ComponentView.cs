using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace MarkdownKit.PlantUml
{
    /// <summary>云朵轮廓几何。照搬官方 USymbolCloud.getSpecificFrontierForCloudNew 的算法。</summary>
    internal static class CloudGeometry
    {
        public static Geometry Build(double width, double height)
        {
            var rnd = new JavaRandom((long)width + 7919L * (long)height);
            var points = new List<Point>();

            double bubbleSize = 11;
            if (Math.Max(width, height) / bubbleSize > 16)
                bubbleSize = Math.Max(width, height) / 16;

            const double margin1 = 8;
            var a = new Point(margin1, margin1);
            var b = new Point(width - margin1, margin1);
            var c = new Point(width - margin1, height - margin1);
            var d = new Point(margin1, height - margin1);

            if (width > 100 && height > 100)
            {
                const double m2 = 7;
                SpecialLine(bubbleSize, rnd, points, MvX(a, m2), MvX(b, -m2));
                points.Add(MvY(b, m2));
                SpecialLine(bubbleSize, rnd, points, MvY(b, m2), MvY(c, -m2));
                points.Add(MvX(c, -m2));
                SpecialLine(bubbleSize, rnd, points, MvX(c, -m2), MvX(d, m2));
                points.Add(MvY(d, -m2));
                SpecialLine(bubbleSize, rnd, points, MvY(d, -m2), MvY(a, m2));
                points.Add(MvX(a, m2));
            }
            else
            {
                SpecialLine(bubbleSize, rnd, points, a, b);
                SpecialLine(bubbleSize, rnd, points, b, c);
                SpecialLine(bubbleSize, rnd, points, c, d);
                SpecialLine(bubbleSize, rnd, points, d, a);
            }

            points.Add(points[0]);

            var geo = new StreamGeometry();
            using (StreamGeometryContext ctx = geo.Open())
            {
                ctx.BeginFigure(points[0], true, true);
                for (int i = 0; i < points.Count - 1; i++)
                    AddCurve(rnd, ctx, points[i], points[i + 1]);
            }
            geo.Freeze();
            return geo;
        }

        private static Point MvX(Point p, double dx) { return new Point(p.X + dx, p.Y); }
        private static Point MvY(Point p, double dy) { return new Point(p.X, p.Y + dy); }

        private static void SpecialLine(double bubbleSize, JavaRandom rnd, List<Point> points, Point p1, Point p2)
        {
            var change = new CoordChange(p1.X, p1.Y, p2.X, p2.Y);
            double length = change.Length;
            Point middle = change.GetTrueCoordinate(length / 2, -rnd.Next(1, 1 + Math.Min(12, bubbleSize * 0.8)));
            BubbleLine(rnd, points, p1, middle, bubbleSize);
            BubbleLine(rnd, points, middle, p2, bubbleSize);
        }

        private static void BubbleLine(JavaRandom rnd, List<Point> points, Point p1, Point p2, double bubbleSize)
        {
            var change = new CoordChange(p1.X, p1.Y, p2.X, p2.Y);
            double length = change.Length;
            int nb = (int)(length / bubbleSize);
            if (nb == 0)
            {
                bubbleSize = length / 2;
                nb = (int)(length / bubbleSize);
            }
            for (int i = 0; i < nb; i++)
                points.Add(RndPoint(rnd, change.GetTrueCoordinate(i * length / nb, 0), bubbleSize * 0.2));
        }

        private static Point RndPoint(JavaRandom rnd, Point pt, double v)
        {
            return new Point(pt.X + v * rnd.NextDouble(), pt.Y + v * rnd.NextDouble());
        }

        private static void AddCurve(JavaRandom rnd, StreamGeometryContext ctx, Point p1, Point p2)
        {
            var change = new CoordChange(p1.X, p1.Y, p2.X, p2.Y);
            double length = change.Length;
            double coef = rnd.Next(.25, .35);
            Point middle = change.GetTrueCoordinate(length * coef, -length * rnd.Next(.4, .55));
            Point middle2 = change.GetTrueCoordinate(length * (1 - coef), -length * rnd.Next(.4, .55));
            ctx.BezierTo(middle, middle2, p2, true, false);
        }
    }

    /// <summary>java.util.Random 的移植——云朵形状要跟官方逐点一致，必须用同款随机序列。</summary>
    internal sealed class JavaRandom
    {
        private const long Multiplier = 0x5DEECE66DL;
        private const long Addend = 0xBL;
        private const long Mask = (1L << 48) - 1;
        private long _seed;

        public JavaRandom(long seed)
        {
            _seed = (seed ^ Multiplier) & Mask;
        }

        private int Next(int bits)
        {
            _seed = (_seed * Multiplier + Addend) & Mask;
            return (int)(_seed >> (48 - bits));
        }

        public double NextDouble()
        {
            return (((long)Next(26) << 27) + Next(27)) / (double)(1L << 53);
        }

        public double Next(double a, double b)
        {
            return NextDouble() * (b - a) + a;
        }
    }

    /// <summary>CoordinateChange 的移植：沿 p1→p2 方向 a、垂直偏移 b 取点。</summary>
    internal sealed class CoordChange
    {
        private readonly double _x1;
        private readonly double _y1;
        private readonly double _ux;
        private readonly double _uy;
        private readonly double _vx;
        private readonly double _vy;

        public CoordChange(double x1, double y1, double x2, double y2)
        {
            _x1 = x1;
            _y1 = y1;
            Length = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
            _ux = (x2 - x1) / Length;
            _uy = (y2 - y1) / Length;
            _vx = -_uy;
            _vy = _ux;
        }

        public double Length { get; private set; }

        public Point GetTrueCoordinate(double a, double b)
        {
            return new Point(_x1 + a * _ux + b * _vx, _y1 + a * _uy + b * _vy);
        }
    }

    /// <summary>
    /// 画一张组件/部署图。各形状的轮廓、留白、图标位置照搬 PlantUML 的 USymbol* 实现
    /// （src/main/java/net/sourceforge/plantuml/decoration/symbol/），常量以元素字号 14 为基准，
    /// 这里按当前字号等比换算。超过预览区宽度就整体等比缩小。
    /// </summary>
    internal sealed class ComponentView : FrameworkElement
    {
        /// <summary>两侧要扣掉的宽度：预览区左右内边距加竖直滚动条。</summary>
        private const double SideMargin = 40;

        private readonly ComponentDiagram _diagram;
        private readonly double _fontSize;
        private ComponentLayout _layout;
        private double _scale = 1;

        private readonly Brush _line;
        private readonly Brush _containerFill;
        private readonly Brush _containerBorder;
        private readonly Brush _leafFill;
        private readonly Brush _leafBorder;
        private readonly Brush _surface;
        private readonly ComponentStyle _componentStyle;

        private readonly Pen _linePen;
        private readonly Pen _containerPen;
        private readonly Pen _leafPen;
        private readonly Pen _dashPen;
        private readonly Pen _notePen;

        /// <summary>注释固定用 PlantUML 的淡黄底 + 深灰边（明暗主题下都这么画，note 是语义元素）。</summary>
        private static readonly Brush NoteFill = Color("#FEFFDD");   // 官方 note 底色
        private static readonly Brush NoteText = Color("#333333");

        private static Brush Color(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        public static readonly DependencyProperty AvailableWidthProperty =
            DependencyProperty.Register(nameof(AvailableWidth), typeof(double), typeof(ComponentView),
                new FrameworkPropertyMetadata(double.PositiveInfinity,
                    FrameworkPropertyMetadataOptions.AffectsMeasure));

        public double AvailableWidth
        {
            get { return (double)GetValue(AvailableWidthProperty); }
            set { SetValue(AvailableWidthProperty, value); }
        }

        public ComponentView(ComponentDiagram diagram, double fontSize, FrameworkElement host)
        {
            _diagram = diagram;
            _fontSize = fontSize;
            HorizontalAlignment = HorizontalAlignment.Center;
            _componentStyle = diagram.ComponentStyle;

            _line = MarkdownTheme.Markdown.Text;
            _containerFill = MarkdownTheme.Markdown.CodeBackground;
            _containerBorder = MarkdownTheme.Markdown.CodeBorder;
            _leafFill = MarkdownTheme.Background;
            _leafBorder = MarkdownTheme.Markdown.Text;
            _surface = MarkdownTheme.Background;

            _linePen = Frozen(new Pen(_line, 1.2));
            _containerPen = Frozen(new Pen(_containerBorder, 1.2));
            _leafPen = Frozen(new Pen(_leafBorder, 1.2));
            _dashPen = Frozen(new Pen(_line, 1.2) { DashStyle = DashStyles.Dash });
            _notePen = Frozen(new Pen(Color("#888888"), 1));

            if (host != null)
            {
                if (host.ActualWidth > 0)
                    AvailableWidth = host.ActualWidth;
                BindingOperations.SetBinding(this, AvailableWidthProperty,
                    new Binding("ActualWidth") { Source = host });
            }
        }

        private static Pen Frozen(Pen pen)
        {
            pen.Freeze();
            return pen;
        }

        /// <summary>PlantUML 元素字号是 14，形状常量按当前字号等比换算。</summary>
        private double U(double v)
        {
            return v * _fontSize / 14.0;
        }

        private void EnsureLayout()
        {
            if (_layout != null)
                return;
            double ppd = 1.0;
            try
            {
                ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            }
            catch (Exception)
            {
                ppd = 1.0;
            }
            _layout = ComponentLayoutBuilder.Build(_diagram, _fontSize, ppd);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            EnsureLayout();

            double limit = double.PositiveInfinity;
            if (!double.IsInfinity(availableSize.Width))
                limit = availableSize.Width;
            if (!double.IsInfinity(AvailableWidth))
                limit = Math.Min(limit, Math.Max(0, AvailableWidth - SideMargin));

            double width = _layout.Width;
            double height = _layout.Height;
            if (!double.IsInfinity(limit) && limit > 0 && width > limit)
            {
                _scale = limit / width;
                return new Size(limit, height * _scale);
            }
            _scale = 1;
            return new Size(width, height);
        }

        protected override void OnRender(DrawingContext dc)
        {
            EnsureLayout();
            if (_scale != 1)
                dc.PushTransform(new ScaleTransform(_scale, _scale));

            foreach (ElementGeom geom in _layout.Elements)
            {
                if (geom.Source.IsContainer)
                    DrawContainer(dc, geom);
                else
                    DrawLeaf(dc, geom);
            }

            DrawEdges(dc);
            DrawNotes(dc);
            DrawLegend(dc);

            if (_layout.Title != null)
            {
                FormattedText title = _layout.Title.Text;
                dc.DrawText(title, new Point(_layout.Title.CenterX - title.Width / 2, _layout.Title.Top));
            }

            if (_scale != 1)
                dc.Pop();
        }

        // ── 容器 ────────────────────────────────────────────────

        private void DrawContainer(DrawingContext dc, ElementGeom geom)
        {
            Element e = geom.Source;
            if (e.Transparent)
                return;   // together 只是个分组，不画框

            if (geom.Compartments.Count > 0 || ClassBox.Is(e.Kind))
            {
                DrawClassBox(dc, geom);
                return;
            }

            switch (e.Kind)
            {
                case ElementKind.Node:
                    DrawNodeShape(dc, geom.Rect, _containerFill, _containerPen);
                    DrawCenteredTop(dc, geom.Text, geom.Rect, U(13));
                    break;
                case ElementKind.Component:
                    DrawComponentShape(dc, geom.Rect);
                    DrawCenteredTop(dc, geom.Text, geom.Rect, U(13));
                    break;
                case ElementKind.Frame:
                    DrawFrameShape(dc, geom);
                    break;
                case ElementKind.Package:
                case ElementKind.Folder:
                    DrawFolderShape(dc, geom);
                    break;
                case ElementKind.Cloud:
                    DrawCloudShape(dc, geom);
                    break;
                case ElementKind.Database:
                    DrawCylinder(dc, geom.Rect);
                    DrawCenteredTop(dc, geom.Text, geom.Rect, U(22));
                    break;
                case ElementKind.Queue:
                    DrawQueueShape(dc, geom.Rect);
                    DrawCenteredTop(dc, geom.Text, geom.Rect, U(5));
                    break;
                default:
                    dc.DrawRectangle(_containerFill, _containerPen, geom.Rect);
                    DrawCenteredTop(dc, geom.Text, geom.Rect, U(2));
                    break;
            }
        }

        /// <summary>类 / ER 框：名称一格 + 下面若干隔间，之间画分隔线；名称左边画个圆标。</summary>
        private void DrawClassBox(DrawingContext dc, ElementGeom geom)
        {
            Rect r = geom.Rect;
            dc.DrawRectangle(_containerFill, _containerPen, r);

            // 名称那一格
            double nameBand = geom.NameBand > 0 ? geom.NameBand : r.Height;
            var nameRect = new Rect(r.X, r.Y, r.Width, nameBand);
            double iconW = geom.Icon == null ? 0 : U(24);
            if (geom.Icon != null)
                DrawClassIcon(dc, geom, new Point(r.X + U(8) + U(10.5), r.Y + nameBand / 2));
            if (geom.Text != null)
            {
                var area = new Rect(r.X + iconW, r.Y, r.Width - iconW, nameBand);
                dc.DrawText(geom.Text, new Point(
                    area.X + (area.Width - geom.Text.Width) / 2,
                    area.Y + (area.Height - geom.Text.Height) / 2));
            }

            // 隔间
            bool isEr = geom.Source.Kind == ElementKind.ErEntity;
            double y = r.Y + nameBand;
            for (int i = 0; i < geom.Compartments.Count; i++)
            {
                dc.DrawLine(_containerPen, new Point(r.X, y), new Point(r.Right, y));
                double padY = U(5);
                double ty = y + padY;

                // 类图里只要这一段有成员写了可见性记号，整段都留出记号的位置，
                // 这样有没有写 `+` / `-` 的成员都左对齐（官方也是这么排的）
                bool anyMark = false;
                if (!isEr)
                {
                    foreach (FormattedText l in geom.Compartments[i])
                    {
                        if (l.Text.Length > 0 && IsVisibility(l.Text[0]))
                        {
                            anyMark = true;
                            break;
                        }
                    }
                }

                for (int li = 0; li < geom.Compartments[i].Count; li++)
                {
                    FormattedText line = geom.Compartments[i][li];
                    // 隔间文字从框左边起排，不带名称那格的图标缩进
                    double tx = r.X + U(8);
                    string raw = line.Text;
                    double bullet = 0;
                    if (isEr)
                    {
                        // ER：主键前面画圆点，非主键不画但照样留位，所有字段左对齐
                        bool pk = i < geom.Bullets.Count && li < geom.Bullets[i].Count && geom.Bullets[i][li];
                        if (pk)
                        {
                            double s = U(3.4);
                            dc.DrawEllipse(_line, null, new Point(tx + U(4), ty + line.Height / 2), s, s);
                        }
                        bullet = U(12);
                    }
                    else if (raw.Length > 0 && IsVisibility(raw[0]))
                    {
                        DrawVisibility(dc, raw[0], new Point(tx + U(4), ty + line.Height / 2));
                        bullet = U(14);
                    }
                    else if (anyMark)
                    {
                        bullet = U(14);
                    }
                    dc.DrawText(line, new Point(tx + bullet, ty));
                    ty += line.Height;
                }
                double h = padY * 2;
                foreach (FormattedText line in geom.Compartments[i])
                    h += line.Height;
                y += h;
            }
        }

        private static bool IsVisibility(char c)
        {
            return c == '+' || c == '-' || c == '#' || c == '~';
        }

        /// <summary>名称左边的圆标（C / I / E / A），颜色按种类区分。</summary>
        private void DrawClassIcon(DrawingContext dc, ElementGeom geom, Point center)
        {
            // 官方 14 号字下量出来是直径 21，描边用深色（不是框线那种浅色）
            double r = U(10.5);
            Brush fill = ClassIconFill(geom.Source.Kind);
            dc.DrawEllipse(fill, _linePen, center, r, r);
            var ft = new FormattedText(geom.Icon, System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight, new Typeface(MarkdownTheme.DiagramFamily, FontStyles.Normal,
                    FontWeights.Bold, FontStretches.Normal), r * 1.3, _line, 1.0);
            dc.DrawText(ft, new Point(center.X - ft.Width / 2, center.Y - ft.Height / 2));
        }

        private static Brush ClassIconFill(ElementKind kind)
        {
            switch (kind)
            {
                case ElementKind.Interface:
                    return Color("#C9B7E8");
                case ElementKind.Enum:
                case ElementKind.ErEntity:
                    return Color("#B5D99C");
                default:
                    return Color("#B5D99C");
            }
        }

        /// <summary>可见性记号：+ 绿圆、- 红方、# 黄菱、~ 蓝三角（跟官方一致）。</summary>
        private void DrawVisibility(DrawingContext dc, char mark, Point center)
        {
            double s = U(4);
            switch (mark)
            {
                case '+':
                    dc.DrawEllipse(Color("#5BA85B"), null, center, s, s);
                    break;
                case '-':
                    dc.DrawRectangle(Color("#D05A5A"), null,
                        new Rect(center.X - s, center.Y - s, s * 2, s * 2));
                    break;
                case '#':
                    dc.DrawGeometry(Color("#D0A93F"), null, Diamond(center, s));
                    break;
                default:
                    dc.DrawGeometry(Color("#5B87C4"), null, Triangle(center, s));
                    break;
            }
        }

        private static Geometry Diamond(Point c, double s)
        {
            var g = new StreamGeometry();
            using (StreamGeometryContext ctx = g.Open())
            {
                ctx.BeginFigure(new Point(c.X, c.Y - s), true, true);
                ctx.LineTo(new Point(c.X + s, c.Y), true, false);
                ctx.LineTo(new Point(c.X, c.Y + s), true, false);
                ctx.LineTo(new Point(c.X - s, c.Y), true, false);
            }
            g.Freeze();
            return g;
        }

        private static Geometry Triangle(Point c, double s)
        {
            var g = new StreamGeometry();
            using (StreamGeometryContext ctx = g.Open())
            {
                ctx.BeginFigure(new Point(c.X, c.Y - s), true, true);
                ctx.LineTo(new Point(c.X + s, c.Y + s), true, false);
                ctx.LineTo(new Point(c.X - s, c.Y + s), true, false);
            }
            g.Freeze();
            return g;
        }

        /// <summary>node：矩形切掉左上、右下两个角，内部三条折线勾出立体感（USymbolNode.drawNode）。</summary>
        private void DrawNodeShape(DrawingContext dc, Rect r, Brush fill, Pen pen)
        {
            double d = U(10);
            var poly = new StreamGeometry();
            using (StreamGeometryContext ctx = poly.Open())
            {
                ctx.BeginFigure(new Point(r.X, r.Y + d), true, true);
                ctx.LineTo(new Point(r.X + d, r.Y), true, false);
                ctx.LineTo(new Point(r.Right, r.Y), true, false);
                ctx.LineTo(new Point(r.Right, r.Bottom - d), true, false);
                ctx.LineTo(new Point(r.Right - d, r.Bottom), true, false);
                ctx.LineTo(new Point(r.X, r.Bottom), true, false);
            }
            poly.Freeze();
            dc.DrawGeometry(fill, pen, poly);

            dc.DrawLine(pen, new Point(r.Right - d, r.Y + d), new Point(r.Right, r.Y));
            dc.DrawLine(pen, new Point(r.X, r.Y + d), new Point(r.Right - d, r.Y + d));
            dc.DrawLine(pen, new Point(r.Right - d, r.Y + d), new Point(r.Right - d, r.Bottom - d));
        }

        /// <summary>component：按 skinparam componentStyle 画（UML2 默认带右上角图标，UML1 左右凸耳，rectangle 纯矩形）。</summary>
        private void DrawComponentShape(DrawingContext dc, Rect r)
        {
            switch (_componentStyle)
            {
                case ComponentStyle.Rectangle:
                    dc.DrawRoundedRectangle(_leafFill, _leafPen, r, U(2.5), U(2.5));
                    return;
                case ComponentStyle.Uml1:
                    dc.DrawRoundedRectangle(_leafFill, _leafPen, r, U(2.5), U(2.5));
                    dc.DrawRectangle(_leafFill, _leafPen, new Rect(r.X - U(5), r.Y + U(5), U(10), U(5)));
                    dc.DrawRectangle(_leafFill, _leafPen, new Rect(r.X - U(5), r.Bottom - U(10), U(10), U(5)));
                    return;
                default:
                    dc.DrawRoundedRectangle(_leafFill, _leafPen, r, U(2.5), U(2.5));
                    dc.DrawRectangle(_leafFill, _leafPen, new Rect(r.Right - U(20), r.Y + U(5), U(15), U(10)));
                    dc.DrawRectangle(_leafFill, _leafPen, new Rect(r.Right - U(22), r.Y + U(7), U(4), U(2)));
                    dc.DrawRectangle(_leafFill, _leafPen, new Rect(r.Right - U(22), r.Y + U(11), U(4), U(2)));
                    return;
            }
        }

        /// <summary>frame：矩形 + 左上角折角标题页（USymbolFrame）。</summary>
        private void DrawFrameShape(DrawingContext dc, ElementGeom geom)
        {
            Rect r = geom.Rect;
            dc.DrawRectangle(_containerFill, _containerPen, r);

            FormattedText text = geom.Text;
            double textWidth = text.Width + U(10);
            double corner = U(10);
            double textHeight = text.Height + U(3);

            var path = new StreamGeometry();
            using (StreamGeometryContext ctx = path.Open())
            {
                ctx.BeginFigure(new Point(r.X + textWidth, r.Y), false, false);
                ctx.LineTo(new Point(r.X + textWidth, r.Y + textHeight - corner), true, false);
                ctx.LineTo(new Point(r.X + textWidth - corner, r.Y + textHeight), true, false);
                ctx.LineTo(new Point(r.X, r.Y + textHeight), true, false);
            }
            path.Freeze();
            dc.DrawGeometry(null, _containerPen, path);
            dc.DrawText(text, new Point(r.X + U(3), r.Y + U(1)));
        }

        /// <summary>package / folder：左上角带斜边页签、四角圆角的文件夹（USymbolFolder.drawFolder，roundCorner=5）。</summary>
        private void DrawFolderShape(DrawingContext dc, ElementGeom geom)
        {
            Rect r = geom.Rect;
            FormattedText text = geom.Text;
            bool hasTitle = r.Width > 0 && text != null;
            double tabW = hasTitle ? text.Width + U(6) : Math.Max(U(30), r.Width / 4);
            double tabH = hasTitle ? text.Height + U(6) : U(10);
            double slant = U(7);
            double rc = U(2.5);      // 主体圆角（官方 roundCorner/2）
            double rct = U(3.75);    // 页签右上圆角（官方 roundCorner/2*1.5）

            var poly = new StreamGeometry();
            using (StreamGeometryContext ctx = poly.Open())
            {
                ctx.BeginFigure(new Point(r.X + rc, r.Y), true, true);
                ctx.LineTo(new Point(r.X + tabW - rct, r.Y), true, false);
                Arc(ctx, new Point(r.X + tabW, r.Y + rct), rct);
                ctx.LineTo(new Point(r.X + tabW + slant, r.Y + tabH), true, false);
                ctx.LineTo(new Point(r.Right - rc, r.Y + tabH), true, false);
                Arc(ctx, new Point(r.Right, r.Y + tabH + rc), rc);
                ctx.LineTo(new Point(r.Right, r.Bottom - rc), true, false);
                Arc(ctx, new Point(r.Right - rc, r.Bottom), rc);
                ctx.LineTo(new Point(r.X + rc, r.Bottom), true, false);
                Arc(ctx, new Point(r.X, r.Bottom - rc), rc);
                ctx.LineTo(new Point(r.X, r.Y + rc), true, false);
            }
            poly.Freeze();
            dc.DrawGeometry(_containerFill, _containerPen, poly);
            dc.DrawLine(_containerPen, new Point(r.X, r.Y + tabH), new Point(r.X + tabW + slant, r.Y + tabH));
            dc.DrawText(text, new Point(r.X + U(4), r.Y + U(3)));
        }

        /// <summary>画一段 90° 圆角弧（官方 svg A 命令，sweep=1 顺时针）。</summary>
        private static void Arc(StreamGeometryContext ctx, Point to, double radius)
        {
            ctx.ArcTo(to, new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
        }

        /// <summary>cloud：照搬官方 USymbolCloud 的算法（确定性随机 + 泡泡边）勾云朵轮廓。</summary>
        private void DrawCloudShape(DrawingContext dc, ElementGeom geom)
        {
            Rect r = geom.Rect;
            Geometry cloud = CloudGeometry.Build(r.Width, r.Height);
            dc.PushTransform(new TranslateTransform(r.X, r.Y));
            dc.DrawGeometry(_containerFill, _containerPen, cloud);
            dc.Pop();
            // 名称在云朵内居中；有构造型时排在构造型下方
            double nameTop = r.Y + U(15);
            if (geom.Stereo != null)
                nameTop += geom.Stereo.Height + U(2);
            else
                nameTop = r.Y + (r.Height - geom.Text.Height) / 2;
            dc.DrawText(geom.Text, new Point(r.X + (r.Width - geom.Text.Width) / 2, nameTop));
        }

        // ── 叶子 ────────────────────────────────────────────────

        /// <summary>画构造型（«xxx»，斜体）在名称上方居中，返回名称要下移的距离。</summary>
        private double DrawStereo(DrawingContext dc, ElementGeom geom, Rect r, double topPad)
        {
            if (geom.Stereo == null)
                return 0;
            dc.DrawText(geom.Stereo, new Point(r.X + (r.Width - geom.Stereo.Width) / 2, r.Y + topPad));
            return geom.Stereo.Height + U(2);
        }

        private void DrawLeaf(DrawingContext dc, ElementGeom geom)
        {
            Element e = geom.Source;
            Rect r = geom.Rect;
            double x1, x2, y1, y2;
            MarginOf(e.Kind, out x1, out x2, out y1, out y2);
            double sdy = geom.Stereo == null ? 0 : geom.Stereo.Height + U(2);

            switch (e.Kind)
            {
                case ElementKind.Node:
                    DrawNodeShape(dc, r, _leafFill, _leafPen);
                    DrawTextAt(dc, geom.Text, r.X + U(x1), r.Y + U(y1) + sdy);
                    break;
                case ElementKind.Frame:
                    DrawFrameShape(dc, geom);
                    break;
                case ElementKind.Package:
                case ElementKind.Folder:
                    DrawFolderShape(dc, geom);
                    break;
                case ElementKind.Component:
                    DrawComponentShape(dc, r);
                    DrawTextAt(dc, geom.Text, r.X + U(x1), r.Y + U(y1) + sdy);
                    break;
                case ElementKind.Cloud:
                    DrawCloudShape(dc, geom);
                    break;
                case ElementKind.Database:
                    DrawCylinder(dc, r);
                    DrawTextAt(dc, geom.Text, r.X + U(x1), r.Y + U(y1) + sdy);
                    break;
                case ElementKind.Queue:
                    DrawQueueShape(dc, r);
                    DrawTextAt(dc, geom.Text, r.X + U(x1), r.Y + U(y1) + sdy);
                    break;
                case ElementKind.Actor:
                case ElementKind.Person:
                    DrawActor(dc, geom);
                    break;
                case ElementKind.Usecase:
                    DrawEllipseShape(dc, r);
                    DrawCentered(dc, geom.Text, r);
                    break;
                case ElementKind.Interface:
                case ElementKind.Circle:
                    DrawInterface(dc, geom);
                    break;
                case ElementKind.Hexagon:
                    DrawHexagon(dc, geom);
                    break;
                case ElementKind.Storage:
                    dc.DrawRoundedRectangle(_leafFill, _leafPen, r, U(70), U(70));
                    DrawTextAt(dc, geom.Text, r.X + U(x1), r.Y + U(y1) + sdy);
                    break;
                case ElementKind.Stack:
                    DrawStackShape(dc, r);
                    DrawTextAt(dc, geom.Text, r.X + U(x1), r.Y + U(y1) + sdy);
                    break;
                case ElementKind.Collections:
                    DrawCollectionsShape(dc, r);
                    DrawTextAt(dc, geom.Text, r.X + U(x1) - U(2), r.Y + U(y1) - U(2));
                    break;
                case ElementKind.Card:
                    dc.DrawRoundedRectangle(_leafFill, _leafPen, r, U(2.5), U(2.5));
                    DrawTextAt(dc, geom.Text, r.X + U(x1), r.Y + U(y1) + sdy);
                    break;
                case ElementKind.File:
                    DrawFileShape(dc, r);
                    DrawTextAt(dc, geom.Text, r.X + U(x1), r.Y + U(y1) + sdy);
                    break;
                case ElementKind.Artifact:
                    DrawArtifactShape(dc, r);
                    DrawTextAt(dc, geom.Text, r.X + U(x1), r.Y + U(y1) + sdy);
                    break;
                case ElementKind.Action:
                case ElementKind.Process:
                    DrawPointedShape(dc, r, e.Kind == ElementKind.Process);
                    DrawTextAt(dc, geom.Text, r.X + U(x1), r.Y + U(y1) + sdy);
                    break;
                default:
                    dc.DrawRoundedRectangle(_leafFill, _leafPen, r, U(2.5), U(2.5));
                    DrawTextAt(dc, geom.Text, r.X + U(x1), r.Y + U(y1) + sdy);
                    break;
            }

            // 构造型最后画，免得被形状的填充盖住
            DrawStereo(dc, geom, r, U(y1));
        }

        /// <summary>database：上下各一个碗口（半径 10）的圆柱（USymbolDatabase）。</summary>
        private void DrawCylinder(DrawingContext dc, Rect r)
        {
            double b = U(10);
            double cx = r.X + r.Width / 2;

            var body = new StreamGeometry();
            using (StreamGeometryContext ctx = body.Open())
            {
                ctx.BeginFigure(new Point(r.X, r.Y + b), true, true);
                ctx.BezierTo(new Point(r.X, r.Y), new Point(cx, r.Y), new Point(cx, r.Y), true, false);
                ctx.BezierTo(new Point(cx, r.Y), new Point(r.Right, r.Y), new Point(r.Right, r.Y + b), true, false);
                ctx.LineTo(new Point(r.Right, r.Bottom - b), true, false);
                ctx.BezierTo(new Point(r.Right, r.Bottom), new Point(cx, r.Bottom), new Point(cx, r.Bottom), true, false);
                ctx.BezierTo(new Point(cx, r.Bottom), new Point(r.X, r.Bottom), new Point(r.X, r.Bottom - b), true, false);
            }
            body.Freeze();
            dc.DrawGeometry(_leafFill, _leafPen, body);

            // 顶面那条封口弧
            var lid = new StreamGeometry();
            using (StreamGeometryContext ctx = lid.Open())
            {
                ctx.BeginFigure(new Point(r.X, r.Y + b), false, false);
                ctx.BezierTo(new Point(r.X, r.Y + 2 * b), new Point(cx, r.Y + 2 * b), new Point(cx, r.Y + 2 * b), true, false);
                ctx.BezierTo(new Point(cx, r.Y + 2 * b), new Point(r.Right, r.Y + 2 * b), new Point(r.Right, r.Y + b), true, false);
            }
            lid.Freeze();
            dc.DrawGeometry(null, _leafPen, lid);
        }

        /// <summary>queue：左右两端圆头的竖长胶囊，右侧再补一条内凹弧（USymbolQueue，dx=5）。</summary>
        private void DrawQueueShape(DrawingContext dc, Rect r)
        {
            double dx = U(5);
            double midY = r.Y + r.Height / 2;

            var body = new StreamGeometry();
            using (StreamGeometryContext ctx = body.Open())
            {
                ctx.BeginFigure(new Point(r.X + dx, r.Y), true, true);
                ctx.LineTo(new Point(r.Right - dx, r.Y), true, false);
                ctx.BezierTo(new Point(r.Right, r.Y), new Point(r.Right, midY), new Point(r.Right, midY), true, false);
                ctx.BezierTo(new Point(r.Right, midY), new Point(r.Right, r.Bottom), new Point(r.Right - dx, r.Bottom), true, false);
                ctx.LineTo(new Point(r.X + dx, r.Bottom), true, false);
                ctx.BezierTo(new Point(r.X, r.Bottom), new Point(r.X, midY), new Point(r.X, midY), true, false);
                ctx.BezierTo(new Point(r.X, midY), new Point(r.X, r.Y), new Point(r.X + dx, r.Y), true, false);
            }
            body.Freeze();
            dc.DrawGeometry(_leafFill, _leafPen, body);

            var inner = new StreamGeometry();
            using (StreamGeometryContext ctx = inner.Open())
            {
                ctx.BeginFigure(new Point(r.Right - dx, r.Y), false, false);
                ctx.BezierTo(new Point(r.Right - 2 * dx, r.Y), new Point(r.Right - 2 * dx, midY), new Point(r.Right - 2 * dx, midY), true, false);
                ctx.BezierTo(new Point(r.Right - 2 * dx, r.Bottom), new Point(r.Right - dx, r.Bottom), new Point(r.Right - dx, r.Bottom), true, false);
            }
            inner.Freeze();
            dc.DrawGeometry(null, _leafPen, inner);
        }

        /// <summary>stack：中间一层矩形 + 外轮廓（USymbolStack，border=15）。</summary>
        private void DrawStackShape(DrawingContext dc, Rect r)
        {
            double border = U(15);
            dc.DrawRectangle(_leafFill, _leafPen,
                new Rect(r.X + border, r.Y, Math.Max(0, r.Width - 2 * border), r.Height));

            var path = new StreamGeometry();
            using (StreamGeometryContext ctx = path.Open())
            {
                ctx.BeginFigure(new Point(r.X, r.Y), false, false);
                ctx.LineTo(new Point(r.X + border, r.Y), true, false);
                ctx.LineTo(new Point(r.X + border, r.Bottom), true, false);
                ctx.LineTo(new Point(r.Right - border, r.Bottom), true, false);
                ctx.LineTo(new Point(r.Right - border, r.Y), true, false);
                ctx.LineTo(new Point(r.Right, r.Y), true, false);
            }
            path.Freeze();
            dc.DrawGeometry(null, _leafPen, path);
        }

        /// <summary>collections：右下、左上两层错开的矩形（USymbolCollections，delta=4）。</summary>
        private void DrawCollectionsShape(DrawingContext dc, Rect r)
        {
            double d = U(4);
            dc.DrawRectangle(_leafFill, _leafPen,
                new Rect(r.X + d, r.Y + d, Math.Max(0, r.Width - d), Math.Max(0, r.Height - d)));
            dc.DrawRectangle(_leafFill, _leafPen,
                new Rect(r.X, r.Y, Math.Max(0, r.Width - d), Math.Max(0, r.Height - d)));
        }

        /// <summary>file：右上角折角的纸（USymbolFile，cornersize=10）。</summary>
        private void DrawFileShape(DrawingContext dc, Rect r)
        {
            double c = U(10);
            var poly = new StreamGeometry();
            using (StreamGeometryContext ctx = poly.Open())
            {
                ctx.BeginFigure(new Point(r.X, r.Y), true, true);
                ctx.LineTo(new Point(r.X, r.Bottom), true, false);
                ctx.LineTo(new Point(r.Right, r.Bottom), true, false);
                ctx.LineTo(new Point(r.Right, r.Y + c), true, false);
                ctx.LineTo(new Point(r.Right - c, r.Y), true, false);
            }
            poly.Freeze();
            dc.DrawGeometry(_leafFill, _leafPen, poly);

            dc.DrawLine(_leafPen, new Point(r.Right - c, r.Y), new Point(r.Right - c, r.Y + c));
            dc.DrawLine(_leafPen, new Point(r.Right - c, r.Y + c), new Point(r.Right, r.Y + c));
        }

        /// <summary>artifact：矩形 + 右上角卷角文件图标（USymbolArtifact，12×14 卷角 6）。</summary>
        private void DrawArtifactShape(DrawingContext dc, Rect r)
        {
            dc.DrawRectangle(_leafFill, _leafPen, r);

            double sw = U(12);
            double sh = U(14);
            double c = U(6);
            double x = r.Right - sw - U(5);
            double y = r.Y + U(5);

            var poly = new StreamGeometry();
            using (StreamGeometryContext ctx = poly.Open())
            {
                ctx.BeginFigure(new Point(x, y), true, true);
                ctx.LineTo(new Point(x, y + sh), true, false);
                ctx.LineTo(new Point(x + sw, y + sh), true, false);
                ctx.LineTo(new Point(x + sw, y + c), true, false);
                ctx.LineTo(new Point(x + sw - c, y), true, false);
            }
            poly.Freeze();
            dc.DrawGeometry(_leafFill, _leafPen, poly);
            dc.DrawLine(_leafPen, new Point(x + sw - c, y), new Point(x + sw - c, y + c));
            dc.DrawLine(_leafPen, new Point(x + sw - c, y + c), new Point(x + sw, y + c));
        }

        private void DrawEllipseShape(DrawingContext dc, Rect r)
        {
            dc.DrawEllipse(_leafFill, _leafPen,
                new Point(r.X + r.Width / 2, r.Y + r.Height / 2), r.Width / 2, r.Height / 2);
        }

        /// <summary>action / process：右侧尖角（process 左侧也尖）的六边形（USymbolAction / USymbolProcess）。</summary>
        private void DrawPointedShape(DrawingContext dc, Rect r, bool both)
        {
            double d = U(10);
            var path = new StreamGeometry();
            using (StreamGeometryContext ctx = path.Open())
            {
                ctx.BeginFigure(new Point(r.X, both ? r.Y + r.Height / 2 : r.Y), true, true);
                ctx.LineTo(new Point(r.X, r.Y), true, false);
                ctx.LineTo(new Point(r.Right - d, r.Y), true, false);
                ctx.LineTo(new Point(r.Right, r.Y + r.Height / 2), true, false);
                ctx.LineTo(new Point(r.Right - d, r.Bottom), true, false);
                ctx.LineTo(new Point(r.X, r.Bottom), true, false);
            }
            path.Freeze();
            dc.DrawGeometry(_leafFill, _leafPen, path);
        }

        /// <summary>interface / circle：小圆点在上，名字在圆下方（棒棒糖记法）。</summary>
        private void DrawInterface(DrawingContext dc, ElementGeom geom)
        {
            Rect r = geom.Rect;
            double diam = U(18);
            double cx = r.X + r.Width / 2;
            double cy = r.Y + U(10) + diam / 2;
            dc.DrawEllipse(_leafFill, _leafPen, new Point(cx, cy), diam / 2, diam / 2);
            if (geom.Text != null)
                dc.DrawText(geom.Text, new Point(cx - geom.Text.Width / 2, r.Y + U(10) + diam + U(4)));
        }

        /// <summary>hexagon：左右各切掉 1/8 宽（USymbolHexagon）。</summary>
        private void DrawHexagon(DrawingContext dc, ElementGeom geom)
        {
            Rect r = geom.Rect;
            double dx = r.Width / 8;
            var hex = new StreamGeometry();
            using (StreamGeometryContext ctx = hex.Open())
            {
                ctx.BeginFigure(new Point(r.X, r.Y + r.Height / 2), true, true);
                ctx.LineTo(new Point(r.X + dx, r.Y), true, false);
                ctx.LineTo(new Point(r.Right - dx, r.Y), true, false);
                ctx.LineTo(new Point(r.Right, r.Y + r.Height / 2), true, false);
                ctx.LineTo(new Point(r.Right - dx, r.Bottom), true, false);
                ctx.LineTo(new Point(r.X + dx, r.Bottom), true, false);
            }
            hex.Freeze();
            dc.DrawGeometry(_leafFill, _leafPen, hex);
            DrawCentered(dc, geom.Text, r);
        }

        /// <summary>actor / person：火柴人（USymbolActor，头 16、身 27、臂 13、腿 15），名字在脚下。</summary>
        private void DrawActor(DrawingContext dc, ElementGeom geom)
        {
            Rect r = geom.Rect;
            FormattedText text = geom.Text;
            double cx = r.X + r.Width / 2;
            double top = r.Y;
            double head = U(16);
            double body = U(27);
            double arm = U(13);
            double legY = U(15);

            double headBottom = top + head;
            double bodyBottom = headBottom + body;
            double armsY = headBottom + U(8);

            dc.DrawEllipse(_leafFill, _linePen, new Point(cx, top + head / 2), head / 2, head / 2);
            dc.DrawLine(_linePen, new Point(cx, headBottom), new Point(cx, bodyBottom));
            dc.DrawLine(_linePen, new Point(cx - arm, armsY), new Point(cx + arm, armsY));
            dc.DrawLine(_linePen, new Point(cx, bodyBottom), new Point(cx - arm, bodyBottom + legY));
            dc.DrawLine(_linePen, new Point(cx, bodyBottom), new Point(cx + arm, bodyBottom + legY));

            if (text != null)
                dc.DrawText(text, new Point(cx - text.Width / 2, r.Bottom - text.Height));
        }

        // ── 文字 ────────────────────────────────────────────────

        /// <summary>与布局里的 Margin 保持一致，供文字定位用。</summary>
        private void MarginOf(ElementKind kind, out double x1, out double x2, out double y1, out double y2)
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
                default:
                    x1 = 10; x2 = 10; y1 = 10; y2 = 10;
                    return;
            }
        }

        private void DrawTextAt(DrawingContext dc, FormattedText text, double x, double y)
        {
            if (text != null)
                dc.DrawText(text, new Point(x, y));
        }

        private void DrawCentered(DrawingContext dc, FormattedText text, Rect area)
        {
            if (text == null)
                return;
            dc.DrawText(text, new Point(
                area.X + (area.Width - text.Width) / 2,
                area.Y + (area.Height - text.Height) / 2));
        }

        private void DrawCenteredTop(DrawingContext dc, FormattedText text, Rect area, double top)
        {
            if (text == null)
                return;
            dc.DrawText(text, new Point(area.X + (area.Width - text.Width) / 2, area.Y + top));
        }

        // ── 关系边 ──────────────────────────────────────────────

        private void DrawEdges(DrawingContext dc)
        {
            foreach (EdgeGeom edge in _layout.Edges)
            {
                Pen pen = edge.Dashed ? _dashPen : _linePen;

                var curve = new StreamGeometry();
                using (StreamGeometryContext ctx = curve.Open())
                {
                    if (edge.HasRoute)
                    {
                        // 绕行路径：折线，但拐角抹成圆弧——对象之间的连线不该是硬邦邦的直角
                        DrawRoundedRoute(ctx, edge.Route);
                    }
                    else
                    {
                        ctx.BeginFigure(edge.From, false, false);
                        ctx.QuadraticBezierTo(edge.Control, edge.To, true, false);
                    }
                }
                curve.Freeze();
                dc.DrawGeometry(null, pen, curve);

                // 箭头方向按线在端点处的走向来，不能按两端直线连线的方向
                Point nearTo = EdgeNear(edge, true);
                Point nearFrom = EdgeNear(edge, false);
                DrawHead(dc, edge.To, nearTo, edge.ToArrow);
                DrawHead(dc, edge.From, nearFrom, edge.FromArrow);
                DrawErEnd(dc, edge.To, nearTo, edge.ToEr, pen);
                DrawErEnd(dc, edge.From, nearFrom, edge.FromEr, pen);

                // 类图的重数标签
                if (edge.FromMultiplicity != null)
                    dc.DrawText(edge.FromMultiplicity, edge.FromMultiplicityPos);
                if (edge.ToMultiplicity != null)
                    dc.DrawText(edge.ToMultiplicity, edge.ToMultiplicityPos);

                // 标签已经挪到线的旁边，直接画文字，不铺底框——底框会把线盖断
                if (edge.Label != null)
                    dc.DrawText(edge.Label, edge.LabelPos);

                if (edge.Note != null)
                    DrawNote(dc, edge.NoteRect, edge.Note);
            }
        }

        /// <summary>
        /// 把折线画成"拐角带弧度"的线：每个拐点前后各让出一小段，中间用一段二次贝塞尔抹圆。
        /// 让出的长度取拐角半径和相邻线段一半的较小值，短线段也不会画歪。
        /// </summary>
        private void DrawRoundedRoute(StreamGeometryContext ctx, System.Collections.Generic.List<Point> pts)
        {
            double r = U(9);
            ctx.BeginFigure(pts[0], false, false);
            for (int i = 1; i < pts.Count - 1; i++)
            {
                Point prev = pts[i - 1];
                Point cur = pts[i];
                Point next = pts[i + 1];
                double lenIn = (cur - prev).Length;
                double lenOut = (next - cur).Length;
                double cut = Math.Min(r, Math.Min(lenIn, lenOut) / 2);
                Point a = PointOn(prev, cur, lenIn - cut);
                Point b = PointOn(cur, next, cut);
                ctx.LineTo(a, true, false);
                ctx.QuadraticBezierTo(cur, b, true, false);
            }
            ctx.LineTo(pts[pts.Count - 1], true, false);
        }

        /// <summary>从 from 往 to 方向走 dist 距离的点。</summary>
        private static Point PointOn(Point from, Point to, double dist)
        {
            double dx = to.X - from.X;
            double dy = to.Y - from.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6)
                return from;
            return new Point(from.X + dx / len * dist, from.Y + dy / len * dist);
        }

        /// <summary>端点附近的一个点，用来定箭头的方向。有绕行路径时取折线最后一段。</summary>
        private static Point EdgeNear(EdgeGeom edge, bool atTo)
        {
            if (edge.HasRoute)
            {
                int n = edge.Route.Count;
                if (atTo)
                    return edge.Route[n - 2];
                return edge.Route[1];
            }
            return QuadCurve.PointAt(edge.From, edge.Control, edge.To, atTo ? 0.92 : 0.08);
        }

        /// <summary>ER 图那一端的鸦爪记号：一个/多个 + 可选零圈，都画在贴住端点的那一小段上。</summary>
        private void DrawErEnd(DrawingContext dc, Point tip, Point near, ErEnd kind, Pen pen)
        {
            if (kind == ErEnd.None)
                return;

            double dx = tip.X - near.X;
            double dy = tip.Y - near.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6)
                return;
            double ux = dx / len;
            double uy = dy / len;
            double px = -uy;
            double py = ux;
            double s = U(9);          // 记号沿线的长度
            double half = U(5);       // 鸦爪张开的半宽

            bool many = kind == ErEnd.Many || kind == ErEnd.OneMany || kind == ErEnd.ZeroMany;
            bool zero = kind == ErEnd.ZeroOne || kind == ErEnd.ZeroMany;

            // 贴住端点的那条横杠（"一"端）
            double barAt = zero ? s : 0;
            if (!many || kind == ErEnd.ZeroMany)
            {
                Point b = new Point(tip.X - ux * barAt, tip.Y - uy * barAt);
                dc.DrawLine(pen, new Point(b.X + px * half, b.Y + py * half),
                    new Point(b.X - px * half, b.Y - py * half));
            }

            if (many)
            {
                // 鸦爪：三条线从同一个根部张开
                Point root = new Point(tip.X - ux * (s + barAt), tip.Y - uy * (s + barAt));
                dc.DrawLine(pen, root, new Point(tip.X + px * half, tip.Y + py * half));
                dc.DrawLine(pen, root, tip);
                dc.DrawLine(pen, root, new Point(tip.X - px * half, tip.Y - py * half));
            }

            if (zero)
            {
                Point c = new Point(tip.X - ux * (s + barAt + U(4)), tip.Y - uy * (s + barAt + U(4)));
                dc.DrawEllipse(null, pen, c, U(3.2), U(3.2));
            }
        }

        /// <summary>边上的注释：淡黄底、右上角折角（跟 PlantUML 的 note 一个样子）。</summary>
        private void DrawNote(DrawingContext dc, Rect rect, FormattedText text)
        {
            double fold = U(MarkdownTheme.NoteFold);
            var geometry = new StreamGeometry();
            using (StreamGeometryContext ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(rect.Left, rect.Top), true, true);
                ctx.LineTo(new Point(rect.Right - fold, rect.Top), true, false);
                ctx.LineTo(new Point(rect.Right, rect.Top + fold), true, false);
                ctx.LineTo(new Point(rect.Right, rect.Bottom), true, false);
                ctx.LineTo(new Point(rect.Left, rect.Bottom), true, false);
            }
            geometry.Freeze();
            dc.DrawGeometry(NoteFill, _notePen, geometry);
            dc.DrawLine(_notePen, new Point(rect.Right - fold, rect.Top), new Point(rect.Right - fold, rect.Top + fold));
            dc.DrawLine(_notePen, new Point(rect.Right - fold, rect.Top + fold), new Point(rect.Right, rect.Top + fold));
            text.SetForegroundBrush(NoteText);
            dc.DrawText(text, new Point(rect.Left + U(6), rect.Top + U(4)));
        }

        /// <summary>挂在元素旁的注释（note left/right/top/bottom of X），多行文本逐行画。</summary>
        private void DrawNotes(DrawingContext dc)
        {
            double fold = U(MarkdownTheme.NoteFold);
            foreach (DiagramNoteGeom note in _layout.Notes)
            {
                Rect rect = note.Rect;
                var geometry = new StreamGeometry();
                using (StreamGeometryContext ctx = geometry.Open())
                {
                    ctx.BeginFigure(new Point(rect.Left, rect.Top), true, true);
                    ctx.LineTo(new Point(rect.Right - fold, rect.Top), true, false);
                    ctx.LineTo(new Point(rect.Right, rect.Top + fold), true, false);
                    ctx.LineTo(new Point(rect.Right, rect.Bottom), true, false);
                    ctx.LineTo(new Point(rect.Left, rect.Bottom), true, false);
                }
                geometry.Freeze();
                dc.DrawGeometry(NoteFill, _notePen, geometry);
                dc.DrawLine(_notePen, new Point(rect.Right - fold, rect.Top), new Point(rect.Right - fold, rect.Top + fold));
                dc.DrawLine(_notePen, new Point(rect.Right - fold, rect.Top + fold), new Point(rect.Right, rect.Top + fold));

                double y = rect.Top + U(4);
                foreach (FormattedText line in note.Lines)
                {
                    line.SetForegroundBrush(NoteText);
                    dc.DrawText(line, new Point(rect.Left + U(6), y));
                    y += line.Height;
                }
            }
        }

        /// <summary>图例框（legend ... endlegend）。</summary>
        private void DrawLegend(DrawingContext dc)
        {
            if (_layout.Legend != null)
                LegendBox.Draw(dc, _layout.Legend, _containerFill, _containerPen, _fontSize);
        }

        /// <summary>在 tip 处画箭头，方向由 tip 相对 from 决定。</summary>
        private void DrawHead(DrawingContext dc, Point tip, Point from, EndArrow kind)
        {
            if (kind == EndArrow.None)
                return;

            double dx = tip.X - from.X;
            double dy = tip.Y - from.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6)
                return;
            double ux = dx / len;
            double uy = dy / len;
            double px = -uy;
            double py = ux;
            double s = _fontSize * 0.55;
            double half = s * 0.5;

            if (kind == EndArrow.Open)
            {
                dc.DrawLine(_linePen, tip, new Point(tip.X - ux * s + px * half, tip.Y - uy * s + py * half));
                dc.DrawLine(_linePen, tip, new Point(tip.X - ux * s - px * half, tip.Y - uy * s - py * half));
                return;
            }

            if (kind == EndArrow.Triangle)
            {
                var tri = new StreamGeometry();
                using (StreamGeometryContext ctx = tri.Open())
                {
                    ctx.BeginFigure(tip, true, true);
                    ctx.LineTo(new Point(tip.X - ux * s + px * half, tip.Y - uy * s + py * half), true, false);
                    ctx.LineTo(new Point(tip.X - ux * s - px * half, tip.Y - uy * s - py * half), true, false);
                }
                tri.Freeze();
                // 空心三角用底色填实，把穿进去的线头盖住
                dc.DrawGeometry(_surface, _linePen, tri);
                return;
            }

            // 菱形：tip → 中点±垂直 → 尾
            double mx = tip.X - ux * s * 1.6;
            double my = tip.Y - uy * s * 1.6;
            double cx = tip.X - ux * s * 0.8;
            double cy = tip.Y - uy * s * 0.8;
            var dia = new StreamGeometry();
            using (StreamGeometryContext ctx = dia.Open())
            {
                ctx.BeginFigure(tip, true, true);
                ctx.LineTo(new Point(cx + px * half, cy + py * half), true, false);
                ctx.LineTo(new Point(mx, my), true, false);
                ctx.LineTo(new Point(cx - px * half, cy - py * half), true, false);
            }
            dia.Freeze();
            Brush fill = kind == EndArrow.DiamondFilled ? _line : _surface;
            dc.DrawGeometry(fill, _linePen, dia);
        }
    }
}

