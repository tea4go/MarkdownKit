using System;
using System.Collections.Generic;

namespace MarkdownKit.Mermaid
{
    /// <summary>流程图形状。认不出的方括号写法一律按矩形排。</summary>
    internal enum FlowShape
    {
        Rect,
        Rounded,
        Diamond,
        Circle
    }

    /// <summary>流程图里的一个节点。</summary>
    internal sealed class FlowNode
    {
        public string Id;
        public string Text;
        public FlowShape Shape = FlowShape.Rect;

        // 下面几项由布局阶段填
        public int Layer;
        public double Width;
        public double Height;
        public double X;
        public double Y;
    }

    /// <summary>一条连线。</summary>
    internal sealed class FlowEdge
    {
        public string From;
        public string To;
        public string Label;
        public bool Dashed;
    }

    /// <summary>一张流程图：方向 + 节点（按首次出现排）+ 连线。</summary>
    internal sealed class Flowchart
    {
        /// <summary>true 表示从左到右（LR / RL），false 表示从上到下（TD / TB / BT）。</summary>
        public bool LeftToRight;

        public readonly List<FlowNode> Nodes = new List<FlowNode>();
        public readonly List<FlowEdge> Edges = new List<FlowEdge>();

        /// <summary>布局出来的画布尺寸（含边距），由视图填。</summary>
        public double Width;
        public double Height;

        public FlowNode Find(string id)
        {
            foreach (FlowNode node in Nodes)
            {
                if (string.Equals(node.Id, id, StringComparison.Ordinal))
                    return node;
            }
            return null;
        }

        public FlowNode Ensure(string id)
        {
            FlowNode node = Find(id);
            if (node != null)
                return node;
            node = new FlowNode { Id = id, Text = id };
            Nodes.Add(node);
            return node;
        }
    }

    /// <summary>
    /// Mermaid flowchart / graph 的解析。
    /// 认：flowchart|graph 加方向、A[文本]/(文本)/{判定}/((圆))/[[文本]] 这些节点写法、
    /// A --&gt; B、A --- B、A -.-&gt; B、A --&gt;|标签| B、A -- 标签 --&gt; B。
    /// 不支持：子图、样式类、一条线串三个以上节点（只认第一条边）。这些行直接跳过，不报错。
    /// </summary>
    internal static class MermaidFlowchartParser
    {
        /// <summary>连线记号，长的排前面，避免 --&gt; 被 -&gt; 抢先匹配掉。</summary>
        private static readonly string[] Arrows = { "-.->", "-->", "---", "==>", "=>", "->", "--x", "--o" };

