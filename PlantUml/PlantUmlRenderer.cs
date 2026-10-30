using System;
using System.Windows;

namespace MarkdownKit.PlantUml
{
    /// <summary>
    /// PlantUML 渲染入口。解析、布局、矢量绘制都在本程序内完成，不依赖 Java、也不走网络。
    /// 同一份源码分别按「时序图」「组件/部署图」「状态图」解析一遍，谁的识别得分高用谁；
    /// 都不像（类图等还没支持的图）就返回 null，调用方退回普通代码块显示源码。
    /// </summary>
    internal static class PlantUmlRenderer
    {
        public static FrameworkElement TryCreate(string source, double fontSize, FrameworkElement host)
        {
            // 图内字号固定，不跟正文走：形状常量都是按这个基准量出来的，
            // 字号变了 U(v) = v * 字号 / 14 会等比缩放，形状比例不变
            fontSize = MarkdownTheme.DiagramFontSize;

            // @startmindmap / @startwbs / @startjson / @startyaml / @startgantt 这些
            // 非 UML 图型各有各的语法，先按开头关键字分派
            string kind = StartKind(source);
            if (kind != null && !kind.Equals("uml", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    TreeKind treeKind;
                    if (TryTreeKind(kind, out treeKind))
                    {
                        TreeDiagram tree = TreeParser.Parse(source, treeKind);
                        if (tree.LooksLikeTree)
                            return new TreeView(tree, fontSize, host);
                        return null;
                    }
                    if (kind.Equals("gantt", StringComparison.OrdinalIgnoreCase))
                    {
                        GanttDiagram gantt = GanttParser.Parse(source);
                        if (gantt.LooksLikeGantt)
                            return new GanttView(gantt, fontSize, host);
                        return null;
                    }
                }
                catch (Exception)
                {
                    return null;
                }
                return null;
            }

            try
            {
                SequenceDiagram sequence = SequenceParser.Parse(source);
                ComponentDiagram component = ComponentParser.Parse(source);
                StateDiagram state = StateParser.Parse(source);
                ActivityDiagram activity = ActivityParser.Parse(source);
                ComponentDiagram klass = ClassParser.Parse(source);
                int sequenceScore = sequence.Known - sequence.Unknown;
                int componentScore = component.Known - component.Unknown;
                int stateScore = state.Known - state.Unknown;
                int activityScore = activity.Known - activity.Unknown;
                int classScore = klass.Known - klass.Unknown;

                // 类图 / ER 图：class / interface / entity / <<PK>> 这些是它们独占的写法。
                // 排在组件图前面：`entity X { ... }` 两边都认，但类图那套判分更严
                if (klass.Known > 0 && classScore > componentScore && klass.Roots.Count > 0)
                    return new ComponentView(klass, fontSize, host);

                // state 关键字 / [*] 端点是状态图独占信号，出现即判状态图。
                // 排在活动图前面：`state A { :entry; }` 里的 `:entry;` 会被活动图当成活动节点，
                // 先让状态图认领掉。也要排在 [X] 之前：转换标签里的方括号（如 A --> B: xx [条件]）
                // 会被组件图当成 [X] 简写，别让它抢走状态图。
                if (state.LooksLikeState && stateScore > 0)
                    return new StateView(state, fontSize, host);

                // start / stop / :动作; / if…then 这些是活动图独占写法，出现即判活动图。
                if (activity.LooksLikeActivity && activityScore > 0)
                    return new ActivityView(activity, fontSize, host);

                // [X] 是组件图独占语法（官方行为）：出现即判组件图，不再比分数
                if (component.HasBracketSyntax && component.Roots.Count > 0)
                    return new ComponentView(component, fontSize, host);

                // 平分时倾向时序图：A -> B 这种写法两边都认，但更像时序图
                if (componentScore > 0 && componentScore > sequenceScore && component.Roots.Count > 0)
                    return new ComponentView(component, fontSize, host);
                if (sequenceScore > 0 && sequence.Participants.Count > 0)
                    return new SequenceView(sequence, fontSize, host);
                return null;
            }
            catch (Exception)
            {
                // 画不出来就当没有这块图，退回代码块，别让一张怪图拖垮整个预览
                return null;
            }
        }

        /// <summary>取 @start 后面那串图型关键字（uml / mindmap / wbs / json / yaml / gantt…），没有则 null。</summary>
        private static string StartKind(string source)
        {
            if (source == null)
                return null;
            string s = source.TrimStart();
            if (!s.StartsWith("@start", StringComparison.OrdinalIgnoreCase))
                return null;
            int i = 6;
            while (i < s.Length && char.IsLetterOrDigit(s[i]))
                i++;
            return s.Substring(6, i - 6);
        }

        private static bool TryTreeKind(string kind, out TreeKind treeKind)
        {
            switch (kind.ToLowerInvariant())
            {
                case "mindmap":
                    treeKind = TreeKind.MindMap;
                    return true;
                case "wbs":
                    treeKind = TreeKind.Wbs;
                    return true;
                case "json":
                    treeKind = TreeKind.Json;
                    return true;
                case "yaml":
                    treeKind = TreeKind.Yaml;
                    return true;
                default:
                    treeKind = TreeKind.MindMap;
                    return false;
            }
        }
    }
}
