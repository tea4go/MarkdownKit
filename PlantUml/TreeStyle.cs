using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Media;

namespace MarkdownKit.PlantUml
{
    /// <summary>一条 `#highlight "a" / "b" <<c>>` 高亮规则。</summary>
    internal sealed class TreeHighlightRule
    {
        public readonly string[] Path;
        public readonly string Stereotype;

        public TreeHighlightRule(string[] path, string stereotype)
        {
            Path = path;
            Stereotype = stereotype;
        }

        /// <summary>当前这一层的键是否命中本规则（`**` 只在第二段时特殊处理）。</summary>
        public bool IsRowMatch(string key)
        {
            if (Path.Length == 1)
                return Path[0] == key;
            if (Path.Length == 2 && Path[0] == "**")
                return Path[1] == key;
            return false;
        }

        /// <summary>
        /// 按官方语义把规则下推一层：`**` 原地不动（可匹配任意深度），
        /// `*` 只吃一层，普通段必须与当前键相等。
        /// </summary>
        public TreeHighlightRule UpOneLevel(string key)
        {
            if (Path.Length <= 1)
                return null;
            string first = Path[0];
            if (first == "**")
                return this;
            if (first != "*" && first != key)
                return null;
            var next = new string[Path.Length - 1];
            Array.Copy(Path, 1, next, 0, next.Length);
            return new TreeHighlightRule(next, Stereotype);
        }
    }

    /// <summary>样式规则里可设置的一小组属性；null 表示这一层没写，继续往下一层找。</summary>
    internal sealed class TreeStyleProps
    {
        public Brush Background;
        public Brush TextColor;
        public Brush LineColor;
        public double? FontSize;
        public string FontName;
        public TreeFontStyle? FontStyle;
        public double? LineThickness;
        public double[] Dash;
        public double? RoundCorner;
    }

    /// <summary>FontStyle 的粗体 / 斜体 / 下划线三个开关。</summary>
    internal struct TreeFontStyle
    {
        public bool Bold;
        public bool Italic;
        public bool Underline;
    }

    /// <summary>`<style>` 解析结果。键是 `.类名` 里的类名。</summary>
    internal sealed class TreeStyleSheet
    {
        public readonly TreeStyleProps Node = new TreeStyleProps();
        public readonly TreeStyleProps Header = new TreeStyleProps();
        public readonly TreeStyleProps Highlight = new TreeStyleProps();
        public readonly TreeStyleProps Separator = new TreeStyleProps();
        public readonly TreeStyleProps Arrow = new TreeStyleProps();

        private readonly Dictionary<string, TreeStyleProps> _classes =
            new Dictionary<string, TreeStyleProps>(StringComparer.OrdinalIgnoreCase);

        public TreeStyleProps Class(string name)
        {
            TreeStyleProps props;
            if (!_classes.TryGetValue(name, out props))
            {
                props = new TreeStyleProps();
                _classes[name] = props;
            }
            return props;
        }

        public TreeStyleProps FindClass(string name)
        {
            TreeStyleProps props;
            return name != null && _classes.TryGetValue(name, out props) ? props : null;
        }
    }

    /// <summary>把 `#highlight` 与 `<style>` 指令从源码里摘出来，剩下的行交给数据解析器。</summary>
    internal static class TreeDirectives
    {
        private const string HighlightTag = "#highlight";

        public static string[] Extract(string[] lines, TreeDiagram diagram)
        {
            var kept = new List<string>(lines.Length);
            var styleLines = new List<string>();
            bool inStyle = false;

            foreach (string raw in lines)
            {
                string t = raw.Trim();

                if (inStyle)
                {
                    if (t.StartsWith("</style", StringComparison.OrdinalIgnoreCase))
                    {
                        inStyle = false;
                        TreeStyleParser.Parse(styleLines, diagram.Styles);
                        styleLines.Clear();
                    }
                    else
                    {
                        styleLines.Add(raw);
                    }
                    continue;
                }

                if (t.StartsWith("<style", StringComparison.OrdinalIgnoreCase))
                {
                    int gt = t.IndexOf('>');
                    int close = t.IndexOf("</style>", StringComparison.OrdinalIgnoreCase);
                    if (close >= 0)
                    {
                        int start = gt >= 0 ? gt + 1 : 6;
                        if (close > start)
                            styleLines.Add(t.Substring(start, close - start));
                        TreeStyleParser.Parse(styleLines, diagram.Styles);
                        styleLines.Clear();
                        continue;
                    }
                    inStyle = true;
                    if (gt >= 0 && gt + 1 < t.Length)
                    {
                        string rest = t.Substring(gt + 1).Trim();
                        if (rest.Length > 0)
                            styleLines.Add(rest);
                    }
                    continue;
                }

                if (TryHighlight(t, diagram))
                    continue;

                kept.Add(raw);
            }

            if (inStyle)
                TreeStyleParser.Parse(styleLines, diagram.Styles);

            return kept.ToArray();
        }

