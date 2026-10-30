using System;
using System.Collections.Generic;
using MarkdownKit.PlantUml;

namespace MarkdownKit.Mermaid
{
    /// <summary>
    /// 把 Mermaid 的 sequenceDiagram 翻译成 PlantUML 那套时序图模型，
    /// 布局和绘制直接复用 PlantUml 的 SequenceView——不写第二套时序图渲染器。
    ///
    /// 认得的写法（其余行直接跳过，不报错）：
    ///   participant X as 名字 / actor X as 名字
    ///   A->>B: 文本 / A-->>B: 文本 / A->B: 文本 / A-->B: 文本 / A-xB: 文本
    ///   Note over A,B: 文本 / Note over A: 文本 / Note left of A: 文本 / Note right of A: 文本
    ///   activate / deactivate X
    /// </summary>
    internal static class MermaidSequenceParser
    {
        /// <summary>消息箭头，长的排前面：先匹配 ->> 再匹配 ->。</summary>
        private static readonly string[] Arrows = { "-->>", "->>", "-->", "->", "--x", "-x", "--)", "-)" };

        public static SequenceDiagram Parse(string source)
        {
            var diagram = new SequenceDiagram();
            string[] lines = Split(source);

            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("%%", StringComparison.Ordinal))
                    continue;
                if (line.Equals("sequenceDiagram", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (line.Equals("autonumber", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (line.StartsWith("title", StringComparison.OrdinalIgnoreCase))
                {
                    diagram.Title = line.Substring(5).Trim().TrimStart(':').Trim();
                    continue;
                }

                if (TryParticipant(line, diagram) || TryActivate(line, diagram)
                    || TryNote(line, diagram) || TryMessage(line, diagram))
                {
                    diagram.Known++;
                    continue;
                }

                diagram.Unknown++;
            }

            diagram.LooksLikeSequence = diagram.Known > 0 && diagram.Known >= diagram.Unknown;
            return diagram;
        }

        /// <summary>participant X as 名字 / actor X as 名字。</summary>
        private static bool TryParticipant(string line, SequenceDiagram diagram)
        {
            bool actor = line.StartsWith("actor ", StringComparison.OrdinalIgnoreCase);
            if (!actor && !line.StartsWith("participant ", StringComparison.OrdinalIgnoreCase))
                return false;

            string rest = line.Substring(actor ? 6 : 12).Trim();
            string key;
            string label;
            SplitAs(rest, out key, out label);

            int index = diagram.Ensure(key);
            diagram.Participants[index].Label = label;
            if (actor)
                diagram.Participants[index].Kind = ParticipantKind.Actor;
            return true;
        }

        /// <summary>activate X / deactivate X。</summary>
        private static bool TryActivate(string line, SequenceDiagram diagram)
        {
            bool deactivate = line.StartsWith("deactivate ", StringComparison.OrdinalIgnoreCase);
            if (!deactivate && !line.StartsWith("activate ", StringComparison.OrdinalIgnoreCase))
                return false;

            string key = line.Substring(deactivate ? 11 : 9).Trim();
            if (key.Length == 0)
                return false;

            diagram.Items.Add(new ActivateItem { Participant = diagram.Ensure(key), Deactivate = deactivate });
            return true;
        }

        /// <summary>Note over A,B: 文本 / Note left of A: 文本 / Note right of A: 文本。</summary>
        private static bool TryNote(string line, SequenceDiagram diagram)
        {
            if (!line.StartsWith("Note", StringComparison.OrdinalIgnoreCase))
                return false;

            int colon = line.IndexOf(':');
            if (colon < 4)
                return false;

            string head = line.Substring(4, colon - 4).Trim();
            string text = line.Substring(colon + 1).Trim();

            NotePlacement placement;
            string who;
            if (head.StartsWith("over", StringComparison.OrdinalIgnoreCase))
            {
                placement = NotePlacement.Over;
                who = head.Substring(4).Trim();
            }
            else if (head.StartsWith("left", StringComparison.OrdinalIgnoreCase))
            {
                placement = NotePlacement.Left;
                who = StripOf(head.Substring(4).Trim());
            }
            else if (head.StartsWith("right", StringComparison.OrdinalIgnoreCase))
            {
                placement = NotePlacement.Right;
                who = StripOf(head.Substring(5).Trim());
            }
            else
            {
                return false;
            }

            int comma = who.IndexOf(',');
            string first = comma < 0 ? who : who.Substring(0, comma);
            string second = comma < 0 ? null : who.Substring(comma + 1);

            var note = new NoteItem
            {
                Placement = placement,
                A = diagram.Ensure(first.Trim()),
                Text = text
            };
            if (!string.IsNullOrEmpty(second))
                note.B = diagram.Ensure(second.Trim());
            diagram.Items.Add(note);
            return true;
        }

        /// <summary>A->>B: 文本 这类消息行。</summary>
        private static bool TryMessage(string line, SequenceDiagram diagram)
        {
            int colon = line.IndexOf(':');
            string head = colon < 0 ? line : line.Substring(0, colon);
            string text = colon < 0 ? null : line.Substring(colon + 1).Trim();

            int at = -1;
            string arrow = null;
            foreach (string candidate in Arrows)
            {
                int i = head.IndexOf(candidate, StringComparison.Ordinal);
                if (i > 0 && (at < 0 || i < at))
                {
                    at = i;
                    arrow = candidate;
                }
            }
            if (at <= 0 || arrow == null)
                return false;

            string from = head.Substring(0, at).Trim();
            string to = head.Substring(at + arrow.Length).Trim();
            if (from.Length == 0 || to.Length == 0)
                return false;

            diagram.Items.Add(new MessageItem
            {
                From = diagram.Ensure(from),
                To = diagram.Ensure(to),
                Text = string.IsNullOrEmpty(text) ? null : text,
                Dashed = arrow.StartsWith("--", StringComparison.Ordinal)
            });
            return true;
        }

        /// <summary>「X as 名字」拆成 key 与显示名；没有 as 就都用 X。</summary>
        private static void SplitAs(string spec, out string key, out string label)
        {
            int asAt = spec.IndexOf(" as ", StringComparison.OrdinalIgnoreCase);
            if (asAt < 0)
            {
                key = spec.Trim();
                label = key;
                return;
            }
            key = spec.Substring(0, asAt).Trim();
            label = spec.Substring(asAt + 4).Trim();
            if (label.Length == 0)
                label = key;
        }

        /// <summary>去掉「of A」里的 of。</summary>
        private static string StripOf(string spec)
        {
            spec = spec.Trim();
            if (spec.StartsWith("of", StringComparison.OrdinalIgnoreCase))
                spec = spec.Substring(2).Trim();
            return spec;
        }

        private static string[] Split(string source)
        {
            return (source ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        }
    }
}
