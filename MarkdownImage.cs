using System;
using System.Windows;
using System.Windows.Controls;

namespace MarkdownKit
{
    /// <summary>
    /// 预览里的图片。超过可用宽度就等比缩小，放得下则按原始尺寸，不做放大。
    /// 不直接用 Image 的 Stretch=Uniform：父级给的约束宽度是整栏宽时，Uniform 会把小图放大到铺满整栏。
    /// 这里先量出图片的原始尺寸，再拿可用宽度封顶，得到「大图缩、小图不放大」。
    /// </summary>
    internal sealed class MarkdownImage : Image
    {
        /// <summary>两侧要扣掉的宽度：预览区左右内边距加竖直滚动条，留出富余免得撑出横向滚动。</summary>
        private const double SideMargin = 40;

        /// <summary>手机与平板的分界（预览区内容宽度的物理英寸数）。</summary>
        private const double PhoneMaxInches = 7;

        /// <summary>平板与 2in1 电脑的分界。</summary>
        private const double PadMaxInches = 14;

        /// <summary>预览区的可用宽度，渲染时写入并随窗口缩放由绑定刷新。</summary>
        public static readonly DependencyProperty AvailableWidthProperty =
            DependencyProperty.Register(nameof(AvailableWidth), typeof(double), typeof(MarkdownImage),
                new FrameworkPropertyMetadata(double.PositiveInfinity,
                    FrameworkPropertyMetadataOptions.AffectsMeasure));

        public double AvailableWidth
        {
            get { return (double)GetValue(AvailableWidthProperty); }
            set { SetValue(AvailableWidthProperty, value); }
        }

        /// <summary>
        /// 这张图最多占可用宽度的几成（`![说明](图.jpg){width=50%}` → 0.5），默认 1 表示占满。
        /// 只是上限：图本身就比它窄时保持原尺寸，不放大。
        /// </summary>
        public static readonly DependencyProperty WidthRatioProperty =
            DependencyProperty.Register(nameof(WidthRatio), typeof(double), typeof(MarkdownImage),
                new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public double WidthRatio
        {
            get { return (double)GetValue(WidthRatioProperty); }
            set { SetValue(WidthRatioProperty, value); }
        }

        /// <summary>手机档比例，NaN 表示没写这一档。</summary>
        public static readonly DependencyProperty PhoneWidthRatioProperty =
            DependencyProperty.Register(nameof(PhoneWidthRatio), typeof(double), typeof(MarkdownImage),
                new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsMeasure));

        /// <summary>平板档比例，NaN 表示没写这一档。</summary>
        public static readonly DependencyProperty PadWidthRatioProperty =
            DependencyProperty.Register(nameof(PadWidthRatio), typeof(double), typeof(MarkdownImage),
                new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsMeasure));

        /// <summary>2in1 电脑档比例，NaN 表示没写这一档。</summary>
        public static readonly DependencyProperty PcWidthRatioProperty =
            DependencyProperty.Register(nameof(PcWidthRatio), typeof(double), typeof(MarkdownImage),
                new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public double PhoneWidthRatio
        {
            get { return (double)GetValue(PhoneWidthRatioProperty); }
            set { SetValue(PhoneWidthRatioProperty, value); }
        }

        public double PadWidthRatio
        {
            get { return (double)GetValue(PadWidthRatioProperty); }
            set { SetValue(PadWidthRatioProperty, value); }
        }

        public double PcWidthRatio
        {
            get { return (double)GetValue(PcWidthRatioProperty); }
            set { SetValue(PcWidthRatioProperty, value); }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            // 以无限约束量一次，拿到图片的原始尺寸，不受父级给的宽度左右。
            // 必须取返回值：MeasureOverride 内部 DesiredSize 还是上一次测量留下的旧值。
            Size natural = base.MeasureOverride(new Size(double.PositiveInfinity, double.PositiveInfinity));
            if (natural.Width <= 0 || natural.Height <= 0)
                return base.MeasureOverride(availableSize);

            double limit = double.PositiveInfinity;
            if (!double.IsInfinity(availableSize.Width))
                limit = availableSize.Width;
            if (!double.IsInfinity(AvailableWidth))
                limit = Math.Min(limit, Math.Max(0, AvailableWidth - SideMargin));
            // 有设备档位就按内容宽度的物理英寸选档；没写的档回退到 width（默认占满）
            double ratio = DeviceWidthRatio(limit);
            if (double.IsNaN(ratio))
                ratio = WidthRatio;
            if (ratio < 1)
                limit *= Math.Max(0, ratio);

            // 放得下就原样，放不下才等比缩到上限
            if (double.IsInfinity(limit) || natural.Width <= limit)
                return natural;
            return new Size(limit, natural.Height * limit / natural.Width);
        }

        /// <summary>
        /// 按内容宽度的物理英寸数挑档：小于 7 英寸算手机，7~14 英寸算平板，14 英寸以上算 2in1 电脑。
        /// WPF 的 DIP 以 1/96 英寸定义，DPI 缩放已经包含在换算里，所以 DIP / 96 就是物理英寸。
        /// 返回 NaN 表示这一档没写，或量不出宽度，由调用方回退到 WidthRatio。
        /// </summary>
        private double DeviceWidthRatio(double contentWidth)
        {
            if (double.IsInfinity(contentWidth) || contentWidth <= 0)
                return double.NaN;
            double inches = contentWidth / 96.0;
            if (inches < PhoneMaxInches)
                return PhoneWidthRatio;
            if (inches < PadMaxInches)
                return PadWidthRatio;
            return PcWidthRatio;
        }
    }
}
