using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MarkdownKit.PlantUml
{
    /// <summary>
    /// 把思维导图 / WBS / JSON / YAML 解析成同一套 <see cref="TreeDiagram"/>。
    /// 前两者是 `*` / `+` 缩进大纲，后两者是嵌套数据，最终都变成"节点 + 子节点"的树。
    /// </summary>
    internal static class TreeParser
    {
        public static TreeDiagram Parse(string source, TreeKind kind)
        {
            var diagram = new TreeDiagram { Kind = kind };
            string[] lines = (source ?? string.Empty)
                .Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            // JSON / YAML 的 #highlight 与 <style> 是渲染指令，不属于数据本身
            if (kind == TreeKind.Json || kind == TreeKind.Yaml)
                lines = TreeDirectives.Extract(lines, diagram);

            if (kind == TreeKind.Json)
                new JsonReader(lines, diagram).Run();
            else if (kind == TreeKind.Yaml)
                new YamlReader(lines, diagram).Run();
            else
                new OutlineReader(lines, diagram).Run();

            return diagram;
        }

        // ── 大纲式（mindmap / wbs）────────────────────────────────

        private sealed class OutlineReader
        {
            private readonly string[] _lines;
            private readonly TreeDiagram _diagram;
            private readonly List<TreeNode> _stack = new List<TreeNode>();

            public OutlineReader(string[] lines, TreeDiagram diagram)
            {
                _lines = lines;
                _diagram = diagram;
            }

            public void Run()
            {
                for (int i = 0; i < _lines.Length; i++)
                {
                    string line = _lines[i].Trim();
                    if (line.Length == 0 || line[0] == '\'')
                        continue;

                    if (line[0] == '@' || line[0] == '!')
                    {
                        _diagram.Known++;
                        continue;
                    }
                    if (line.StartsWith("title", StringComparison.OrdinalIgnoreCase) &&
                        (line.Length == 5 || line[5] == ' '))
                    {
                        _diagram.Title = line.Length > 5 ? line.Substring(5).Trim() : "";
                        _diagram.Known++;
                        continue;
                    }
                    if (line.StartsWith("skinparam", StringComparison.OrdinalIgnoreCase)
                        || line.StartsWith("caption", StringComparison.OrdinalIgnoreCase)
                        || line.StartsWith("header", StringComparison.OrdinalIgnoreCase)
                        || line.StartsWith("footer", StringComparison.OrdinalIgnoreCase)
                        || line.StartsWith("legend", StringComparison.OrdinalIgnoreCase))
                    {
                        _diagram.Known++;
                        continue;
                    }

                    if (Outline(line, ref i))
                        _diagram.Known++;
                    else
                        _diagram.Unknown++;
                }
            }

            /// <summary>`*` / `+` 的个数就是层级，`+` 开头表示摆到根的左边。</summary>
            private bool Outline(string line, ref int index)
            {
                int i = 0;
                bool left = line[0] == '+';
                while (i < line.Length && (line[i] == '*' || line[i] == '+'))
                    i++;
                if (i == 0)
                    return false;

                int depth = i;
                string text = line.Substring(i).Trim();

                // *: 多行节点，读到单独一行的 ;
                if (text.StartsWith(":", StringComparison.Ordinal))
                {
                    var sb = new StringBuilder(text.Substring(1).Trim());
                    while (++index < _lines.Length)
                    {
                        string t = _lines[index].Trim();
                        if (t == ";")
                            break;
                        sb.Append('\n').Append(t);
                    }
                    text = sb.ToString();
                }

                text = StripColor(text);
                if (text.Length == 0)
                    return false;

                var node = new TreeNode
                {
                    Text = text,
                    LeftSide = left && _diagram.Kind == TreeKind.MindMap
                };

                if (depth <= 1 || _stack.Count == 0)
                {
                    _diagram.Roots.Add(node);
                    _stack.Clear();
                }
                else
                {
                    while (_stack.Count >= depth)
                        _stack.RemoveAt(_stack.Count - 1);
                    _stack[_stack.Count - 1].Children.Add(node);
                }
                _stack.Add(node);
                return true;
            }

            /// <summary>去掉 `[#颜色]` 这类前缀。</summary>
            private static string StripColor(string text)
            {
                if (text.StartsWith("[", StringComparison.Ordinal))
                {
                    int close = text.IndexOf(']');
                    if (close > 0)
                        return text.Substring(close + 1).Trim();
                }
                return text;
            }
        }

        // ── JSON ─────────────────────────────────────────────────

        private sealed class JsonReader
        {
            private readonly string[] _lines;
            private readonly TreeDiagram _diagram;
            private int _pos;

            public JsonReader(string[] lines, TreeDiagram diagram)
            {
                _lines = lines;
                _diagram = diagram;
            }

            public void Run()
            {
                var sb = new StringBuilder();
                foreach (string raw in _lines)
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '\'')
                        continue;
                    if (line.StartsWith("@start", StringComparison.OrdinalIgnoreCase)
                        || line.StartsWith("@end", StringComparison.OrdinalIgnoreCase)
                        || line.StartsWith("!", StringComparison.Ordinal))
                        continue;
                    sb.Append(line);
                }

                _text = sb.ToString();
                _pos = 0;
                Skip();
                TreeNode root;
                if (_pos < _text.Length && _text[_pos] == '{')
                    root = Table();
                else if (_pos < _text.Length && _text[_pos] == '[')
                    root = ArrayTable();
                else
                    root = new TreeNode { IsTable = true };

                _diagram.Roots.Add(root);
                _diagram.Known = root.Rows.Count > 0 ? 1 : 0;
                _diagram.Unknown = 0;
            }

            private string _text;

            /// <summary>一个对象 = 一张表，表里每行是"键 | 值"；值又是对象就挂到 Nested。</summary>
            private TreeNode Table()
            {
                _pos++;   // {
                var table = new TreeNode { IsTable = true };
                while (true)
                {
                    Skip();
                    if (_pos >= _text.Length || _text[_pos] == '}')
                    {
                        _pos++;
                        break;
                    }
                    if (_text[_pos] == ',')
                    {
                        _pos++;
                        continue;
                    }
                    string key = Key();
                    Skip();
                    if (_pos < _text.Length && _text[_pos] == ':')
                        _pos++;
                    table.Rows.Add(Row(key));
                }
                return table;
            }

            private TreeNode ArrayTable()
            {
                _pos++;   // [
                var table = new TreeNode { IsTable = true };
                int i = 0;
                while (true)
                {
                    Skip();
                    if (_pos >= _text.Length || _text[_pos] == ']')
                    {
                        _pos++;
                        break;
                    }
                    if (_text[_pos] == ',')
                    {
                        _pos++;
                        continue;
                    }
                    table.Rows.Add(Row((i++).ToString()));
                }
                return table;
            }

            private TreeNode Row(string key)
            {
                Skip();
                if (_pos >= _text.Length)
                    return new TreeNode { Text = key, Value = "" };
                char c = _text[_pos];
                if (c == '{')
                    return new TreeNode { Text = key, Value = "", Nested = Table() };
                if (c == '[')
                    return new TreeNode { Text = key, Value = "", Nested = ArrayTable() };
                return new TreeNode { Text = key, Value = Scalar() };
            }

            private string Key()
            {
                Skip();
                if (_pos < _text.Length && _text[_pos] == '"')
                    return Scalar();
                int start = _pos;
                while (_pos < _text.Length && _text[_pos] != ':' && _text[_pos] != ',' && _text[_pos] != '}')
                    _pos++;
                return _text.Substring(start, _pos - start).Trim();
            }

            private string Scalar()
            {
                Skip();
                if (_pos >= _text.Length)
                    return "";
                if (_text[_pos] == '"')
                {
                    _pos++;
                    var sb = new StringBuilder();
                    while (_pos < _text.Length && _text[_pos] != '"')
                    {
                        if (_text[_pos] == '\\' && _pos + 1 < _text.Length)
                        {
                            _pos++;
                            switch (_text[_pos])
                            {
                                case 'n': sb.Append('\n'); break;
                                case 'r': sb.Append('\r'); break;
                                case 't': sb.Append('\t'); break;
                                case 'b': sb.Append('\b'); break;
                                case 'f': sb.Append('\f'); break;
                                case 'u': AppendUnicode(sb); break;
                                default: sb.Append(_text[_pos]); break;
                            }
                        }
                        else
                        {
                            sb.Append(_text[_pos]);
                        }
                        _pos++;
                    }
                    _pos++;
                    return sb.ToString();
                }
                int start = _pos;
                while (_pos < _text.Length && _text[_pos] != ',' && _text[_pos] != '}' && _text[_pos] != ']')
                    _pos++;
                return _text.Substring(start, _pos - start).Trim();
            }

            /// <summary>已停在 'u' 上，往后读 4 位十六进制。代理对分两个 \u 写，逐个追加即可。</summary>
            private void AppendUnicode(StringBuilder sb)
            {
                if (_pos + 4 >= _text.Length)
                    return;
                int code;
                if (int.TryParse(_text.Substring(_pos + 1, 4), NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out code))
                {
                    sb.Append((char)code);
                    _pos += 4;
                }
            }

            private void Skip()
            {
                while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos]))
                    _pos++;
            }
        }

        // ── YAML ─────────────────────────────────────────────────

        private sealed class YamlReader
        {
            private readonly string[] _lines;
            private readonly TreeDiagram _diagram;

            public YamlReader(string[] lines, TreeDiagram diagram)
            {
                _lines = lines;
                _diagram = diagram;
            }

            public void Run()
            {
                // 先把源码里真正的内容行收集起来，带上缩进
                var items = new List<KeyValuePair<int, string>>();
                foreach (string raw in _lines)
                {
                    if (raw.Trim().Length == 0 || raw.TrimStart()[0] == '#')
                        continue;
                    string t = raw.Trim();
                    if (t.StartsWith("@start", StringComparison.OrdinalIgnoreCase)
                        || t.StartsWith("@end", StringComparison.OrdinalIgnoreCase)
                        || t.StartsWith("!", StringComparison.Ordinal))
                        continue;
                    items.Add(new KeyValuePair<int, string>(Indent(raw), t));
                }

                int index = 0;
                var table = new TreeNode { IsTable = true };
                table.Rows.AddRange(ParseBlock(items, ref index, items.Count > 0 ? items[0].Key : 0));
                _diagram.Roots.Add(table);
                _diagram.Known = table.Rows.Count > 0 ? 1 : 0;
                _diagram.Unknown = 0;
            }

            private static int Indent(string raw)
            {
                // 官方把空格和制表符都算一层缩进；文档示例大量使用 Tab
                int n = 0;
                while (n < raw.Length && (raw[n] == ' ' || raw[n] == '\t'))
                    n++;
                return n;
            }

            /// <summary>同缩进的一组行。值是空、下一行缩进更深的，就是嵌套对象。</summary>
            private List<TreeNode> ParseBlock(List<KeyValuePair<int, string>> items, ref int index, int indent)
            {
                var rows = new List<TreeNode>();
                while (index < items.Count)
                {
                    int cur = items[index].Key;
                    if (cur != indent)
                        break;

                    string line = items[index].Value;
                    index++;

                    // "- xxx" 列表项
                    if (line.StartsWith("- ", StringComparison.Ordinal) || line == "-")
                    {
                        string text = line.Length > 2 ? line.Substring(2).Trim() : "";
                        var item = new TreeNode { Text = rows.Count.ToString(), Value = text };
                        if (text.Length == 0 && index < items.Count && items[index].Key > cur)
                        {
                            var nested = new TreeNode { IsTable = true };
                            nested.Rows.AddRange(ParseBlock(items, ref index, items[index].Key));
                            item.Nested = nested;
                        }
                        rows.Add(item);
                        continue;
                    }

                    int colon = line.IndexOf(':');
                    if (colon < 0)
                        continue;
                    string key = line.Substring(0, colon).Trim();
                    string value = line.Substring(colon + 1).Trim();
                    var row = new TreeNode { Text = key, Value = value };
                    if (value.Length == 0 && index < items.Count && items[index].Key > cur)
                    {
                        var nested = new TreeNode { IsTable = true };
                        nested.Rows.AddRange(ParseBlock(items, ref index, items[index].Key));
                        row.Nested = nested;
                    }
                    rows.Add(row);
                }
                return rows;
            }
        }
    }
}
