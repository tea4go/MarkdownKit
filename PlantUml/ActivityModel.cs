using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace MarkdownKit.PlantUml
{
    /// <summary>活动图里一个块的种类。整张图是一棵块树，Seq 往下堆，If / Fork 往两边分。</summary>
    internal enum ActKind
    {
        /// <summary>竖向顺序容器。</summary>
        Seq,

        /// <summary>活动节点 `:文字;`，画成圆角框。</summary>
        Action,

        /// <summary>起始节点，实心圆。</summary>
        Start,

        /// <summary>结束节点，同心圆（牛眼）。</summary>
        Stop,

        /// <summary>`end`，跟 Stop 画法一样但用圆角矩形外框（官方 end 是圆角方框）。</summary>
        End,

        /// <summary>`detach`，一条断掉的短横线。</summary>
        Detach,

        /// <summary>if / else，判断菱形 + 两支 + 汇合菱形。</summary>
        If,

        /// <summary>while，判断菱形 + 循环体 + 回边。</summary>
        While,

        /// <summary>repeat，循环体 + 判断菱形 + 回边。</summary>
        Repeat,

        /// <summary>fork / split，上下两条粗黑条夹着并列分支。</summary>
        Fork,

        /// <summary>判断菱形（if / while 的条件、repeat 的入口）。</summary>
        Diamond,

        /// <summary>分支汇合菱形。</summary>
        Merge,

        /// <summary>fork / split 的同步粗条。</summary>
        Bar,

        /// <summary>只画注释框的占位节点（注释挂在复合块上时用）。</summary>
        Note
    }

    /// <summary>活动图里的一个块（布局前后都挂在这个结构上）。</summary>
    internal sealed class ActBlock
    {
        public ActKind Kind;

        /// <summary>活动文字 / 判断条件。</summary>
        public string Text;

        /// <summary>分支上的标签（是 / 否 / 循环条件标签）。</summary>
        public string Label;

        /// <summary>循环出口的标签（while 的 is / repeat 的 not）。</summary>
        public string ExitLabel;

        /// <summary>Seq 的条目、Fork 的分支。</summary>
        public readonly List<ActBlock> Items = new List<ActBlock>();

        /// <summary>If / While / Repeat 的两支；While / Repeat 只用 Then。</summary>
        public ActBlock Then;
        public ActBlock Else;

        /// <summary>注释（note right: xxx）。</summary>
        public string Note;
        public bool NoteRight = true;

        /// <summary>所在的泳道序号（`|泳道名|`），没写泳道时是 0。</summary>
        public int Lane;

        // 布局结果
        public double X;
        public double Y;
        public double W;
        public double H;

        /// <summary>形状本身的宽度。挂了注释时 W 是"形状 + 注释"的总宽，形状仍按 ShapeW 画。</summary>
        public double ShapeW;

        /// <summary>入口点相对左上角的位置。</summary>
        public double InX;
        public double InY;

        /// <summary>出口点相对左上角的位置。</summary>
        public double OutX;
        public double OutY;

        /// <summary>文字排版结果。</summary>
        public FormattedText Ft;

        /// <summary>标签排版结果（是 / 否）。</summary>
        public FormattedText LabelFt;

        /// <summary>出口标签排版结果。</summary>
        public FormattedText ExitFt;

        /// <summary>注释排版结果。</summary>
        public FormattedText NoteFt;
    }

    /// <summary>一条连线（折线），Arrow 为真时末端画箭头。</summary>
    internal sealed class ActWire
    {
        public readonly List<Point> Points = new List<Point>();
        public bool Arrow = true;

        /// <summary>贴在连线上的文字（分支标签），可为 null。</summary>
        public FormattedText Label;

        public Point LabelAt;
    }

    /// <summary>一张活动图解析出来的全部内容。</summary>
    internal sealed class ActivityDiagram
    {
        public string Title;
        public ActBlock Root;

        /// <summary>泳道名（`|名字|`），按首次出现的顺序。空表示没写泳道。</summary>
        public readonly List<string> Lanes = new List<string>();

        public int Known;
        public int Unknown;

        public bool LooksLikeActivity
        {
            get { return Root != null && HasFlow(Root) && Unknown <= Known; }
        }

        /// <summary>至少要有一个真正的活动节点或起止节点，否则不值得画。</summary>
        private static bool HasFlow(ActBlock block)
        {
            if (block.Kind == ActKind.Action || block.Kind == ActKind.Start
                || block.Kind == ActKind.Stop || block.Kind == ActKind.End
                || block.Kind == ActKind.If || block.Kind == ActKind.Fork
                || block.Kind == ActKind.While || block.Kind == ActKind.Repeat)
                return true;
            foreach (ActBlock item in block.Items)
            {
                if (HasFlow(item))
                    return true;
            }
            return (block.Then != null && HasFlow(block.Then))
                || (block.Else != null && HasFlow(block.Else));
        }
    }
}
