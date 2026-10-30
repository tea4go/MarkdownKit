using System.Windows.Media;

namespace MarkdownKit
{
    /// <summary>
    /// 渲染层需要的主题输入：配色、字体、图内尺寸。宿主构造好交给 <see cref="MarkdownTheme.Configure"/>。
    /// </summary>
    public sealed class MarkdownStyle
    {
        /// <summary>正文与各类元素的配色。</summary>
        public MarkdownPalette Markdown { get; set; }

        /// <summary>正文与标题字体。</summary>
        public FontFamily PreviewFamily { get; set; }

        /// <summary>代码块与行内代码字体。</summary>
        public FontFamily EditorFamily { get; set; }

        /// <summary>PlantUML / Mermaid 图内字体。</summary>
        public FontFamily DiagramFamily { get; set; }

        public double DiagramFontSize { get; set; } = 16;

        /// <summary>PlantUML 注释/便签的折角尺寸。</summary>
        public double NoteFold { get; set; } = 14;

        public bool IsDark { get; set; }

        /// <summary>没有真粗体字形时回退用的字体名。</summary>
        public string DefaultFont { get; set; } = "Microsoft YaHei";

        public Brush Background { get; set; }
        public Brush Foreground { get; set; }
        public Brush Muted { get; set; }
        public Brush Border { get; set; }
        public Brush Copied { get; set; }
    }
}
