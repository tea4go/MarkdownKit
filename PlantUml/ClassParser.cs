using System;
using System.Collections.Generic;
using System.Text;

namespace MarkdownKit.PlantUml
{
    /// <summary>
    /// 类图 / ER 图解析器。两者都产出 <see cref="ComponentDiagram"/>，复用组件图那套布局和绘制，
    /// 只是元素换成"带隔间的框"、连线换成类图/鸦爪记号。
    ///
    /// 认得出的写法：class / abstract class / interface / enum / annotation / struct / object /
    /// entity（含 `{ ... }` 体、`--` 分隔线、`&lt;&lt;构造型&gt;&gt;`、可见性 +-#~）、
    /// 各种关系连接符（继承 &lt;|-- 、组合 *-- 、聚合 o-- 、关联 -- 、依赖 ..&gt; 、ER 的 ||--o{ 等）、
    /// 两端重数、hide 指令、package / namespace、note。
    /// </summary>
    internal static class ClassParser
    {
        public static ComponentDiagram Parse(string source)
        {
            var diagram = new ComponentDiagram { IsClassDiagram = true };
            if (string.IsNullOrEmpty(source))
                return diagram;

            new Reader(source, diagram).Run();
            return diagram;
        }

        private sealed class Reader
        {
            private readonly List<string> _lines = new List<string>();
            private readonly ComponentDiagram _diagram;
            private int _i;
            private readonly List<Element> _stack = new List<Element>();

            public Reader(string source, ComponentDiagram diagram)
            {
                _diagram = diagram;
                string[] raw = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                foreach (string line in raw)
                {
                    string t = line.Trim();
                    if (t.Length == 0 || t[0] == '\'')
                        continue;
                    if (t.StartsWith("@start", StringComparison.OrdinalIgnoreCase)
                        || t.StartsWith("@end", StringComparison.OrdinalIgnoreCase))
                        continue;
                    _lines.Add(t);
                }
            }

            public void Run()
            {
                while (_i < _lines.Count)
                {
                    int before = _i;
                    bool ok = Statement(_lines[_i]);
                    // Statement 认不出时可能没消费这一行，强推一格，免得死循环
                    if (_i == before)
                        _i++;
                    if (ok)
                        _diagram.Known++;
                    else
                        _diagram.Unknown++;
                }
            }

            private bool Statement(string line)
            {
                string head = Head(line);

                switch (head)
                {
                    case "title":
                        _diagram.Title = line.Length > 5 ? line.Substring(5).Trim() : "";
                        _i++;
                        return true;

                    case "hide":
                    case "show":
                        Hide(line);
                        return true;

                    case "note":
                        return Note(line);

                    case "package":
                    case "namespace":
                    case "together":
                        return Package(line);

                    case "}":
                        _i++;
                        if (_stack.Count > 0)
                            _stack.RemoveAt(_stack.Count - 1);
                        return true;

                    case "class":
                    case "interface":
                    case "enum":
                    case "annotation":
                    case "struct":
                    case "object":
                    case "entity":
                    case "abstract":
                    case "protocol":
                    case "exception":
                    case "metaclass":
                    case "stereotype":
                        return Declare(line, head);

                    case "skinparam":
                    case "scale":
                    case "header":
                    case "footer":
                    case "caption":
                    case "legend":
                    case "left":
                    case "top":
                    case "bottom":
                    case "right":
                    case "!define":
                    case "!":
                    case "set":
                    case "allowmixing":
                    case "mainframe":
                    case "style":
                        _i++;
                        return true;
                }

                // 剩下的当关系看：找不到连接符就不认
                return Relation(line);
            }

            // ── 声明元素 ─────────────────────────────────────────

            private bool Declare(string line, string head)
            {
                string text = line;
                ElementKind kind = ElementKind.Class;
                switch (head)
                {
                    case "interface":
                        kind = ElementKind.Interface;
                        break;
                    case "enum":
                        kind = ElementKind.Enum;
                        break;
                    case "annotation":
                        kind = ElementKind.Annotation;
                        break;
                    case "struct":
                        kind = ElementKind.Struct;
                        break;
                    case "object":
                        kind = ElementKind.Object;
                        break;
                    case "entity":
                        kind = ElementKind.ErEntity;
                        break;
                    case "abstract":
                        kind = ElementKind.AbstractClass;
                        break;
                    default:
                        kind = ElementKind.Class;
                        break;
                }

                // 关键字后面那段： `"显示名" as Key {` 或 `Key {` 或 `Key`
                int body = text.IndexOf('{');
                string decl = body >= 0 ? text.Substring(0, body) : text;
                decl = decl.Substring(head.Length).Trim();
                if (decl.StartsWith("class ", StringComparison.OrdinalIgnoreCase))
                    decl = decl.Substring(6).Trim();   // abstract class / entity class 之类

                string label;
                string key;
                SplitDecl(decl, out label, out key);
                if (key.Length == 0)
                    return false;

                Element parent = _stack.Count > 0 ? _stack[_stack.Count - 1] : null;
                Element e = _diagram.Declare(key, label, kind, body >= 0, parent);
                e.ShowIcon = !_diagram.HideCircle;

                if (body < 0)
                {
                    _i++;
                    return true;
                }

                _i++;
                Body(e);
                return true;
            }

