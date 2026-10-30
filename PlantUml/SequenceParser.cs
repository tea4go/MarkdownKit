using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MarkdownKit.PlantUml
{
    /// <summary>
    /// 把 PlantUML 时序图源码解析成 <see cref="SequenceDiagram"/>。
    /// 只认时序图语法：认不出的行直接跳过，不抛错——预览要的是「能画就画、画不了就退回代码块」，
    /// 不该因为一行怪语法让整张图塌掉。
    /// </summary>
    internal static class SequenceParser
    {
        public static SequenceDiagram Parse(string source)
        {
            return new Impl(source ?? string.Empty).Run();
        }

        /// <summary>分组框关键字。后面必须跟空格或行尾，免得把 participant 当成 par。</summary>
        private static readonly string[] FragmentKinds =
            { "alt", "opt", "loop", "par", "group", "break", "critical" };

        private sealed class Impl
        {
            private readonly string _source;
            private readonly SequenceDiagram _diagram = new SequenceDiagram();
            private readonly Stack<FragmentItem> _stack = new Stack<FragmentItem>();
            private string[] _lines;
            private int _index;
            private bool _autoNumber;
            private int _number;
            private int _skipDepth;   // 跳过的 box / legend 块数，它们的 end 不该弹分组框
            private int _known;       // 认得出的行数
            private int _unknown;     // 认不出的行数

            public Impl(string source)
            {
                _source = source;
            }

            public SequenceDiagram Run()
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

                    if (Line(line) || AddMessage(line))
                        _known++;
                    else
                        _unknown++;
                }

                _diagram.Known = _known;
                _diagram.Unknown = _unknown;
                _diagram.LooksLikeSequence = _unknown <= _known;
                return _diagram;
            }

            /// <summary>处理一行。认出来的返回 true，认不出交给消息解析。</summary>
            private bool Line(string line)
            {
                if (line[0] == '@' || line[0] == '!')
                    return true;   // @startuml / @enduml / 预处理指令

                if (Title(line) || Declaration(line) || AutoNumber(line) || Activate(line)
                    || Note(line) || Fragment(line) || Else(line) || End(line)
                    || Divider(line) || Delay(line) || Legend(line) || Ignored(line))
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

            // ── 逐类语法 ────────────────────────────────────────────

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

            /// <summary>participant / actor / boundary / control / entity / database / collections / queue。</summary>
            private bool Declaration(string line)
            {
                ParticipantKind kind;
                int cut;
                if (!KindOf(line, out kind, out cut))
                    return false;

                string rest = line.Substring(cut).Trim();
                int hash = rest.IndexOf('#');
                if (hash > 0)
                    rest = rest.Substring(0, hash).Trim();   // 去掉 #颜色 后缀

                int pos = 0;
                string first = ReadToken(rest, ref pos);
                if (first == null)
                    return true;
                string second = null;
                if (ReadToken(rest, ref pos) == "as")
                    second = ReadToken(rest, ref pos);

                int index = _diagram.Ensure(second ?? first);
                Participant p = _diagram.Participants[index];
                p.Label = first;
                p.Kind = kind;
                return true;
            }

            private static bool KindOf(string line, out ParticipantKind kind, out int cut)
            {
                kind = ParticipantKind.Participant;
                cut = 0;
                string[] words = { "participant", "actor", "boundary", "control",
                                   "entity", "database", "collections", "queue" };
                for (int i = 0; i < words.Length; i++)
                {
                    if (line == words[i] || line.StartsWith(words[i] + " ", StringComparison.Ordinal))
                    {
                        kind = (ParticipantKind)i;
                        cut = words[i].Length;
                        return true;
                    }
                }
                return false;
            }

            private bool AutoNumber(string line)
            {
                if (line != "autonumber" && !line.StartsWith("autonumber ", StringComparison.Ordinal))
                    return false;
                _autoNumber = true;
                string arg = line.Length > 10 ? line.Substring(10).Trim() : "";
                if (arg == "stop")
                {
                    _autoNumber = false;
                }
                else if (arg.Length > 0)
                {
                    int start;
                    if (int.TryParse(arg, out start))
                        _number = start - 1;
                }
                return true;
            }

            private bool Activate(string line)
            {
                bool deactivate;
                string name;
                if (line.StartsWith("activate ", StringComparison.Ordinal))
                {
                    deactivate = false;
                    name = line.Substring(9).Trim();
                }
                else if (line.StartsWith("deactivate ", StringComparison.Ordinal))
                {
                    deactivate = true;
                    name = line.Substring(11).Trim();
                }
                else if (line.StartsWith("destroy ", StringComparison.Ordinal))
                {
                    deactivate = true;
                    name = line.Substring(8).Trim();
                }
                else
                {
                    return false;
                }

                name = CleanName(name);
                if (name.Length == 0)
                    return true;
                Current().Add(new ActivateItem
                {
                    Participant = _diagram.Ensure(name),
                    Deactivate = deactivate
                });
                return true;
            }

            private bool Note(string line)
            {
                if (line != "note" && !line.StartsWith("note ", StringComparison.Ordinal))
                    return false;

                string rest = line.Length > 4 ? line.Substring(4).Trim() : "";
                NotePlacement placement;
                if (rest.StartsWith("left", StringComparison.Ordinal))
                {
                    placement = NotePlacement.Left;
                    rest = rest.Substring(4).Trim();
                }
                else if (rest.StartsWith("right", StringComparison.Ordinal))
                {
                    placement = NotePlacement.Right;
                    rest = rest.Substring(5).Trim();
                }
                else if (rest.StartsWith("over", StringComparison.Ordinal))
                {
                    placement = NotePlacement.Over;
                    rest = rest.Substring(4).Trim();
                }
                else
                {
                    placement = NotePlacement.Over;
                }
                if (rest.StartsWith("of", StringComparison.Ordinal))
                    rest = rest.Substring(2).Trim();

                string target;
                string text;
                int colon = ColonOutsideQuotes(rest);
                if (colon >= 0)
                {
                    target = rest.Substring(0, colon).Trim();
                    text = Unescape(rest.Substring(colon + 1).Trim());
                }
                else
                {
                    target = rest.Trim();
                    text = ReadNoteBody();
                }

                int a = -1;
                int b = -1;
                if (target.Length > 0)
                {
                    string[] parts = SplitTop(target, ',');
                    if (parts.Length >= 1)
                        a = _diagram.Ensure(CleanName(parts[0]));
                    if (parts.Length >= 2)
                        b = _diagram.Ensure(CleanName(parts[1]));
                }
                // note left : 文字 这种没写对象的，挂到最后一个参与者上
                if (a < 0)
                {
                    if (_diagram.Participants.Count == 0)
                        return true;
                    a = _diagram.Participants.Count - 1;
                }

                Current().Add(new NoteItem
                {
                    Placement = placement,
                    A = a,
                    B = placement == NotePlacement.Over ? b : -1,
                    Text = text
                });
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

            private bool Fragment(string line)
            {
                for (int i = 0; i < FragmentKinds.Length; i++)
                {
                    string kind = FragmentKinds[i];
                    if (line != kind && !line.StartsWith(kind + " ", StringComparison.Ordinal))
                        continue;

                    string label = line.Length > kind.Length
                        ? Unescape(line.Substring(kind.Length).Trim())
                        : null;
                    var fragment = new FragmentItem { Kind = kind };
                    fragment.Sections.Add(new FragmentSection { Label = label });
                    Current().Add(fragment);
                    _stack.Push(fragment);
                    return true;
                }
                return false;
            }

            private bool Else(string line)
            {
                if (line != "else" && !line.StartsWith("else ", StringComparison.Ordinal))
                    return false;
                if (_stack.Count == 0)
                    return true;
                string label = line.Length > 4 ? Unescape(line.Substring(4).Trim()) : null;
                _stack.Peek().Sections.Add(new FragmentSection { Label = label });
                return true;
            }

            private bool End(string line)
            {
                if (line != "end" && line != "endlegend")
                    return false;
                if (_skipDepth > 0)
                    _skipDepth--;
                else if (_stack.Count > 0)
                    _stack.Pop();
                return true;
            }

            private bool Divider(string line)
            {
                if (line.Length < 2 || line[0] != '=' || line[1] != '=')
                    return false;
                string text = line.Trim('=', ' ').Trim();
                Current().Add(new DividerItem { Text = text.Length > 0 ? Unescape(text) : null });
                return true;
            }

            private bool Delay(string line)
            {
                if (line.Length < 3 || line[0] != '.' || line[1] != '.' || line[2] != '.')
                    return false;
                string text = line.Trim('.').Trim();
                Current().Add(new DelayItem { Text = text.Length > 0 ? Unescape(text) : null });
                return true;
            }

            /// <summary>整行忽略的指令：皮肤参数、页眉页脚、box / legend 块等。</summary>
            private bool Ignored(string line)
            {
                string[] prefixes = { "skinparam", "hide", "show", "header", "footer",
                                      "scale", "box", "left to right direction",
                                      "top to bottom direction", "newpage", "caption" };
                for (int i = 0; i < prefixes.Length; i++)
                {
                    if (line == prefixes[i] || line.StartsWith(prefixes[i] + " ", StringComparison.Ordinal))
                    {
                        // box 会带自己的 end，记一笔免得那个 end 弹错分组框
                        if (prefixes[i] == "box")
                            _skipDepth++;
                        return true;
                    }
                }
                return false;
            }

            // ── 消息 ────────────────────────────────────────────────

            /// <summary>解析一条消息。认不出（没有箭头、缺一边）就当没这行，返回 false。</summary>
            private bool AddMessage(string line)
            {
                int colon = ColonOutsideQuotes(line);
                string body = colon >= 0 ? line.Substring(0, colon) : line;
                string label = colon >= 0 ? Unescape(line.Substring(colon + 1).Trim()) : null;

                // 箭头取最后一个「像样」的候选：参与者名字里带 - 时（web-server），
                // 从右往左找才不会把名字里的那个 - 当成箭头。
                Match arrow = null;
                MatchCollection matches = ArrowRegex.Matches(body);
                for (int i = matches.Count - 1; i >= 0; i--)
                {
                    if (IsArrow(matches[i].Value))
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

                bool activateTarget = false;
                bool deactivateSource = false;
                if (right.EndsWith("++", StringComparison.Ordinal))
                {
                    activateTarget = true;
                    right = right.Substring(0, right.Length - 2).Trim();
                }
                else if (right.EndsWith("--", StringComparison.Ordinal))
                {
                    deactivateSource = true;
                    right = right.Substring(0, right.Length - 2).Trim();
                }

                string from = CleanName(left);
                string to = CleanName(right);
                if (from.Length == 0 || to.Length == 0)
                    return false;

                int fromIndex = _diagram.Ensure(from);
                int toIndex = _diagram.Ensure(to);
                if (arrow.Value[0] == '<')
                {
                    int swap = fromIndex;
                    fromIndex = toIndex;
                    toIndex = swap;
                }

                int number = 0;
                if (_autoNumber)
                    number = ++_number;

                Current().Add(new MessageItem
                {
                    From = fromIndex,
                    To = toIndex,
                    Text = label,
                    Dashed = CountDashes(arrow.Value) >= 2,
                    OpenHead = arrow.Value.Contains(">>"),
                    LostHead = arrow.Value.Contains("x"),
                    ActivateTarget = activateTarget,
                    DeactivateSource = deactivateSource,
                    Number = number
                });
                return true;
            }

            /// <summary>判断一段候选文本是不是真箭头：有箭头头、两段以上横线、或 &lt; 反向。</summary>
            private static bool IsArrow(string token)
            {
                if (token.Length == 0)
                    return false;
                char last = token[token.Length - 1];
                if (last == '>' || last == 'x' || last == 'o' || last == '\\' || last == '/')
                    return true;
                if (CountDashes(token) >= 2)
                    return true;
                return token[0] == '<';
            }

            private static int CountDashes(string token)
            {
                int count = 0;
                for (int i = 0; i < token.Length; i++)
                {
                    if (token[i] == '-')
                        count++;
                }
                return count;
            }

            private static readonly Regex ArrowRegex =
                new Regex(@"(?:<)?-+[>xo\\/]*", RegexOptions.Compiled);

            // ── 工具 ────────────────────────────────────────────────

            private List<SeqItem> Current()
            {
                if (_stack.Count == 0)
                    return _diagram.Items;
                FragmentItem top = _stack.Peek();
                return top.Sections[top.Sections.Count - 1].Items;
            }

            /// <summary>读一个词：带引号的整段，或不带空白的连续串。</summary>
            private static string ReadToken(string text, ref int pos)
            {
                while (pos < text.Length && char.IsWhiteSpace(text[pos]))
                    pos++;
                if (pos >= text.Length)
                    return null;

                if (text[pos] == '"')
                {
                    int end = text.IndexOf('"', pos + 1);
                    if (end < 0)
                    {
                        string tail = text.Substring(pos + 1);
                        pos = text.Length;
                        return tail;
                    }
                    string quoted = text.Substring(pos + 1, end - pos - 1);
                    pos = end + 1;
                    return quoted;
                }

                int start = pos;
                while (pos < text.Length && !char.IsWhiteSpace(text[pos]))
                    pos++;
                return text.Substring(start, pos - start);
            }

            private static string CleanName(string text)
            {
                text = text.Trim();
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

            /// <summary>找引号外的第一个冒号。</summary>
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

            /// <summary>按分隔符切分，引号内的不切。</summary>
            private static string[] SplitTop(string text, char separator)
            {
                var parts = new List<string>();
                var sb = new StringBuilder();
                bool inQuote = false;
                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    if (c == '"')
                        inQuote = !inQuote;
                    if (c == separator && !inQuote)
                    {
                        parts.Add(sb.ToString());
                        sb.Length = 0;
                        continue;
                    }
                    sb.Append(c);
                }
                parts.Add(sb.ToString());
                return parts.ToArray();
            }

            /// <summary>把 /' ... '/ 块注释整段去掉，行内保留换行数以维持行号无关紧要（这里不需要行号）。</summary>
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
                        // 注释里的换行照样补上，别把前后两行粘一起
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
