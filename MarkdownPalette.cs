using System.Windows.Media;

namespace MarkdownKit
{
    /// <summary>Markdown 渲染用到的整套配色。宿主可按自己的主题构造，或直接用内置的明暗两套。</summary>
    public class MarkdownPalette
    {
        public Brush Text { get; set; }
        public Brush Heading { get; set; }
        public Brush HeadingRule1 { get; set; }
        public Brush HeadingRule2 { get; set; }
        public Brush Link { get; set; }
        public Brush MarkBackground { get; set; }
        public Brush QuoteText { get; set; }
        public Brush QuoteBorder { get; set; }
        public Brush QuoteBackground { get; set; }
        public Brush CodeBackground { get; set; }
        public Brush CodeBorder { get; set; }
        public Brush CodeKeyword { get; set; }
        public Brush CodeString { get; set; }
        public Brush CodeComment { get; set; }
        public Brush CodeNumber { get; set; }
        public Brush CodeFunction { get; set; }
        public Brush CodeType { get; set; }
        public Brush CodeTag { get; set; }
        public Brush CodeAttribute { get; set; }
        public Brush CodeProperty { get; set; }
        public Brush CodeSection { get; set; }
        public Brush CodeVariable { get; set; }
        public Brush InlineCodeBackground { get; set; }
        public Brush InlineCodeText { get; set; }
        public Brush SegmentFill { get; set; }
        public Brush TableBackground { get; set; }
        public Brush TableBorder { get; set; }
        public Brush TableHeaderBackground { get; set; }
        public Brush TableEvenRowBackground { get; set; }

        /// <summary>浅色主题。色值沿用 MdView 默认主题。</summary>
        public static MarkdownPalette Light()
        {
            return new MarkdownPalette
            {
                Text = Brush("#2C3E50"),
                Heading = Brush("#0077BB"),
                HeadingRule1 = Brush("#0077BB"),
                HeadingRule2 = Brush("#D2D5DA"),
                Link = Brush("#0000FF"),
                MarkBackground = Brush("#FFF1A6"),
                QuoteText = Brush("#777777"),
                QuoteBorder = Brush("#D6DBDF"),
                QuoteBackground = Brush("#14668099"),
                CodeBackground = Brush("#EEEEEE"),
                CodeBorder = Brush("#7EA9C9"),
                CodeKeyword = Brush("#D73A49"),
                CodeString = Brush("#032F62"),
                CodeComment = Brush("#6A737D"),
                CodeNumber = Brush("#005CC5"),
                CodeFunction = Brush("#6F42C1"),
                CodeType = Brush("#E36209"),
                CodeTag = Brush("#22863A"),
                CodeAttribute = Brush("#0451A5"),
                CodeProperty = Brush("#005CC5"),
                CodeSection = Brush("#9A6700"),
                CodeVariable = Brush("#1B7C83"),
                InlineCodeBackground = Brush("#FAE7A0"),
                InlineCodeText = Brush("#263238"),
                SegmentFill = Brush("#F1F8FF"),
                TableBackground = Brush("#FFFFFF"),
                TableBorder = Brush("#C8E1FF"),
                TableHeaderBackground = Brush("#F1F8FF"),
                TableEvenRowBackground = Brush("#17668099")
            };
        }

        /// <summary>深色主题。色值沿用 MdView 默认主题。</summary>
        public static MarkdownPalette Dark()
        {
            return new MarkdownPalette
            {
                Text = Brush("#D6DEE8"),
                Heading = Brush("#EEEEEE"),
                HeadingRule1 = Brush("#1A7778"),
                HeadingRule2 = Brush("#2E4B4B"),
                Link = Brush("#2EA0FF"),
                MarkBackground = Brush("#5C4B00"),
                QuoteText = Brush("#999999"),
                QuoteBorder = Brush("#4A5058"),
                QuoteBackground = Brush("#14FFFFFF"),
                CodeBackground = Brush("#00202B"),
                CodeBorder = Brush("#1A7778"),
                CodeKeyword = Brush("#FF7B72"),
                CodeString = Brush("#A5D6FF"),
                CodeComment = Brush("#8B949E"),
                CodeNumber = Brush("#79C0FF"),
                CodeFunction = Brush("#D2A8FF"),
                CodeType = Brush("#FFA657"),
                CodeTag = Brush("#7EE787"),
                CodeAttribute = Brush("#9CDCFE"),
                CodeProperty = Brush("#79C0FF"),
                CodeSection = Brush("#E3B341"),
                CodeVariable = Brush("#56D4DD"),
                InlineCodeBackground = Brush("#3F444C"),
                InlineCodeText = Brush("#839496"),
                SegmentFill = Brush("#33383F"),
                TableBackground = Brush("#002C37"),
                TableBorder = Brush("#1A7778"),
                TableHeaderBackground = Brush("#004F5E"),
                TableEvenRowBackground = Brush("#00404D")
            };
        }

        private static Brush Brush(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
    }
}
