using System;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace MarkdownKit.PlantUml
{
    /// <summary>
    /// 画一张活动图：圆角活动框、实心起始圆、牛眼结束节点、判断菱形、汇合菱形、
    /// fork 的粗同步条，块与块之间用带箭头的折线连起来。
    /// </summary>
    internal sealed class ActivityView : FrameworkElement
    {
        private const double SideMargin = 40;

        private readonly ActivityDiagram _diagram;
        private readonly double _fontSize;
        private ActivityLayout _layout;
        private double _scale = 1;

        private readonly Brush _fill;
        private readonly Brush _diamondFill;
        private readonly Brush _border;
        private readonly Brush _barFill;
        private readonly Brush _noteFill;
        private readonly Pen _borderPen;
        private readonly Pen _linePen;
        private readonly Pen _barPen;
        private readonly Pen _notePen;
        private readonly Pen _lanePen;

        public static readonly DependencyProperty AvailableWidthProperty =
            DependencyProperty.Register(nameof(AvailableWidth), typeof(double), typeof(ActivityView),
                new FrameworkPropertyMetadata(double.PositiveInfinity,
                    FrameworkPropertyMetadataOptions.AffectsMeasure));

        public double AvailableWidth
        {
            get { return (double)GetValue(AvailableWidthProperty); }
            set { SetValue(AvailableWidthProperty, value); }
        }

        public ActivityView(ActivityDiagram diagram, double fontSize, FrameworkElement host)
        {
            _diagram = diagram;
            _fontSize = fontSize;
            HorizontalAlignment = HorizontalAlignment.Center;

            _fill = MarkdownTheme.Markdown.CodeBackground;
            _diamondFill = MarkdownTheme.Background;
            _border = MarkdownTheme.Markdown.Text;
            _barFill = MarkdownTheme.Markdown.Text;
            _noteFill = Solid("#FEFFDD");

            _borderPen = Frozen(new Pen(_border, 1.2));
            _linePen = Frozen(new Pen(_border, 1.2));
            _barPen = Frozen(new Pen(_barFill, 1));
            _notePen = Frozen(new Pen(Solid("#888888"), 1));
            _lanePen = Frozen(new Pen(_border, 1.4));

            if (host != null)
            {
                if (host.ActualWidth > 0)
                    AvailableWidth = host.ActualWidth;
                BindingOperations.SetBinding(this, AvailableWidthProperty,
                    new Binding("ActualWidth") { Source = host });
            }
        }

        private static Brush Solid(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
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
            _layout = ActivityLayoutBuilder.Build(_diagram, _fontSize, ppd);
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

            DrawLanes(dc);
            foreach (ActWire wire in _layout.Wires)
                DrawWire(dc, wire);
            foreach (ActBlock node in _layout.Nodes)
                DrawNode(dc, node);

            if (_layout.Title != null)
            {
                FormattedText title = _layout.Title.Text;
                dc.DrawText(title, new Point(_layout.Title.CenterX - title.Width / 2, _layout.Title.Top));
            }

            if (_scale != 1)
                dc.Pop();
        }

        /// <summary>泳道：每个泳道画左边那条线（最后一条再补右边），泳道名印在顶上。</summary>
        private void DrawLanes(DrawingContext dc)
        {
            for (int i = 0; i < _layout.Lanes.Count; i++)
            {
                ActLane lane = _layout.Lanes[i];
                Rect r = lane.Rect;
                dc.DrawLine(_lanePen, new Point(r.Left, r.Top), new Point(r.Left, r.Bottom));
                if (i == _layout.Lanes.Count - 1)
                    dc.DrawLine(_lanePen, new Point(r.Right, r.Top), new Point(r.Right, r.Bottom));
                if (lane.Title != null)
                {
                    lane.Title.SetForegroundBrush(_border);
                    dc.DrawText(lane.Title, new Point(
                        r.X + (r.Width - lane.Title.Width) / 2, r.Top + U(4)));
                }
            }
        }

        // ── 连线 ────────────────────────────────────────────────

        private void DrawWire(DrawingContext dc, ActWire wire)
        {
            if (wire.Points.Count >= 2)
            {
                var geo = new StreamGeometry();
                using (StreamGeometryContext ctx = geo.Open())
                {
                    ctx.BeginFigure(wire.Points[0], false, false);
                    for (int i = 1; i < wire.Points.Count; i++)
                        ctx.LineTo(wire.Points[i], true, false);
                }
                geo.Freeze();
                dc.DrawGeometry(null, _linePen, geo);
                if (wire.Arrow)
                    DrawArrow(dc, wire.Points[wire.Points.Count - 2], wire.Points[wire.Points.Count - 1]);
            }

            if (wire.Label != null)
            {
                wire.Label.SetForegroundBrush(_border);
                dc.DrawText(wire.Label, wire.LabelAt);
            }
        }

        private void DrawArrow(DrawingContext dc, Point from, Point tip)
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
            double s = U(9);
            double half = s * 0.38;
            var tri = new StreamGeometry();
            using (StreamGeometryContext ctx = tri.Open())
            {
                ctx.BeginFigure(tip, true, true);
                ctx.LineTo(new Point(tip.X - ux * s + px * half, tip.Y - uy * s + py * half), true, false);
                ctx.LineTo(new Point(tip.X - ux * s - px * half, tip.Y - uy * s - py * half), true, false);
            }
            tri.Freeze();
            dc.DrawGeometry(_border, null, tri);
        }

        // ── 形状 ────────────────────────────────────────────────

        private void DrawNode(DrawingContext dc, ActBlock node)
        {
            switch (node.Kind)
            {
                case ActKind.Action:
                    DrawAction(dc, node);
                    break;
                case ActKind.Start:
                    dc.DrawEllipse(_border, null, Center(node, node.W, node.H), node.W / 2, node.H / 2);
                    break;
                case ActKind.Stop:
                    DrawStop(dc, node);
                    break;
                case ActKind.End:
                    DrawEnd(dc, node);
                    break;
                case ActKind.Detach:
                    dc.DrawLine(_borderPen, new Point(node.X, node.Y + node.H / 2),
                        new Point(node.X + node.W, node.Y + node.H / 2));
                    break;
                case ActKind.Diamond:
                case ActKind.Merge:
                    DrawDiamond(dc, node, _diamondFill);
                    break;
                case ActKind.Bar:
                    dc.DrawRectangle(_barFill, null, new Rect(node.X, node.Y, node.W, node.H));
                    break;
            }

            if (node.NoteFt != null)
                DrawNoteFor(dc, node);
        }

        private void DrawAction(DrawingContext dc, ActBlock node)
        {
            double w = node.ShapeW > 0 ? node.ShapeW : node.W;
            var rect = new Rect(node.X, node.Y, w, node.H);
            double r = Math.Min(U(10), rect.Height / 2);
            dc.DrawRoundedRectangle(_fill, _borderPen, rect, r, r);
            if (node.Ft != null)
                DrawCentered(dc, node.Ft, rect);
        }

        /// <summary>挂在块右边的注释框：淡黄底、右上角折角。</summary>
        private void DrawNoteFor(DrawingContext dc, ActBlock node)
        {
            double shapeW = node.ShapeW > 0 ? node.ShapeW : node.W;
            var note = new Rect(node.X + shapeW + U(14), node.Y,
                node.NoteFt.Width + U(16), node.NoteFt.Height + U(10));
            double fold = U(MarkdownTheme.NoteFold);
            var geo = new StreamGeometry();
            using (StreamGeometryContext ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(note.Left, note.Top), true, true);
                ctx.LineTo(new Point(note.Right - fold, note.Top), true, false);
                ctx.LineTo(new Point(note.Right, note.Top + fold), true, false);
                ctx.LineTo(new Point(note.Right, note.Bottom), true, false);
                ctx.LineTo(new Point(note.Left, note.Bottom), true, false);
            }
            geo.Freeze();
            dc.DrawGeometry(_noteFill, _notePen, geo);
            dc.DrawLine(_notePen, new Point(note.Right - fold, note.Top), new Point(note.Right - fold, note.Top + fold));
            dc.DrawLine(_notePen, new Point(note.Right - fold, note.Top + fold), new Point(note.Right, note.Top + fold));
            dc.DrawText(node.NoteFt, new Point(note.Left + U(8), note.Top + U(5)));
        }

        /// <summary>结束节点：外圈 + 实心内点。</summary>
        private void DrawStop(DrawingContext dc, ActBlock node)
        {
            Point c = Center(node, node.W, node.H);
            dc.DrawEllipse(null, _borderPen, c, node.W / 2, node.H / 2);
            double inner = node.W / 2 * 0.55;
            dc.DrawEllipse(_border, null, c, inner, inner);
        }

        /// <summary>`end`：圆角方框里一个实心圆。</summary>
        private void DrawEnd(DrawingContext dc, ActBlock node)
        {
            var rect = new Rect(node.X, node.Y, node.W, node.H);
            double r = U(6);
            dc.DrawRoundedRectangle(_fill, _borderPen, rect, r, r);
            Point c = Center(node, node.W, node.H);
            dc.DrawEllipse(_border, null, c, node.W * 0.18, node.W * 0.18);
        }

        private void DrawDiamond(DrawingContext dc, ActBlock node, Brush fill)
        {
            double cx = node.X + node.W / 2;
            double cy = node.Y + node.H / 2;
            var geo = new StreamGeometry();
            using (StreamGeometryContext ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(cx, node.Y), true, true);
                ctx.LineTo(new Point(node.X + node.W, cy), true, false);
                ctx.LineTo(new Point(cx, node.Y + node.H), true, false);
                ctx.LineTo(new Point(node.X, cy), true, false);
            }
            geo.Freeze();
            dc.DrawGeometry(fill, _borderPen, geo);
            if (node.Ft != null)
                DrawCentered(dc, node.Ft, new Rect(node.X, cy - node.Ft.Height / 2, node.W, node.Ft.Height));
        }

        private static Point Center(ActBlock node, double w, double h)
        {
            return new Point(node.X + w / 2, node.Y + h / 2);
        }

        private static void DrawCentered(DrawingContext dc, FormattedText text, Rect area)
        {
            if (text == null)
                return;
            dc.DrawText(text, new Point(
                area.X + (area.Width - text.Width) / 2,
                area.Y + (area.Height - text.Height) / 2));
        }
    }
}
