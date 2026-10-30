using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MarkdownKit.PlantUml
{
    /// <summary>
    /// 把 PlantUML 组件/部署图源码解析成 <see cref="ComponentDiagram"/>。
    /// 认得出的行数和认不出的行数都记下来，交给分发器跟时序图比，谁的得分高用谁。
    /// </summary>
    internal static class ComponentParser
    {
        public static ComponentDiagram Parse(string source)
        {
            return new Impl(source ?? string.Empty).Run();
        }

        private sealed class Impl
        {
            private readonly string _source;
            private readonly ComponentDiagram _diagram = new ComponentDiagram();
            private readonly Stack<Element> _stack = new Stack<Element>();
            private string[] _lines;
            private int _index;

            public Impl(string source)
            {
                _source = source;
            }

            public ComponentDiagram Run()
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

                    if (HasBracketComponent(line))
                        _diagram.HasBracketSyntax = true;

                    if (Line(line) || Relation(line))
                        _diagram.Known++;
                    else
                        _diagram.Unknown++;
                }

                return _diagram;
            }

            /// <summary>处理非关系行。认出来返回 true。</summary>
            private bool Line(string line)
            {
                if (line[0] == '@' || line[0] == '!')
                    return true;

                if (Title(line) || Direction(line) || Together(line) || Close(line)
                    || Declaration(line) || Note(line) || Legend(line) || Ignored(line))
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

            /// <summary>
            /// 注释。note ... on link 挂在最后一条关系上；note left/right/top/bottom of X 挂在元素旁；
            /// 其余形式（note over 等）先整段吃掉不画，但多行的 end note 要消费掉。
            /// </summary>
            private bool Note(string line)
            {
                if (line != "note" && !line.StartsWith("note ", StringComparison.Ordinal))
                    return false;

                string rest = line.Length > 4 ? line.Substring(4).Trim() : "";
                bool onLink = rest.IndexOf("on link", StringComparison.Ordinal) >= 0;

                // note <placement> of <target>
                string placement = null;
                string target = null;
                Match m = NoteTargetRegex.Match(rest);
                if (m.Success)
                {
                    placement = m.Groups[1].Value.ToLowerInvariant();
                    target = m.Groups[2].Value;
                }

                string text;
                int colon = ColonOutsideQuotes(rest);
                if (colon >= 0)
                    text = Unescape(rest.Substring(colon + 1).Trim());
                else
                    text = ReadNoteBody();   // 多行，读到 end note

                if (onLink && _diagram.Relations.Count > 0)
                    _diagram.Relations[_diagram.Relations.Count - 1].Note = text;
                else if (placement != null)
                    _diagram.Notes.Add(new DiagramNote
                    {
                        Target = CleanName(target),
                        Placement = placement,
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

            private bool Together(string line)
            {
                if (line != "together" && !line.StartsWith("together ", StringComparison.Ordinal))
                    return false;
                // together { } 只是个分组：把子元素凑一起排，不画框
                var group = new Element { IsContainer = true, Transparent = true, Kind = ElementKind.Rectangle };
                Siblings().Add(group);
                group.Parent = _stack.Count == 0 ? null : _stack.Peek();
                _stack.Push(group);
                return true;
            }

            private bool Close(string line)
            {
                if (line != "}" && line != "end")
                    return false;
                if (_stack.Count > 0)
                    _stack.Pop();
                return true;
            }

            private bool Declaration(string line)
            {
                // [Name] 是 component 的简写
                if (line[0] == '[')
                {
                    int close = line.IndexOf(']');
                    if (close > 1)
                    {
                        // [A] --> [B] 这种带箭头的是关系，交给 Relation 处理
                        if (FindArrow(line) != null)
                            return false;
                        string name = line.Substring(1, close - 1).Trim();
                        Declare(name, name, ElementKind.Component, false);
                        return true;
                    }
                    return false;
                }

                ElementKind kind;
                int cut;
                if (!KindOf(line, out kind, out cut))
                    return false;

                // <<构造型>> 先抽出来，后面 StripDecorations 会把它去掉
                Match sm = StereotypeRegex.Match(line);
                string stereotype = sm.Success ? sm.Groups[1].Value.Trim() : null;

                string rest = line.Substring(cut).Trim();
                bool open = false;
                if (rest.EndsWith("{", StringComparison.Ordinal))
                {
                    open = true;
                    rest = rest.Substring(0, rest.Length - 1).Trim();
                }
                rest = StripDecorations(rest);

                int pos = 0;
                string first = ReadToken(rest, ref pos);
                if (first == null)
                    return true;
                // node --> db 这种其实是关系，别把箭头当成元素名
                if (!rest.StartsWith("\"", StringComparison.Ordinal)
                    && (first.IndexOf('-') >= 0 || first.IndexOf('.') >= 0))
                    return false;
                string second = null;
                if (ReadToken(rest, ref pos) == "as")
                    second = ReadToken(rest, ref pos);

                Declare(second ?? first, first, kind, open, stereotype);
                return true;
            }

            private static readonly Regex StereotypeRegex =
                new Regex(@"<<([^>]*)>>", RegexOptions.Compiled);

            /// <summary>这一行是否含 [X] 组件简写（内容非空的方括号，且不是 [#颜色]）。</summary>
            private static bool HasBracketComponent(string line)
            {
                int open = line.IndexOf('[');
                while (open >= 0)
                {
                    int close = line.IndexOf(']', open + 1);
                    if (close < 0)
                        return false;
                    if (close > open + 1 && line[open + 1] != '#')
                        return true;
                    open = line.IndexOf('[', close + 1);
                }
                return false;
            }

            private void Declare(string key, string label, ElementKind kind, bool open, string stereotype = null)
            {
                Element parent = _stack.Count == 0 ? null : _stack.Peek();
                Element e = _diagram.Declare(key, Unescape(label), kind, open, parent);
                if (!string.IsNullOrEmpty(stereotype))
                    e.Stereotype = stereotype;
                if (open)
                    _stack.Push(e);
            }

            /// <summary>关键字 → 元素种类。必须整词匹配，免得把 node1 当成 node。</summary>
            private static bool KindOf(string line, out ElementKind kind, out int cut)
            {
                kind = ElementKind.Rectangle;
                cut = 0;
                string[] words =
                {
                    "node", "package", "folder", "frame", "cloud", "rectangle", "storage", "stack",
                    "component", "database", "queue", "artifact", "card", "file", "usecase",
                    "actor", "agent", "boundary", "control", "entity", "collections",
                    "interface", "circle", "hexagon", "person", "label", "action", "process"
                };
                for (int i = 0; i < words.Length; i++)
                {
                    if (line == words[i] || line.StartsWith(words[i] + " ", StringComparison.Ordinal))
                    {
                        kind = (ElementKind)i;
                        cut = words[i].Length;
                        return true;
                    }
                }
                return false;
            }

            private bool Ignored(string line)
            {
                string[] prefixes =
                {
                    "skinparam", "hide", "show", "scale", "header", "footer", "caption",
                    "newpage", "note", "allowmixing", "left to right", "top to bottom"
                };
                for (int i = 0; i < prefixes.Length; i++)
                {
                    if (line == prefixes[i] || line.StartsWith(prefixes[i] + " ", StringComparison.Ordinal))
                    {
                        if (prefixes[i] == "skinparam")
                            SkinParam(line.Substring(prefixes[i].Length).Trim());
                        return true;
                    }
                }
                return false;
            }

            /// <summary>只认 skinparam componentStyle，其余皮肤参数忽略。</summary>
            private void SkinParam(string body)
            {
                int eq = body.IndexOf('=');
                if (eq < 0)
                    return;
                string key = body.Substring(0, eq).Trim().ToLowerInvariant();
                string value = body.Substring(eq + 1).Trim().ToLowerInvariant();
                if (key != "componentstyle")
                    return;
                switch (value)
                {
                    case "rectangle":
                        _diagram.ComponentStyle = ComponentStyle.Rectangle;
                        break;
                    case "uml1":
                        _diagram.ComponentStyle = ComponentStyle.Uml1;
                        break;
                    case "uml2":
                        _diagram.ComponentStyle = ComponentStyle.Uml2;
                        break;
                }
            }

            // ── 关系 ────────────────────────────────────────────────

            private bool Relation(string line)
            {
                int colon = ColonOutsideQuotes(line);
                string body = colon >= 0 ? line.Substring(0, colon) : line;
                string label = colon >= 0 ? Unescape(line.Substring(colon + 1).Trim()) : null;

                body = NormalizeHints(body);
                Match arrow = FindArrow(body);
                if (arrow == null)
                    return false;

                string left = body.Substring(0, arrow.Index).Trim();
                string right = body.Substring(arrow.Index + arrow.Length).Trim();
                if (left.Length == 0 || right.Length == 0)
                    return false;

                string leftName = TrimMultiplicity(left, true);
                string rightName = TrimMultiplicity(right, false);
                string lk = CleanName(leftName);
                string rk = CleanName(rightName);
                if (lk.Length == 0 || rk.Length == 0)
                    return false;

                _diagram.Relations.Add(new Relation
                {
                    Left = _diagram.Ensure(lk),
                    Right = _diagram.Ensure(rk),
                    Label = label,
                    Dashed = arrow.Value.IndexOf('.') >= 0,
                    Length = DashCount(arrow.Value),
                    LeftArrow = ArrowOf(arrow.Value, true),
                    RightArrow = ArrowOf(arrow.Value, false)
                });
                return true;
            }

            /// <summary>连接符里横线/点的个数（官方 Link.getLength()）：-> 是 1，--> / -- / ..> 是 2。</summary>
            private static int DashCount(string token)
            {
                int bars = 0;
                for (int i = 0; i < token.Length; i++)
                {
                    if (token[i] == '-' || token[i] == '.')
                        bars++;
                }
                return Math.Max(1, bars);
            }

            /// <summary>去掉方向提示（-down-）和颜色提示（[#red]），剩下的箭头按常规解析。</summary>
            private static string NormalizeHints(string body)
            {
                body = Regex.Replace(body, @"\[#[^\]]*\]", " ");
                body = Regex.Replace(body, @"-(down|up|left|right|l|r|u|d)-", "-");
                return body;
            }

            private static Match FindArrow(string body)
            {
                MatchCollection matches = ArrowRegex.Matches(body);
                for (int i = matches.Count - 1; i >= 0; i--)
                {
                    if (IsRelationArrow(matches[i].Value))
                        return matches[i];
                }
                return null;
            }

            /// <summary>像不像关系箭头：两段以上横线/点，或一段横线带尖（-&gt;、&lt;-）。
            /// 免得把名字里的单个 -（web-server）当成箭头。</summary>
            private static bool IsRelationArrow(string token)
            {
                int bars = 0;
                for (int i = 0; i < token.Length; i++)
                {
                    if (token[i] == '-' || token[i] == '.')
                        bars++;
                }
                if (bars >= 2)
                    return true;
                return bars >= 1 && (token.IndexOf('>') >= 0 || token.IndexOf('<') >= 0);
            }

            private static readonly Regex ArrowRegex =
                new Regex(@"[<>|*o]*[-.]+[<>|*o]*", RegexOptions.Compiled);

            /// <summary>箭头两端的装饰。left=true 取左端（前缀），否则取右端（后缀）。</summary>
            private static EndArrow ArrowOf(string token, bool left)
            {
                int start = 0;
                while (start < token.Length && token[start] != '-' && token[start] != '.')
                    start++;
                int end = token.Length;
                while (end > start && token[end - 1] != '-' && token[end - 1] != '.')
                    end--;

                string chars = left ? token.Substring(0, start) : token.Substring(end);
                if (chars.IndexOf('*') >= 0)
                    return EndArrow.DiamondFilled;
                if (chars.IndexOf('o') >= 0)
                    return EndArrow.DiamondHollow;
                char tip = left ? '<' : '>';
                if (chars.IndexOf('|') >= 0 && chars.IndexOf(tip) >= 0)
                    return EndArrow.Triangle;
                if (chars.IndexOf(tip) >= 0)
                    return EndArrow.Open;
                return EndArrow.None;
            }

            /// <summary>剥掉重数（A "1" -- "n" B 里的 "1" / "n"），返回纯名字。</summary>
            private static string TrimMultiplicity(string text, bool trailing)
            {
                text = text.Trim();
                if (trailing)
                {
                    Match m = Regex.Match(text, "^(.+?)\\s*\"[^\"]*\"$");
                    if (m.Success)
                        return m.Groups[1].Value.Trim();
                }
                else
                {
                    Match m = Regex.Match(text, "^\"[^\"]*\"\\s*(.+)$");
                    if (m.Success)
                        return m.Groups[1].Value.Trim();
                }
                return text;
            }

            // ── 工具 ────────────────────────────────────────────────

            private List<Element> Siblings()
            {
                return _stack.Count == 0 ? _diagram.Roots : _stack.Peek().Children;
            }

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

            /// <summary>去掉 [方括号] 简写、引号，再还原 \n。</summary>
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
