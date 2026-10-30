using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace MarkdownKit.PlantUml
{
    /// <summary>
    /// 画一张时序图。按图自身的原始尺寸排版，超过预览区宽度就整体等比缩小，放得下则不动。
    /// 用矢量绘制（DrawingContext），缩放、换主题都不糊。
    /// </summary>
    internal sealed class SequenceView : FrameworkElement
    {
        /// <summary>两侧要扣掉的宽度：预览区左右内边距加竖直滚动条，留富余免得撑出横向滚动。</summary>
        private const double SideMargin = 40;

        private readonly SequenceDiagram _diagram;
        private readonly double _fontSize;
        private SequenceLayout _layout;
        private double _scale = 1;

        private readonly Brush _line;
        private readonly Brush _boxFill;
        private readonly Brush _boxBorder;
        private readonly Brush _noteFill;
        private readonly Brush _noteBorder;
        private readonly Brush _frameBorder;
        private readonly Brush _segmentFill;
        private readonly Brush _lifeline;
        private readonly Brush _barFill;

        private readonly Pen _linePen;
        private readonly Pen _dashPen;
        private readonly Pen _lifelinePen;
        private readonly Pen _boxPen;
        private readonly Pen _notePen;
        private readonly Pen _framePen;
        private readonly Pen _frameDashPen;
        private readonly Pen _frameSegmentPen;
        private readonly Pen _barPen;

        /// <summary>预览区可用宽度，随窗口缩放由绑定刷新。</summary>
        public static readonly DependencyProperty AvailableWidthProperty =
            DependencyProperty.Register(nameof(AvailableWidth), typeof(double), typeof(SequenceView),
                new FrameworkPropertyMetadata(double.PositiveInfinity,
                    FrameworkPropertyMetadataOptions.AffectsMeasure));

        public double AvailableWidth
        {
            get { return (double)GetValue(AvailableWidthProperty); }
            set { SetValue(AvailableWidthProperty, value); }
        }

        public SequenceView(SequenceDiagram diagram, double fontSize, FrameworkElement host)
        {
            _diagram = diagram;
            _fontSize = fontSize;
            HorizontalAlignment = HorizontalAlignment.Center;

            _line = MarkdownTheme.Markdown.Text;
            _boxFill = MarkdownTheme.Markdown.CodeBackground;
            _boxBorder = MarkdownTheme.Markdown.CodeBorder;
            _noteFill = MarkdownTheme.Markdown.InlineCodeBackground;
            _noteBorder = MarkdownTheme.Markdown.TableBorder;
            _frameBorder = _line;
            _segmentFill = MarkdownTheme.Markdown.SegmentFill;
            _lifeline = MarkdownTheme.Muted;
            _barFill = MarkdownTheme.Background;

            _linePen = Frozen(new Pen(_line, 1.2));
            _dashPen = Frozen(new Pen(_line, 1.2) { DashStyle = DashStyles.Dash });
            _lifelinePen = Frozen(new Pen(_lifeline, 1) { DashStyle = DashStyles.Dash });
            _boxPen = Frozen(new Pen(_boxBorder, 1.2));
            _notePen = Frozen(new Pen(_noteBorder, 1.2));
            _framePen = Frozen(new Pen(_frameBorder, 1.8));
            _frameDashPen = Frozen(new Pen(_frameBorder, 1.2) { DashStyle = DashStyles.Dash });
            // 帧外框/标签页与 else 分隔线都走深色，但分隔线保持细虚线（对齐官方）
            _frameSegmentPen = Frozen(new Pen(_line, 1.8));
            _barPen = Frozen(new Pen(_line, 1.2));

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
            _layout = SequenceLayoutBuilder.Build(_diagram, _fontSize, ppd);
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

            DrawFrames(dc);
            DrawLifelines(dc);
            DrawBars(dc);
            DrawArrows(dc);
            DrawBoxes(dc);
            DrawNotes(dc);
            DrawDividers(dc);
            DrawDelays(dc);
            if (_layout.Legend != null)
                LegendBox.Draw(dc, _layout.Legend, _noteFill, _notePen, _fontSize);
            if (_layout.Title != null)
            {
                FormattedText title = _layout.Title.Text;
                dc.DrawText(title, new Point(_layout.Title.CenterX - title.Width / 2, _layout.Title.Top));
            }

            if (_scale != 1)
                dc.Pop();
        }

        // ── 各层 ────────────────────────────────────────────────

        private void DrawLifelines(DrawingContext dc)
        {
            foreach (double x in _layout.Lifelines)
                dc.DrawLine(_lifelinePen, new Point(x, _layout.LifelineTop), new Point(x, _layout.LifelineBottom));
        }

        private void DrawBars(DrawingContext dc)
        {
            foreach (BarGeom bar in _layout.Bars)
                dc.DrawRectangle(_barFill, _barPen, bar.Rect);
        }

        private void DrawBoxes(DrawingContext dc)
        {
            foreach (BoxGeom box in _layout.Boxes)
            {
                if (box.Kind == ParticipantKind.Actor)
                {
                    DrawActor(dc, box);
                    continue;
                }
                if (box.Kind == ParticipantKind.Database)
                {
                    DrawDatabase(dc, box);
                    continue;
                }
                dc.DrawRoundedRectangle(_boxFill, _boxPen, box.Rect, _fontSize * 0.18, _fontSize * 0.18);
                FormattedText text = box.Text;
                dc.DrawText(text, new Point(
                    box.Rect.Left + (box.Rect.Width - text.Width) / 2,
                    box.Rect.Top + (box.Rect.Height - text.Height) / 2));
            }
        }

        /// <summary>actor 画成火柴人，标签放在脚下——这是 PlantUML 里最好认的一种参与者。</summary>
        private void DrawActor(DrawingContext dc, BoxGeom box)
        {
            double cx = box.Rect.Left + box.Rect.Width / 2;
            double top = box.Rect.Top;
            double r = _fontSize * 0.32;

            var headCenter = new Point(cx, top + r);
            double neckY = top + r * 2;
            double bodyBottom = neckY + _fontSize * 0.75;
            double armsY = neckY + _fontSize * 0.25;

            dc.DrawEllipse(null, _linePen, headCenter, r, r);
            dc.DrawLine(_linePen, new Point(cx, neckY), new Point(cx, bodyBottom));
            dc.DrawLine(_linePen, new Point(cx - _fontSize * 0.45, armsY), new Point(cx + _fontSize * 0.45, armsY));
            dc.DrawLine(_linePen, new Point(cx, bodyBottom), new Point(cx - _fontSize * 0.4, bodyBottom + _fontSize * 0.7));
            dc.DrawLine(_linePen, new Point(cx, bodyBottom), new Point(cx + _fontSize * 0.4, bodyBottom + _fontSize * 0.7));

            FormattedText text = box.Text;
            dc.DrawText(text, new Point(cx - text.Width / 2, box.Rect.Bottom - text.Height));
        }

        /// <summary>database 参与者：圆柱（顶面椭圆 + 直身 + 底弧），标签在下方。尺寸取自官方 USymbolDatabase。</summary>
        private void DrawDatabase(DrawingContext dc, BoxGeom box)
        {
            double w = _fontSize * 2.6;      // 圆柱宽（官方 36/14）
            double h = _fontSize * 3.3;      // 圆柱高（官方 46/14）
            double rh = w * 0.28;            // 顶/底椭圆半高（官方 10/36）
            double cx = box.Rect.Left + box.Rect.Width / 2;
            double left = cx - w / 2;
            double right = cx + w / 2;
            double top = box.Rect.Top;
            double bottom = top + h;

            // 主体轮廓：顶弧 + 右边 + 底弧 + 左边
            var body = new StreamGeometry();
            using (StreamGeometryContext ctx = body.Open())
            {
                ctx.BeginFigure(new Point(left, top + rh), true, true);
                ctx.BezierTo(new Point(left, top), new Point(cx, top), new Point(cx, top), true, false);
                ctx.BezierTo(new Point(cx, top), new Point(right, top), new Point(right, top + rh), true, false);
                ctx.LineTo(new Point(right, bottom - rh), true, false);
                ctx.BezierTo(new Point(right, bottom), new Point(cx, bottom), new Point(cx, bottom), true, false);
                ctx.BezierTo(new Point(cx, bottom), new Point(left, bottom), new Point(left, bottom - rh), true, false);
                ctx.LineTo(new Point(left, top + rh), true, false);
            }
            body.Freeze();
            dc.DrawGeometry(_boxFill, _boxPen, body);

            // 顶面椭圆的下半弧
            var cap = new StreamGeometry();
            using (StreamGeometryContext ctx = cap.Open())
            {
                ctx.BeginFigure(new Point(left, top + rh), false, false);
                ctx.BezierTo(new Point(left, top + 2 * rh), new Point(cx, top + 2 * rh), new Point(cx, top + 2 * rh), true, false);
                ctx.BezierTo(new Point(cx, top + 2 * rh), new Point(right, top + 2 * rh), new Point(right, top + rh), true, false);
            }
            cap.Freeze();
            dc.DrawGeometry(null, _boxPen, cap);

            FormattedText text = box.Text;
            dc.DrawText(text, new Point(cx - text.Width / 2, bottom + _fontSize * 0.1));
        }

        private void DrawNotes(DrawingContext dc)
        {
            double fold = _fontSize * MarkdownTheme.NoteFold / 14.0;
            foreach (NoteGeom note in _layout.Notes)
            {
                Rect rect = note.Rect;
                double l = rect.Left;
                double t = rect.Top;
                double r = rect.Right;
                double b = rect.Bottom;

                var geometry = new StreamGeometry();
                using (StreamGeometryContext ctx = geometry.Open())
                {
                    ctx.BeginFigure(new Point(l, t), true, true);
                    ctx.LineTo(new Point(r - fold, t), true, false);
                    ctx.LineTo(new Point(r, t + fold), true, false);
                    ctx.LineTo(new Point(r, b), true, false);
                    ctx.LineTo(new Point(l, b), true, false);
                }
                geometry.Freeze();
                dc.DrawGeometry(_noteFill, _notePen, geometry);
                // 右上角那个折角
                dc.DrawLine(_notePen, new Point(r - fold, t), new Point(r - fold, t + fold));
                dc.DrawLine(_notePen, new Point(r - fold, t + fold), new Point(r, t + fold));

                dc.DrawText(note.Text, new Point(l + _fontSize * 0.6, t + _fontSize * 0.4));
            }
        }

        private void DrawFrames(DrawingContext dc)
        {
            foreach (FrameGeom frame in _layout.Frames)
            {
                Rect rect = frame.Rect;

                // 官方帧：外框 1.5px 黑，标签页 1.5px 黑填充，else 分隔线是 1px 黑色虚线。
                // 外框分四边画，被分隔线覆盖的那段让位给虚线，避免实线盖住虚线。
                var dividers = new List<LineGeom>();
                foreach (FrameSectionGeom section in frame.Sections)
                {
                    if (section.HasDivider)
                        dividers.Add(new LineGeom
                        {
                            X1 = rect.Left,
                            Y1 = section.DividerY,
                            X2 = rect.Right,
                            Y2 = section.DividerY
                        });
                }

                DrawFrameEdge(dc, rect.Left, rect.Top, rect.Right, rect.Top, dividers);          // 上
                DrawFrameEdge(dc, rect.Left, rect.Bottom, rect.Right, rect.Bottom, dividers);    // 下
                DrawFrameEdge(dc, rect.Left, rect.Top, rect.Left, rect.Bottom, dividers);        // 左
                DrawFrameEdge(dc, rect.Right, rect.Top, rect.Right, rect.Bottom, dividers);      // 右

                // else 分隔线：官方是 1px 黑色虚线，全宽
                foreach (LineGeom d in dividers)
                    dc.DrawLine(_frameDashPen, new Point(d.X1, d.Y1), new Point(d.X2, d.Y2));

                // 左上角的小标签页：背景填充 + 右下角削一刀的折角，写 alt / loop 这类关键字
                double tabW = frame.Kind.Width + _fontSize * 1.2;
                double fold = _fontSize * MarkdownTheme.NoteFold / 14.0;
                double tabH = frame.Kind.Height + _fontSize * 0.3;
                var tab = new StreamGeometry();
                using (StreamGeometryContext ctx = tab.Open())
                {
                    ctx.BeginFigure(new Point(rect.Left, rect.Top), true, true);
                    ctx.LineTo(new Point(rect.Left + tabW - fold, rect.Top), true, false);
                    ctx.LineTo(new Point(rect.Left + tabW, rect.Top + fold), true, false);
                    ctx.LineTo(new Point(rect.Left + tabW, rect.Top + tabH), true, false);
                    ctx.LineTo(new Point(rect.Left, rect.Top + tabH), true, false);
                }
                tab.Freeze();
                dc.DrawGeometry(_segmentFill, _frameSegmentPen, tab);
                dc.DrawText(frame.Kind, new Point(rect.Left + _fontSize * 0.6, rect.Top + _fontSize * 0.15));

                for (int i = 0; i < frame.Sections.Count; i++)
                {
                    FrameSectionGeom section = frame.Sections[i];
                    double labelX;
                    if (section.HasDivider)
                    {
                        labelX = rect.Left + _fontSize * 0.6;
                    }
                    else
                    {
                        // 第 0 段的标签排在关键字标签页的右边
                        labelX = rect.Left + tabW + _fontSize * 0.6;
                    }
                    if (section.Label != null)
                        dc.DrawText(section.Label, new Point(labelX, section.Y));
                }
            }
        }

        private sealed class LineGeom
        {
            public double X1, Y1, X2, Y2;
        }

        /// <summary>
        /// 画一条框边。与分隔线重合的整段让位给虚线，其余部分用实线。
        /// 框边总是水平或竖直，分隔线总是水平全宽，因此只有「上/下」横边可能被分隔线覆盖。
        /// </summary>
        private void DrawFrameEdge(DrawingContext dc, double x1, double y1, double x2, double y2,
            List<LineGeom> dividers)
        {
            // 竖边不可能与水平分隔线重合
            if (Near(x1, x2))
            {
                dc.DrawLine(_framePen, new Point(x1, y1), new Point(x2, y2));
                return;
            }

            // 横边：若其 y 与某条分隔线 y 重合，则跳过（分隔线单独画虚线）
            foreach (LineGeom d in dividers)
            {
                if (Near(y1, d.Y1))
                    return;
            }
            dc.DrawLine(_framePen, new Point(x1, y1), new Point(x2, y2));
        }

        private static bool Near(double a, double b)
        {
            return Math.Abs(a - b) < 0.5;
        }

        private void DrawArrows(DrawingContext dc)
        {
            foreach (ArrowGeom arrow in _layout.Arrows)
            {
                Pen pen = arrow.Dashed ? _dashPen : _linePen;
                double x0 = _layout.Lifelines[arrow.From];
                double x1 = _layout.Lifelines[arrow.To];

                if (arrow.Self)
                {
                    double xr = x0 + arrow.SelfWidth;
                    double yb = arrow.Y + arrow.SelfHeight;
                    dc.DrawLine(pen, new Point(x0, arrow.Y), new Point(xr, arrow.Y));
                    dc.DrawLine(pen, new Point(xr, arrow.Y), new Point(xr, yb));
                    dc.DrawLine(pen, new Point(xr, yb), new Point(x0, yb));
                    DrawHead(dc, new Point(x0, yb), -1, arrow);
                    if (arrow.Label != null)
                        dc.DrawText(arrow.Label, new Point(xr + _fontSize * 0.3, arrow.LabelTop));
                }
                else
                {
                    int dir = x1 >= x0 ? 1 : -1;
                    dc.DrawLine(pen, new Point(x0, arrow.Y), new Point(x1, arrow.Y));
                    DrawHead(dc, new Point(x1, arrow.Y), dir, arrow);
                    if (arrow.Label != null)
                        dc.DrawText(arrow.Label, new Point(
                            arrow.LabelCenterX - arrow.Label.Width / 2, arrow.LabelTop));
                }
            }
        }

        /// <summary>箭头头。dir=1 指向右，-1 指向左。</summary>
        private void DrawHead(DrawingContext dc, Point tip, int dir, ArrowGeom arrow)
        {
            double size = _fontSize * 0.5;
            double backX = tip.X - dir * size;
            double dy = size * 0.45;

            if (arrow.Lost)
            {
                dc.DrawLine(_linePen, new Point(tip.X - size * 0.4, tip.Y - dy),
                    new Point(tip.X + size * 0.4, tip.Y + dy));
                dc.DrawLine(_linePen, new Point(tip.X - size * 0.4, tip.Y + dy),
                    new Point(tip.X + size * 0.4, tip.Y - dy));
                return;
            }

            if (arrow.Open)
            {
                dc.DrawLine(_linePen, tip, new Point(backX, tip.Y - dy));
                dc.DrawLine(_linePen, tip, new Point(backX, tip.Y + dy));
                return;
            }

            var geometry = new StreamGeometry();
            using (StreamGeometryContext ctx = geometry.Open())
            {
                ctx.BeginFigure(tip, true, true);
                ctx.LineTo(new Point(backX, tip.Y - dy), true, false);
                ctx.LineTo(new Point(backX, tip.Y + dy), true, false);
            }
            geometry.Freeze();
            dc.DrawGeometry(_line, null, geometry);
        }

        private void DrawDividers(DrawingContext dc)
        {
            double left = _layout.SpanLeft;
            double right = _layout.SpanRight;
            double center = _layout.SpanCenter;
            foreach (DividerGeom divider in _layout.Dividers)
            {
                if (divider.Text == null)
                {
                    dc.DrawLine(_linePen, new Point(left, divider.Y), new Point(right, divider.Y));
                    continue;
                }
                // 线在文字两侧断开，文字压在正中
                double half = divider.Text.Width / 2 + _fontSize * 0.6;
                double gapLeft = center - half;
                double gapRight = center + half;
                if (gapLeft > left)
                    dc.DrawLine(_linePen, new Point(left, divider.Y), new Point(gapLeft, divider.Y));
                if (gapRight < right)
                    dc.DrawLine(_linePen, new Point(gapRight, divider.Y), new Point(right, divider.Y));
                dc.DrawText(divider.Text, new Point(center - divider.Text.Width / 2,
                    divider.Y - divider.Text.Height / 2));
            }
        }

        private void DrawDelays(DrawingContext dc)
        {
            double center = _layout.SpanCenter;
            foreach (DelayGeom delay in _layout.Delays)
            {
                if (delay.Text != null)
                {
                    dc.DrawText(delay.Text, new Point(center - delay.Text.Width / 2, delay.Y));
                }
                else
                {
                    // 没文字的延时画一段竖虚线，表示这里停了一会儿
                    dc.DrawLine(_lifelinePen,
                        new Point(center, delay.Y), new Point(center, delay.Y + _fontSize * 0.8));
                }
            }
        }
    }
}
