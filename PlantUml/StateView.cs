using System;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace MarkdownKit.PlantUml
{
    /// <summary>
    /// 画一张状态图。普通状态是圆角矩形（圆角 25，对齐官方 stateDiagram.state.RoundCorner），
    /// [*] 是实心圆 / 双圈，伪状态是小形状，复合状态是带标题栏的圆角容器。
    /// 常量以元素字号 14 为基准，这里按当前字号等比换算。超过预览区宽度就整体等比缩小。
    /// </summary>
    internal sealed class StateView : FrameworkElement
    {
        /// <summary>两侧要扣掉的宽度：预览区左右内边距加竖直滚动条。</summary>
        private const double SideMargin = 40;

        private readonly StateDiagram _diagram;
        private readonly double _fontSize;
        private StateLayout _layout;
        private double _scale = 1;

        private readonly Brush _line;
        private readonly Brush _fill;
        private readonly Brush _compositeFill;
        private readonly Brush _border;

        private readonly Pen _linePen;
        private readonly Pen _borderPen;
        private readonly Pen _boldPen;
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
            DependencyProperty.Register(nameof(AvailableWidth), typeof(double), typeof(StateView),
                new FrameworkPropertyMetadata(double.PositiveInfinity,
                    FrameworkPropertyMetadataOptions.AffectsMeasure));

        public double AvailableWidth
        {
            get { return (double)GetValue(AvailableWidthProperty); }
            set { SetValue(AvailableWidthProperty, value); }
        }

        public StateView(StateDiagram diagram, double fontSize, FrameworkElement host)
        {
            _diagram = diagram;
            _fontSize = fontSize;
            HorizontalAlignment = HorizontalAlignment.Center;

            _line = MarkdownTheme.Markdown.Text;
            _fill = MarkdownTheme.Background;
            _compositeFill = MarkdownTheme.Markdown.CodeBackground;
            _border = MarkdownTheme.Markdown.Text;

            _linePen = Frozen(new Pen(_line, 1.2));
            _borderPen = Frozen(new Pen(_border, 1.2));
            _boldPen = Frozen(new Pen(_line, 2.2));
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
            _layout = StateLayoutBuilder.Build(_diagram, _fontSize, ppd);
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

            foreach (StateGeom geom in _layout.Elements)
                DrawState(dc, geom);

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

        // ── 状态 ────────────────────────────────────────────────

        private void DrawState(DrawingContext dc, StateGeom geom)
        {
            StateNode e = geom.Source;
            Rect r = geom.Rect;
            switch (e.Kind)
            {
                case StateKind.Start:
                    dc.DrawEllipse(_line, null, Center(r), r.Width / 2, r.Height / 2);
                    break;
                case StateKind.End:
                    dc.DrawEllipse(null, _borderPen, Center(r), r.Width / 2, r.Height / 2);
                    double inner = r.Width * 12.0 / 22.0 / 2;
                    dc.DrawEllipse(_line, null, Center(r), inner, inner);
                    break;
                case StateKind.History:
                    DrawHistory(dc, r, "H");
                    break;
                case StateKind.DeepHistory:
                    DrawHistory(dc, r, "H*");
                    break;
                case StateKind.Choice:
                    DrawDiamond(dc, r);
                    break;
                case StateKind.ForkJoin:
                    dc.DrawRectangle(_line, null, r);
                    break;
                case StateKind.SynchroBar:
                    dc.DrawRectangle(_line, null, r);
                    break;
                case StateKind.Composite:
                    DrawComposite(dc, geom);
                    break;
                default:
                    DrawNormal(dc, geom);
                    break;
            }
        }

        /// <summary>
        /// 普通状态：圆角矩形 + 顶部名称 + 一条全宽分隔线（下半段放描述，没有描述也保留）。
        /// 对齐官方 EntityImageState：名称画在 MARGIN 处、分隔线落在名称下方 MARGIN_LINE 处、线走满整框宽。
        /// </summary>
        private void DrawNormal(DrawingContext dc, StateGeom geom)
        {
            Rect r = geom.Rect;
            double corner = Math.Min(U(12.5), Math.Min(r.Width, r.Height) / 2);
            dc.DrawRoundedRectangle(_fill, _borderPen, r, corner, corner);

            double y = r.Top + U(5);
            if (geom.Stereo != null)
            {
                dc.DrawText(geom.Stereo, new Point(r.X + (r.Width - geom.Stereo.Width) / 2, y));
                y += geom.Stereo.Height + U(2);
            }
            dc.DrawText(geom.Text, new Point(r.X + (r.Width - geom.Text.Width) / 2, y));

            // 分隔线：名称下方 MARGIN_LINE，走满整框宽
            double yLine = y + geom.Text.Height + U(5);
            dc.DrawLine(_borderPen, new Point(r.Left, yLine), new Point(r.Right, yLine));

            if (geom.Fields != null)
            {
                double fy = yLine + U(5);
                foreach (FormattedText f in geom.Fields)
                {
                    dc.DrawText(f, new Point(r.X + (r.Width - f.Width) / 2, fy));
                    fy += f.Height;
                }
            }
        }

        /// <summary>复合状态：圆角外框 + 顶部标题栏分隔线。</summary>
        private void DrawComposite(DrawingContext dc, StateGeom geom)
        {
            Rect r = geom.Rect;
            double corner = U(5);
            dc.DrawRoundedRectangle(_compositeFill, _borderPen, r, corner, corner);

            double band = U(6) + geom.Text.Height + U(6);
            dc.DrawLine(_borderPen, new Point(r.Left, r.Top + band), new Point(r.Right, r.Top + band));
            dc.DrawText(geom.Text, new Point(r.X + (r.Width - geom.Text.Width) / 2, r.Top + U(6)));
        }

        private void DrawHistory(DrawingContext dc, Rect r, string label)
        {
            dc.DrawEllipse(_fill, _borderPen, Center(r), r.Width / 2, r.Height / 2);
            var ft = new FormattedText(label, System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight, new Typeface(MarkdownTheme.DiagramFamily, FontStyles.Normal,
                    FontWeights.Normal, FontStretches.Normal), _fontSize * 0.8, _border,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            DrawCentered(dc, ft, r);
        }

        private void DrawDiamond(DrawingContext dc, Rect r)
        {
            Point c = Center(r);
            var geo = new StreamGeometry();
            using (StreamGeometryContext ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(c.X, r.Top), true, true);
                ctx.LineTo(new Point(r.Right, c.Y), true, false);
                ctx.LineTo(new Point(c.X, r.Bottom), true, false);
                ctx.LineTo(new Point(r.Left, c.Y), true, false);
            }
            geo.Freeze();
            dc.DrawGeometry(_fill, _borderPen, geo);
        }

        private static Point Center(Rect r)
        {
            return new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        }

        private void DrawCentered(DrawingContext dc, FormattedText text, Rect area)
        {
            if (text == null)
                return;
            dc.DrawText(text, new Point(
                area.X + (area.Width - text.Width) / 2,
                area.Y + (area.Height - text.Height) / 2));
        }

        // ── 转换边 ──────────────────────────────────────────────

        private void DrawEdges(DrawingContext dc)
        {
            foreach (StateEdgeGeom edge in _layout.Edges)
            {
                Pen pen = edge.Dashed ? _dashPen : (edge.Bold ? _boldPen : _linePen);

                var curve = new StreamGeometry();
                using (StreamGeometryContext ctx = curve.Open())
                {
                    ctx.BeginFigure(edge.From, false, false);
                    ctx.QuadraticBezierTo(edge.Control, edge.To, true, false);
                }
                curve.Freeze();
                dc.DrawGeometry(null, pen, curve);

                // 箭头方向按曲线端点处的切线来，不能按两端直线连线的方向
                Point nearTo = QuadCurve.PointAt(edge.From, edge.Control, edge.To, 0.92);
                Point nearFrom = QuadCurve.PointAt(edge.From, edge.Control, edge.To, 0.08);
                DrawHead(dc, edge.To, nearTo, edge.Head);
                if (edge.CrossStart)
                    DrawCross(dc, edge.From, nearFrom);

                // 标签已经挪到线的旁边，直接画文字，不铺底框——底框会把线盖断
                if (edge.Label != null)
                    dc.DrawText(edge.Label, edge.LabelPos);
            }
        }

        /// <summary>在 tip 处画箭头，方向由 tip 相对 from 决定。</summary>
        private void DrawHead(DrawingContext dc, Point tip, Point from, TransitionHead kind)
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

            if (kind == TransitionHead.Circle)
            {
                double rr = _fontSize * 0.22;
                Point c = new Point(tip.X - ux * rr, tip.Y - uy * rr);
                dc.DrawEllipse(_fill, _linePen, c, rr, rr);
                return;
            }

            double s = _fontSize * 0.75;
            double half = s * 0.42;
            var tri = new StreamGeometry();
            using (StreamGeometryContext ctx = tri.Open())
            {
                ctx.BeginFigure(tip, true, true);
                ctx.LineTo(new Point(tip.X - ux * s + px * half, tip.Y - uy * s + py * half), true, false);
                ctx.LineTo(new Point(tip.X - ux * s - px * half, tip.Y - uy * s - py * half), true, false);
            }
            tri.Freeze();
            dc.DrawGeometry(_line, _linePen, tri);   // 实心箭头
        }

        /// <summary>起点带 x 时在起点画个叉。</summary>
        private void DrawCross(DrawingContext dc, Point tip, Point from)
        {
            double dx = tip.X - from.X;
            double dy = tip.Y - from.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6)
                return;
            double ux = dx / len;
            double uy = dy / len;
            double s = _fontSize * 0.3;
            double px = -uy * s;
            double py = ux * s;
            double qx = ux * s;
            double qy = uy * s;
            dc.DrawLine(_linePen, new Point(tip.X - qx + px, tip.Y - qy + py), new Point(tip.X + qx - px, tip.Y + qy - py));
            dc.DrawLine(_linePen, new Point(tip.X - qx - px, tip.Y - qy - py), new Point(tip.X + qx + px, tip.Y + qy + py));
        }

        /// <summary>挂在状态旁的注释（note left/right/top/bottom of X），多行文本逐行画。</summary>
        private void DrawNotes(DrawingContext dc)
        {
            double fold = U(MarkdownTheme.NoteFold);
            foreach (StateNoteGeom note in _layout.Notes)
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
                LegendBox.Draw(dc, _layout.Legend, _compositeFill, _borderPen, _fontSize);
        }
    }
}


