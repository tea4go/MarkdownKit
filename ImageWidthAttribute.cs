using System;
using System.Collections.Generic;
using System.Globalization;
using Markdig.Helpers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace MarkdownKit
{
    /// <summary>
    /// 图片宽度标记。两种写法：
    /// 1. 旧的裸标记 `![说明](图.jpg){width=50%}`，按预览区宽度的百分比封顶；
    /// 2. HTML 注释 `![说明](图.jpg)&lt;!--{width=50% phone_width=80% pad_width=30% pc_width=10%}--&gt;`，
    ///    一次给出多档宽度，渲染时按预览区物理宽度选档。注释在别的 Markdown 渲染器里本来就不显示，
    ///    所以不认这套语法的宿主也不会把花括号当正文露出来。
    ///
    /// 解析放在 Markdig 出完语法树之后自己走一遍，没有往管线里塞内联解析器：
    /// 这个标记只在「图片后面紧跟着」这一种位置有意义，走一遍只认这一个位置，
    /// 就不会去碰公式里的花括号、代码里的 `{width=...}` 这类正常正文。
    /// </summary>
    internal static class ImageWidthAttribute
    {
        /// <summary>挂在图片节点上的键，值是 <see cref="ImageWidthSpec"/>。挂在树上而不是另开一张表，图片删了它自然也没了。</summary>
        private static readonly object Key = new object();

        /// <summary>一张图的多档宽度比例；null 表示这一档没写。比例都换算成 0~1。</summary>
        internal sealed class ImageWidthSpec
        {
            /// <summary>旧的 `width`：没有对应设备档位时用它兜底。</summary>
            public double? Width;

            /// <summary>手机档（预览区内容宽度 &lt; 7 英寸）。</summary>
            public double? Phone;

            /// <summary>平板档（7~14 英寸）。</summary>
            public double? Pad;

            /// <summary>2in1 电脑档（&gt;= 14 英寸）。</summary>
            public double? Pc;
        }

        /// <summary>把全文的宽度标记摘下来，挂到对应的图片节点上，并从正文里删掉这段文字。</summary>
        public static void Apply(MarkdownDocument document)
        {
            if (document == null)
                return;
            foreach (Block block in document)
                ApplyBlock(block);
        }

        /// <summary>取这张图指定的各档宽度；没写标记返回 false。</summary>
        public static bool TryGetWidthSpec(LinkInline image, out ImageWidthSpec spec)
        {
            spec = null;
            if (image == null || !image.ContainsData(Key))
                return false;
            spec = (ImageWidthSpec)image.GetData(Key);
            return spec != null;
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

            // 新写法：<!--{width=50% phone_width=80% pad_width=30% pc_width=10%}-->
            // Markdig 把整段注释收成一个 HtmlInline，内容在 Tag 里
            var html = next as HtmlInline;
            if (html != null)
            {
                ImageWidthSpec commentSpec;
                if (!TryReadComment(html.Tag, out commentSpec))
                    return;
                html.Remove();
                if (softBreak != null)
                    softBreak.Remove();
                image.SetData(Key, commentSpec);
                return;
            }

            // 旧写法：{width=50%}
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
            var spec = new ImageWidthSpec { Width = ratio };
            image.SetData(Key, spec);
        }

        /// <summary>
        /// 从整段 HTML 注释里认 `&lt;!--{width=50% phone_width=80% pad_width=30% pc_width=10%}--&gt;`。
        /// 只认「花括号包住、至少有一个已知键」的注释；普通 HTML 注释原样留着。
        /// </summary>
        private static bool TryReadComment(string tag, out ImageWidthSpec spec)
        {
            spec = null;
            if (tag == null)
                return false;

            string text = tag.Trim();
            const string open = "<!--";
            const string close = "-->";
            if (!text.StartsWith(open, StringComparison.Ordinal) || !text.EndsWith(close, StringComparison.Ordinal))
                return false;

            string inner = text.Substring(open.Length, text.Length - open.Length - close.Length).Trim();
            if (inner.Length < 2 || inner[0] != '{' || inner[inner.Length - 1] != '}')
                return false;
            inner = inner.Substring(1, inner.Length - 2);

            var result = new ImageWidthSpec();
            bool any = false;
            int i = 0;
            while (i < inner.Length)
            {
                while (i < inner.Length && char.IsWhiteSpace(inner[i]))
                    i++;
                int start = i;
                while (i < inner.Length && !char.IsWhiteSpace(inner[i]))
                    i++;
                if (start == i)
                    break;

                string name;
                double ratio;
                if (!TryReadKeyValue(inner.Substring(start, i - start), out name, out ratio))
                    continue;

                switch (name)
                {
                    case "width":
                        result.Width = ratio;
                        break;
                    case "phone_width":
                        result.Phone = ratio;
                        break;
                    case "pad_width":
                        result.Pad = ratio;
                        break;
                    case "pc_width":
                        result.Pc = ratio;
                        break;
                    default:
                        continue;
                }
                any = true;
            }

            if (!any)
                return false;
            spec = result;
            return true;
        }

        /// <summary>认一个 `键=百分比` 的片段；键名统一小写、中划线换成下划线。</summary>
        private static bool TryReadKeyValue(string token, out string name, out double ratio)
        {
            name = null;
            ratio = 1;
            int eq = token.IndexOf('=');
            if (eq <= 0 || eq >= token.Length - 1)
                return false;

            string key = token.Substring(0, eq).Trim();
            string value = token.Substring(eq + 1).Trim();
            if (key.Length == 0 || value.Length < 2 || value[value.Length - 1] != '%')
                return false;

            double number;
            if (!double.TryParse(value.Substring(0, value.Length - 1),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                return false;

            number = number / 100.0;
            if (number <= 0)
                return false;

            ratio = Math.Min(1.0, number);
            name = key.Replace('-', '_').ToLowerInvariant();
            return true;
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
