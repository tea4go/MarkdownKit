using System;
using System.Collections.Generic;

namespace MarkdownKit.Mermaid
{
    /// <summary>
    /// flowchart.js 的 DSL 解析，产出与 Mermaid 流程图相同的 Flowchart 模型，
    /// 布局和绘制直接复用 MermaidFlowchartView——不写第二套流程图渲染器。
    ///
    /// 认得的写法（其余行直接跳过，不报错）：
    ///   id=&gt;type: label     节点定义，type ∈ start / end / operation / condition / inputoutput / subroutine
    ///   A-&gt;B-&gt;C             链式连线
    ///   cond(yes)-&gt;e         括号里是这条出边的标签
    /// </summary>
    internal static class FlowchartJsParser
    {
        public static Flowchart Parse(string source)
        {
            var chart = new Flowchart();

            foreach (string raw in Split(source))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("%%", StringComparison.Ordinal))
                    continue;

                int def = line.IndexOf("=>", StringComparison.Ordinal);
                if (def > 0)
                    ParseDefinition(chart, line, def);
                else
                    ParseConnections(chart, line);
            }

            // 连线两端都会 Ensure，这里只是保险
            chart.Edges.RemoveAll(e => chart.Find(e.From) == null || chart.Find(e.To) == null);
            return chart;
        }

        /// <summary>id=&gt;type: label。</summary>
        private static void ParseDefinition(Flowchart chart, string line, int at)
        {
            string id = line.Substring(0, at).Trim();
            if (id.Length == 0)
                return;

            string rest = line.Substring(at + 2).Trim();
            int colon = rest.IndexOf(':');
            string type = (colon < 0 ? rest : rest.Substring(0, colon)).Trim();
            string label = colon < 0 ? null : rest.Substring(colon + 1).Trim();

            FlowNode node = chart.Ensure(id);
            node.Shape = ShapeOf(type);
            if (!string.IsNullOrEmpty(label))
                node.Text = label;
        }

        private static FlowShape ShapeOf(string type)
        {
            switch (type.ToLowerInvariant())
            {
                case "start":
                case "end":
                    return FlowShape.Rounded;
                case "condition":
                    return FlowShape.Diamond;
                default:
                    return FlowShape.Rect;
            }
        }

        /// <summary>A-&gt;B-&gt;C 或 cond(yes)-&gt;e，括号是前一条边的标签。</summary>
        private static void ParseConnections(Flowchart chart, string line)
        {
            string[] parts = line.Split(new[] { "->" }, StringSplitOptions.None);
            if (parts.Length < 2)
                return;

            for (int i = 0; i < parts.Length - 1; i++)
            {
                string from;
                string label;
                SplitRef(parts[i], out from, out label);
                string to;
                string ignored;
                SplitRef(parts[i + 1], out to, out ignored);

                if (from.Length == 0 || to.Length == 0)
                    continue;

                chart.Edges.Add(new FlowEdge
                {
                    From = chart.Ensure(from).Id,
                    To = chart.Ensure(to).Id,
                    Label = string.IsNullOrEmpty(label) ? null : label
                });
            }
        }

        /// <summary>id 或 id(label)。</summary>
        private static void SplitRef(string spec, out string id, out string label)
        {
            spec = spec.Trim();
            label = null;

            int open = spec.IndexOf('(');
            if (open > 0 && spec.EndsWith(")", StringComparison.Ordinal))
            {
                id = spec.Substring(0, open).Trim();
                label = spec.Substring(open + 1, spec.Length - open - 2).Trim();
            }
            else
            {
                id = spec;
            }
        }

        private static string[] Split(string source)
        {
            return (source ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        }
    }
}
