using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace MarkdownKit.PlantUml
{
    /// <summary>
    /// INI / .conf：逐行的 [section] 与 key=value。注释认 `;` 与 `#`，
    /// 同名 section 合并到同一张表，没有 section 的键挂在根表上。
    /// </summary>
    internal sealed class IniReader
    {
        private readonly string[] _lines;
        private readonly TreeDiagram _diagram;

        public IniReader(string[] lines, TreeDiagram diagram)
        {
            _lines = lines;
            _diagram = diagram;
        }

        public void Run()
        {
            TreeNode root = ConfigTree.NewTable();
            TreeNode current = root;
            var pending = new List<string>();

            foreach (string raw in _lines)
            {
                string line = raw.Trim();
                if (line.Length == 0)
                {
                    pending.Clear();
                    continue;
                }
                if (line[0] == ';' || line[0] == '#')
                {
                    pending.Add(ConfigTree.CommentText(line));
                    continue;
                }
                if (line[0] == '[')
                {
                    int close = line.IndexOf(']');
                    if (close <= 1)
                        continue;
                    current = ConfigTree.ChildTable(root, line.Substring(1, close - 1).Trim());
                    pending.Clear();
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;

                string key = line.Substring(0, eq).Trim();
                if (key.Length == 0)
                    continue;
                string value = line.Substring(eq + 1).Trim();
                value = ConfigTree.StripInlineComment(value, ';');
                value = ConfigTree.StripInlineComment(value, '#');

                string note = pending.Count == 0 ? null : string.Join("\n", pending);
                pending.Clear();
                current.Rows.Add(ConfigTree.Row(key, value, note));
            }

            ConfigTree.Complete(_diagram, root);
        }
    }

    /// <summary>
    /// TOML：表头、数组表、键值。值是数组或内联表时展开成子表（数组按索引成行），
    /// 其余按标量原样显示。跨行数组与三引号字符串会拼成一行再解析。
    /// </summary>
    internal sealed class TomlReader
    {
        private readonly string[] _lines;
        private readonly TreeDiagram _diagram;
        private TreeNode _root;
        private TreeNode _current;
        private readonly List<string> _pending = new List<string>();

        public TomlReader(string[] lines, TreeDiagram diagram)
        {
            _lines = lines;
            _diagram = diagram;
        }

        public void Run()
        {
            _root = ConfigTree.NewTable();
            _current = _root;

            for (int i = 0; i < _lines.Length; i++)
            {
                string line = _lines[i].Trim();
                if (line.Length == 0)
                {
                    _pending.Clear();
                    continue;
                }
                if (line[0] == '#')
                {
                    _pending.Add(ConfigTree.CommentText(line));
                    continue;
                }

                if (line.StartsWith("[[", StringComparison.Ordinal))
                {
                    int close = line.IndexOf("]]", StringComparison.Ordinal);
                    if (close > 2)
                        _current = ConfigTree.ArrayElementAt(_root, line.Substring(2, close - 2));
                    _pending.Clear();
                    continue;
                }
                if (line[0] == '[')
                {
                    int close = line.IndexOf(']');
                    if (close > 1)
                        _current = ConfigTree.TableAt(_root, line.Substring(1, close - 1));
                    _pending.Clear();
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    _pending.Clear();
                    continue;
                }

                string key = line.Substring(0, eq).Trim();
                string rawValue = ReadValue(line.Substring(eq + 1).Trim(), ref i);
                rawValue = ConfigTree.StripInlineComment(rawValue, '#');
                string note = _pending.Count == 0 ? null : string.Join("\n", _pending);
                _pending.Clear();

                AddKey(_current, key, rawValue, note);
            }

            ConfigTree.Complete(_diagram, _root);
        }

        /// <summary>值跨行（数组 / 内联表未闭合、三引号字符串）时把后续行拼进来。</summary>
        private string ReadValue(string first, ref int index)
        {
            string text = first;
            while (ConfigTree.NeedsMoreValue(text) && index + 1 < _lines.Length)
                text += "\n" + _lines[++index].Trim();
            return text;
        }

        /// <summary>键可以带点（a.b.c），逐层建嵌套表，最后一段写值。</summary>
        private static void AddKey(TreeNode table, string keyPath, string rawValue, string note)
        {
            string[] parts = ConfigTree.SplitKeyPath(keyPath);
            for (int i = 0; i < parts.Length - 1; i++)
                table = ConfigTree.ChildTable(table, parts[i]);
            table.Rows.Add(ConfigTree.ValueRow(parts[parts.Length - 1], rawValue, note));
        }
    }

    /// <summary>
    /// XML：根元素一行，属性写成 @name、直接文本写成 #text，子元素各自成行。
    /// 同名子元素直接重复成行，不折叠成数组。解析失败由调用方退回普通文本预览。
    /// </summary>
    internal sealed class XmlReader
    {
        private readonly string _source;
        private readonly TreeDiagram _diagram;

        public XmlReader(string source, TreeDiagram diagram)
        {
            _source = source;
            _diagram = diagram;
        }

        public void Run()
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null
            };

            XDocument document;
            using (var text = new StringReader(StripDirectives(_source)))
            using (var reader = System.Xml.XmlReader.Create(text, settings))
                document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);

            if (document.Root == null)
                return;

            TreeNode root = ConfigTree.NewTable();
            root.Rows.Add(ElementRow(document.Root));
            ConfigTree.Complete(_diagram, root);
        }

        /// <summary>预览包装的 @startxml / @endxml 不是 XML 内容，解析前剥掉。</summary>
        private static string StripDirectives(string source)
        {
            string[] lines = (source ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var sb = new StringBuilder(source == null ? 0 : source.Length);
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("@start", StringComparison.OrdinalIgnoreCase)
                    || trimmed.StartsWith("@end", StringComparison.OrdinalIgnoreCase))
                    continue;
                sb.Append(line).Append('\n');
            }
            return sb.ToString();
        }

        private static TreeNode ElementRow(XElement element)
        {
            return new TreeNode
            {
                Text = element.Name.LocalName,
                Value = "",
                Nested = ElementBody(element)
            };
        }

        private static TreeNode ElementBody(XElement element)
        {
            TreeNode table = ConfigTree.NewTable();

            foreach (XAttribute attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration)
                    continue;
                table.Rows.Add(ConfigTree.Row("@" + attribute.Name.LocalName, attribute.Value));
            }

            foreach (XNode node in element.Nodes())
            {
                XText text = node as XText;
                if (text == null)
                    continue;
                string content = text.Value.Trim();
                if (content.Length > 0)
                    table.Rows.Add(ConfigTree.Row("#text", content));
            }

            foreach (XElement child in element.Elements())
                table.Rows.Add(ElementRow(child));

            return table;
        }
    }

    /// <summary>INI / TOML / XML 三个解析器共用的建表与切段工具。</summary>
    internal static class ConfigTree
    {
        public static TreeNode NewTable()
        {
            return new TreeNode { IsTable = true };
        }

        public static TreeNode Row(string key, string value, string note = null)
        {
            return new TreeNode { Text = key, Value = value ?? "", Note = note };
        }

        public static TreeNode NestedRow(string key, TreeNode nested, string note = null)
        {
            return new TreeNode { Text = key, Value = "", Note = note, Nested = nested };
        }

        public static void Complete(TreeDiagram diagram, TreeNode root)
        {
            diagram.Roots.Add(root);
            diagram.Known = root.Rows.Count > 0 ? 1 : 0;
            diagram.Unknown = 0;
        }

        /// <summary>去掉注释行开头的 `#` / `;` 与空白。</summary>
        public static string CommentText(string trimmed)
        {
            int i = 0;
            while (i < trimmed.Length && (trimmed[i] == '#' || trimmed[i] == ';'))
                i++;
            return trimmed.Substring(i).Trim();
        }

        /// <summary>拆行尾注释：标记前须是空白，且不在引号内。</summary>
        public static string StripInlineComment(string text, char marker)
        {
            bool single = false;
            bool doubleQuote = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\'' && !doubleQuote)
                    single = !single;
                else if (c == '"' && !single && (i == 0 || text[i - 1] != '\\'))
                    doubleQuote = !doubleQuote;
                else if (c == marker && !single && !doubleQuote
                    && (i == 0 || char.IsWhiteSpace(text[i - 1])))
                    return text.Substring(0, i).TrimEnd();
            }
            return text;
        }

        /// <summary>取（或新建）名叫 name 的子表；同名 section / table 合并到一起。</summary>
        public static TreeNode ChildTable(TreeNode parent, string name)
        {
            foreach (TreeNode row in parent.Rows)
            {
                if (row.Nested != null && row.Nested.IsTable && row.Text == name)
                    return row.Nested;
            }
            TreeNode table = NewTable();
            parent.Rows.Add(NestedRow(name, table));
            return table;
        }

        /// <summary>按 a.b.c 找到（或新建）深层表，返回最深一段对应的表。</summary>
        public static TreeNode TableAt(TreeNode root, string path)
        {
            TreeNode table = root;
            foreach (string part in SplitKeyPath(path))
                table = ChildTable(table, part);
            return table;
        }

        /// <summary>`[[name]]` 的每个块都是同一个数组表里的一个元素，返回新元素那张表。</summary>
        public static TreeNode ArrayElementAt(TreeNode root, string path)
        {
            TreeNode parent = root;
            string[] parts = SplitKeyPath(path);
            for (int i = 0; i < parts.Length - 1; i++)
                parent = ChildTable(parent, parts[i]);

            string name = parts[parts.Length - 1];
            TreeNode array = null;
            foreach (TreeNode row in parent.Rows)
            {
                if (row.Nested != null && row.Nested.IsTable && row.Text == name)
                {
                    array = row.Nested;
                    break;
                }
            }
            if (array == null)
            {
                array = NewTable();
                parent.Rows.Add(NestedRow(name, array));
            }

            TreeNode element = NewTable();
            array.Rows.Add(NestedRow(array.Rows.Count.ToString(), element));
            return element;
        }

        /// <summary>按点拆键路径，引号里的点不算分隔符。</summary>
        public static string[] SplitKeyPath(string path)
        {
            var parts = new List<string>();
            var sb = new StringBuilder();
            char quote = '\0';
            foreach (char c in path ?? "")
            {
                if (quote != '\0')
                {
                    if (c == quote)
                        quote = '\0';
                    else
                        sb.Append(c);
                    continue;
                }
                if (c == '"' || c == '\'')
                {
                    quote = c;
                    continue;
                }
                if (c == '.')
                {
                    parts.Add(sb.ToString().Trim());
                    sb.Length = 0;
                    continue;
                }
                sb.Append(c);
            }
            parts.Add(sb.ToString().Trim());

            var result = new List<string>();
            foreach (string part in parts)
            {
                if (part.Length > 0)
                    result.Add(part);
            }
            if (result.Count == 0)
                result.Add((path ?? "").Trim());
            return result.ToArray();
        }

        /// <summary>TOML 的一行值：数组 / 内联表展开成子表，其余按标量显示。</summary>
        public static TreeNode ValueRow(string key, string rawValue, string note = null)
        {
            string value = (rawValue ?? "").Trim();
            if (value.StartsWith("[", StringComparison.Ordinal))
                return NestedRow(key, ParseArray(value), note);
            if (value.StartsWith("{", StringComparison.Ordinal))
                return NestedRow(key, ParseInlineTable(value), note);
            return Row(key, DecodeScalar(value), note);
        }

        private static TreeNode ParseArray(string text)
        {
            TreeNode table = NewTable();
            string inner = text.Length >= 2 ? text.Substring(1, text.Length - 2) : "";
            int index = 0;
            foreach (string part in SplitTopLevel(inner, ','))
            {
                string item = part.Trim();
                if (item.Length == 0)
                    continue;
                table.Rows.Add(ArrayItemRow((index++).ToString(), item));
            }
            return table;
        }

        private static TreeNode ArrayItemRow(string index, string raw)
        {
            if (raw.StartsWith("[", StringComparison.Ordinal))
                return NestedRow(index, ParseArray(raw));
            if (raw.StartsWith("{", StringComparison.Ordinal))
                return NestedRow(index, ParseInlineTable(raw));
            return Row(index, DecodeScalar(raw));
        }

        private static TreeNode ParseInlineTable(string text)
        {
            TreeNode table = NewTable();
            string inner = text.Length >= 2 ? text.Substring(1, text.Length - 2) : "";
            foreach (string part in SplitTopLevel(inner, ','))
            {
                string item = part.Trim();
                if (item.Length == 0)
                    continue;
                int eq = item.IndexOf('=');
                if (eq <= 0)
                    continue;
                string key = item.Substring(0, eq).Trim().Trim('"', '\'');
                string value = StripInlineComment(item.Substring(eq + 1).Trim(), '#');
                table.Rows.Add(ValueRow(key, value, null));
            }
            return table;
        }

        /// <summary>按顶层分隔符切分，引号内与 [] {} 里的分隔符不算。</summary>
        private static List<string> SplitTopLevel(string text, char separator)
        {
            var parts = new List<string>();
            var sb = new StringBuilder();
            int depth = 0;
            char quote = '\0';
            bool escape = false;

            foreach (char c in text)
            {
                if (quote != '\0')
                {
                    sb.Append(c);
                    if (escape)
                        escape = false;
                    else if (c == '\\' && quote == '"')
                        escape = true;
                    else if (c == quote)
                        quote = '\0';
                    continue;
                }
                if (c == '"' || c == '\'')
                {
                    quote = c;
                    sb.Append(c);
                    continue;
                }
                if (c == '[' || c == '{')
                    depth++;
                else if (c == ']' || c == '}')
                    depth--;

                if (c == separator && depth == 0)
                {
                    parts.Add(sb.ToString());
                    sb.Length = 0;
                    continue;
                }
                sb.Append(c);
            }

            parts.Add(sb.ToString());
            return parts;
        }

        /// <summary>数组 / 内联表还没闭合，或者三引号字符串还没收尾，都要继续吃下一行。</summary>
        public static bool NeedsMoreValue(string text)
        {
            if (text.StartsWith("\"\"\"", StringComparison.Ordinal))
                return !(text.Length > 6 && text.EndsWith("\"\"\"", StringComparison.Ordinal));
            if (text.StartsWith("'''", StringComparison.Ordinal))
                return !(text.Length > 6 && text.EndsWith("'''", StringComparison.Ordinal));

            int depth = 0;
            char quote = '\0';
            bool escape = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quote != '\0')
                {
                    if (escape)
                        escape = false;
                    else if (c == '\\' && quote == '"')
                        escape = true;
                    else if (c == quote)
                        quote = '\0';
                    continue;
                }
                if (c == '"' || c == '\'')
                {
                    quote = c;
                    continue;
                }
                if (c == '#' && (i == 0 || char.IsWhiteSpace(text[i - 1])))
                    break;
                if (c == '[' || c == '{')
                    depth++;
                else if (c == ']' || c == '}')
                    depth--;
            }
            return depth > 0;
        }

        /// <summary>去掉字符串引号与常见转义；数字 / 布尔 / 日期原样保留。</summary>
        public static string DecodeScalar(string raw)
        {
            string text = (raw ?? "").Trim();
            if (text.Length >= 6 && text.StartsWith("\"\"\"", StringComparison.Ordinal)
                && text.EndsWith("\"\"\"", StringComparison.Ordinal))
                return text.Substring(3, text.Length - 6).Trim();
            if (text.Length >= 6 && text.StartsWith("'''", StringComparison.Ordinal)
                && text.EndsWith("'''", StringComparison.Ordinal))
                return text.Substring(3, text.Length - 6).Trim();
            if (text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"')
                return Unescape(text.Substring(1, text.Length - 2));
            if (text.Length >= 2 && text[0] == '\'' && text[text.Length - 1] == '\'')
                return text.Substring(1, text.Length - 2);
            return text;
        }

        private static string Unescape(string text)
        {
            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '\\' || i + 1 >= text.Length)
                {
                    sb.Append(text[i]);
                    continue;
                }

                i++;
                switch (text[i])
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case 'u': i = AppendHex(sb, text, i, 4); break;
                    case 'U': i = AppendHex(sb, text, i, 8); break;
                    default: sb.Append('\\').Append(text[i]); break;
                }
            }
            return sb.ToString();
        }

        /// <summary>从 at 后面读 count 位十六进制，返回最后一个被吃掉的字符下标。</summary>
        private static int AppendHex(StringBuilder sb, string text, int at, int count)
        {
            if (at + count >= text.Length)
                return at;

            int code;
            if (int.TryParse(text.Substring(at + 1, count),
                    System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out code)
                && code >= 0 && code <= 0x10FFFF)
            {
                sb.Append(char.ConvertFromUtf32(code));
                return at + count;
            }
            return at;
        }
    }
}
