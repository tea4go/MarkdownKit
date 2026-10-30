namespace MarkdownKit
{
    /// <summary>
    /// 文档库的附件目录约定：图片统一收在文档目录下的 files 里，正文里只写文件名或 files/文件名。
    /// 预览解析图片路径（<see cref="MarkdownRenderer"/>）与编辑区「粘贴图片」（MainWindow）认的必须是
    /// 同一个名字，所以在放这儿，不各写一份字面量——哪天改了名，两边一起改。
    /// </summary>
    public static class MarkdownAssets
    {
        /// <summary>放图片的固定目录名。</summary>
        public const string ImageDir = "files";
    }
}