        private static bool TryHighlight(string line, TreeDiagram diagram)
        {
            if (!line.StartsWith(HighlightTag, StringComparison.OrdinalIgnoreCase))
                return false;
            if (line.Length > HighlightTag.Length && !char.IsWhiteSpace(line[HighlightTag.Length]))
                return false;

            string rest = line.Length > HighlightTag.Length
                ? line.Substring(HighlightTag.Length).Trim()
                : "";
            if (rest.Length == 0)
                return false;

            string stereotype = null;
            int lt = rest.LastIndexOf("<<", StringComparison.Ordinal);
            int gt = rest.LastIndexOf(">>", StringComparison.Ordinal);
            if (lt >= 0 && gt > lt)
            {
                stereotype = rest.Substring(lt + 2, gt - lt - 2).Trim();
                rest = rest.Substring(0, lt).Trim();
            }

            string[] segments = rest.Split('/');
            var path = new List<string>(segments.Length);
            foreach (string segment in segments)
            {
                string s = segment.Trim();
                if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
                    s = s.Substring(1, s.Length - 2);
                if (s.Length > 0)
                    path.Add(s);
            }
            if (path.Count == 0)
                return false;

            diagram.Highlights.Add(new TreeHighlightRule(path.ToArray(),
                string.IsNullOrEmpty(stereotype) ? null : stereotype));
            return true;
        }
    }

    /// <summary>
    /// 解析 PlantUML 的 CSS 式子集：`yamlDiagram { node { highlight { … } } }`、
    /// 以及 `#highlight … <<h1>>` 配套的 `.h1 { … }`。
    /// </summary>
    internal static class TreeStyleParser
    {
        private sealed class Scope
        {
            public string Selector;
            public bool Active;
        }

        public static void Parse(IEnumerable<string> lines, TreeStyleSheet sheet)
        {
            var stack = new List<Scope>();
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal)
                    || line.StartsWith("'", StringComparison.Ordinal)
                    || line.StartsWith("#", StringComparison.Ordinal))
                    continue;

                int brace = line.IndexOf('{');
                if (brace >= 0)
                {
                    string selector = line.Substring(0, brace).Trim();
                    bool parentActive = stack.Count == 0 || stack[stack.Count - 1].Active;
                    stack.Add(new Scope
                    {
                        Selector = selector,
                        Active = parentActive && IsKnownSelector(selector)
                    });
                    continue;
                }

                if (line.StartsWith("}", StringComparison.Ordinal))
                {
                    if (stack.Count > 0)
                        stack.RemoveAt(stack.Count - 1);
                    continue;
                }

                if (stack.Count == 0)
                    continue;

