using System;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using WpfMath.Rendering;
using XamlMath;
using XamlMath.Boxes;

namespace MarkdownKit.Formula
{
    /// <summary>
    /// 一段 LaTeX 公式的绘制控件，行内和块级共用。
    ///
    /// 单位：WpfMath 的 Box.Height / Depth / Width 是「em」而不是 DIP，要乘字号才是像素
    /// （库自己的 FormulaControl 是把 Box 交给带 scale 的渲染器去画，所以量出来才是 DIP）。
    /// RenderTo 的 x / y 也是 em，而且它自己会把基线放在「y 加 Box.Height」处，传 0 即可。
    ///
    /// 基线是这里最绕的一点。WpfMath 把数学基线画在 Box.Height 处，而 InlineUIContainer
    /// 只能给 BaselineAlignment 枚举、给不了任意像素偏移，所以把基线做进控件自己的坐标里：
    ///
    /// - 行内：控件高度只取「基线以上」那一段，配 BaselineAlignment.Baseline 让控件底边落到
    ///   正文基线上，公式的基线（分数线、等号所在的那条线）就跟相邻文字严格对齐。
    ///   降部（分数线下面的分母、上下标的下缘）画到控件外面去 —— WPF 不裁剪，画出去照样可见。
    /// - 块级：独占一段，不存在基线对齐问题，控件高度取完整高度，公式整块落在控件里。
    ///
    /// 注意：行盒不会自己给降部让位。实测 BaselineAlignment.Bottom 只把元素底边贴到行盒下沿，
    /// 并不会把行盒撑大，所以光靠对齐属性救不了降部。调用方得按 InkHeight 把所在段落的
    /// LineHeight 撑开，见 MarkdownRenderer 里的 FormulaLineHeight。
    /// </summary>
    internal sealed class FormulaElement : FrameworkElement
    {
        /// <summary>
        /// WpfMath 只公开了「画」的接口：TexFormula.CreateBox 是 internal，造不出 Box，
        /// 而 Box.Height / Depth / Width 本身是 public。反射取一次方法名缓存下来，
        /// 取不到就退回按墨迹估尺寸（公式会整体偏高，但不崩、不空白）。
        /// </summary>
        private static readonly MethodInfo CreateBoxMethod = typeof(TexFormula).GetMethod(
            "CreateBox", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly TexFormula _formula;
        private readonly TexStyle _style;
        private readonly double _scale;
        private readonly double _width;
        private readonly double _height;

        /// <summary>行盒还需要补的降部（DIP）；块级恒为 0。</summary>
        public double Descent { get; private set; }

        /// <summary>公式的完整墨迹高度（基线以上加降部，DIP）。段落据此把行高撑开。</summary>
        public double InkHeight
        {
            get { return _height + Descent; }
        }

        public FormulaElement(TexFormula formula, double fontSize, TexStyle style, bool inline)
        {
            _formula = formula;
            _style = style;
            _scale = fontSize;

            double width;
            double height;
            double depth;
            if (!TryMeasureBox(formula, fontSize, out width, out height, out depth))
                MeasureInk(formula, fontSize, out width, out height, out depth);

            _width = width;
            if (inline)
            {
                // 只占基线以上：控件底边要落在正文基线上
                _height = height;
                Descent = depth;
            }
            else
            {
                // 独占一段，不存在基线对齐问题，取完整高度把公式整块装进控件
                _height = height + depth;
                Descent = 0;
                HorizontalAlignment = HorizontalAlignment.Center;
            }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            return new Size(_width, _height);
        }

        protected override void OnRender(DrawingContext dc)
        {
            // RenderTo 的 x/y 是 box 的 em 坐标（渲染器内部再乘 scale），而且它自己会把基线
            // 放在「y 加 Box.Height」处 —— 传 0 正好让基线落在控件内 Box.Height 的位置：
            // 行内控件高度就等于 Box.Height，基线于是落在控件底边；块级高度是完整高度，
            // 基线落在里面，公式整块装得下。别在这里再补偏移，补了就翻倍。
            _formula.RenderTo(dc, Environment(_scale, _style), _scale, 0, 0);
        }

        /// <summary>量公式的盒模型。CreateBox 是 internal，只能反射拿；拿不到返回 false，走墨迹估算。</summary>
        private bool TryMeasureBox(TexFormula formula, double scale,
            out double width, out double height, out double depth)
        {
            width = 0;
            height = 0;
            depth = 0;
            if (CreateBoxMethod == null)
                return false;
            try
            {
                var box = CreateBoxMethod.Invoke(formula, new object[] { Environment(scale, _style) }) as Box;
                if (box == null)
                    return false;
                // Box 的量纲是 em，乘字号换成 DIP
                width = box.Width * scale;
                height = box.Height * scale;
                depth = box.Depth * scale;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>拿不到盒模型时的退路：按墨迹包围盒估（墨迹本来就是 DIP），整块都当在基线上方。</summary>
        private void MeasureInk(TexFormula formula, double scale,
            out double width, out double height, out double depth)
        {
            Rect ink = formula.RenderToGeometry(Environment(scale, _style), scale).Bounds;
            width = ink.Width;
            height = ink.Height;
            depth = 0;
        }

        private static TexEnvironment Environment(double scale, TexStyle style)
        {
            return WpfTeXEnvironment.Create(style, scale, FormulaRenderer.SystemTextFont);
        }

        /// <summary>正文在该字号下的下沉量（基线到底边的距离）。段落撑行高时要用。</summary>
        public static double TextDescent(double fontSize)
        {
            try
            {
                var typeface = new Typeface(MarkdownTheme.PreviewFamily, FontStyles.Normal,
                    FontWeights.Normal, FontStretches.Normal);
                var probe = new FormattedText("Hg", CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight, typeface, fontSize, Brushes.Black, 1.0);
                return System.Math.Max(0, probe.Height - probe.Baseline);
            }
            catch (Exception)
            {
                // 量不出来就按常见的四分之一字号估
                return fontSize * 0.25;
            }
        }
    }
}
