using System;
using System.Collections.Generic;
using System.Text;

namespace MarkdownKit.PlantUml
{
    /// <summary>
    /// 把 PlantUML 活动图源码解析成块树。
    /// 认得出的写法：start / stop / end / detach、`:动作;`、if…then…else…endif、
    /// while…endwhile、repeat…repeat while、fork…fork again…end fork、split…end split、
    /// note right/left、partition 和泳道（只取里面的内容，不画框）。
    /// </summary>
    internal static class ActivityParser
    {
        public static ActivityDiagram Parse(string source)
        {
            var diagram = new ActivityDiagram();
            if (string.IsNullOrEmpty(source))
                return diagram;

            var reader = new Reader(source, diagram);
            diagram.Root = reader.Run();
            return diagram;
        }

        private sealed class Reader
        {
            private readonly List<string> _lines = new List<string>();
            private readonly ActivityDiagram _diagram;
            private int _i;
            private int _lane;

            public Reader(string source, ActivityDiagram diagram)
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

            public ActBlock Run()
            {
                // 注意别在这里重算 Unknown：Block 已经把认不出的行一条条数过了，
                // 覆盖掉的话部署图这种"只有 end note 能蒙对"的图会被误判成活动图
                return Block(new string[0]);
            }

            /// <summary>读到 stops 里的关键字（或行尾）为止，返回一个 Seq。</summary>
            private ActBlock Block(string[] stops)
            {
                var seq = new ActBlock { Kind = ActKind.Seq };
                while (_i < _lines.Count)
                {
                    string line = _lines[_i];
                    string head = Head(line);

                    if (Stops(line, stops))
                        break;

                    // 泳道号要在解析这一句之前取：复合块内部的 |泳道| 会把 _lane 改掉，
                    // 而复合块本身（比如 if 的菱形）属于它开始那一刻的泳道
                    int laneHere = _lane;
                    ActBlock one = Statement(line, head);
                    if (one == Noise)
                        continue;   // 认出来了但不产生节点（设置项、括号之类），不计分
                    if (one == null)
                    {
                        // 认不出的行直接跳过，别把整张图卡死
                        _diagram.Unknown++;
                        _i++;
                        continue;
                    }

                    _diagram.Known++;
                    one.Lane = laneHere;
                    if (one.Kind == ActKind.Seq && one.Note != null && one.Text == null)
                    {
                        // 光秃秃的 `note right:` —— 挂到上一条语句上，跟官方一样摆在它右边
                        if (seq.Items.Count > 0)
                        {
                            ActBlock last = seq.Items[seq.Items.Count - 1];
                            last.Note = one.Note;
                            last.NoteRight = one.NoteRight;
                        }
                        continue;
                    }
                    if (one.Kind != ActKind.Seq || one.Items.Count > 0)
                        seq.Items.Add(one);
                }
                return seq;
            }

            /// <summary>认出来了但不产生节点的行（设置项、`}`、`--` 之类）用的占位标记。</summary>
            private static readonly ActBlock Noise = new ActBlock { Kind = ActKind.Seq };

            /// <summary>这一行是不是 stops 里的关键字。按整词比，`else` 不会误吃 `elseif`。</summary>
            private static bool Stops(string line, string[] stops)
            {
                foreach (string stop in stops)
                {
                    if (Match(line, stop))
                        return true;
                }
                return false;
            }

            /// <summary>行首是不是关键字 kw（后面跟空白或左括号，或正好结束）。</summary>
            private static bool Match(string line, string kw)
            {
                if (!line.StartsWith(kw, StringComparison.OrdinalIgnoreCase))
                    return false;
                if (line.Length == kw.Length)
                    return true;
                char c = line[kw.Length];
                return char.IsWhiteSpace(c) || c == '(';
            }

            /// <summary>把当前泳道号盖到整棵子树上——复合块里的语句都跟着声明它时的泳道。</summary>
            private static void Stamp(ActBlock b, int lane)
            {
                b.Lane = lane;
                foreach (ActBlock item in b.Items)
                    Stamp(item, lane);
                if (b.Then != null)
                    Stamp(b.Then, lane);
                if (b.Else != null)
                    Stamp(b.Else, lane);
            }

