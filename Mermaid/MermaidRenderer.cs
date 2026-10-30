using System;
using System.Windows;
using MarkdownKit.PlantUml;

namespace MarkdownKit.Mermaid
{
    /// <summary>
    /// Mermaid 渲染入口。目前只接两种图：
    ///   sequenceDiagram —— 翻译成 PlantUML 的时序图模型，布局和绘制直接复用 SequenceView；
    ///   flowchart / graph —— 自己的分层布局 + Canvas 绘制。
    /// 其他图型（classDiagram / stateDiagram / pie / gantt / journey …）返回 null，
    /// 调用方退回普通代码块显示源码。
    /// </summary>
    internal static class MermaidRenderer
    {
        public static FrameworkElement TryCreate(string source, double fontSize, FrameworkElement host)
        {
            if (string.IsNullOrEmpty(source))
                return null;

            // 图内字号跟 PlantUML 那套保持同一个基准，形状常量就不用两套比例
            fontSize = MarkdownTheme.DiagramFontSize;

            string kind = FirstKeyword(source);
            try
            {
                if (kind == "sequencediagram")
                {
                    SequenceDiagram diagram = MermaidSequenceParser.Parse(source);
                    if (!diagram.LooksLikeSequence || diagram.Participants.Count == 0)
                        return null;
                    return new SequenceView(diagram, fontSize, host);
                }

                if (kind == "flowchart" || kind == "graph")
                {
                    Flowchart chart = MermaidFlowchartParser.Parse(source);
                    if (chart.Nodes.Count == 0)
                        return null;
                    return new MermaidFlowchartView(chart, fontSize, host);
                }
            }
            catch (Exception)
            {
                // 画不出来就当没有这块图，退回代码块，别让一张怪图拖垮预览
            }
            return null;
        }

        /// <summary>第一行有效内容里的第一个词，转小写：sequenceDiagram → sequencediagram。</summary>
        private static string FirstKeyword(string source)
        {
            string[] lines = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("%%", StringComparison.Ordinal) || line == "---")
                    continue;

                int end = line.IndexOf(' ');
                if (end < 0)
                    end = line.Length;
                return line.Substring(0, end).Trim().ToLowerInvariant();
            }
            return string.Empty;
        }
    }
}