        public static Flowchart Parse(string source)
        {
            var chart = new Flowchart();
            string[] lines = Split(source);

            foreach (string raw in lines)
            {
                string line = StripComment(raw).Trim();
                if (line.Length == 0)
                    continue;

                if (line.StartsWith("flowchart", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("graph", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyDirection(chart, line);
                    continue;
                }

                // 这几类行跟节点/连线无关，跳过
                if (line.StartsWith("subgraph", StringComparison.OrdinalIgnoreCase)
                    || line.Equals("end", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("direction", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("style", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("classDef", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("class", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("linkStyle", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("click", StringComparison.OrdinalIgnoreCase))
                    continue;

                ParseLine(chart, line);
            }

            // 只留两端都存在的连线（解析时都会建节点，这里是保险）
            chart.Edges.RemoveAll(e => chart.Find(e.From) == null || chart.Find(e.To) == null);
            return chart;
        }

        private static void ApplyDirection(Flowchart chart, string line)
        {
            string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            string dir = parts.Length > 1 ? parts[1].ToUpperInvariant() : "TD";
            chart.LeftToRight = dir == "LR" || dir == "RL";
        }

        /// <summary>解析一行「节点 连线 节点」，只认第一条连线。</summary>
        private static void ParseLine(Flowchart chart, string line)
        {
            int arrowAt;
            string arrow;
            if (!FindArrow(line, out arrowAt, out arrow))
            {
                // 没有连线：可能只是一个节点定义
                ParseNodeSpec(chart, line);
                return;
            }

            string left = line.Substring(0, arrowAt).Trim();
            string right = line.Substring(arrowAt + arrow.Length).Trim();

            string label = null;
            // A -->|标签| B
            if (right.StartsWith("|", StringComparison.Ordinal))
            {
                int close = right.IndexOf('|', 1);
                if (close > 0)
                {
                    label = right.Substring(1, close - 1).Trim();
                    right = right.Substring(close + 1).Trim();
                }
            }
            else
            {
                // A -- 标签 --> B：连线记号前面那段就是标签
                int dash = left.LastIndexOf("--", StringComparison.Ordinal);
                if (dash > 0)
                {
                    string middle = left.Substring(dash + 2).Trim();
                    if (middle.Length > 0 && middle.IndexOf('[') < 0 && middle.IndexOf('{') < 0
                        && middle.IndexOf('(') < 0)
                    {
                        label = middle;
                        left = left.Substring(0, dash).Trim();
                    }
                }
            }

            // 右边可能还串着下一条边，这里只取第一个节点
            int nextArrow;
            string ignored;
            if (FindArrow(right, out nextArrow, out ignored))
                right = right.Substring(0, nextArrow).Trim();

            FlowNode from = ParseNodeSpec(chart, left);
            FlowNode to = ParseNodeSpec(chart, right);
            if (from == null || to == null)
                return;

            chart.Edges.Add(new FlowEdge
            {
                From = from.Id,
                To = to.Id,
                Label = string.IsNullOrEmpty(label) ? null : label,
                Dashed = arrow.IndexOf('.') >= 0 || arrow.StartsWith("-.", StringComparison.Ordinal)
            });
        }

        /// <summary>在括号之外找第一个连线记号。</summary>
        private static bool FindArrow(string line, out int at, out string arrow)
        {
            at = -1;
            arrow = null;
            int depth = 0;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '[' || c == '{' || c == '(')
                {
                    depth++;
                    continue;
                }
                if (c == ']' || c == '}' || c == ')')
                {
                    if (depth > 0)
                        depth--;
                    continue;
                }
                if (depth != 0)
                    continue;

                foreach (string candidate in Arrows)
                {
                    if (string.CompareOrdinal(line, i, candidate, 0, candidate.Length) != 0)
                        continue;
                    // --- 之后还能跟 >，即 --->，按 --> 处理
                    if (candidate == "---" && i + 3 < line.Length && line[i + 3] == '>')
                        continue;

                    at = i;
                    arrow = candidate;
                    return true;
                }
            }
            return false;
        }

        /// <summary>解析一个节点写法：id 加可选的形状括号。已经定义过文本就不再覆盖（保留第一次的）。</summary>
        private static FlowNode ParseNodeSpec(Flowchart chart, string spec)
        {
            spec = spec.Trim();
            if (spec.Length == 0)
                return null;

            // id：字母、数字、下划线，直到遇到形状括号或空白
            int end = 0;
            while (end < spec.Length && (char.IsLetterOrDigit(spec[end]) || spec[end] == '_' || spec[end] == '-' || spec[end] == '.'))
                end++;
            string id = spec.Substring(0, end);
            if (id.Length == 0)
                return null;

            FlowNode node = chart.Ensure(id);
            string rest = spec.Substring(end).Trim();
            if (rest.Length == 0)
                return node;

            FlowShape shape = FlowShape.Rect;
            string text = null;

            if (rest.StartsWith("((", StringComparison.Ordinal) && rest.EndsWith("))", StringComparison.Ordinal))
            {
                shape = FlowShape.Circle;
                text = rest.Substring(2, rest.Length - 4);
            }
            else if (rest.StartsWith("([", StringComparison.Ordinal) && rest.EndsWith("])", StringComparison.Ordinal))
            {
                shape = FlowShape.Rounded;
                text = rest.Substring(2, rest.Length - 4);
            }
            else if (rest.StartsWith("[[", StringComparison.Ordinal) && rest.EndsWith("]]", StringComparison.Ordinal))
            {
                shape = FlowShape.Rect;
                text = rest.Substring(2, rest.Length - 4);
            }
            else if (rest.StartsWith("{{", StringComparison.Ordinal) && rest.EndsWith("}}", StringComparison.Ordinal))
            {
                shape = FlowShape.Diamond;
                text = rest.Substring(2, rest.Length - 4);
            }
            else if (rest.StartsWith("[(", StringComparison.Ordinal) && rest.EndsWith(")]", StringComparison.Ordinal))
            {
                shape = FlowShape.Rect;
                text = rest.Substring(2, rest.Length - 4);
            }
            else if (rest.StartsWith("[", StringComparison.Ordinal) && rest.EndsWith("]", StringComparison.Ordinal))
            {
                shape = FlowShape.Rect;
                text = rest.Substring(1, rest.Length - 2);
            }
            else if (rest.StartsWith("(", StringComparison.Ordinal) && rest.EndsWith(")", StringComparison.Ordinal))
            {
                shape = FlowShape.Rounded;
                text = rest.Substring(1, rest.Length - 2);
            }
            else if (rest.StartsWith("{", StringComparison.Ordinal) && rest.EndsWith("}", StringComparison.Ordinal))
            {
                shape = FlowShape.Diamond;
                text = rest.Substring(1, rest.Length - 2);
            }
            else if (rest.StartsWith(">", StringComparison.Ordinal) && rest.EndsWith("]", StringComparison.Ordinal))
            {
                shape = FlowShape.Rect;
                text = rest.Substring(1, rest.Length - 2);
            }
            else
            {
                return node;
            }

            if (text != null)
            {
                text = Unquote(text.Trim()).Replace("<br/>", "\n").Replace("<br>", "\n");
                if (text.Length > 0)
                {
                    node.Text = text;
                    node.Shape = shape;
                }
            }
            return node;
        }

        /// <summary>去掉包在文字外层的引号。</summary>
        private static string Unquote(string text)
        {
            if (text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"')
                return text.Substring(1, text.Length - 2);
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
