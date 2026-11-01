using System;
using System.Collections.Generic;
using System.Globalization;

namespace MarkdownKit.Mermaid
{
    /// <summary>饼图里的一瓣。</summary>
    internal sealed class PieSlice
    {
        public string Label;
        public double Value;
    }

    /// <summary>一张 Mermaid 饼图：标题 + 是否显示数值 + 各瓣。</summary>
    internal sealed class PieChart
    {
        public string Title;
        public bool ShowData;
        public readonly List<PieSlice> Slices = new List<PieSlice>();
    }

    /// <summary>
    /// Mermaid pie 的解析。
    /// 认：pie [showData] [title 标题]、单独的 title 行、``"标签" : 数值`` 数据行。
    /// 数值支持小数，非正数或解析不出来的行直接跳过，不报错。
    /// </summary>
    internal static class MermaidPieParser
    {
        public static PieChart Parse(string source)
        {
            var chart = new PieChart();
            string[] lines = Split(source);
            bool first = true;

            foreach (string raw in lines)
            {
                string line = StripComment(raw).Trim();
                if (line.Length == 0 || line == "---")
                    continue;

                if (first)
                {
                    first = false;
                    if (StartsWithWord(line, "pie"))
                        line = line.Substring(3).Trim();
                }

                if (line.Length == 0)
                    continue;

                if (StartsWithWord(line, "title"))
                {
                    chart.Title = line.Substring(5).Trim();
                    continue;
                }

                if (StartsWithWord(line, "showData"))
                {
                    chart.ShowData = true;
                    line = line.Substring(8).Trim();
                    if (line.Length == 0)
                        continue;
                    if (StartsWithWord(line, "title"))
                    {
                        chart.Title = line.Substring(5).Trim();
                        continue;
                    }
                }

                // 标签里可能带冒号，分隔符取最后一个
                int colon = line.LastIndexOf(':');
                if (colon < 0)
                    continue;

                string label = Unquote(line.Substring(0, colon).Trim());
                string valueText = line.Substring(colon + 1).Trim();

                double value;
                if (!double.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                    continue;
                if (value <= 0)
                    continue;

                chart.Slices.Add(new PieSlice { Label = label, Value = value });
            }

            return chart;
        }

        /// <summary>整行就是这个词，或者后面跟着空白。</summary>
        private static bool StartsWithWord(string line, string word)
        {
            if (!line.StartsWith(word, StringComparison.OrdinalIgnoreCase))
                return false;
            return line.Length == word.Length || char.IsWhiteSpace(line[word.Length]);
        }

        private static string Unquote(string text)
        {
            if (text.Length >= 2
                && ((text[0] == '"' && text[text.Length - 1] == '"')
                    || (text[0] == '\'' && text[text.Length - 1] == '\'')))
                return text.Substring(1, text.Length - 2).Trim();
            return text;
        }

        private static string StripComment(string line)
        {
            int at = line.IndexOf("%%", StringComparison.Ordinal);
            return at < 0 ? line : line.Substring(0, at);
        }

        private static string[] Split(string source)
        {
            return (source ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        }
    }
}
