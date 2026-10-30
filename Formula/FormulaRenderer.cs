using System;
using System.Collections.Generic;
using System.Windows;
using WpfMath.Parsers;
using XamlMath;

namespace MarkdownKit.Formula
{
    /// <summary>
    /// 预览区的 LaTeX 公式入口。解析交给 WpfMath，排版和基线由 FormulaElement 自己算。
    /// 认不出的公式返回 null，调用方原样显示 $...$ 源码 —— 一个坏公式不该拖垮整篇预览。
    /// </summary>
    internal static class FormulaRenderer
    {
        /// <summary>
        /// text 命令里的文字走系统字体，得挑一个带中文字形的：库默认的 Arial 会把中文画成方块。
        /// 数学符号本身用库自带的字体，不受这个影响。
        /// </summary>
        public static string SystemTextFont { get { return MarkdownTheme.DefaultFont; } }

        /// <summary>
        /// 解析结果缓存，键是「样式 + 归一化后的源码」。TexFormula 与字号、颜色无关，
        /// 同一公式不必反复解析；样式不同的同一个公式要各存一份（分数大小由样式决定）。
        /// </summary>
        private static readonly Dictionary<string, TexFormula> Cache = new Dictionary<string, TexFormula>();

        /// <summary>行内公式：夹在文字中间的 $...$。默认按行内样式（分数是小的），写了 \dfrac 才放大。</summary>
        public static FrameworkElement TryCreateInline(string latex, double fontSize)
        {
            TexStyle style = WantsDisplayStyle(latex) ? TexStyle.Display : TexStyle.Text;
            return Create(latex, fontSize, style, true);
        }

        /// <summary>块级公式：独占一段的 $$...$$。一律按行间样式，分数是大的。</summary>
        public static FrameworkElement TryCreateBlock(string latex, double fontSize)
        {
            return Create(latex, fontSize, TexStyle.Display, false);
        }

        private static FrameworkElement Create(string latex, double fontSize, TexStyle style, bool inline)
        {
            TexFormula formula = Parse(latex, style);
            if (formula == null)
                return null;
            try
            {
                return new FormulaElement(formula, fontSize, style, inline);
            }
            catch (Exception)
            {
                // 量尺寸时字体解析不出来之类的，也当画不出来
                return null;
            }
        }

        /// <summary>解析 LaTeX；认不出返回 null。失败也缓存，同一处坏公式不必反复解析。</summary>
        private static TexFormula Parse(string latex, TexStyle style)
        {
            if (string.IsNullOrEmpty(latex))
                return null;

            string key = style + "\n" + Normalize(latex);
            TexFormula formula;
            if (Cache.TryGetValue(key, out formula))
                return formula;

            try
            {
                formula = WpfTeXFormulaParser.Instance.Parse(Normalize(latex));
            }
            catch (Exception)
            {
                // 语法不认识、字体名解析不了等等，一律当认不出
                formula = null;
            }
            Cache[key] = formula;
            return formula;
        }

        /// <summary>
        /// 把 WpfMath 不认、但常见于各种编辑器（KaTeX / MathJax 都认）的写法替换成语义相同的等价写法，
        /// 实测过的都记在注释里，别再猜：
        ///
        /// - \dfrac、\tfrac：amsmath 的两个分数别名，只差字号样式，换成 \frac 由 TexStyle 决定大小
        /// - \ ：LaTeX 的控制空格，等于一个普通空格（约 0.33em）。WpfMath 报 Unknown command，
        ///   换成 \text{ }（一个真实的文本空格，宽度一致）
        /// - \quad（1em）、\qquad（2em）：WpfMath 不认，用三个 / 六个 \text{ } 拼出来
        ///
        /// 注意 \quad 不是 \qquad 的子串（\qquad 是 \q + qquad），替换顺序无所谓。
        /// </summary>
        private static string Normalize(string latex)
        {
            if (latex.IndexOf('\\') < 0)
                return latex;
            return latex
                .Replace(@"\dfrac", @"\frac")
                .Replace(@"\tfrac", @"\frac")
                .Replace(@"\ ", @"\text{ }")
                .Replace(@"\quad", @"\text{ }\text{ }\text{ }")
                .Replace(@"\qquad", @"\text{ }\text{ }\text{ }\text{ }\text{ }\text{ }");
        }

        /// <summary>\dfrac 是「行间大小的分数」，出现在行内时要把整条公式提到 Display 样式。</summary>
        private static bool WantsDisplayStyle(string latex)
        {
            return latex.IndexOf(@"\dfrac", StringComparison.Ordinal) >= 0;
        }
    }
}
