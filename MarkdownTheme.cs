using System.Windows;
using System.Windows.Media;

namespace MarkdownKit
{
    /// <summary>
    /// 渲染层读取的主题快照。宿主在主题/字号变化时调用 <see cref="Configure"/> 刷新；
    /// 未配置时使用内置的浅色默认值，保证直接调用 <see cref="MarkdownRenderer"/> 也能跑。
    /// </summary>
    public static class MarkdownTheme
    {
        public static MarkdownPalette Markdown { get; private set; } = MarkdownPalette.Light();
        public static FontFamily PreviewFamily { get; private set; } = SystemFonts.MessageFontFamily;
        public static FontFamily EditorFamily { get; private set; } = new FontFamily("Consolas");
        public static FontFamily DiagramFamily { get; private set; } = new FontFamily("Microsoft YaHei");
        public static double DiagramFontSize { get; private set; } = 16;
        public static double NoteFold { get; private set; } = 14;
        public static bool IsDark { get; private set; }
        public static string DefaultFont { get; private set; } = "Microsoft YaHei";
        public static Brush Background { get; private set; } = Brushes.White;
        public static Brush Foreground { get; private set; } = Brushes.Black;
        public static Brush Muted { get; private set; } = Brushes.Gray;
        public static Brush Border { get; private set; } = Brushes.LightGray;
        public static Brush Copied { get; private set; } = Brushes.SeaGreen;

        /// <summary>用宿主主题覆盖默认值。未提供的字段保持原值。</summary>
        public static void Configure(MarkdownStyle style)
        {
            if (style == null)
                return;
            if (style.Markdown != null) Markdown = style.Markdown;
            if (style.PreviewFamily != null) PreviewFamily = style.PreviewFamily;
            if (style.EditorFamily != null) EditorFamily = style.EditorFamily;
            if (style.DiagramFamily != null) DiagramFamily = style.DiagramFamily;
            if (style.DiagramFontSize > 0) DiagramFontSize = style.DiagramFontSize;
            if (style.NoteFold > 0) NoteFold = style.NoteFold;
            IsDark = style.IsDark;
            if (!string.IsNullOrWhiteSpace(style.DefaultFont)) DefaultFont = style.DefaultFont;
            if (style.Background != null) Background = style.Background;
            if (style.Foreground != null) Foreground = style.Foreground;
            if (style.Muted != null) Muted = style.Muted;
            if (style.Border != null) Border = style.Border;
            if (style.Copied != null) Copied = style.Copied;
        }

        /// <summary>
        /// 取一套"真能画出粗体"的字型，给图标题用。选中的字体不一定带粗体字形——
        /// 比如 Microsoft YaHei Mono 只有一个字体文件，粗体是渲染时现描出来的，
        /// 中文几乎看不出加粗（实测墨量只多 29%，而 YaHei UI 的真粗体多 81%）。
        /// 判断办法：看正常体和粗体解析到的是不是同一个字体文件，是就说明没有真粗体，
        /// 退回默认字体，宁可换个字体也要让标题真的粗起来。
        /// </summary>
        public static Typeface BoldTypeface(FontFamily family)
        {
            var bold = new Typeface(family, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            if (HasRealBold(family, bold))
                return bold;

            var fallback = new FontFamily(DefaultFont);
            var fallbackBold = new Typeface(fallback, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            return HasRealBold(fallback, fallbackBold) ? fallbackBold : bold;
        }

        /// <summary>正常体和粗体落在不同字体文件上，才算这个字体真有粗体字形。</summary>
        private static bool HasRealBold(FontFamily family, Typeface bold)
        {
            GlyphTypeface normalFace;
            GlyphTypeface boldFace;
            var normal = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            if (!normal.TryGetGlyphTypeface(out normalFace) || !bold.TryGetGlyphTypeface(out boldFace))
                return false;
            return normalFace.FontUri != boldFace.FontUri;
        }
    }
}
