using System.Collections.Generic;
using System.Windows.Media;

namespace MarkdownKit
{
    /// <summary>
    /// 预览里「图片加载不出来」时的占位图标：一张折角的纸，纸面上画着一片绿色山丘，
    /// 跟资源管理器里图片文件的图标同一个观感——一眼就能看出「这里本该是一张图」，而不是一堆字。
    ///
    /// 用矢量画而不是带一张位图：跟着主题配色走，放大缩小都不糊，也不用往安装包里塞资源。
    /// 画好的图纸按主题缓存一份，整篇文档里几十张破图共用同一个冻结的 DrawingImage。
    /// </summary>
    internal static class BrokenImageIcon
    {
        /// <summary>纸张轮廓：左上角留圆角，右上角缺一块给折角。设计坐标按 24×24 摆。</summary>
        private const string PageData =
            "M5.6,1.6 H14.2 L20.4,7.8 V21.4" +
            " A1.6,1.6 0 0 1 18.8,23 H5.6 A1.6,1.6 0 0 1 4,21.4 V3.2 A1.6,1.6 0 0 1 5.6,1.6 Z";

        /// <summary>折角：纸角翻起来的那片三角，颜色比纸面深一档。</summary>
        private const string FoldData = "M14.2,1.6 V6.2 A1.6,1.6 0 0 0 15.8,7.8 H20.4 Z";

        /// <summary>山丘：从左边界起坡，两个峰，再落回右边界，下沿贴着纸的底边。</summary>
        private const string HillData =
            "M4,23 V18.4 L9.1,13.6 L13.2,17 L16.3,14.4 L20.4,18.8 V23 Z";

        /// <summary>山丘的绿。明暗两个主题都用这一支——纸上那点颜色本来就该是彩色的。</summary>
        private static readonly Color HillColor = Color.FromRgb(0x6F, 0xB1, 0x3C);

        /// <summary>纸张描边。比正文浅一档，但在白底上仍能看出一张纸的轮廓。</summary>
        private static readonly Color EdgeColor = Color.FromRgb(0x86, 0x8D, 0x97);

        /// <summary>已画好的图纸，键是主题（明/暗），一份主题一张。</summary>
        private static readonly Dictionary<bool, ImageSource> Cache = new Dictionary<bool, ImageSource>();

        /// <summary>
        /// 取当前主题下的破图图标。返回冻结的矢量图，可以随便塞进 Image 共用，
        /// 尺寸由调用方用 Width/Height 定，缩放不丢细节。
        /// </summary>
        public static ImageSource Get()
        {
            bool dark = MarkdownTheme.IsDark;
            lock (Cache)
            {
                ImageSource cached;
                if (Cache.TryGetValue(dark, out cached))
                    return cached;
                ImageSource icon = Build(dark);
                Cache[dark] = icon;
                return icon;
            }
        }

        /// <summary>画一遍图纸：纸面 → 山丘（裁在纸内）→ 折角 → 纸的描边。</summary>
        private static ImageSource Build(bool dark)
        {
            Geometry page = Geometry.Parse(PageData);
            Geometry fold = Geometry.Parse(FoldData);

            // 暗色主题下纸面压得没那么白，免得在深色预览上戳眼
            var paper = new SolidColorBrush(dark ? Color.FromRgb(0xE4, 0xE9, 0xEF)
                                                : Color.FromRgb(0xFB, 0xFC, 0xFD));
            var foldFill = new SolidColorBrush(dark ? Color.FromRgb(0xC3, 0xCA, 0xD3)
                                                   : Color.FromRgb(0xE4, 0xE8, 0xEE));
            var hill = new SolidColorBrush(HillColor);
            var edge = new SolidColorBrush(EdgeColor);
            paper.Freeze();
            foldFill.Freeze();
            hill.Freeze();
            edge.Freeze();

            // 小图上描边太细就化没了，1.5 个单位缩到 16px 时约 1px，正好立得住
            var pen = new Pen(edge, 1.5) { LineJoin = PenLineJoin.Round };
            pen.Freeze();
            var thinPen = new Pen(edge, 1.0) { LineJoin = PenLineJoin.Round };
            thinPen.Freeze();

            var group = new DrawingGroup();

            // 纸面先铺满，后面画的东西才有底
            group.Children.Add(new GeometryDrawing(paper, null, page));

            // 山丘会长出纸外（坡脚顶到边界），裁一刀让边缘干净
            var land = new DrawingGroup { ClipGeometry = page };
            land.Children.Add(new GeometryDrawing(hill, null, Geometry.Parse(HillData)));
            land.Freeze();
            group.Children.Add(land);

            // 折角连那道斜边一起描，不然小图上就看不出纸角是翻起来的
            group.Children.Add(new GeometryDrawing(foldFill, thinPen, fold));
            group.Children.Add(new GeometryDrawing(null, pen, page));
            group.Freeze();

            var image = new DrawingImage(group);
            image.Freeze();
            return image;
        }
    }
}
