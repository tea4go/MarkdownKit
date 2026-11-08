namespace MarkdownKit.PlantUml
{
    /// <summary>结构化文本（JSON / YAML / INI / TOML / XML）能否画成树形图的外部判断入口。</summary>
    public static class MarkdownStructured
    {
        /// <summary>
        /// 顶层标量、空对象这类内容画不出树。预览前先问一句，能画才包成 plantuml 代码块。
        /// format 用文件扩展名（json / yaml / ini / toml / xml …），认不出返回 false。
        /// </summary>
        public static bool CanDrawTree(string text, string format)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            TreeKind kind;
            if (!TryParseKind(format, out kind))
                return false;

            TreeDiagram tree;
            try
            {
                tree = TreeParser.Parse(text, kind);
            }
            catch (System.Exception)
            {
                return false;
            }
            return tree.Roots.Count > 0 && tree.Roots[0].Rows.Count > 0;
        }

        private static bool TryParseKind(string format, out TreeKind kind)
        {
            switch ((format ?? "").Trim().TrimStart('.').ToLowerInvariant())
            {
                case "json":
                    kind = TreeKind.Json;
                    return true;
                case "yaml":
                case "yml":
                    kind = TreeKind.Yaml;
                    return true;
                case "ini":
                case "conf":
                    kind = TreeKind.Ini;
                    return true;
                case "toml":
                    kind = TreeKind.Toml;
                    return true;
                case "xml":
                    kind = TreeKind.Xml;
                    return true;
                default:
                    kind = TreeKind.Json;
                    return false;
            }
        }
    }
}
