using System;
using System.Collections.Generic;
using System.Globalization;

namespace MarkdownKit.PlantUml
{
    /// <summary>甘特图里的一条任务。</summary>
    internal sealed class GanttTask
    {
        public string Name;

        /// <summary>起止日期（都含当天）。</summary>
        public DateTime Start;
        public DateTime End;

        /// <summary>持续天数。</summary>
        public int Days = 1;

        /// <summary>依赖的上一环（starts at [X]'s end），画一个小箭头。</summary>
        public string After;

        /// <summary>true 表示开始日期是显式给的，不用顺序推导。</summary>
        public bool FixedStart;

        public DateTime EndDate
        {
            get { return Start.AddDays(Days - 1); }
        }
    }

    /// <summary>一张甘特图解析出来的全部内容。</summary>
    internal sealed class GanttDiagram
    {
        public string Title;

        /// <summary>Project starts 给的开工日；没写就用今天。</summary>
        public DateTime ProjectStart = DateTime.Today;

        public readonly List<GanttTask> Tasks = new List<GanttTask>();

        public int Known;
        public int Unknown;

        public bool LooksLikeGantt
        {
            get { return Tasks.Count > 0; }
        }
    }

    /// <summary>
    /// 把 PlantUML 甘特图源码解析成 <see cref="GanttDiagram"/>。
    /// 认得出的写法：Project starts、[X] lasts N days、[X] starts &lt;date&gt;、
    /// [X] starts at [Y]'s end/start、[X] ends &lt;date&gt;。其余（颜色、完成度、分隔线）忽略。
    /// </summary>
    internal static class GanttParser
    {
        public static GanttDiagram Parse(string source)
        {
            var diagram = new GanttDiagram();
            if (string.IsNullOrEmpty(source))
                return diagram;

            string[] lines = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var byName = new Dictionary<string, GanttTask>();
            var pending = new List<GanttTask>();

            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '\'')
                    continue;

                if (line.StartsWith("@start", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("@end", StringComparison.OrdinalIgnoreCase)
                    || line[0] == '!')
                {
                    diagram.Known++;
                    continue;
                }
                if (line.StartsWith("title", StringComparison.OrdinalIgnoreCase)
                    && (line.Length == 5 || line[5] == ' '))
                {
                    diagram.Title = line.Length > 5 ? line.Substring(5).Trim() : "";
                    diagram.Known++;
                    continue;
                }
                if (line.StartsWith("Project starts", StringComparison.OrdinalIgnoreCase))
                {
                    DateTime d;
                    if (TryDate(line.Substring(14).Trim(), out d))
                        diagram.ProjectStart = d;
                    diagram.Known++;
                    continue;
                }
                if (line == "--" || line.StartsWith("--", StringComparison.Ordinal))
                {
                    diagram.Known++;
                    continue;
                }

                GanttTask task = Task(line, diagram, byName);
                if (task != null)
                {
                    diagram.Known++;
                    if (!pending.Contains(task))
                        pending.Add(task);
                }
                else if (Ignored(line))
                {
                    diagram.Known++;
                }
                else
                {
                    diagram.Unknown++;
                }
            }

            Resolve(diagram, pending);
            return diagram;
        }