            /// <summary>把 `"显示名" as Key` / `Key` / `"显示名"` 拆成显示名和别名。</summary>
            private static void SplitDecl(string decl, out string label, out string key)
            {
                label = decl;
                key = decl;
                int asAt = decl.LastIndexOf(" as ", StringComparison.OrdinalIgnoreCase);
                if (asAt >= 0)
                {
                    label = decl.Substring(0, asAt).Trim();
                    key = decl.Substring(asAt + 4).Trim();
                }
                label = Unquote(label);
                key = Unquote(key);
                if (key.Length == 0)
                    key = label;
            }

            private static string Unquote(string s)
            {
                s = s.Trim();
                if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
                    s = s.Substring(1, s.Length - 2);
                return s.Trim();
            }

            /// <summary>读 `{ ... }` 里的内容，按 `--` 之类的分隔线切成隔间。</summary>
            private void Body(Element e)
            {
                var raw = new List<string>();
                int depth = 1;
                while (_i < _lines.Count)
                {
                    string line = _lines[_i];
                    int opens = Count(line, '{');
                    int closes = Count(line, '}');
                    if (closes > 0 && depth - closes <= 0)
                    {
                        _i++;
                        break;
                    }
                    depth += opens - closes;
                    raw.Add(line);
                    _i++;
                }

                // 先按显式分隔线切
                var groups = new List<List<string>>();
                var cur = new List<string>();
                foreach (string line in raw)
                {
                    if (IsSeparator(line))
                    {
                        groups.Add(cur);
                        cur = new List<string>();
                        continue;
                    }
                    cur.Add(line);
                }
                groups.Add(cur);

                // 没有分隔线时，按"带不带括号"自动拆成字段 / 方法两段（官方就是这么分的）
                if (groups.Count == 1)
                {
                    var fields = new List<string>();
                    var methods = new List<string>();
                    foreach (string line in groups[0])
                        (line.IndexOf('(') >= 0 ? methods : fields).Add(line);
                    groups.Clear();
                    if (fields.Count > 0)
                        groups.Add(fields);
                    if (methods.Count > 0)
                        groups.Add(methods);
                }

                foreach (List<string> group in groups)
                {
                    var c = new Compartment();
                    foreach (string line in group)
                    {
                        if (_diagram.HideMembers)
                            continue;
                        bool isMethod = line.IndexOf('(') >= 0;
                        if (isMethod && _diagram.HideMethods)
                            continue;
                        if (!isMethod && _diagram.HideFields)
                            continue;
                        c.Lines.Add(Clean(line));
                    }
                    if (c.Lines.Count > 0 || !_diagram.HideEmptyMembers)
                        e.Compartments.Add(c);
                }
                while (e.Compartments.Count > 0 && e.Compartments[e.Compartments.Count - 1].Lines.Count == 0)
                    e.Compartments.RemoveAt(e.Compartments.Count - 1);
            }

            /// <summary>`--` / `..` / `==` / `__` 这类分隔线。</summary>
            private static bool IsSeparator(string line)
            {
                if (line.Length < 2)
                    return false;
                char c = line[0];
                if (c != '-' && c != '.' && c != '=' && c != '_')
                    return false;
                foreach (char ch in line)
                {
                    if (ch != c)
                        return false;
                }
                return true;
            }

            /// <summary>ER 的 `<<PK>>` 换成书名号，`*` 前缀保留给绘制层画圆点。</summary>
            private static string Clean(string line)
            {
                return line.Replace("<<", "«").Replace(">>", "»");
            }

            // ── 关系 ─────────────────────────────────────────────

