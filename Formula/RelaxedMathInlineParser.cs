using Markdig.Extensions.Mathematics;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Syntax;

namespace MarkdownKit.Formula
{
    /// <summary>
    /// 取代 Markdig 自带的 MathInlineParser。默认那个要求 $ 前后是空白，
    /// 「公式$$E=mc^2$$公式」这种紧贴中文（或字母数字）的写法会被整段当成普通文本，
    /// 而中文排版里公式前后不加空格是常态，所以这里放宽：只看内容边界干净
    /// （非空、不跨行、首尾不是空白），左右贴着什么字符都认。
    /// 块级 $$...$$ 仍由 Markdig 的 MathBlockParser 处理，不受影响。
    /// </summary>
    internal sealed class RelaxedMathInlineParser : InlineParser
    {
        public RelaxedMathInlineParser()
        {
            OpeningCharacters = new[] { '$' };
        }

        public override bool Match(InlineProcessor processor, ref StringSlice slice)
        {
            // 被反斜杠转义的 \$ 不认
            if (slice.PeekCharExtra(-1) == '\\')
                return false;

            int start = slice.Start;
            string text = slice.Text;
            int end = slice.End;

            // 数开头连续的 $，只支持行内 $ 和 $$，三个以上不认
            int delimiterCount = 0;
            int i = start;
            while (i <= end && text[i] == '$')
            {
                delimiterCount++;
                i++;
                if (delimiterCount > 2)
                    return false;
            }

            // 开定界符后紧跟的必须是内容，不能是空白或行尾（$ 公式 $ 这种不认，也避免误吞货币写法）
            if (i > end || IsSpace(text[i]))
                return false;

            int contentStart = i;
            int closing = -1;
            while (i <= end)
            {
                char c = text[i];
                if (c == '\r' || c == '\n')
                    return false;   // 行内公式不跨行

                if (c != '$')
                {
                    i++;
                    continue;
                }

                int run = 0;
                int j = i;
                while (j <= end && text[j] == '$')
                {
                    run++;
                    j++;
                }

                // 收尾的 $ 数量要和开头一致
                if (run == delimiterCount && i > contentStart && !IsSpace(text[i - 1]))
                {
                    closing = i;
                    break;
                }
                i = j;
            }

            if (closing < 0)
                return false;

            var math = new MathInline
            {
                Content = new StringSlice(text, contentStart, closing - 1),
                DelimiterCount = delimiterCount,
                Span = new SourceSpan(start, closing + delimiterCount - 1)
            };
            processor.Inline = math;
            slice.Start = closing + delimiterCount;
            return true;
        }

        private static bool IsSpace(char c)
        {
            return c == ' ' || c == '\t';
        }
    }
}
