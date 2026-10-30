using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MarkdownKit.PlantUml
{
    /// <summary>
    /// 把 PlantUML 状态图源码解析成 <see cref="StateDiagram"/>。
    /// 认得出的行数和认不出的行数都记下来，交给分发器跟时序图/组件图比，谁的得分高用谁。
    /// 语法对照官方 statediagram/command/ 下的 CommandCreateState / CommandLinkState / CommandCreatePackageState。
    /// </summary>
    internal static class StateParser
    {
        public static StateDiagram Parse(string source)
        {
            return new Impl(source ?? string.Empty).Run();
        }

        private sealed class Impl
        {
            private readonly string _source;
            private readonly StateDiagram _diagram = new StateDiagram();
            private readonly Stack<StateNode> _stack = new Stack<StateNode>();
            private string[] _lines;
            private int _index;

            public Impl(string source)
            {
                _source = source;
            }

            /// <summary>当前所在容器（复合状态），根级为 null。</summary>
            private StateNode Current
            {
                get { return _stack.Count == 0 ? null : _stack.Peek(); }
            }

            public StateDiagram Run()
            {
                if (_source.Length == 0)
                    return _diagram;

                _lines = StripBlockComments(PlantUmlPreprocessor.Process(_source))
                    .Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

                for (_index = 0; _index < _lines.Length; _index++)
                {
                    string line = _lines[_index].Trim();
                    if (line.Length == 0 || line[0] == '\'')
                        continue;

                    if (Line(line) || Transition(line))
                        _diagram.Known++;
                    else
                        _diagram.Unknown++;
                }

                return _diagram;
            }

            /// <summary>处理非转换行。认出来返回 true。</summary>
            private bool Line(string line)
            {
                if (line[0] == '@' || line[0] == '!')
                    return true;

                if (Title(line) || Direction(line) || Close(line)
                    || StateDecl(line) || Note(line) || Legend(line) || Ignored(line))
                    return true;

                return false;
            }

            private static readonly string[] LegendPlacements = { "left", "right", "top", "bottom", "center" };

            /// <summary>
            /// 图例：legend [left|right|top|bottom|center] ... endlegend。
            /// 正文原样收进模型（每行一条），布局时在图外画一个圆角框。
            /// </summary>
            private bool Legend(string line)
            {
                if (line != "legend" && !line.StartsWith("legend ", StringComparison.Ordinal))
                    return false;

                string rest = line.Length > 6 ? line.Substring(6).Trim().ToLowerInvariant() : "";
                for (int i = 0; i < LegendPlacements.Length; i++)
                {
                    if (rest == LegendPlacements[i])
                    {
                        _diagram.LegendPlacement = LegendPlacements[i];
                        break;
                    }
                }

                _diagram.Legend.Clear();
                while (++_index < _lines.Length)
                {
                    string t = _lines[_index].Trim();
                    if (t == "endlegend" || t == "end legend")
                        break;
                    _diagram.Legend.Add(t);
                }
                return true;
            }

            private static readonly Regex NoteTargetRegex =
                new Regex(@"^(left|right|top|bottom)\s+of\s+(\S+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

            /// <summary>注释：note left/right/top/bottom of X（可带 : 单行，或读到 end note 的多行）。</summary>
            private bool Note(string line)
            {
                if (line != "note" && !line.StartsWith("note ", StringComparison.Ordinal))
                    return false;

                string rest = line.Length > 4 ? line.Substring(4).Trim() : "";

                string placement = null;
                string target = null;
                Match m = NoteTargetRegex.Match(rest);
                if (m.Success)
                {
                    placement = m.Groups[1].Value.ToLowerInvariant();
                    target = CleanName(m.Groups[2].Value);
                }

                string text;
                int colon = ColonOutsideQuotes(rest);
                if (colon >= 0)
                    text = Unescape(rest.Substring(colon + 1).Trim());
                else
                    text = ReadNoteBody();   // 多行，读到 end note

                if (placement != null && target != null)
                {
                    var note = new StateNote { TargetKey = target, Placement = placement };
                    foreach (string part in text.Split('\n'))
                        note.Lines.Add(part);
                    _diagram.Notes.Add(note);
                }
                return true;
            }

            /// <summary>读到 end note 为止，多行注释的正文。</summary>
            private string ReadNoteBody()
            {
                var sb = new StringBuilder();
                while (++_index < _lines.Length)
                {
                    string t = _lines[_index].Trim();
                    if (t == "end note" || t == "endnote")
                        break;
                    if (sb.Length > 0)
                        sb.Append('\n');
                    sb.Append(t);
                }
                return Unescape(sb.ToString());
            }

            private bool Title(string line)
            {
                if (line != "title" && !line.StartsWith("title ", StringComparison.Ordinal))
                    return false;

                if (line == "title")
                {
                    var sb = new StringBuilder();
                    while (++_index < _lines.Length)
                    {
                        string t = _lines[_index].Trim();
                        if (t == "end title" || t == "endtitle")
                            break;
                        if (sb.Length > 0)
                            sb.Append('\n');
                        sb.Append(t);
                    }
                    _diagram.Title = sb.ToString();
                }
                else
                {
                    _diagram.Title = Unescape(line.Substring(6).Trim());
                }
                return true;
            }

            private bool Direction(string line)
            {
                if (line == "left to right direction")
                {
                    _diagram.LeftToRight = true;
                    return true;
                }
                if (line == "top to bottom direction")
                {
                    _diagram.LeftToRight = false;
                    return true;
                }
                return false;
            }

            private bool Close(string line)
            {
                if (line != "}" && line != "end" && line != "end state")
                    return false;
                if (_stack.Count > 0)
                    _stack.Pop();
                return true;
            }

            private static readonly Regex StereotypeRegex =
                new Regex(@"<<([^>]*)>>", RegexOptions.Compiled);

            /// <summary>state X / state "desc" as X / state X as "desc" / state "desc"，以及 state X { 开复合状态。</summary>
            private static readonly Regex StateNameRegex = new Regex(
                "^(?:\"(?<d1>[^\"]+)\"\\s+as\\s+(?<c1>\\S+)|(?<c2>\\S+)\\s+as\\s+\"(?<d2>[^\"]+)\"|\"(?<d3>[^\"]+)\"|(?<c4>\\S+))$",
                RegexOptions.Compiled);

            private bool StateDecl(string line)
            {
                if (line != "state" && !line.StartsWith("state ", StringComparison.Ordinal))
                    return false;

                _diagram.SawStateKeyword = true;
                string rest = line.Length > 5 ? line.Substring(5).Trim() : "";

                // <<构造型>> 先抽出来，后面 StripDecorations 会把它去掉
                Match sm = StereotypeRegex.Match(rest);
                string stereotype = sm.Success ? sm.Groups[1].Value.Trim() : null;

                bool open = false;
                if (rest.EndsWith("{", StringComparison.Ordinal))
                {
                    open = true;
                    rest = rest.Substring(0, rest.Length - 1).Trim();
                }
                else if (rest.EndsWith(" begin", StringComparison.Ordinal))
                {
                    open = true;
                    rest = rest.Substring(0, rest.Length - 6).Trim();
                }
                rest = StripDecorations(rest);

                int colon = ColonOutsideQuotes(rest);
                string namePart = colon >= 0 ? rest.Substring(0, colon).Trim() : rest;
                string field = colon >= 0 ? Unescape(rest.Substring(colon + 1).Trim()) : null;

                Match m = StateNameRegex.Match(namePart);
                if (!m.Success)
                    return true;

                string code;
                string display;
                if (m.Groups["d1"].Success) { display = m.Groups["d1"].Value; code = m.Groups["c1"].Value; }
                else if (m.Groups["c2"].Success) { code = m.Groups["c2"].Value; display = m.Groups["d2"].Value; }
                else if (m.Groups["d3"].Success) { display = m.Groups["d3"].Value; code = display; }
                else { code = m.Groups["c4"].Value; display = code; }
                code = CleanName(code);

                if (open)
                {
                    StateNode e = _diagram.Declare(code, Unescape(display), StateKind.Composite, Current);
                    if (!string.IsNullOrEmpty(stereotype))
                        e.Stereotype = stereotype;
                    _stack.Push(e);
                    return true;
                }

                if (field != null)
                {
                    // state X : 描述 —— 给状态加一行字段，别覆盖已有标签
                    StateNode e = _diagram.Ensure(code, Current);
                    e.Fields.Add(field);
                    return true;
                }

                StateKind kind = KindOfStereotype(stereotype);
                StateNode node = _diagram.Declare(code, Unescape(display), kind, Current);
                if (!string.IsNullOrEmpty(stereotype))
                    node.Stereotype = stereotype;
                return true;
            }

            /// <summary>构造型 → 节点种类（对照官方 stereo/Stereogroup.getLeafType）。</summary>
            private static StateKind KindOfStereotype(string stereotype)
            {
                if (string.IsNullOrEmpty(stereotype))
                    return StateKind.Normal;
                switch (stereotype.ToLowerInvariant())
                {
                    case "choice": return StateKind.Choice;
                    case "fork":
                    case "join": return StateKind.ForkJoin;
                    case "junction": return StateKind.Junction;
                    case "history": return StateKind.History;
                    case "history*":
                    case "deephistory": return StateKind.DeepHistory;
                    case "start": return StateKind.Start;
                    case "end": return StateKind.End;
                    default: return StateKind.Normal;
                }
            }

            private bool Ignored(string line)
            {
                string[] prefixes =
                {
                    "skinparam", "hide", "show", "scale", "header", "footer", "caption",
                    "newpage", "note", "allowmixing", "left to right", "top to bottom",
                    "stateStyle"
                };
                for (int i = 0; i < prefixes.Length; i++)
                {
                    if (line == prefixes[i] || line.StartsWith(prefixes[i] + " ", StringComparison.Ordinal))
                        return true;
                }
                return false;
            }

            // ── 转换 ────────────────────────────────────────────────

            /// <summary>箭头核心：可选起点 x、一串 -、可选 [style]、可选方向词、可选 [style]、可选 -、&gt;、可选终点 o。</summary>
            private static readonly Regex ArrowRegex = new Regex(
                @"(x)?(-+)(\[[^\]]*\])?(left|right|up|down|le?|ri?|up?|do?)?(\[[^\]]*\])?(-*)>(o)?",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

            private bool Transition(string line)
            {
                int colon = ColonOutsideQuotes(line);
                string body = colon >= 0 ? line.Substring(0, colon) : line;
                string label = colon >= 0 ? Unescape(line.Substring(colon + 1).Trim()) : null;

                string color = null;
                Match cm = Regex.Match(body, @"\[#[^\]]*\]");
                if (cm.Success)
                {
                    color = cm.Value;
                    body = body.Remove(cm.Index, cm.Length);
                }
                body = body.Trim();

                // 从右往左找箭头，免得名字里的 - 被当成箭头
                Match arrow = null;
                MatchCollection matches = ArrowRegex.Matches(body);
                for (int i = matches.Count - 1; i >= 0; i--)
                {
                    if (matches[i].Value.IndexOf('>') >= 0)
                    {
                        arrow = matches[i];
                        break;
                    }
                }
                if (arrow == null)
                    return false;

                string left = body.Substring(0, arrow.Index).Trim();
                string right = body.Substring(arrow.Index + arrow.Length).Trim();
                if (left.Length == 0 || right.Length == 0)
                    return false;

                StateNode from = Endpoint(left, false);
                StateNode to = Endpoint(right, true);
                if (from == null || to == null)
                    return false;

                var t = new Transition
                {
                    From = from,
                    To = to,
                    Label = label,
                    Color = color,
                    CrossStart = arrow.Groups[1].Success,
                    Head = arrow.Groups[7].Success ? TransitionHead.Circle : TransitionHead.Arrow
                };
                string dir = arrow.Groups[4].Success ? arrow.Groups[4].Value.ToLowerInvariant() : null;
                if (dir == "l" || dir == "le" || dir == "left") t.Direction = "left";
                else if (dir == "r" || dir == "ri" || dir == "right") t.Direction = "right";
                else if (dir == "u" || dir == "up") t.Direction = "up";
                else if (dir == "d" || dir == "do" || dir == "down") t.Direction = "down";

                string style = arrow.Groups[3].Success ? arrow.Groups[3].Value
                    : (arrow.Groups[5].Success ? arrow.Groups[5].Value : null);
                if (style != null)
                {
                    if (style.IndexOf("bold", StringComparison.OrdinalIgnoreCase) >= 0)
                        t.Bold = true;
                    if (style.IndexOf("dotted", StringComparison.OrdinalIgnoreCase) >= 0
                        || style.IndexOf("dashed", StringComparison.OrdinalIgnoreCase) >= 0)
                        t.Dashed = true;
                }

                _diagram.Transitions.Add(t);
                return true;
            }

            /// <summary>解析转换的一个端点。rightSide 决定 [*] 是起始圆还是结束圆（对照官方 getEntityStart/getEntityEnd）。</summary>
            private StateNode Endpoint(string token, bool rightSide)
            {
                token = token.Trim();
                if (token.Length >= 2 && token[0] == '"' && token[token.Length - 1] == '"')
                    token = token.Substring(1, token.Length - 2);

                if (token == "[*]")
                {
                    _diagram.SawStartBracket = true;
                    return rightSide ? _diagram.GetEnd(Current) : _diagram.GetStart(Current);
                }
                if (token == "[H]")
                    return EnsurePseudo(token, StateKind.History);
                if (token == "[H*]")
                    return EnsurePseudo(token, StateKind.DeepHistory);
                if (token.Length >= 4 && token.StartsWith("==", StringComparison.Ordinal)
                    && token.EndsWith("==", StringComparison.Ordinal))
                {
                    string name = token.Substring(2, token.Length - 4);
                    return EnsurePseudo("==" + name + "==", StateKind.SynchroBar, name);
                }

                return _diagram.Ensure(CleanName(token), Current);
            }

            /// <summary>[H]/[H*]/同步条这类带固定 key 的伪状态，按当前容器归属。</summary>
            private StateNode EnsurePseudo(string key, StateKind kind, string label = null)
            {
                StateNode e = _diagram.Find(key);
                if (e != null)
                    return e;
                e = new StateNode { Key = key, Label = label ?? "", Kind = kind, Parent = Current };
                if (Current == null)
                    _diagram.Roots.Add(e);
                else
                    Current.Children.Add(e);
                return e;
            }

            // ── 工具 ────────────────────────────────────────────────

            private static string CleanName(string text)
            {
                text = text.Trim();
                if (text.Length >= 2 && text[0] == '[' && text[text.Length - 1] == ']')
                    text = text.Substring(1, text.Length - 2).Trim();
                if (text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"')
                    text = text.Substring(1, text.Length - 2);
                return Unescape(text).Trim();
            }

            private static string Unescape(string text)
            {
                if (string.IsNullOrEmpty(text))
                    return text;
                return text.Replace("\\n", "\n").Replace("\\t", "\t");
            }

            private static int ColonOutsideQuotes(string text)
            {
                bool inQuote = false;
                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    if (c == '"')
                        inQuote = !inQuote;
                    else if (c == ':' && !inQuote)
                        return i;
                }
                return -1;
            }

            /// <summary>去掉 &lt;&lt;构造型&gt;&gt; 和 #颜色，引号内的不动。</summary>
            private static string StripDecorations(string text)
            {
                int guard = 0;
                while (guard++ < 8)
                {
                    int a = IndexOfOutsideQuotes(text, "<<");
                    if (a < 0)
                        break;
                    int b = text.IndexOf(">>", a + 2, StringComparison.Ordinal);
                    if (b < 0)
                    {
                        text = text.Substring(0, a).Trim();
                        break;
                    }
                    text = (text.Substring(0, a) + " " + text.Substring(b + 2)).Trim();
                }

                int hash = IndexOfOutsideQuotes(text, "#");
                if (hash >= 0)
                {
                    int end = hash + 1;
                    while (end < text.Length && !char.IsWhiteSpace(text[end]))
                        end++;
                    text = (text.Substring(0, hash) + " " + text.Substring(end)).Trim();
                }
                return text;
            }

            private static int IndexOfOutsideQuotes(string text, string needle)
            {
                bool inQuote = false;
                for (int i = 0; i + needle.Length <= text.Length; i++)
                {
                    char c = text[i];
                    if (c == '"')
                        inQuote = !inQuote;
                    if (!inQuote && string.CompareOrdinal(text, i, needle, 0, needle.Length) == 0)
                        return i;
                }
                return -1;
            }

            private static string StripBlockComments(string source)
            {
                if (source.IndexOf("/'", StringComparison.Ordinal) < 0)
                    return source;

                var sb = new StringBuilder(source.Length);
                int i = 0;
                while (i < source.Length)
                {
                    if (i + 1 < source.Length && source[i] == '/' && source[i + 1] == '\'')
                    {
                        int end = source.IndexOf("'/", i + 2, StringComparison.Ordinal);
                        if (end < 0)
                            break;
                        for (int k = i; k < end + 2; k++)
                        {
                            if (source[k] == '\n')
                                sb.Append('\n');
                        }
                        i = end + 2;
                        continue;
                    }
                    sb.Append(source[i]);
                    i++;
                }
                return sb.ToString();
            }
        }
    }
}
