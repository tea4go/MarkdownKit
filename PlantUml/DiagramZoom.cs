using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace MarkdownKit
{
    /// <summary>
    /// 可缩放的预览图（目前是树形图）。宿主程序通过 <see cref="MarkdownZoom.Find"/> 找到它，
    /// 再调放大 / 缩小 / 1:1；缩放状态挂在实例上，按页签记忆由宿主自己保存。
    /// </summary>
    public interface IZoomableView
    {
        /// <summary>固定倍率，相对原始尺寸；0 表示按预览区宽度自适应（默认）。</summary>
        double Zoom { get; set; }

        /// <summary>当前实际显示倍率，100 表示 1:1。</summary>
        double ScalePercent { get; }

        /// <summary>当前显示宽度（已乘倍率）。宿主用它决定文档页宽和横向滚动条。</summary>
        double DisplayWidth { get; }

        void ZoomIn();

        void ZoomOut();

        void ActualSize();
    }

    /// <summary>从渲染好的 FlowDocument 里找可缩放图。结构化文本预览只有一张树形图。</summary>
    public static class MarkdownZoom
    {
        public static IZoomableView Find(FlowDocument document)
        {
            if (document == null)
                return null;
            return FindIn(document.Blocks);
        }

        private static IZoomableView FindIn(BlockCollection blocks)
        {
            foreach (Block block in blocks)
            {
                var section = block as Section;
                if (section != null)
                {
                    IZoomableView hit = FindIn(section.Blocks);
                    if (hit != null)
                        return hit;
                }

                var container = block as BlockUIContainer;
                if (container == null)
                    continue;

                // 树形图外面包了一层横向滚动条，先把这层剥掉
                var zoom = container.Child as IZoomableView;
                if (zoom != null)
                    return zoom;
                var scroller = container.Child as ScrollViewer;
                if (scroller != null)
                {
                    zoom = scroller.Content as IZoomableView;
                    if (zoom != null)
                        return zoom;
                }
            }
            return null;
        }
    }
}
