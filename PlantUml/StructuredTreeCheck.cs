namespace MarkdownKit.PlantUml
{
    /// <summary>结构化文本（JSON/YAML）能否画成树形图的外部判断入口。</summary>
    public static class MarkdownStructured
    {
        /// <summary>
        /// 顶层标量、空对象这类内容画不出树。预览前先问一句，能画才包成 plantuml 代码块。
        /// </summary>
        public static bool CanDrawTree(string text, bool json)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;
            TreeDiagram tree;
            try
            {
                tree = TreeParser.Parse(text, json ? TreeKind.Json : TreeKind.Yaml);
            }
            catch (System.Exception)
            {
                return false;
            }
            return tree.Roots.Count > 0 && tree.Roots[0].Rows.Count > 0;
        }
    }
}
