using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace MarkdownKit.PlantUml
{
    /// <summary>甘特图里一行任务算好的几何。</summary>
    internal sealed class GanttRowGeom
    {
        public GanttTask Task;
        public Rect Row;          // 整行（左侧表格 + 时间轴）的纵向范围
        public Rect Bar;          // 时间条
        public FormattedText Start;
        public FormattedText End;
        public FormattedText Days;
        public FormattedText Name;
    }

    /// <summary>一格日期（表头/表尾的星期字母与日号）。</summary>
    internal sealed class GanttDayGeom
    {
        public DateTime Day;
        public double X;
        public FormattedText Weekday;
        public FormattedText Number;
    }

    /// <summary>月份标题（图顶、图底各一条）。</summary>
    internal sealed class GanttMonthGeom
    {
        public Rect Rect;
        public FormattedText Text;
    }

    /// <summary>一张甘特图算好的几何。</summary>
    internal sealed class GanttLayout
    {
        public double Width;
        public double Height;
        public TitleGeom Title;

        /// <summary>左侧表格的列宽（Start / End / Duration）。</summary>
        public double[] ColumnWidths = new double[3];
        public double TableWidth;

        /// <summary>时间轴起点 x 和每天宽度。</summary>
        public double AxisX;
        public double DayWidth;
        public double AxisWidth;

        public double HeaderHeight;
        public double WeekdayHeight;
        public double NumberHeight;
        public double MonthHeight;

        public readonly List<GanttRowGeom> Rows = new List<GanttRowGeom>();
        public readonly List<GanttDayGeom> Days = new List<GanttDayGeom>();
        public readonly List<GanttMonthGeom> Months = new List<GanttMonthGeom>();

        /// <summary>左侧表格的竖线 x 位置（含两端）。</summary>
        public readonly List<double> ColumnEdges = new List<double>();
    }

    /// <summary>
    /// 由模型算出几何。左边一张 Start / End / Duration 表，右边一条按天分格的轴，
    /// 任务条落在轴上；轴的上下各印一份"月份 / 星期 / 日号"，跟官方一致。
    /// </summary>
    internal static class GanttLayoutBuilder
    {
        public static GanttLayout Build(GanttDiagram diagram, double fontSize, double pixelsPerDip)
        {
            return new Impl(diagram, fontSize, pixelsPerDip).Build();
        }

        private sealed class Impl
        {
            private readonly GanttDiagram _diagram;
            private readonly double _fs;
            private readonly double _ppd;
            private readonly Typeface _tf;
            private readonly Typeface _tfBold;
            private readonly Brush _text;
            private readonly GanttLayout _layout = new GanttLayout();

            private double U(double v)
            {
                return v * _fs / 14.0;
            }

            private double RowHeight { get { return U(20); } }
            private double CellPad { get { return U(4); } }

            public Impl(GanttDiagram diagram, double fontSize, double pixelsPerDip)
            {
                _diagram = diagram;
                _fs = fontSize;
                _ppd = pixelsPerDip <= 0 ? 1.0 : pixelsPerDip;
                _tf = new Typeface(MarkdownTheme.DiagramFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                _tfBold = MarkdownTheme.BoldTypeface(MarkdownTheme.DiagramFamily);
                _text = MarkdownTheme.Markdown.Text;
            }

            public GanttLayout Build()
            {
                if (_diagram.Tasks.Count == 0)
                    return _layout;

                double margin = _fs * 0.8;
                double top = margin;
                if (!string.IsNullOrEmpty(_diagram.Title))
                {
                    FormattedText title = Text(_diagram.Title, _tfBold, _text);
                    _layout.Title = new TitleGeom { Text = title, Top = top, CenterX = 0 };
                    top += title.Height + _fs * 0.6;
                }

                DateTime first = DateTime.MaxValue;
                DateTime last = DateTime.MinValue;
                foreach (GanttTask task in _diagram.Tasks)
                {
                    if (task.Start < first) first = task.Start;
                    if (task.EndDate > last) last = task.EndDate;
                }
                if (first > last)
                    first = last = _diagram.ProjectStart;

                // 左侧表格：三列宽度取表头和内容里最宽的那条
                string[] heads = { "Start", "End", "Duration" };
                var rows = new List<GanttRowGeom>();
                for (int c = 0; c < 3; c++)
                    _layout.ColumnWidths[c] = Text(heads[c], _tfBold, _text).Width + CellPad * 2;

                foreach (GanttTask task in _diagram.Tasks)
                {
                    var row = new GanttRowGeom { Task = task };
                    row.Start = Text(FormatDate(task.Start), _tf, _text);
                    row.End = Text(FormatDate(task.EndDate), _tf, _text);
                    row.Days = Text(task.Days + " days", _tf, _text);
                    row.Name = Text(task.Name, _tf, _text);
                    _layout.ColumnWidths[0] = Math.Max(_layout.ColumnWidths[0], row.Start.Width + CellPad * 2);
                    _layout.ColumnWidths[1] = Math.Max(_layout.ColumnWidths[1], row.End.Width + CellPad * 2);
                    _layout.ColumnWidths[2] = Math.Max(_layout.ColumnWidths[2], row.Days.Width + CellPad * 2);
                    rows.Add(row);
                }
                _layout.TableWidth = _layout.ColumnWidths[0] + _layout.ColumnWidths[1] + _layout.ColumnWidths[2];
                _layout.ColumnEdges.Add(0);
                double edge = 0;
                foreach (double w in _layout.ColumnWidths)
                {
                    edge += w;
                    _layout.ColumnEdges.Add(edge);
                }

                int dayCount = (int)(last - first).TotalDays + 1;

                // 每格多宽由"星期字母 / 日号"里最宽的那个决定：字体比官方宽（等宽字体），
                // 写死一个宽度的话 "Th" 和 "27" 会顶满格子、挤在一起看不清
                double labelW = 0;
                for (int i = 0; i < dayCount; i++)
                {
                    DateTime day = first.AddDays(i);
                    labelW = Math.Max(labelW, Text(WeekdayLetter(day), _tf, _text).Width);
                    labelW = Math.Max(labelW, Text(day.Day.ToString(CultureInfo.InvariantCulture), _tf, _text).Width);
                }
                _layout.DayWidth = Math.Max(U(14), labelW + U(6));
                _layout.AxisWidth = dayCount * _layout.DayWidth;
                _layout.AxisX = _layout.TableWidth;

                FormattedText probe = Text("测", _tf, _text);
                _layout.MonthHeight = probe.Height + U(2);
                _layout.WeekdayHeight = probe.Height + U(2);
                _layout.NumberHeight = probe.Height + U(2);
                _layout.HeaderHeight = _layout.MonthHeight + _layout.WeekdayHeight + _layout.NumberHeight;

                // 轴的上下各一份表头
                double axisTop = top + _layout.HeaderHeight;
                double rowsTop = axisTop;
                double rowsBottom = rowsTop + rows.Count * RowHeight;

                for (int i = 0; i < dayCount; i++)
                {
                    DateTime day = first.AddDays(i);
                    _layout.Days.Add(new GanttDayGeom
                    {
                        Day = day,
                        X = _layout.AxisX + i * _layout.DayWidth,
                        Weekday = Text(WeekdayLetter(day), _tf, _text),
                        Number = Text(day.Day.ToString(CultureInfo.InvariantCulture), _tf, _text)
                    });
                }

                // 月份标题：跨月就分段
                int runStart = 0;
                for (int i = 1; i <= dayCount; i++)
                {
                    if (i < dayCount && first.AddDays(i).Month == first.AddDays(runStart).Month)
                        continue;
                    double x0 = _layout.AxisX + runStart * _layout.DayWidth;
                    double x1 = _layout.AxisX + i * _layout.DayWidth;
                    FormattedText ft = Text(MonthLabel(first.AddDays(runStart)), _tf, _text);
                    _layout.Months.Add(new GanttMonthGeom
                    {
                        Rect = new Rect(x0, top, x1 - x0, _layout.MonthHeight),
                        Text = ft
                    });
                    runStart = i;
                }

                for (int i = 0; i < rows.Count; i++)
                {
                    GanttRowGeom row = rows[i];
                    row.Row = new Rect(0, rowsTop + i * RowHeight, _layout.AxisX + _layout.AxisWidth, RowHeight);
                    double x0 = _layout.AxisX + (row.Task.Start - first).TotalDays * _layout.DayWidth;
                    double x1 = _layout.AxisX + ((row.Task.EndDate - first).TotalDays + 1) * _layout.DayWidth;
                    row.Bar = new Rect(x0, row.Row.Y + U(2), Math.Max(x1 - x0, _layout.DayWidth), RowHeight - U(4));
                    _layout.Rows.Add(row);
                }

                _layout.Width = _layout.AxisX + _layout.AxisWidth + margin;
                _layout.Height = rowsBottom + _layout.HeaderHeight + margin;
                if (_layout.Title != null)
                    _layout.Title.CenterX = _layout.Width / 2;
                return _layout;
            }

            private static string WeekdayLetter(DateTime day)
            {
                string[] names = { "Su", "Mo", "Tu", "We", "Th", "Fr", "Sa" };
                return names[(int)day.DayOfWeek];
            }

            private static string MonthLabel(DateTime day)
            {
                return day.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
            }

            private static string FormatDate(DateTime day)
            {
                return day.ToString("MMM d", CultureInfo.InvariantCulture);
            }

            private FormattedText Text(string text, Typeface typeface, Brush brush)
            {
                return new FormattedText(text ?? string.Empty, CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight, typeface, _fs, brush, _ppd);
            }
        }
    }
}


