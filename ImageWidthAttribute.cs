using System;
using System.Collections.Generic;
using System.Globalization;
using Markdig.Helpers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace MarkdownKit
{
    /// <summary>
    /// 图片宽度标记：`![说明](图.jpg){width=50%}`——按预览区宽度的百分比给这张图封顶，
    /// 高度按原图比例跟着缩；原图本身比这个上限还小就保持原尺寸，不放大。
    ///
    /// 解析放在 Markdig 出完语法树之后自己走一遍，没有往管线里塞内联解析器：
    /// 这个标记只在「图片后面紧跟着」这一种位置有意义，走一遍只认这一个位置，
    /// 就不会去碰公式里的花括号、代码里的 `{width=...}` 这类正常正文。
    /// </summary>
    internal static class ImageWidthAttribute
    {
        /// <summary>挂在图片节点上的键，值是比例（0.5 表示 50%）。挂在树上而不是另开一张表，图片删了它自然也没了。</summary>
        private static readonly object Key = new object();

        /// <summary>把全文的宽度标记摘下来，挂到对应的图片节点上，并从正文里删掉这段文字。</summary>
        public static void Apply(MarkdownDocument document)
        {
            if (document == null)
                return;
            foreach (Block block in document)
                ApplyBlock(block);
        }

        /// <summary>取这张图指定的宽度比例；没写标记返回 false，ratio 置 1（占满可用宽度）。</summary>
        public static bool TryGetWidthRatio(LinkInline image, out double ratio)
        {
            ratio = 1;
            if (image == null || !image.ContainsData(Key))
                return false;
            ratio = (double)image.GetData(Key);
            return true;
        }

        private static void ApplyBlock(Block block)
        {
            var container = block as ContainerBlock;
            if (container != null)
            {
                foreach (Block child in container)
                    ApplyBlock(child);
                return;
            }
            var leaf = block as LeafBlock;
            if (leaf != null && leaf.Inline != null)
                ApplyInlines(leaf.Inline);
        }

        private static void ApplyInlines(ContainerInline container)
        {
            // 先把图片都收齐再处理：摘标记会把字面量节点从链上删掉，而 Remove 会清空
            // 被删节点的 NextSibling，边走边删的话后面同级的图片就跟丢了
            var images = new List<LinkInline>();
            CollectImages(container, images);
            foreach (LinkInline image in images)
                ApplyWidth(image);
        }

        private static void CollectImages(ContainerInline container, List<LinkInline> images)
        {
            Inline inline = container.FirstChild;
            while (inline != null)
            {
                var nested = inline as ContainerInline;
                if (nested != null)
                    CollectImages(nested, images);
                var image = inline as LinkInline;
                if (image != null && image.IsImage)
                    images.Add(image);
                inline = inline.NextSibling;
            }
        }

        /// <summary>
        /// 图片后面那段文字要是宽度标记就摘下来。标记可以紧跟在同一行，也可以单独放下一行
        /// （Markdig 会把换行拆成 LineBreakInline，这里顺着它往后看一个节点）。
        /// </summary>
        private static void ApplyWidth(LinkInline image)
        {
            Inline next = image.NextSibling;
            LineBreakInline softBreak = null;
            var lineBreak = next as LineBreakInline;
            if (lineBreak != null)
            {
                // 硬换行后面是另一行的内容，不该被当成这张图的标记
                if (lineBreak.IsHard)
                    return;
                softBreak = lineBreak;
                next = lineBreak.NextSibling;
            }

            var literal = next as LiteralInline;
            if (literal == null)
                return;
            StringSlice content = literal.Content;
            if (content.Text == null)
                return;

            double ratio;
            int end;
            if (!TryRead(content.Text, content.Start, content.End, out ratio, out end))
                return;

            if (end >= content.End)
                literal.Remove();   // 标记后面没别的内容，整段字面量都吃掉
            else
                literal.Content = new StringSlice(content.Text, end + 1, content.End);
            // 标记独占一行时，把那个换行也撤掉，免得图片后面多出一行空白
            if (softBreak != null)
                softBreak.Remove();
            image.SetData(Key, ratio);
        }

        /// <summary>
        /// 从 start 开始认 `{width=50%}`（大小写不敏感，花括号内外允许空格，数字可以是小数）。
        /// 认出来时 end 指向收尾的 `}`；比例已经换算好并夹在 (0, 1] 之间。
        /// </summary>
        private static bool TryRead(string text, int start, int limit, out double ratio, out int end)
        {
            ratio = 1;
            end = start;
            if (text == null || start < 0 || start > limit)
                return false;

            int i = SkipSpace(text, start, limit);
            if (i > limit || text[i] != '{')
                return false;
            i = SkipSpace(text, i + 1, limit);

            if (!StartsWith(text, i, limit, "width"))
                return false;
            i = SkipSpace(text, i + 5, limit);
            if (i > limit || text[i] != '=')
                return false;
            i = SkipSpace(text, i + 1, limit);

            int numberStart = i;
            while (i <= limit && (char.IsDigit(text[i]) || text[i] == '.'))
                i++;
            if (i == numberStart)
                return false;

            double number;
            if (!double.TryParse(text.Substring(numberStart, i - numberStart),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                return false;

            i = SkipSpace(text, i, limit);
            if (i > limit || text[i] != '%')
                return false;
            i = SkipSpace(text, i + 1, limit);
            if (i > limit || text[i] != '}')
                return false;

            // 只认百分比，换算成 0~1 的比例；超过 100% 按 100% 算，0 或负数当没写
            number = number / 100.0;
            if (number <= 0)
                return false;
            ratio = Math.Min(1.0, number);
            end = i;
            return true;
        }

        /// <summary>跳过空格和制表符；换行不算——跨行就不再是紧跟在图片后面的标记了。</summary>
        private static int SkipSpace(string text, int index, int limit)
        {
            while (index <= limit && (text[index] == ' ' || text[index] == '\t'))
                index++;
            return index;
        }

        private static bool StartsWith(string text, int index, int limit, string word)
        {
            if (index < 0 || index + word.Length - 1 > limit)
                return false;
            return string.Compare(text, index, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) == 0;
        }
    }
}
