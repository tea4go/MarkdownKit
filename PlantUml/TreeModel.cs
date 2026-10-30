using System.Collections.Generic;

namespace MarkdownKit.PlantUml
{
    /// <summary>树形图的种类。四者结构一样，只是排布方向和节点形状不同。</summary>
    internal enum TreeKind
    {
        /// <summary>思维导图：根在左、子节点往右铺，`+` 开头的分支铺到左边，圆角框 + 曲线。</summary>
        MindMap,

        /// <summary>WBS：根在上、子节点往下铺，矩形框 + 折线。</summary>
        Wbs,

        /// <summary>JSON：每个对象画成"键 | 值"两列表格，嵌套对象用虚线箭头连出去。</summary>
        Json,

        /// <summary>YAML：同 JSON，只是输入是缩进式的 YAML。</summary>
        Yaml
    }

    /// <summary>树形图里的一个节点。</summary>
    internal sealed class TreeNode
    {
        /// <summary>节点文字；JSON / YAML 里是键名。</summary>
        public string Text;

        /// <summary>JSON / YAML 的值；容器节点是空串，非表格节点是 null。</summary>
        public string Value;

        /// <summary>
        /// JSON / YAML 里一个对象/数组整体画成一张表：这时本节点的内容在 <see cref="Rows"/> 里，
        /// Text / Value 不用。表与表之间靠 <see cref="Nested"/> 连。
        /// </summary>
        public bool IsTable;

        /// <summary>表里的行（只有 <see cref="IsTable"/> 为 true 时有意义）。</summary>
        public readonly List<TreeNode> Rows = new List<TreeNode>();

        /// <summary>这一行的值本身是个对象时，指向那张表。</summary>
        public TreeNode Nested;

        /// <summary>mindmap 里 `+` 开头的分支，摆到根左边。</summary>
        public bool LeftSide;

        public readonly List<TreeNode> Children = new List<TreeNode>();

        // 下面是布局算出来的，绘制直接用
        public double X;
        public double Y;
        public double W;
        public double H;

        /// <summary>子树在"层内轴"上占的尺寸（布局中间量）。</summary>
        public double Band;
    }

    /// <summary>一张树形图解析出来的全部内容。</summary>
    internal sealed class TreeDiagram
    {
        public TreeKind Kind;
        public string Title;
        public bool LeftToRight = true;

        /// <summary>认得出的行数与认不出的行数，供分发器判断该不该画。</summary>
        public int Known;
        public int Unknown;

        /// <summary>根节点。mindmap / wbs 只有一个；JSON / YAML 顶层是对象时也只有一个。</summary>
        public readonly List<TreeNode> Roots = new List<TreeNode>();

        /// <summary>像不像一张能画的树形图。</summary>
        public bool LooksLikeTree
        {
            get { return Roots.Count > 0 && Unknown <= Known; }
        }
    }
}