                TreeStyleProps target = ResolveTarget(stack, sheet);
                if (target != null)
                    ApplyProperty(target, line);
            }
        }

        private static bool IsKnownSelector(string selector)
        {
            foreach (string token in selector.Split(','))
            {
                string s = token.Trim();
                if (s.Length == 0)
                    continue;
                if (s[0] == '.')
                    return true;
                switch (s.ToLowerInvariant())
                {
                    case "yamldiagram":
                    case "jsondiagram":
                    case "node":
                    case "element":
                    case "highlight":
                    case "separator":
                    case "arrow":
                    case "header":
                    case "rootnode":
                        return true;
                }
            }
            return false;
        }

        private static TreeStyleProps ResolveTarget(List<Scope> stack, TreeStyleSheet sheet)
        {
            foreach (Scope scope in stack)
                if (!scope.Active)
                    return null;

            for (int i = stack.Count - 1; i >= 0; i--)
            {
                foreach (string token in stack[i].Selector.Split(','))
                {
                    string s = token.Trim();
                    if (s.Length > 1 && s[0] == '.')
                        return sheet.Class(s.Substring(1));
                }
            }

            for (int i = stack.Count - 1; i >= 0; i--)
            {
                foreach (string token in stack[i].Selector.Split(','))
                {
                    switch (token.Trim().ToLowerInvariant())
                    {
                        case "arrow":
                            return sheet.Arrow;
                        case "separator":
                            return sheet.Separator;
                        case "highlight":
                            return sheet.Highlight;
                        case "header":
                            return sheet.Header;
                    }
                }
            }

            return sheet.Node;
        }

        private static void ApplyProperty(TreeStyleProps target, string line)
        {
            int split = -1;
            for (int i = 0; i < line.Length; i++)
            {
                if (char.IsWhiteSpace(line[i]) || line[i] == ':')
                {
                    split = i;
                    break;
                }
            }
            if (split <= 0)
                return;

            string key = line.Substring(0, split).Trim();
            string value = line.Substring(split + 1).Trim();
            if (value.EndsWith(";", StringComparison.Ordinal))
                value = value.Substring(0, value.Length - 1).Trim();

            switch (key.ToLowerInvariant())
            {
                case "backgroundcolor":
                case "bgcolor":
                    Brush background = ParseBrush(value);
                    if (background != null)
                        target.Background = background;
                    break;
                case "fontcolor":
                    Brush textBrush = ParseBrush(value);
                    if (textBrush != null)
                        target.TextColor = textBrush;
                    break;
                case "linecolor":
                    Brush lineBrush = ParseBrush(value);
                    if (lineBrush != null)
                        target.LineColor = lineBrush;
                    break;
                case "fontsize":
                    double size;
                    if (TryNumber(value, out size))
                        target.FontSize = size;
                    break;
                case "fontname":
                    target.FontName = Unquote(value);
                    break;
                case "fontstyle":
                    target.FontStyle = ParseFontStyle(value);
                    break;
                case "linethickness":
                    double thickness;
                    if (TryNumber(value, out thickness))
                        target.LineThickness = thickness;
                    break;
                case "linestyle":
                    target.Dash = ParseDash(value);
                    break;
                case "roundcorner":
                case "corner":
                    double corner;
                    if (TryNumber(value, out corner))
                        target.RoundCorner = corner;
                    break;
            }
        }

        private static string Unquote(string value)
        {
            string s = value.Trim();
            if (s.Length >= 2 && (s[0] == '"' || s[0] == '\'') && s[s.Length - 1] == s[0])
                s = s.Substring(1, s.Length - 2);
            return s;
        }

        private static bool TryNumber(string value, out double number)
        {
            return double.TryParse(Unquote(value), NumberStyles.Float,
                CultureInfo.InvariantCulture, out number);
        }

        private static Brush ParseBrush(string value)
        {
            string s = Unquote(value);
            if (s.Length == 0 || s.StartsWith("var(", StringComparison.OrdinalIgnoreCase))
                return null;
            if (s.Equals("transparent", StringComparison.OrdinalIgnoreCase)
                || s.Equals("none", StringComparison.OrdinalIgnoreCase))
                return Brushes.Transparent;

            Brush brush;
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(s);
                brush = new SolidColorBrush(color);
                brush.Freeze();
            }
            catch (Exception)
            {
                return null;
            }
            return brush;
        }

        private static TreeFontStyle? ParseFontStyle(string value)
        {
            string s = Unquote(value).ToLowerInvariant();
            if (s.Length == 0)
                return null;
            var style = new TreeFontStyle();
            foreach (string raw in s.Split(new[] { '+', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                switch (raw)
                {
                    case "bold":
                        style.Bold = true;
                        break;
                    case "italic":
                    case "italics":
                        style.Italic = true;
                        break;
                    case "underline":
                    case "underlined":
                        style.Underline = true;
                        break;
                    case "plain":
                    case "normal":
                        style = new TreeFontStyle();
                        break;
                }
            }
            return style;
        }

        private static double[] ParseDash(string value)
        {
            string s = Unquote(value).ToLowerInvariant();
            if (s == "solid" || s == "none" || s == "1")
                return new double[0];
            if (s == "dashed" || s == "dash")
                return new[] { 4.0, 2.0 };
            if (s == "dotted" || s == "dot")
                return new[] { 1.0, 2.0 };

            int dash = s.IndexOf('-');
            if (dash <= 0)
                return null;
            double on;
            double off;
            if (!double.TryParse(s.Substring(0, dash).Trim(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out on))
                return null;
            if (!double.TryParse(s.Substring(dash + 1).Trim(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out off))
                return null;
            return new[] { on, off };
        }
    }
}
