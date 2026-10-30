using System;
using System.Windows.Documents;
using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace MarkdownKit
{
    /// <summary>
    /// 预览里点了站内链接要打开什么。渲染期由 MarkdownRenderer 交给主窗口去解析，
    /// 渲染器自己不碰文件系统——当前文档目录、已打开的文件夹只有窗口那边知道。
    /// </summary>
    public sealed class PreviewLinkTarget
    {
        /// <summary>true 是 [[name]] 这种 wiki 链接，false 是 [文字](相对路径) 那种标准 md 链接。</summary>
        public bool IsWiki;

        /// <summary>目标：wiki 链接是名字，标准链接是原始相对路径（不含 # 锚点）。</summary>
        public string Path;

        /// <summary># 后面那段锚点标题，没有则为 null。</summary>
        public string Anchor;

        /// <summary>源码原样文本，找不到目标时拿去提示。</summary>
        public string Raw;

        /// <summary>
        /// 脚注跳转的目标：正文里的引用，或文末那条脚注。带着已经排好的文档元素，
        /// 主窗口拿到直接滚过去就行——脚注不跨文件，不用走路径解析那一套。
        /// 非脚注的链接这里是 null。
        /// </summary>
        public TextElement Target;

        /// <summary>[[目标|别名]] 里的目标；Anchor 是 # 后那段。</summary>
        public static PreviewLinkTarget Wiki(string name, string anchor, string raw)
        {
            return new PreviewLinkTarget { IsWiki = true, Path = name, Anchor = anchor, Raw = raw };
        }

        /// <summary>脚注的双向跳转：target 是渲染出来的那条脚注，或正文里那个引用。</summary>
        public static PreviewLinkTarget Footnote(TextElement target)
        {
            return new PreviewLinkTarget { Target = target };
        }

        /// <summary>标准链接的地址：拆出 # 锚点，URL 编码的字符（%20 之类）解回来。</summary>
        public static PreviewLinkTarget Relative(string url)
        {
            string raw = url;
            string anchor = null;
            int hash = url.IndexOf('#');
            if (hash >= 0)
            {
                anchor = Decode(url.Substring(hash + 1));
                url = url.Substring(0, hash);
            }
            return new PreviewLinkTarget { IsWiki = false, Path = Decode(url), Anchor = anchor, Raw = raw };
        }

        /// <summary>解码；% 后面不是合法的转义序列就按原样返回，别为这个抛异常。</summary>
        private static string Decode(string value)
        {
            try
            {
                return Uri.UnescapeDataString(value).Trim();
            }
            catch (UriFormatException)
            {
                return value.Trim();
            }
        }
    }

    /// <summary>[[...]] 解析出来的一段：目标、锚点、显示文本。</summary>
    public sealed class WikiLinkInline : LeafInline
    {
        /// <summary>目标名字，可为空（[[#标题]] 只在本文件里跳）。</summary>
        public string Target;

        /// <summary># 后面那段锚点标题，可为空。</summary>
        public string Anchor;

        /// <summary>显示文本：有 | 别名就用别名，否则用目标本身。</summary>
        public string Label;

        /// <summary>源码原样文本，含 [[ ]]。</summary>
        public string Raw;
    }

    /// <summary>
    /// 认 [[目标]]、[[目标|显示文本]]、[[目标#标题]] 三种写法，跟 Obsidian 的 wiki 链接同一套语法。
    /// 认不出来（跨行、空目标、里面又有 [ ）就把游标留在原地返回 false，
    /// 交回给 Markdig 自带的标准链接解析器，普通 [文字](地址) 不受影响。
    /// </summary>
    public sealed class WikiLinkParser : InlineParser
    {
        public WikiLinkParser()
        {
            // 只在 '[' 处进来试一把，别的字符不进 Match
            OpeningCharacters = new[] { '[' };
        }

        public override bool Match(InlineProcessor processor, ref StringSlice slice)
        {
            int start = slice.Start;
            string text = slice.Text;
            if (slice.PeekChar(1) != '[')
                return false;

            int close;
            if (!TryFindClose(text, start, slice.End, out close))
                return false;

            string inner = text.Substring(start + 2, close - start - 2);
            slice.Start = close + 2;   // 游标推到 ]] 之后

            string target = inner;
            string anchor = null;
            string label = null;

            // 先切别名：| 右边全是显示文本
            int bar = target.IndexOf('|');
            if (bar >= 0)
            {
                label = target.Substring(bar + 1).Trim();
                target = target.Substring(0, bar);
            }

            // 再切锚点：# 右边是标题
            int hash = target.IndexOf('#');
            if (hash >= 0)
            {
                anchor = target.Substring(hash + 1).Trim();
                target = target.Substring(0, hash);
            }
            target = target.Trim();

            if (anchor != null && anchor.Length == 0)
                anchor = null;
            if (string.IsNullOrEmpty(label))
                label = anchor == null ? target : target + "#" + anchor;

            var wiki = new WikiLinkInline
            {
                Target = target,
                Anchor = anchor,
                Label = label,
                Raw = text.Substring(start, close + 2 - start),
                Span = new SourceSpan(start, close + 1)
            };
            processor.Inline = wiki;
            return true;
        }

        /// <summary>
        /// 找收尾的 ]]：close 是第一个 ']' 的下标。
        /// 中间不许跨行、不许再出现 '['，单个 ']' 算内容（[[a]b]] 这种照样认）。
        /// </summary>
        private static bool TryFindClose(string text, int start, int end, out int close)
        {
            close = -1;
            for (int i = start + 2; i < end; i++)
            {
                char c = text[i];
                if (c == '\r' || c == '\n' || c == '[')
                    return false;
                if (c != ']')
                    continue;
                if (text[i + 1] == ']')
                {
                    close = i;
                    return close > start + 2;   // [[]] 空目标不认
                }
            }
            return false;
        }
    }

    /// <summary>把 wiki 链接解析器挂进管线，用法与 Markdig 自带的 UseXxx 一致。</summary>
    public sealed class WikiLinkExtension : IMarkdownExtension
    {
        public void Setup(MarkdownPipelineBuilder pipeline)
        {
            if (!pipeline.InlineParsers.Contains<WikiLinkParser>())
                pipeline.InlineParsers.Insert(0, new WikiLinkParser());
        }

        public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
        {
            // 排版由 MarkdownRenderer 自己做，这里没有要挂的 renderer
        }
    }
}