            private bool Relation(string line)
            {
                string text = line;
                string label = null;
                int colon = IndexOfLabel(text);
                if (colon >= 0)
                {
                    label = text.Substring(colon + 1).Trim();
                    text = text.Substring(0, colon);
                }

                string[] tokens = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                int at = -1;
                for (int i = 0; i < tokens.Length; i++)
                {
                    if (IsConnector(tokens[i]))
                    {
                        at = i;
                        break;
                    }
                }
                if (at <= 0 || at >= tokens.Length - 1)
                    return false;

                // 重数紧贴在连接符两边，元素名在更外面一格
                int leftAt = at - 1;
                string leftMult = null;
                if (leftAt > 0 && IsMultiplicity(tokens[leftAt]))
                {
                    leftMult = Unquote(tokens[leftAt]);
                    leftAt--;
                }
                int rightAt = at + 1;
                string rightMult = null;
                if (rightAt < tokens.Length - 1 && IsMultiplicity(tokens[rightAt]))
                {
                    rightMult = Unquote(tokens[rightAt]);
                    rightAt++;
                }
                if (leftAt < 0 || rightAt >= tokens.Length)
                    return false;

                string leftName = tokens[leftAt];
                string rightName = tokens[rightAt];

                Element left = _diagram.Ensure(leftName);
                Element right = _diagram.Ensure(rightName);
                var rel = new Relation { Left = left, Right = right, Label = label };
                Connector(tokens[at], rel);
                rel.LeftMultiplicity = leftMult;
                rel.RightMultiplicity = rightMult;
                _diagram.Relations.Add(rel);
                _i++;
                return true;
            }

            /// <summary>`"1"` / `1` / `1..*` / `*` / `0..1` 这种重数写法。</summary>
            private static bool IsMultiplicity(string token)
            {
                if (token.Length == 0)
                    return false;
                if (token[0] == '"')
                    return true;
                foreach (char c in token)
                {
                    if (!char.IsDigit(c) && c != '.' && c != '*' && c != 'n' && c != 'm')
                        return false;
                }
                return true;
            }

            /// <summary>找标签的冒号，跳过 `"..."` 里的和 `::` 这种。</summary>
            private static int IndexOfLabel(string line)
            {
                bool quoted = false;
                for (int i = 0; i < line.Length; i++)
                {
                    char c = line[i];
                    if (c == '"')
                        quoted = !quoted;
                    else if (c == ':' && !quoted)
                    {
                        if (i + 1 < line.Length && line[i + 1] == ':')
                        {
                            i++;
                            continue;
                        }
                        return i;
                    }
                }
                return -1;
            }

            /// <summary>整串都是连接符字符，且至少有一个 `-` 或 `.`。</summary>
            private static bool IsConnector(string token)
            {
                if (token.Length < 2)
                    return false;
                bool dash = false;
                foreach (char c in token)
                {
                    if (c == '-' || c == '.')
                        dash = true;
                    else if (c != '|' && c != '<' && c != '>' && c != '*' && c != 'o'
                             && c != '{' && c != '}' && c != '#' && c != 'x' && c != '_')
                        return false;
                }
                return dash;
            }

            /// <summary>把连接符翻译成两端的箭头 / 鸦爪 / 线型 / 长度。</summary>
            private static void Connector(string token, Relation rel)
            {
                rel.Dashed = token.IndexOf('.') >= 0;
                int dashes = 0;
                foreach (char c in token)
                {
                    if (c == '-' || c == '.')
                        dashes++;
                }
                rel.Length = Math.Max(1, dashes);

                // 鸦爪记号：|| / o| / |< / o{ / <
                if (token.IndexOf('|') >= 0 || token.IndexOf('{') >= 0 || token.IndexOf('}') >= 0)
                {
                    rel.LeftEr = ErEndOf(token, true);
                    rel.RightEr = ErEndOf(token, false);
                    return;
                }

                string left = Left(token);
                string right = Right(token);
                rel.LeftArrow = Arrow(left);
                rel.RightArrow = Arrow(right);
            }

            /// <summary>取连接符里 `-` / `.` 之前的那半段。</summary>
            private static string Left(string token)
            {
                int i = 0;
                while (i < token.Length && token[i] != '-' && token[i] != '.')
                    i++;
                return token.Substring(0, i);
            }

            private static string Right(string token)
            {
                int i = token.Length - 1;
                while (i >= 0 && token[i] != '-' && token[i] != '.')
                    i--;
                return token.Substring(i + 1);
            }