            /// <summary>`|泳道名|`：换泳道，新名字追加到列表。</summary>
            private void Lane(string line)
            {
                string name = line.Trim().Trim('|').Trim();
                if (name.Length == 0)
                {
                    _i++;
                    return;
                }
                int index = _diagram.Lanes.IndexOf(name);
                if (index < 0)
                {
                    _diagram.Lanes.Add(name);
                    index = _diagram.Lanes.Count - 1;
                }
                _lane = index;
                _i++;
            }

            private ActBlock Statement(string line, string head)
            {
                switch (head)
                {
                    case "start":
                        _i++;
                        return new ActBlock { Kind = ActKind.Start };

                    case "stop":
                    case "kill":
                        _i++;
                        return new ActBlock { Kind = ActKind.Stop };

                    case "end":
                        // 只有光秃秃一个 `end` 才是结束节点；`end note` / `end fork` 之类另有归属
                        if (line != "end")
                            return null;
                        _i++;
                        return new ActBlock { Kind = ActKind.End };

                    case "detach":
                        _i++;
                        return new ActBlock { Kind = ActKind.Detach };

                    case "if":
                        return ParseIf(line);

                    case "while":
                        return ParseWhile(line);

                    case "repeat":
                        return ParseRepeat(line);

                    case "fork":
                    case "split":
                        return ParseFork(line, head);

                    case "partition":
                    case "group":
                        // 只取里面的内容，外框不画
                        _i++;
                        Block(new[] { "}" });
                        return Noise;

                    case "note":
                        return ParseNote(line);
                }

                if (line[0] == '|')
                {
                    Lane(line);
                    return Noise;
                }

                if (line[0] == ':')
                    return ParseAction(line);

                if (head == "title")
                {
                    _diagram.Title = line.Length > 5 ? line.Substring(5).Trim() : "";
                    _i++;
                    return Noise;
                }

                // 这些是设置 / 装饰，认了但不产生节点
                string[] ignored = { "skinparam", "hide", "show", "scale", "header", "footer",
                                     "caption", "legend", "!define", "!", "|", "}", "--", "->", "-->",
                                     "floating note", "swimlane", "backward", "label", "goto" };
                foreach (string ig in ignored)
                {
                    if (head == ig || line.StartsWith(ig, StringComparison.Ordinal))
                    {
                        _i++;
                        return Noise;
                    }
                }
                return null;
            }

            // ── 各条语句 ─────────────────────────────────────────

            /// <summary>`:文字;`，可以跨行，也可以带 `|` 泳道后缀。</summary>
            private ActBlock ParseAction(string line)
            {
                var sb = new StringBuilder();
                string cur = line.Substring(1);
                while (true)
                {
                    int semi = cur.IndexOf(';');
                    if (semi >= 0)
                    {
                        sb.Append(cur.Substring(0, semi));
                        _i++;
                        break;
                    }
                    sb.Append(cur);
                    _i++;
                    if (_i >= _lines.Count)
                        break;
                    sb.Append('\n');
                    cur = _lines[_i];
                }

                string text = sb.ToString().Trim();
                // 去掉 `#颜色` 前缀和 `|泳道` 后缀
                if (text.StartsWith("#", StringComparison.Ordinal))
                {
                    int sp = text.IndexOf(' ');
                    if (sp > 0)
                        text = text.Substring(sp + 1).Trim();
                }
                int bar = text.LastIndexOf('|');
                if (bar >= 0 && text.IndexOf('\n') < 0)
                    text = text.Substring(0, bar).Trim();

                return new ActBlock { Kind = ActKind.Action, Text = text };
            }

            private ActBlock ParseIf(string line)
            {
                var block = new ActBlock { Kind = ActKind.If };
                block.Text = Inside(line, '(', ')');
                block.Label = Inside(line, '(', ')', 2);   // then 后面那个
                _i++;

                block.Then = Block(new[] { "else", "elseif", "endif" });
                if (_i < _lines.Count)
                {
                    string head = Head(_lines[_i]);
                    if (Match(_lines[_i], "elseif"))
                    {
                        block.Else = ParseIf(_lines[_i]);
                        return block;   // ParseIf 已经把 endif 吃掉了
                    }
                    if (Match(_lines[_i], "else"))
                    {
                        block.Else = new ActBlock { Kind = ActKind.Seq, Label = Inside(_lines[_i], '(', ')') };
                        _i++;
                        block.Else.Items.AddRange(Block(new[] { "endif" }).Items);
                    }
                }
                if (_i < _lines.Count && Match(_lines[_i], "endif"))
                    _i++;
                return block;
            }

