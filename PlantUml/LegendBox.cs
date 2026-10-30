using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace MarkdownKit.PlantUml
{
    /// <summary>
    /// 图例框（legend ... endlegend）算好的几何：一个圆角框加里面的内容。
    /// 内容要么是普通文本行，要么是 <c>| 甲 | 乙 |</c> 这样的表格——官方在 legend 里
    /// 就是按表格画的。三种图型共用同一套量尺寸/绘制代码，布局只负责把它摆到哪。
    /// </summary>
    internal sealed class LegendGeom
    {
        /// <summary>框的位置和大小，由布局填。</summary>
        public Rect Rect;

        /// <summary>普通文本行（不是表格时用）。</summary>
        public readonly List<FormattedText> Lines = new List<FormattedText>();

        /// <summary>表格行，每格一个 FormattedText。</summary>
        public readonly List<List<FormattedText>> Rows = new List<List<FormattedText>>();

        /// <summary>表格每列的宽度（含格子左右内边距）。</summary>
        public readonly List<double> ColumnWidths = new List<double>();

        /// <summary>普通行行高。</summary>
        public double LineHeight;

        /// <summary>表格行行高。</summary>
        public double RowHeight;

        /// <summary>框的内边距。</summary>
        public double Pad;

        public bool HasTable { get { return Rows.Count > 0; } }

        /// <summary>内容整体尺寸（不含内边距）。</summary>
        public Size ContentSize()
        {
            double w = 0;
            foreach (FormattedText t in Lines)
                w = Math.Max(w, t.Width);
            if (HasTable)
            {
                double tw = 0;
                foreach (double c in ColumnWidths)
                    tw += c;
                w = Math.Max(w, tw);
            }
            double h = Lines.Count * LineHeight + Rows.Count * RowHeight;
            return new Size(w, h);
        }

        public void Shift(double dx, double dy)
        {
            Rect = new Rect(Rect.X + dx, Rect.Y + dy, Rect.Width, Rect.Height);
        }
    }

    /// <summary>图例的量尺寸与绘制。三种图型（状态 / 时序 / 组件）共用。</summary>
    internal static class LegendBox
    {
        private static double U(double v, double fs)
        {
            return v * fs / 14.0;
        }

        /// <summary>把图例正文量成几何。正文为空时返回 null。</summary>
        public static LegendGeom Build(IList<string> text, Typeface typeface, Brush brush, double fs, double ppd)
        {
            if (text == null || text.Count == 0)
                return null;

            var geom = new LegendGeom();
            geom.Pad = U(8, fs);

            FormattedText probe = Text("测", typeface, brush, fs, ppd);
            geom.LineHeight = probe.Height + U(3, fs);
            geom.RowHeight = probe.Height + U(4, fs);

            // 整段都是 | 开头的行才当表格，混着写就整段按普通文本画
            bool table = true;
            foreach (string raw in text)
            {
                if (!IsTableRow(raw))
                {
                    table = false;
                    break;
                }
            }

            if (table)
            {
                double cellPad = U(4, fs);
                foreach (string raw in text)
                {
                    string[] cells = SplitRow(raw);
                    var row = new List<FormattedText>();
                    for (int i = 0; i < cells.Length; i++)
                    {
                        FormattedText ft = Text(cells[i], typeface, brush, fs, ppd);
                        row.Add(ft);
                        while (geom.ColumnWidths.Count <= i)
                            geom.ColumnWidths.Add(0);
                        geom.ColumnWidths[i] = Math.Max(geom.ColumnWidths[i], ft.Width + cellPad * 2);
                    }
                    geom.Rows.Add(row);
                }
                return geom;
            }

            foreach (string raw in text)
                geom.Lines.Add(Text(raw, typeface, brush, fs, ppd));
            return geom;
        }

        /// <summary>按内容算框的大小，并把它摆到 rect 里（rect 只给位置，尺寸这里定）。</summary>
        public static Rect Measure(LegendGeom geom)
        {
            Size content = geom.ContentSize();
            return new Rect(0, 0, content.Width + geom.Pad * 2, content.Height + geom.Pad * 2);
        }

        public static void Draw(DrawingContext dc, LegendGeom geom, Brush fill, Pen pen, double fs)
        {
            double corner = U(8, fs);
            dc.DrawRoundedRectangle(fill, pen, geom.Rect, corner, corner);

            double left = geom.Rect.X + geom.Pad;
            double top = geom.Rect.Y + geom.Pad;

            if (!geom.HasTable)
            {
                double y = top;
                foreach (FormattedText line in geom.Lines)
                {
                    dc.DrawText(line, new Point(left, y));
                    y += geom.LineHeight;
                }
                return;
            }

            double tableW = 0;
            foreach (double c in geom.ColumnWidths)
                tableW += c;
            double tableH = geom.Rows.Count * geom.RowHeight;

            for (int r = 0; r <= geom.Rows.Count; r++)
            {
                double y = top + r * geom.RowHeight;
                dc.DrawLine(pen, new Point(left, y), new Point(left + tableW, y));
            }
            double x = left;
            dc.DrawLine(pen, new Point(x, top), new Point(x, top + tableH));
            foreach (double c in geom.ColumnWidths)
            {
                x += c;
                dc.DrawLine(pen, new Point(x, top), new Point(x, top + tableH));
            }

            double cellPad = U(4, fs);
            for (int r = 0; r < geom.Rows.Count; r++)
            {
                double cx = left;
                List<FormattedText> row = geom.Rows[r];
                for (int c = 0; c < row.Count && c < geom.ColumnWidths.Count; c++)
                {
                    dc.DrawText(row[c], new Point(cx + cellPad, top + r * geom.RowHeight + U(2, fs)));
                    cx += geom.ColumnWidths[c];
                }
            }
        }

        private static bool IsTableRow(string raw)
        {
            string s = raw == null ? "" : raw.Trim();
            return s.Length >= 2 && s[0] == '|' && s.IndexOf('|', 1) > 0;
        }

        /// <summary>把 | 甲 | 乙 | 拆成 {"甲","乙"}。</summary>
        private static string[] SplitRow(string raw)
        {
            string s = raw.Trim();
            var parts = new List<string>();
            int start = 1;
            for (int i = 1; i < s.Length; i++)
            {
                if (s[i] != '|')
                    continue;
                parts.Add(s.Substring(start, i - start).Trim());
                start = i + 1;
            }
            return parts.ToArray();
        }

        private static FormattedText Text(string text, Typeface typeface, Brush brush, double fs, double ppd)
        {
            return new FormattedText(text ?? string.Empty, CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight, typeface, fs, brush, ppd);
        }
    }
}
