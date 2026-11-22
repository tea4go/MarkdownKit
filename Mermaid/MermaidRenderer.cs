using System;
using System.Windows;
using MarkdownKit.PlantUml;

namespace MarkdownKit.Mermaid
{
    /// <summary>
    /// Mermaid 渲染入口。目前接三种图：
    ///   sequenceDiagram —— 翻译成 PlantUML 的时序图模型，布局和绘制直接复用 SequenceView；
    ///   flowchart / graph —— 自己的分层布局 + Canvas 绘制；
    ///   pie —— 扇形 + 图例，Canvas 绘制。
    /// 另外兼容 flowchart.js 的 id=&gt;type: label 语法（见 TryCreateFlowchartJsOnly），复用同一套流程图视图。
    /// 其他图型（classDiagram / stateDiagram / gantt / journey …）返回 null，
    /// 调用方退回普通代码块显示源码。
    /// </summary>
    internal static class MermaidRenderer
    {
        /// <summary>序列图消息箭头，长的排前面：先匹配 ->> 再匹配 ->。</summary>
        private static readonly string[] SequenceArrows = { "-->>", "->>", "-->", "->", "--x", "-x", "--)", "-)" };

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

                if (kind == "pie")
                {
                    PieChart chart = MermaidPieParser.Parse(source);
                    if (chart.Slices.Count == 0)
                        return null;
                    return new MermaidPieView(chart, fontSize, host);
                }

                // 没写图种头（如省略 sequenceDiagram）时，整段看起来就是时序图也照样画
                FrameworkElement sequence = TryCreateSequence(source, fontSize, host);
                if (sequence != null)
                    return sequence;

                // 标了 mermaid、实际写的是 flowchart.js（id=>type: label）
                return TryCreateFlowchartJsOnly(source, fontSize, host);
            }
            catch (Exception)
            {
                // 画不出来就当没有这块图，退回代码块，别让一张怪图拖垮预览
            }
            return null;
        }

        /// <summary>
        /// 只认序列图。给没标 ```mermaid 的裸代码块用：流程图/饼图的裸语法太宽，误判代价大，
        /// 这里只按序列图判据识别，别的图种继续留在代码块里。
        /// </summary>
        public static FrameworkElement TryCreateSequenceOnly(string source, double fontSize, FrameworkElement host)
        {
            if (string.IsNullOrEmpty(source))
                return null;

            fontSize = MarkdownTheme.DiagramFontSize;
            try
            {
                return TryCreateSequence(source, fontSize, host);
            }
            catch (Exception)
            {
            }
            return null;
        }

        /// <summary>
        /// 没有显式图种头时画出序列图。首行必须是参与者/注释/消息之一（挡掉 classDiagram、
        /// flowchart 这类首行是关键字、正文又可能撞上 A --> B 的图），且至少两条消息。
        /// </summary>
        private static FrameworkElement TryCreateSequence(string source, double fontSize, FrameworkElement host)
        {
            if (!FirstLineStartsSequence(source))
                return null;

            SequenceDiagram diagram = MermaidSequenceParser.Parse(source);
            if (!diagram.LooksLikeSequence || diagram.Participants.Count < 2)
                return null;

            int arrows = 0;
            foreach (SeqItem item in diagram.Items)
            {
                if (item is MessageItem || item is NoteItem)
                    arrows++;
            }
            if (arrows < 2)
                return null;

            return new SequenceView(diagram, fontSize, host);
        }

        /// <summary>
        /// 首条有效行能否作为序列图的开头：sequenceDiagram 头、participant/actor/Note 声明，
        /// 或「标识符 + 箭头」的消息行（发送方不含空格，避免把随便一段伪代码认成消息）。
        /// </summary>
        private static bool FirstLineStartsSequence(string source)
        {
            string[] lines = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("%%", StringComparison.Ordinal) || line == "---"
                    || line.StartsWith("title ", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (line.Equals("sequenceDiagram", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("participant ", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("actor ", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("Note ", StringComparison.OrdinalIgnoreCase))
                    return true;

                foreach (string arrow in SequenceArrows)
                {
                    int at = line.IndexOf(arrow, StringComparison.Ordinal);
                    if (at <= 0)
                        continue;
                    string from = line.Substring(0, at).Trim();
                    if (from.Length > 0 && from.IndexOf(' ') < 0)
                        return true;
                }
                return false;
            }
            return false;
        }

        /// <summary>
        /// 只认 flowchart.js。给 ```flow / ```flowchart 和没标语言的裸代码块用：首行必须是
        /// id=&gt;type: label 这种节点定义，且解析出至少两个节点和一条连线，避免误伤普通代码。
        /// </summary>
        public static FrameworkElement TryCreateFlowchartJsOnly(string source, double fontSize, FrameworkElement host)
        {
            if (string.IsNullOrEmpty(source))
                return null;

            fontSize = MarkdownTheme.DiagramFontSize;
            try
            {
                if (!FirstLineStartsFlowchartJs(source))
                    return null;

                Flowchart chart = FlowchartJsParser.Parse(source);
                if (chart.Nodes.Count < 2 || chart.Edges.Count < 1)
                    return null;

                return new MermaidFlowchartView(chart, fontSize, host);
            }
            catch (Exception)
            {
            }
            return null;
        }

        /// <summary>首条有效行是不是 flowchart.js 的节点定义（id=&gt;type: label）。</summary>
        private static bool FirstLineStartsFlowchartJs(string source)
        {
            string[] lines = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("%%", StringComparison.Ordinal))
                    continue;
                return IsFlowchartJsDefinition(line);
            }
            return false;
        }

        /// <summary>id=&gt;type[: label]，要求 id 和 type 都是纯标识符，挡掉 JS 的 x=&gt;x*2 之类。</summary>
        private static bool IsFlowchartJsDefinition(string line)
        {
            int at = line.IndexOf("=>", StringComparison.Ordinal);
            if (at <= 0 || !IsIdentifier(line.Substring(0, at).Trim()))
                return false;

            string rest = line.Substring(at + 2).Trim();
            int colon = rest.IndexOf(':');
            string type = (colon < 0 ? rest : rest.Substring(0, colon)).Trim();
            return IsIdentifier(type);
        }

        private static bool IsIdentifier(string text)
        {
            if (text.Length == 0)
                return false;
            foreach (char c in text)
            {
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '-')
                    return false;
            }
            return true;
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
