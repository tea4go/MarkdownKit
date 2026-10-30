using System;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace MarkdownKit.PlantUml
{
    /// <summary>
    /// 画一张甘特图：左边 Start / End / Duration 表，右边按天分格的时间轴，
    /// 轴上下各一份"月份 / 星期 / 日号"，任务条画在轴上，依赖之间补一个小箭头。
    /// </summary>
    internal sealed class GanttView : FrameworkElement
    {
        private const double SideMargin = 40;

        private readonly GanttDiagram _diagram;
        private readonly double _fontSize;
        private GanttLayout _layout;
        private double _scale = 1;

        private readonly Brush _fill;
        private readonly Brush _barFill;
        private readonly Brush _headFill;
        private readonly Brush _border;
        private readonly Pen _borderPen;
        private readonly Pen _gridPen;

        public static readonly DependencyProperty AvailableWidthProperty =
            DependencyProperty.Register(nameof(AvailableWidth), typeof(double), typeof(GanttView),
                new FrameworkPropertyMetadata(double.PositiveInfinity,
                    FrameworkPropertyMetadataOptions.AffectsMeasure));

        public double AvailableWidth
        {
            get { return (double)GetValue(AvailableWidthProperty); }
            set { SetValue(AvailableWidthProperty, value); }
        }

        public GanttView(GanttDiagram diagram, double fontSize, FrameworkElement host)
        {
            _diagram = diagram;
            _fontSize = fontSize;
            HorizontalAlignment = HorizontalAlignment.Center;

            _fill = MarkdownTheme.Background;
            _barFill = MarkdownTheme.Markdown.SegmentFill;
            _headFill = MarkdownTheme.Markdown.CodeBackground;
            _border = MarkdownTheme.Markdown.Text;

            _borderPen = Frozen(new Pen(_border, 1.2));
            var grid = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0));
            grid.Freeze();
            _gridPen = Frozen(new Pen(grid, 0.8));

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
            _layout = GanttLayoutBuilder.Build(_diagram, _fontSize, ppd);
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

            if (_layout.Rows.Count > 0)
            {
                DrawCalendar(dc);
                DrawTable(dc);
                DrawBars(dc);
            }

            if (_layout.Title != null)
            {
                FormattedText title = _layout.Title.Text;
                dc.DrawText(title, new Point(_layout.Title.CenterX - title.Width / 2, _layout.Title.Top));
            }

            if (_scale != 1)
                dc.Pop();
        }

        /// <summary>轴上下各一份表头：月份、星期字母、日号，以及每天一条竖线。</summary>
        private void DrawCalendar(DrawingContext dc)
        {
            double monthH = _layout.MonthHeight;
            double weekH = _layout.WeekdayHeight;
            double numH = _layout.NumberHeight;
            double bandH = monthH + weekH + numH;
            double rowsTop = _layout.Rows[0].Row.Top;
            double rowsBottom = _layout.Rows[_layout.Rows.Count - 1].Row.Bottom;
            double top = rowsTop - bandH;
            double axisLeft = _layout.AxisX;
            double axisRight = _layout.AxisX + _layout.AxisWidth;

            // 底色带先铺，再写字——反过来的话字会被盖住
            dc.DrawRectangle(_headFill, null, new Rect(axisLeft, top, _layout.AxisWidth, bandH));
            dc.DrawRectangle(_headFill, null, new Rect(axisLeft, rowsBottom, _layout.AxisWidth, bandH));

            foreach (GanttDayGeom day in _layout.Days)
                dc.DrawLine(_gridPen, new Point(day.X, rowsTop), new Point(day.X, rowsBottom));
            dc.DrawLine(_gridPen, new Point(axisRight, rowsTop), new Point(axisRight, rowsBottom));

            // 月份（上、下各一份）
            foreach (GanttMonthGeom month in _layout.Months)
            {
                Rect r = month.Rect;
                DrawCentered(dc, month.Text, r);
                DrawCentered(dc, month.Text, new Rect(r.X, rowsBottom, r.Width, monthH));
            }

            // 星期字母 + 日号（上、下各一份）
            foreach (GanttDayGeom day in _layout.Days)
            {
                double x = day.X;
                DrawCentered(dc, day.Weekday, new Rect(x, top + monthH, _layout.DayWidth, weekH));
                DrawCentered(dc, day.Number, new Rect(x, top + monthH + weekH, _layout.DayWidth, numH));
                DrawCentered(dc, day.Number, new Rect(x, rowsBottom + monthH, _layout.DayWidth, numH));
                DrawCentered(dc, day.Weekday, new Rect(x, rowsBottom + monthH + numH, _layout.DayWidth, weekH));
            }

            dc.DrawLine(_borderPen, new Point(axisLeft, top), new Point(axisLeft, rowsTop));
            dc.DrawLine(_borderPen, new Point(axisLeft, rowsBottom), new Point(axisLeft, rowsBottom + bandH));
            dc.DrawLine(_borderPen, new Point(axisLeft, top + monthH), new Point(axisRight, top + monthH));
            dc.DrawLine(_borderPen, new Point(axisLeft, top + monthH + weekH), new Point(axisRight, top + monthH + weekH));
            dc.DrawLine(_borderPen, new Point(axisLeft, rowsBottom + monthH), new Point(axisRight, rowsBottom + monthH));
            dc.DrawLine(_borderPen, new Point(axisLeft, rowsBottom + monthH + numH), new Point(axisRight, rowsBottom + monthH + numH));
        }

        /// <summary>左边的 Start / End / Duration 表。</summary>
        private void DrawTable(DrawingContext dc)
        {
            double left = 0;
            double top = _layout.Rows[0].Row.Top;
            double bottom = _layout.Rows[_layout.Rows.Count - 1].Row.Bottom;
            double headH = _layout.WeekdayHeight + _layout.NumberHeight;

            dc.DrawRectangle(_headFill, null, new Rect(left, top - headH, _layout.TableWidth, headH));
            string[] heads = { "Start", "End", "Duration" };
            for (int c = 0; c < 3; c++)
            {
                double x0 = _layout.ColumnEdges[c];
                DrawCentered(dc, Text(heads[c], true), new Rect(x0, top - headH, _layout.ColumnWidths[c], headH));
            }

            foreach (GanttRowGeom row in _layout.Rows)
            {
                Rect r = row.Row;
                dc.DrawLine(_gridPen, new Point(left, r.Bottom), new Point(_layout.TableWidth, r.Bottom));
                DrawCentered(dc, row.Start, new Rect(_layout.ColumnEdges[0], r.Y, _layout.ColumnWidths[0], r.Height));
                DrawCentered(dc, row.End, new Rect(_layout.ColumnEdges[1], r.Y, _layout.ColumnWidths[1], r.Height));
                DrawCentered(dc, row.Days, new Rect(_layout.ColumnEdges[2], r.Y, _layout.ColumnWidths[2], r.Height));
            }

            foreach (double x in _layout.ColumnEdges)
                dc.DrawLine(_borderPen, new Point(x, top - headH), new Point(x, bottom));
        }

        /// <summary>任务条 + 依赖小箭头。</summary>
        private void DrawBars(DrawingContext dc)
        {
            foreach (GanttRowGeom row in _layout.Rows)
            {
                Rect bar = row.Bar;
                dc.DrawRectangle(_barFill, _borderPen, bar);
                FormattedText name = row.Name;
                double x = bar.X + U(4);
                double y = bar.Y + (bar.Height - name.Height) / 2;
                dc.DrawText(name, new Point(x, y));

                if (string.IsNullOrEmpty(row.Task.After))
                    continue;
                GanttRowGeom prev = Find(row.Task.After);
                if (prev == null)
                    continue;

                // 从上一环的尾巴绕到下面，再拐进本条的起点——两条常常是紧挨着的，
                // 走直线没地方画，官方也是在条的下面绕一格
                double busY = Math.Max(prev.Bar.Bottom, bar.Bottom) + U(4);
                double x0 = prev.Bar.Right - U(3);
                double x1 = bar.X + U(3);
                var geo = new StreamGeometry();
                using (StreamGeometryContext ctx = geo.Open())
                {
                    ctx.BeginFigure(new Point(x0, prev.Bar.Bottom), false, false);
                    ctx.LineTo(new Point(x0, busY), true, false);
                    ctx.LineTo(new Point(x1, busY), true, false);
                    ctx.LineTo(new Point(x1, bar.Bottom), true, false);
                }
                geo.Freeze();
                dc.DrawGeometry(null, _borderPen, geo);

                double s = _fontSize * 0.5;
                var tri = new StreamGeometry();
                using (StreamGeometryContext ctx = tri.Open())
                {
                    ctx.BeginFigure(new Point(x1, bar.Bottom), true, true);
                    ctx.LineTo(new Point(x1 - s * 0.45, bar.Bottom + s), true, false);
                    ctx.LineTo(new Point(x1 + s * 0.45, bar.Bottom + s), true, false);
                }
                tri.Freeze();
                dc.DrawGeometry(_border, null, tri);
            }
        }

        private GanttRowGeom Find(string name)
        {
            foreach (GanttRowGeom row in _layout.Rows)
            {
                if (row.Task.Name == name)
                    return row;
            }
            return null;
        }

        private double U(double v)
        {
            return v * _fontSize / 14.0;
        }

        private FormattedText Text(string text, bool bold)
        {
            return new FormattedText(text ?? string.Empty, System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(MarkdownTheme.DiagramFamily, FontStyles.Normal,
                    bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal),
                _fontSize, _border, 1.0);
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