            private static EndArrow Arrow(string part)
            {
                if (part.Length == 0)
                    return EndArrow.None;
                if (part[0] == '<')
                    return EndArrow.Open;
                if (part[0] == '>')
                    return EndArrow.Open;
                if (part.IndexOf('|') >= 0)
                    return EndArrow.Triangle;
                if (part[0] == '*')
                    return EndArrow.DiamondFilled;
                if (part[0] == 'o')
                    return EndArrow.DiamondHollow;
                return EndArrow.None;
            }

            /// <summary>ER 那一端：`||` / `o|` / `|&lt;` / `o{` / `&lt;`。</summary>
            private static ErEnd ErEndOf(string token, bool left)
            {
                string part = left ? Left(token) : Right(token);
                if (part.Length == 0)
                    return ErEnd.None;
                bool zero = part.IndexOf('o') >= 0;
                bool many = part.IndexOf('{') >= 0 || part.IndexOf('}') >= 0 || part.IndexOf('<') >= 0 || part.IndexOf('>') >= 0;
                bool one = part.IndexOf('|') >= 0;
                if (many)
                    return zero ? ErEnd.ZeroMany : ErEnd.OneMany;
                if (one)
                    return zero ? ErEnd.ZeroOne : ErEnd.One;
                return ErEnd.None;
            }

            // ── 其它语句 ─────────────────────────────────────────

            private void Hide(string line)
            {
                string what = line.Length > 4 ? line.Substring(4).Trim().ToLowerInvariant() : "";
                if (what.StartsWith("member"))
                    _diagram.HideMembers = line.StartsWith("hide", StringComparison.OrdinalIgnoreCase);
                else if (what.StartsWith("method"))
                    _diagram.HideMethods = line.StartsWith("hide", StringComparison.OrdinalIgnoreCase);
                else if (what.StartsWith("field") || what.StartsWith("attribute"))
                    _diagram.HideFields = line.StartsWith("hide", StringComparison.OrdinalIgnoreCase);
                else if (what.StartsWith("circle"))
                    _diagram.HideCircle = line.StartsWith("hide", StringComparison.OrdinalIgnoreCase);
                else if (what.StartsWith("empty"))
                    _diagram.HideEmptyMembers = line.StartsWith("hide", StringComparison.OrdinalIgnoreCase);
                _i++;
            }

            private bool Package(string line)
            {
                int body = line.IndexOf('{');
                string decl = body >= 0 ? line.Substring(0, body) : line;
                int sp = decl.IndexOf(' ');
                string name = sp >= 0 ? decl.Substring(sp + 1).Trim() : "";
                string label;
                string key;
                SplitDecl(name, out label, out key);
                if (key.Length == 0)
                {
                    _i++;
                    return true;
                }
                Element parent = _stack.Count > 0 ? _stack[_stack.Count - 1] : null;
                Element e = _diagram.Declare(key, label, ElementKind.Package, body >= 0, parent);
                e.ShowIcon = false;
                _i++;
                if (body >= 0)
                    _stack.Add(e);
                return true;
            }

            private bool Note(string line)
            {
                if (!line.StartsWith("note", StringComparison.OrdinalIgnoreCase))
                    return false;
                // note bottom of X  /  note right of X  /  note left of X
                string placement = "bottom";
                foreach (string p in new[] { "left", "right", "top", "bottom" })
                {
                    if (line.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        placement = p;
                        break;
                    }
                }
                int of = line.IndexOf(" of ", StringComparison.OrdinalIgnoreCase);
                string target = of >= 0 ? line.Substring(of + 4).Trim() : "";
                string text = "";
                int colon = line.IndexOf(':');
                if (colon >= 0)
                {
                    text = line.Substring(colon + 1).Trim();
                    _i++;
                }
                else
                {
                    _i++;
                    var sb = new StringBuilder();
                    while (_i < _lines.Count && !Head(_lines[_i]).Equals("end", StringComparison.OrdinalIgnoreCase))
                    {
                        if (sb.Length > 0)
                            sb.Append('\n');
                        sb.Append(_lines[_i]);
                        _i++;
                    }
                    if (_i < _lines.Count)
                        _i++;
                    text = sb.ToString();
                }
                _diagram.Notes.Add(new DiagramNote { Target = target, Placement = placement, Text = text });
                return true;
            }

            private static string Head(string line)
            {
                int i = 0;
                while (i < line.Length && !char.IsWhiteSpace(line[i]) && line[i] != '(' && line[i] != ':')
                    i++;
                return line.Substring(0, i).ToLowerInvariant();
            }

            private static int Count(string s, char c)
            {
                int n = 0;
                foreach (char ch in s)
                {
                    if (ch == c)
                        n++;
                }
                return n;
            }
        }
    }
}