            private ActBlock ParseWhile(string line)
            {
                var block = new ActBlock { Kind = ActKind.While };
                block.Text = Inside(line, '(', ')');
                block.Label = Inside(line, '(', ')', 2);
                _i++;
                block.Then = Block(new[] { "endwhile" });
                if (_i < _lines.Count && Match(_lines[_i], "endwhile"))
                {
                    // 出口标签写在 endwhile 上：endwhile (否)
                    block.ExitLabel = Inside(_lines[_i], '(', ')');
                    _i++;
                }
                return block;
            }

            private ActBlock ParseRepeat(string line)
            {
                var block = new ActBlock { Kind = ActKind.Repeat };
                _i++;
                block.Then = Block(new[] { "repeat" });
                if (_i < _lines.Count && Match(_lines[_i], "repeat"))
                {
                    string tail = _lines[_i];
                    block.Text = Inside(tail, '(', ')');
                    block.Label = Inside(tail, '(', ')', 2);        // while 后面
                    block.ExitLabel = Inside(tail, '(', ')', 3);    // not 后面
                    _i++;
                }
                return block;
            }

            private ActBlock ParseFork(string line, string head)
            {
                var block = new ActBlock { Kind = ActKind.Fork };
                string again = head == "split" ? "split again" : "fork again";
                string stop = head == "split" ? "end split" : "end fork";
                _i++;
                while (true)
                {
                    ActBlock branch = Block(new[] { again, stop });
                    block.Items.Add(branch);
                    if (_i >= _lines.Count)
                        break;
                    if (Match(_lines[_i], again))
                    {
                        _i++;
                        continue;
                    }
                    if (Match(_lines[_i], stop))
                        _i++;
                    break;
                }
                return block;
            }

            private ActBlock ParseNote(string line)
            {
                // `note right of Alice: xxx` 是时序图的写法，活动图只认 `note right: xxx`
                if (line.IndexOf(" of ", StringComparison.OrdinalIgnoreCase) > 0)
                    return null;

                bool right = !line.StartsWith("note left", StringComparison.OrdinalIgnoreCase);
                string text = "";
                int colon = line.IndexOf(':');
                if (colon >= 0)
                {
                    text = line.Substring(colon + 1).Trim();
                    _i++;
                }
                else
                {
                    // note right / left 换行写，读到 end note
                    _i++;
                    var sb = new StringBuilder();
                    while (_i < _lines.Count && !Match(_lines[_i], "end note"))
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

                // 注释挂到上一条语句上（Text 为 null 表示"这是条纯注释"）；没有上一条就当成孤立节点
                var holder = new ActBlock { Kind = ActKind.Seq, Text = null, Note = text, NoteRight = right };
                return holder;
            }

            // ── 小工具 ───────────────────────────────────────────

            /// <summary>取行首关键字（小写）。</summary>
            private static string Head(string line)
            {
                int i = 0;
                while (i < line.Length && !char.IsWhiteSpace(line[i]) && line[i] != '(' && line[i] != ':')
                    i++;
                return line.Substring(0, i).ToLowerInvariant();
            }

            /// <summary>取第 n 组括号里的内容（n 从 1 开始）。</summary>
            private static string Inside(string line, char open, char close, int n = 1)
            {
                int from = 0;
                for (int k = 0; k < n; k++)
                {
                    int a = line.IndexOf(open, from);
                    if (a < 0)
                        return "";
                    int b = line.IndexOf(close, a + 1);
                    if (b < 0)
                        return "";
                    if (k == n - 1)
                        return line.Substring(a + 1, b - a - 1).Trim();
                    from = b + 1;
                }
                return "";
            }
        }
    }
}