        /// <summary>解析一条任务行。</summary>
        private static GanttTask Task(string line, GanttDiagram diagram, Dictionary<string, GanttTask> byName)
        {
            int close = line.IndexOf(']');
            if (line.Length == 0 || line[0] != '[' || close < 2)
                return null;

            string name = line.Substring(1, close - 1).Trim();
            string rest = line.Substring(close + 1).Trim();

            GanttTask task;
            if (!byName.TryGetValue(name, out task))
            {
                task = new GanttTask { Name = name };
                byName[name] = task;
                diagram.Tasks.Add(task);
            }

            if (rest.StartsWith("lasts", StringComparison.OrdinalIgnoreCase))
            {
                task.Days = ReadDays(rest.Substring(5).Trim());
                return task;
            }
            if (rest.StartsWith("starts at", StringComparison.OrdinalIgnoreCase))
            {
                string what = rest.Substring(9).Trim();
                int quote = what.IndexOf("'s", StringComparison.Ordinal);
                if (quote > 0 && what.StartsWith("[", StringComparison.Ordinal))
                {
                    // starts at [X]'s end / start
                    string other = what.Substring(1, what.IndexOf(']') - 1).Trim();
                    string which = what.Substring(quote + 2).Trim();
                    if (which.StartsWith("end", StringComparison.OrdinalIgnoreCase))
                        task.After = other;
                    else
                        task.After = other;
                    return task;
                }
                DateTime d;
                if (TryDate(what, out d))
                {
                    task.Start = d;
                    task.FixedStart = true;
                }
                return task;
            }
            if (rest.StartsWith("starts", StringComparison.OrdinalIgnoreCase))
            {
                DateTime d;
                if (TryDate(rest.Substring(6).Trim(), out d))
                {
                    task.Start = d;
                    task.FixedStart = true;
                }
                return task;
            }
            if (rest.StartsWith("ends", StringComparison.OrdinalIgnoreCase))
            {
                DateTime d;
                if (TryDate(rest.Substring(4).Trim(), out d))
                {
                    task.End = d;
                    task.FixedStart = true;   // 结束日显式给了，开始日按它倒推
                }
                return task;
            }
            if (rest.StartsWith("is ", StringComparison.OrdinalIgnoreCase))
                return task;   // is colored in / is N% completed，只认任务名

            return task;
        }

        private static bool Ignored(string line)
        {
            string[] prefixes = { "skinparam", "hide", "show", "scale", "header", "footer",
                                  "caption", "legend", "printscale", "today", "dateFormat",
                                  "now", "language", "weeks", "months", "years", "project" };
            foreach (string p in prefixes)
            {
                if (line.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static int ReadDays(string text)
        {
            // "5 days" / "5 day" / "5"
            int i = 0;
            while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.'))
                i++;
            double v;
            if (double.TryParse(text.Substring(0, i), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                return Math.Max(1, (int)Math.Round(v));
            return 1;
        }

        /// <summary>认 2026-10-01 / 2026/10/01 / 2026-10-01 三种。</summary>
        private static bool TryDate(string text, out DateTime date)
        {
            date = DateTime.MinValue;
            string s = text.Trim().Trim('"');
            if (s.Length < 8)
                return false;
            string[] formats = { "yyyy-MM-dd", "yyyy/MM/dd", "yyyy.MM.dd" };
            return DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out date);
        }

        /// <summary>把每条任务的起止日期定下来：没给开始日的按顺序接在上一条后面。</summary>
        private static void Resolve(GanttDiagram diagram, List<GanttTask> tasks)
        {
            DateTime cursor = diagram.ProjectStart;
            foreach (GanttTask task in tasks)
            {
                if (task.FixedStart && task.Start != DateTime.MinValue)
                {
                    // 开始日显式给了
                }
                else if (task.FixedStart && task.End != DateTime.MinValue)
                {
                    task.Start = task.End.AddDays(-(task.Days - 1));
                }
                else if (!string.IsNullOrEmpty(task.After))
                {
                    GanttTask other = Find(diagram, task.After);
                    task.Start = other != null ? other.EndDate.AddDays(1) : cursor;
                }
                else
                {
                    task.Start = cursor;
                }
                cursor = task.EndDate.AddDays(1);
            }

            // 依赖可能引用了排在后面的任务，再跑两轮把日期传播开
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (GanttTask task in tasks)
                {
                    if (string.IsNullOrEmpty(task.After))
                        continue;
                    GanttTask other = Find(diagram, task.After);
                    if (other != null)
                        task.Start = other.EndDate.AddDays(1);
                }
            }
        }

        private static GanttTask Find(GanttDiagram diagram, string name)
        {
            foreach (GanttTask t in diagram.Tasks)
            {
                if (t.Name == name)
                    return t;
            }
            return null;
        }
    }
}
