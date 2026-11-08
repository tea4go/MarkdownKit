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
                // 拼接解析文本时顺带剥离注释：键上方紧邻的注释攒着挂到下一行内容上，
                // 遇纯空行断开；行尾注释也算该行的说明。拼接后的文本不再含注释，
                // 用每行的起始偏移把注释查回键所在行。
                var sb = new StringBuilder();
                var starts = new List<int>();
                var notes = new List<string>();
                var pending = new List<string>();
                var block = new StringBuilder();
                bool inBlock = false;
                bool blockIsLeading = false;
                int blockStartLine = -1;
                int lineNo = -1;
                bool inString = false;
                char stringQuote = '"';
                bool escape = false;

                foreach (string raw in _lines)
                {
                    lineNo++;
                    string line = raw;

                    // 上一行末尾是反斜杠：字符串跨行延续，行尾的 \ 已吃掉换行；
                    // 没有续行标记却仍未闭合的，宽松复位，避免吞掉后面的行
                    if (inString)
                    {
                        if (escape)
                            escape = false;
                        else
                            inString = false;
                    }

                    string trimmed = line.Trim();
                    if (!inString
                        && (trimmed.StartsWith("@start", StringComparison.OrdinalIgnoreCase)
                            || trimmed.StartsWith("@end", StringComparison.OrdinalIgnoreCase)
                            || trimmed.StartsWith("!", StringComparison.Ordinal)))
                        continue;
                    if (!inString && line.Length > 0 && line[0] == '\''
                        && !LooksLikeSingleQuotedData(line))
                        continue;

                    var clean = new StringBuilder();
                    string inline = null;
                    bool hadComment = false;

                    for (int i = 0; i < line.Length;)
                    {
                        char c = line[i];

                        if (inBlock)
                        {
                            int end = line.IndexOf("*/", i, StringComparison.Ordinal);
                            if (end < 0)
                            {
                                AppendBlockLine(block, line.Substring(i));
                                i = line.Length;
                                break;
                            }
                            AppendBlockLine(block, line.Substring(i, end - i));
                            i = end + 2;
                            inBlock = false;
                            // 行首块注释（含跨行）作为说明；行内块注释只显示单行那种
                            if (blockIsLeading)
                                pending.Add(block.ToString().Trim());
                            else if (lineNo == blockStartLine)
                                inline = ConcatNote(inline, block.ToString().Trim());
                            block.Clear();
                            hadComment = true;
                            continue;
                        }

                        if (inString)
                        {
                            clean.Append(c);
                            if (escape)
                                escape = false;
                            else if (c == '\\')
                                escape = true;
                            else if (c == stringQuote)
                                inString = false;
                            i++;
                            continue;
                        }

                        if (c == '"' || c == '\'')
                        {
                            inString = true;
                            stringQuote = c;
                            clean.Append(c);
                            i++;
                            continue;
                        }

                        if (c == '/' && i + 1 < line.Length && line[i + 1] == '/')
                        {
                            string comment = line.Substring(i + 2).Trim();
                            if (clean.ToString().Trim().Length == 0)
                                pending.Add(comment);
                            else
                                inline = ConcatNote(inline, comment);
                            hadComment = true;
                            i = line.Length;
                            break;
                        }

                        if (c == '/' && i + 1 < line.Length && line[i + 1] == '*')
                        {
                            blockIsLeading = clean.ToString().Trim().Length == 0;
                            blockStartLine = lineNo;
                            block.Clear();
                            inBlock = true;
                            hadComment = true;
                            i += 2;
                            continue;
                        }

                        // 非标准的 # 注释：JSON 里字符串外出现、且前面是空白时才认
                        if (c == '#' && (i == 0 || char.IsWhiteSpace(line[i - 1])))
                        {
                            string comment = line.Substring(i + 1).Trim();
                            if (clean.ToString().Trim().Length == 0)
                                pending.Add(comment);
                            else
                                inline = ConcatNote(inline, comment);
                            hadComment = true;
                            i = line.Length;
                            break;
                        }

                        clean.Append(c);
                        i++;
                    }

                    // 跨行块注释还没结束、本行又没有内容：整行都是注释
                    if (inBlock && clean.ToString().Trim().Length == 0)
                        continue;

                    string text = clean.ToString().Trim();
                    if (text.Length == 0)
                    {
                        // 纯空行才断开注释；整行只有注释不算空行
                        if (!hadComment)
                            pending.Clear();
                        continue;
                    }

                    string note = pending.Count > 0 ? string.Join("\n", pending) : null;
                    note = ConcatNote(note, inline);
                    pending.Clear();

                    starts.Add(sb.Length);
                    notes.Add(note);
                    sb.Append(text).Append('\n');
                }

                _text = sb.ToString();
                _lineStarts = starts.ToArray();
                _lineNotes = notes.ToArray();
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

            /// <summary>每行内容在拼接文本里的起始偏移，以及该行的说明。</summary>
            private int[] _lineStarts;
            private string[] _lineNotes;

            private static void AppendBlockLine(StringBuilder block, string part)
            {
                string text = part.Trim();
                if (text.Length == 0 && block.Length == 0)
                    return;
                if (block.Length > 0)
                    block.Append('\n');
                block.Append(text);
            }

            private static string ConcatNote(string above, string below)
            {
                if (string.IsNullOrEmpty(below))
                    return string.IsNullOrEmpty(above) ? null : above;
                return string.IsNullOrEmpty(above) ? below : above + "\n" + below;
            }

            /// <summary>按字符偏移二分查找所在行，返回该行的说明。</summary>
            private string LookupNote(int offset)
            {
                int lo = 0;
                int hi = _lineStarts.Length - 1;
                int found = -1;
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;
                    if (_lineStarts[mid] <= offset)
                    {
                        found = mid;
                        lo = mid + 1;
                    }
                    else
                    {
                        hi = mid - 1;
                    }
                }
                return found >= 0 ? _lineNotes[found] : null;
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
                    int keyPos = _pos;
                    string key = Key();
                    Skip();
                    if (_pos < _text.Length && _text[_pos] == ':')
                        _pos++;
                    table.Rows.Add(Row(key, LookupNote(keyPos)));
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
                    int itemPos = _pos;
                    table.Rows.Add(Row((i++).ToString(), LookupNote(itemPos)));
                }
                return table;
            }

            private TreeNode Row(string key, string note)
            {
                Skip();
                if (_pos >= _text.Length)
                    return new TreeNode { Text = key, Value = "", Note = note };
                char c = _text[_pos];
                if (c == '{')
                    return new TreeNode { Text = key, Value = "", Note = note, Nested = Table() };
                if (c == '[')
                    return new TreeNode { Text = key, Value = "", Note = note, Nested = ArrayTable() };
                return new TreeNode { Text = key, Value = Scalar(), Note = note };
            }

            private string Key()
            {
                Skip();
                if (_pos < _text.Length && (_text[_pos] == '"' || _text[_pos] == '\''))
                    return Scalar();
                int start = _pos;
                while (_pos < _text.Length && _text[_pos] != ':' && _text[_pos] != ',' && _text[_pos] != '}')
                    _pos++;
                return _text.Substring(start, _pos - start).Trim();
            }

            // 列首的单引号既可能是 PlantUML 注释，也可能是 JSON5 顶格的单引号键/值。
            // 右引号后面只允许空白再跟 : , ] } / 或行尾，才按数据行处理，否则当注释跳过。
            // 行内没有右引号时，行尾未转义的反斜杠表示字符串续到下一行，也算数据行。
            private static bool LooksLikeSingleQuotedData(string line)
            {
                bool escape = false;
                int closing = -1;
                for (int i = 1; i < line.Length; i++)
                {
                    char c = line[i];
                    if (escape)
                    {
                        escape = false;
                        continue;
                    }
                    if (c == '\\')
                    {
                        escape = true;
                        continue;
                    }
                    if (c == '\'')
                    {
                        closing = i;
                        break;
                    }
                }

                if (closing < 0)
                {
                    int j = line.Length - 1;
                    while (j >= 0 && (line[j] == ' ' || line[j] == '\t' || line[j] == '\r'))
                        j--;
                    int backslashes = 0;
                    for (; j >= 0 && line[j] == '\\'; j--)
                        backslashes++;
                    return backslashes % 2 == 1;
                }

                for (int i = closing + 1; i < line.Length; i++)
                {
                    char c = line[i];
                    if (c == ' ' || c == '\t' || c == '\r')
                        continue;
                    return c == ':' || c == ',' || c == ']' || c == '}' || c == '/' || c == '#';
                }
                return true;
            }

            private string Scalar()
            {
                Skip();
                if (_pos >= _text.Length)
                    return "";
                char quote = _text[_pos];
                if (quote == '"' || quote == '\'')
                {
                    _pos++;
                    var sb = new StringBuilder();
                    while (_pos < _text.Length && _text[_pos] != quote && _text[_pos] != '\n')
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
                                case '\n': break;   // JSON5 行尾 \ 续行，换行不进入值
                                // Parse 已把 CRLF 归一化为 LF，这里只是防御。
                                case '\r':
                                    if (_pos + 1 < _text.Length && _text[_pos + 1] == '\n')
                                        _pos++;
                                    break;
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
                // 先把源码里真正的内容行收集起来，带上缩进；
                // 键上方的注释先攒着，挂到紧挨着的下一个内容行上，遇空行即断开
                var items = new List<YamlItem>();
                var pending = new List<string>();
                foreach (string raw in _lines)
                {
                    string t = raw.Trim();
                    if (t.Length == 0)
                    {
                        pending.Clear();
                        continue;
                    }
                    if (t[0] == '#')
                    {
                        pending.Add(CommentText(t));
                        continue;
                    }
                    if (t.StartsWith("@start", StringComparison.OrdinalIgnoreCase)
                        || t.StartsWith("@end", StringComparison.OrdinalIgnoreCase)
                        || t.StartsWith("!", StringComparison.Ordinal))
                        continue;
                    items.Add(new YamlItem
                    {
                        Indent = Indent(raw),
                        Text = t,
                        Note = pending.Count == 0 ? null : string.Join("\n", pending)
                    });
                    pending.Clear();
                }

                int index = 0;
                var table = new TreeNode { IsTable = true };
                table.Rows.AddRange(ParseBlock(items, ref index, items.Count > 0 ? items[0].Indent : 0));
                _diagram.Roots.Add(table);
                _diagram.Known = table.Rows.Count > 0 ? 1 : 0;
                _diagram.Unknown = 0;
            }

            /// <summary>一条内容行；Note 是它上方紧邻的注释。</summary>
            private sealed class YamlItem
            {
                public int Indent;
                public string Text;
                public string Note;
            }

            /// <summary>去掉注释行开头的 # 与空白；`## 分区` 也只留正文。</summary>
            private static string CommentText(string trimmed)
            {
                int i = 0;
                while (i < trimmed.Length && trimmed[i] == '#')
                    i++;
                return trimmed.Substring(i).Trim();
            }

            /// <summary>拆出行尾注释（`#` 前须是空白，引号内的不算），返回注释文字。</summary>
            private static string SplitInlineComment(ref string text)
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
                    else if (c == '#' && !single && !doubleQuote
                        && (i == 0 || text[i - 1] == ' ' || text[i - 1] == '\t'))
                    {
                        string comment = text.Substring(i + 1).Trim();
                        text = text.Substring(0, i).TrimEnd();
                        return comment.Length == 0 ? null : comment;
                    }
                }
                return null;
            }

            private static string MergeNote(string above, string inline)
            {
                if (string.IsNullOrEmpty(inline))
                    return string.IsNullOrEmpty(above) ? null : above;
                return string.IsNullOrEmpty(above) ? inline : above + "\n" + inline;
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
            private List<TreeNode> ParseBlock(List<YamlItem> items, ref int index, int indent)
            {
                var rows = new List<TreeNode>();
                while (index < items.Count)
                {
                    int cur = items[index].Indent;
                    if (cur != indent)
                        break;

                    string line = items[index].Text;
                    string note = MergeNote(items[index].Note, SplitInlineComment(ref line));
                    index++;

                    // "- xxx" 列表项
                    if (line.StartsWith("- ", StringComparison.Ordinal) || line == "-")
                    {
                        string text = line.Length > 2 ? line.Substring(2).Trim() : "";
                        var item = new TreeNode { Text = rows.Count.ToString(), Value = text, Note = note };
                        if (text.Length == 0 && index < items.Count && items[index].Indent > cur)
                        {
                            var nested = new TreeNode { IsTable = true };
                            nested.Rows.AddRange(ParseBlock(items, ref index, items[index].Indent));
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
                    var row = new TreeNode { Text = key, Value = value, Note = note };
                    if (value.Length == 0 && index < items.Count && items[index].Indent > cur)
                    {
                        var nested = new TreeNode { IsTable = true };
                        nested.Rows.AddRange(ParseBlock(items, ref index, items[index].Indent));
                        row.Nested = nested;
                    }
                    rows.Add(row);
                }
                return rows;
            }
        }
    }
}
