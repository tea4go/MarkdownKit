using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace MarkdownKit.PlantUml
{
    /// <summary>
    /// 画一张树形图（思维导图 / WBS / JSON / YAML）。
    /// 思维导图和 JSON/YAML 是横着长的树：圆角框，父子之间用一段平滑曲线连；
    /// WBS 是竖着长的树：矩形框，父子之间走折线（下 → 横 → 下）。
    /// JSON/YAML 的节点是"键 | 值"两列表格，嵌套对象从值格用虚线箭头连出去。
    /// </summary>
    internal sealed class TreeView : FrameworkElement
    {
        /// <summary>两侧要扣掉的宽度：预览区左右内边距加竖直滚动条。</summary>
        private const double SideMargin = 40;

        /// <summary>JSON / YAML 名称列最多占可用宽度的比例，超出折行。</summary>
        private const double KeyWidthRatio = 0.2;

        /// <summary>JSON / YAML 值列最多占可用宽度的比例，超出折行。</summary>
        private const double ValueWidthRatio = 0.3;

        /// <summary>布局按多宽算的（表格折行上限跟着它走）；宽度变了要重排。</summary>
        private const double RelayoutThreshold = 8;

        private readonly TreeDiagram _diagram;
        private readonly double _fontSize;
        private TreeLayout _layout;
        private double _layoutLimit = double.NaN;
        private double _scale = 1;

        private readonly Brush _fill;
        private readonly Brush _border;
        private readonly Pen _borderPen;
        private readonly Pen _linePen;

        public static readonly DependencyProperty AvailableWidthProperty =
            DependencyProperty.Register(nameof(AvailableWidth), typeof(double), typeof(TreeView),
                new FrameworkPropertyMetadata(double.PositiveInfinity,
                    FrameworkPropertyMetadataOptions.AffectsMeasure));

        public double AvailableWidth
        {
            get { return (double)GetValue(AvailableWidthProperty); }
            set { SetValue(AvailableWidthProperty, value); }
        }

        public TreeView(TreeDiagram diagram, double fontSize, FrameworkElement host)
        {
            _diagram = diagram;
            _fontSize = fontSize;
            HorizontalAlignment = HorizontalAlignment.Center;

            _fill = MarkdownTheme.Background;
            _border = MarkdownTheme.Markdown.Text;

            _borderPen = Frozen(new Pen(_border, 1.2));
            _linePen = Frozen(new Pen(_border, 1.2));

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

        private double U(double v)
        {
            return v * _fontSize / 14.0;
        }

        private bool IsTable
        {
            get { return _diagram.Kind == TreeKind.Json || _diagram.Kind == TreeKind.Yaml; }
        }

        private void EnsureLayout()
        {
            if (_layout == null)
                BuildLayout(_layoutLimit);
        }

        /// <summary>按给定可用宽度重排：表格的键 / 值列分别以 20% / 30% 封顶，超出折行。</summary>
        private void BuildLayout(double limit)
        {
            double ppd = 1.0;
            try
            {
                ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            }
            catch (Exception)
            {
                ppd = 1.0;
            }
            _layoutLimit = limit;
            bool capped = !double.IsInfinity(limit) && limit > 0;
            _layout = TreeLayoutBuilder.Build(_diagram, _fontSize, ppd,
                capped ? limit * KeyWidthRatio : double.PositiveInfinity,
                capped ? limit * ValueWidthRatio : double.PositiveInfinity);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            double limit = double.PositiveInfinity;
            if (!double.IsInfinity(availableSize.Width))
                limit = availableSize.Width;
            if (!double.IsInfinity(AvailableWidth))
                limit = Math.Min(limit, Math.Max(0, AvailableWidth - SideMargin));

            // 表格的折行宽度取决于可用宽度，窗口缩放后要按新宽度重排；
            // 思维导图 / WBS 的几何跟宽度无关，只排一次，之后靠 _scale 缩放
            if (_layout == null || (IsTable && Math.Abs(limit - _layoutLimit) > RelayoutThreshold))
            {
                BuildLayout(limit);
                // 测量阶段换了布局要主动重绘：不加这一句，画面还停在换布局前的那一帧
                InvalidateVisual();
            }

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

            DrawEdges(dc);
            if (_layout.Kind == TreeKind.Json || _layout.Kind == TreeKind.Yaml)
                DrawTables(dc);
            else
            {
                foreach (TreeGeom geom in _layout.Nodes)
                    DrawNode(dc, geom);
            }

            if (_layout.Title != null)
            {
                FormattedText title = _layout.Title.Text;
                dc.DrawText(title, new Point(_layout.Title.CenterX - title.Width / 2, _layout.Title.Top));
            }

            if (_scale != 1)
                dc.Pop();
        }

        // ── 连线 ────────────────────────────────────────────────

        private void DrawEdges(DrawingContext dc)
        {
            if (_layout.Kind == TreeKind.Wbs)
            {
                foreach (TreeEdgeGeom edge in _layout.Edges)
                {
                    double bus = (edge.From.Y + edge.To.Y) / 2;
                    var geo = new StreamGeometry();
                    using (StreamGeometryContext ctx = geo.Open())
                    {
                        ctx.BeginFigure(edge.From, false, false);
                        ctx.LineTo(new Point(edge.From.X, bus), true, false);
                        ctx.LineTo(new Point(edge.To.X, bus), true, false);
                        ctx.LineTo(edge.To, true, false);
                    }
                    geo.Freeze();
                    dc.DrawGeometry(null, _linePen, geo);
                }
                return;
            }

            bool table = _layout.Kind == TreeKind.Json || _layout.Kind == TreeKind.Yaml;
            Pen pen = table ? _layout.EdgePen : _linePen;
            Brush arrowFill = table ? _layout.EdgeBrush : _border;
            foreach (TreeEdgeGeom edge in _layout.Edges)
            {
                Point from = edge.From;
                Point to = edge.To;
                double dx = Math.Max(U(12), Math.Abs(to.X - from.X) * 0.5);
                var geo = new StreamGeometry();
                using (StreamGeometryContext ctx = geo.Open())
                {
                    ctx.BeginFigure(from, false, false);
                    ctx.BezierTo(new Point(from.X + dx, from.Y), new Point(to.X - dx, to.Y), to, true, false);
                }
                geo.Freeze();
                dc.DrawGeometry(null, pen, geo);
                if (table)
                    DrawArrowHead(dc, to, from, pen, arrowFill);
            }
        }

        private void DrawArrowHead(DrawingContext dc, Point tip, Point from, Pen pen, Brush fill)
        {
            double dx = tip.X - from.X;
            double dy = tip.Y - from.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6)
                return;
            double ux = dx / len;
            double uy = dy / len;
            double px = -uy;
            double py = ux;
            double s = _fontSize * 0.6;
            double half = s * 0.4;
            var tri = new StreamGeometry();
            using (StreamGeometryContext ctx = tri.Open())
            {
                ctx.BeginFigure(tip, true, true);
                ctx.LineTo(new Point(tip.X - ux * s + px * half, tip.Y - uy * s + py * half), true, false);
                ctx.LineTo(new Point(tip.X - ux * s - px * half, tip.Y - uy * s - py * half), true, false);
            }
            tri.Freeze();
            dc.DrawGeometry(fill, pen, tri);
        }

        // ── 节点 ────────────────────────────────────────────────

        /// <summary>JSON / YAML：一个对象一张表，先铺底色和竖线，再画高亮行、格线和文字。</summary>
        private void DrawTables(DrawingContext dc)
        {
            TreeTableStyle style = _layout.Table;
            if (style == null)
                return;

            double corner = style.Corner;
            foreach (TreeGeom table in _layout.Nodes)
            {
                Rect r = table.Rect;
                dc.DrawRoundedRectangle(style.Fill, style.Border, r, corner, corner);
                dc.DrawRectangle(style.KeyFill, null, new Rect(r.X, r.Y, table.KeyW, r.Height));
                dc.DrawRoundedRectangle(null, style.Border, r, corner, corner);
            }

            // 高亮铺在整行（键格 + 值格）下面：官方也是整行换底色
            double highlightCorner = U(4);
            foreach (TreeGeom row in _layout.Rows)
            {
                if (row.RowBackground == null)
                    continue;
                Rect r = row.Rect;
                dc.DrawRoundedRectangle(row.RowBackground, null,
                    new Rect(r.X + 1.5, r.Y, Math.Max(0, r.Width - 3), r.Height),
                    highlightCorner, highlightCorner);
            }

            // 竖线必须压在高亮底色之上，否则高亮行整行铺色会把键值分隔线盖掉
            foreach (TreeGeom table in _layout.Nodes)
            {
                Rect r = table.Rect;
                dc.DrawLine(style.Border, new Point(r.X + table.KeyW, r.Top), new Point(r.X + table.KeyW, r.Bottom));
            }

            foreach (TreeGeom row in _layout.Rows)
            {
                Rect r = row.Rect;
                dc.DrawLine(style.Separator, new Point(r.X, r.Bottom), new Point(r.Right, r.Bottom));
            }

            double padX = U(8);
            foreach (TreeGeom row in _layout.Rows)
            {
                Rect r = row.Rect;
                // 键靠右、值靠左：两列之间的留白集中在键格左侧，键和值自然夹紧读
                DrawRightAligned(dc, row.Text, new Rect(r.X, r.Y, row.KeyW, r.Height), padX);
                DrawLeftAligned(dc, row.Value, new Rect(r.X + row.KeyW, r.Y, r.Width - row.KeyW, r.Height), padX);
            }
        }

        private void DrawNode(DrawingContext dc, TreeGeom geom)
        {
            Rect r = geom.Rect;
            bool rounded = _layout.Kind == TreeKind.MindMap;
            if (rounded)
            {
                double corner = Math.Min(U(12), r.Height / 2);
                dc.DrawRoundedRectangle(_fill, _borderPen, r, corner, corner);
            }
            else
            {
                dc.DrawRectangle(_fill, _borderPen, r);
            }
            DrawCentered(dc, geom.Text, r, 0);
        }

        private static void DrawCentered(DrawingContext dc, FormattedText text, Rect area, double padX)
        {
            if (text == null)
                return;
            dc.DrawText(text, new Point(
                area.X + (area.Width - text.Width) / 2,
                area.Y + (area.Height - text.Height) / 2));
        }

        private static void DrawLeftAligned(DrawingContext dc, FormattedText text, Rect area, double padX)
        {
            if (text == null)
                return;
            dc.DrawText(text, new Point(
                area.X + padX,
                area.Y + (area.Height - text.Height) / 2));
        }

        private static void DrawRightAligned(DrawingContext dc, FormattedText text, Rect area, double padX)
        {
            if (text == null)
                return;
            // 键带 TextAlignment.Right：WPF 是在「MaxTextWidth 宽的对齐框」里靠右，
            // 所以起点要按对齐框算。按文字实际宽度算会把短键再往右推一截，压进值列
            double box = double.IsInfinity(text.MaxTextWidth) ? text.Width : text.MaxTextWidth;
            dc.DrawText(text, new Point(
                area.Right - padX - box,
                area.Y + (area.Height - text.Height) / 2));
        }
    }
}
