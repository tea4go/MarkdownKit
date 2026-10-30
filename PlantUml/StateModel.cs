using System.Collections.Generic;

namespace MarkdownKit.PlantUml
{
    /// <summary>状态图里一个节点的种类。Normal 是普通圆角矩形状态，其余是起止圆、伪状态或复合状态。</summary>
    internal enum StateKind
    {
        /// <summary>普通状态：圆角矩形。</summary>
        Normal,

        /// <summary>[*] 出现在转换左侧：起始实心圆。</summary>
        Start,

        /// <summary>[*] 出现在转换右侧：结束双圈。</summary>
        End,

        /// <summary>[H] 历史伪状态：圆内写 H。</summary>
        History,

        /// <summary>[H*] 深历史伪状态：圆内写 H*。</summary>
        DeepHistory,

        /// <summary>&lt;&lt;choice&gt;&gt; 选择：菱形。</summary>
        Choice,

        /// <summary>&lt;&lt;fork&gt;&gt; / &lt;&lt;join&gt;&gt;：粗实线。</summary>
        ForkJoin,

        /// <summary>&lt;&lt;junction&gt;&gt;：小实心圆。</summary>
        Junction,

        /// <summary>==x== 同步条。</summary>
        SynchroBar,

        /// <summary>state X { ... } 复合状态：能装子状态的圆角容器。</summary>
        Composite
    }

    /// <summary>状态图里的一个状态节点（含伪状态和复合状态）。</summary>
    internal sealed class StateNode
    {
        /// <summary>引用 id（as 别名或名字）。</summary>
        public string Key;

        /// <summary>框里显示的文字。</summary>
        public string Label;

        public StateKind Kind = StateKind.Normal;

        /// <summary>构造型（&lt;&lt;xxx&gt;&gt; 里的 xxx），没有则为 null。</summary>
        public string Stereotype;

        /// <summary>state X : 字段，每个字段一行，画在名称下方。</summary>
        public readonly List<string> Fields = new List<string>();

        public StateNode Parent;

        public readonly List<StateNode> Children = new List<StateNode>();

        // 复合状态内部自己的起止圆（对应官方每个 group 独立的 *start*/*end*）
        public StateNode StartNode;
        public StateNode EndNode;

        // 下面几个是布局算出来的，绘制直接用
        public double X;
        public double Y;
        public double W;
        public double H;

        /// <summary>右侧挂 note 时要预留的宽度（布局用，不含在 W 里，免得节点框被撑宽）。</summary>
        public double NoteW;

        /// <summary>挂在本状态上的转换标签里最宽的那条（布局用）。标签横向会伸出节点，同层要给它留位置。</summary>
        public double LabelW;

        /// <summary>从根到自己的深度，0 起。</summary>
        public int Depth
        {
            get
            {
                int d = 0;
                for (StateNode p = Parent; p != null; p = p.Parent)
                    d++;
                return d;
            }
        }

        /// <summary>是否在容器 c 内（含自身就是 c 的情况）。</summary>
        public bool Inside(StateNode c)
        {
            for (StateNode e = this; e != null; e = e.Parent)
            {
                if (e == c)
                    return true;
            }
            return false;
        }
    }

    /// <summary>转换箭头末端的样式。</summary>
    internal enum TransitionHead
    {
        /// <summary>-&gt; 箭头。</summary>
        Arrow,

        /// <summary>-o 空心圆。</summary>
        Circle
    }

    /// <summary>两个状态之间的一条转换。From/To 按源码书写顺序。</summary>
    internal sealed class Transition
    {
        public StateNode From;
        public StateNode To;
        public string Label;

        /// <summary>方向提示（-up-&gt; 之类），可空。</summary>
        public string Direction;

        /// <summary>颜色提示（[#red]），可空。</summary>
        public string Color;

        /// <summary>起点带 x（尾端画叉）。</summary>
        public bool CrossStart;

        /// <summary>[bold] 粗线。</summary>
        public bool Bold;

        /// <summary>[dotted] / [dashed] 虚线。</summary>
        public bool Dashed;

        public TransitionHead Head = TransitionHead.Arrow;
    }

    /// <summary>挂在状态旁的注释（note left/right/top/bottom of X）。</summary>
    internal sealed class StateNote
    {
        /// <summary>目标状态的 key。</summary>
        public string TargetKey;

        /// <summary>left / right / top / bottom。</summary>
        public string Placement;

        /// <summary>注释正文，按行拆开。</summary>
        public readonly List<string> Lines = new List<string>();
    }

    /// <summary>一张状态图解析出来的全部内容。</summary>
    internal sealed class StateDiagram
    {
        public string Title;

        /// <summary>true 表示 left to right direction，分层方向由竖改横。</summary>
        public bool LeftToRight;

        /// <summary>源码里出现过 state 声明关键字。</summary>
        public bool SawStateKeyword;

        /// <summary>源码里出现过 [*] 端点。</summary>
        public bool SawStartBracket;

        /// <summary>图例（legend ... endlegend）的正文，按行拆开；没有图例时为空。</summary>
        public readonly List<string> Legend = new List<string>();

        /// <summary>图例位置：bottom（默认）/ top / left / right / center。</summary>
        public string LegendPlacement = "bottom";

        /// <summary>认得出的行数与认不出的行数，供分发器判断该用哪套渲染器。</summary>
        public int Known;
        public int Unknown;

        public readonly List<StateNode> Roots = new List<StateNode>();
        public readonly List<Transition> Transitions = new List<Transition>();
        public readonly List<StateNote> Notes = new List<StateNote>();

        /// <summary>根级的 [*] 起始 / 结束（懒建）。</summary>
        public StateNode StartNode;
        public StateNode EndNode;

        private readonly Dictionary<string, StateNode> _byKey = new Dictionary<string, StateNode>();

        /// <summary>
        /// 像不像状态图：没认错太多，且出现过 state 关键字或 [*] 端点，并且解析出了节点。
        /// 纯 A --&gt; B（两者都没有）不算，留给时序图/组件图判。
        /// </summary>
        public bool LooksLikeState
        {
            get
            {
                return Unknown <= Known && (SawStateKeyword || SawStartBracket) && Roots.Count > 0;
            }
        }

        public StateNode Find(string key)
        {
            StateNode e;
            return key != null && _byKey.TryGetValue(key, out e) ? e : null;
        }

        /// <summary>声明一个状态。已经存在（转换里先引用过）就复用并补全信息。</summary>
        public StateNode Declare(string key, string label, StateKind kind, StateNode parent)
        {
            StateNode e = Find(key);
            if (e == null)
            {
                e = new StateNode { Key = key };
                _byKey[key] = e;
                Siblings(parent).Add(e);
            }
            else
            {
                // 之前挂在别处（多半是被转换提前创建在根上），挪到正确的父下
                List<StateNode> old = e.Parent == null ? Roots : e.Parent.Children;
                List<StateNode> now = Siblings(parent);
                if (old != now)
                {
                    old.Remove(e);
                    now.Add(e);
                }
            }
            e.Label = label;
            if (kind != StateKind.Normal || e.Kind == StateKind.Normal)
                e.Kind = kind;
            e.Parent = parent;
            return e;
        }

        /// <summary>转换里引用到的名字，没声明过就补一个默认普通状态，挂在 parent（根级为 null）下。</summary>
        public StateNode Ensure(string key, StateNode parent)
        {
            StateNode e = Find(key);
            if (e != null)
                return e;
            e = new StateNode { Key = key, Label = key, Kind = StateKind.Normal, Parent = parent };
            _byKey[key] = e;
            Siblings(parent).Add(e);
            return e;
        }

        /// <summary>取容器（或根）的起始圆，没有就建一个。</summary>
        public StateNode GetStart(StateNode container)
        {
            if (container == null)
            {
                if (StartNode == null)
                {
                    StartNode = new StateNode { Key = "*start*", Label = "", Kind = StateKind.Start };
                    Roots.Add(StartNode);
                }
                return StartNode;
            }
            if (container.StartNode == null)
            {
                container.StartNode = new StateNode { Key = "*start*" + container.Key, Label = "", Kind = StateKind.Start, Parent = container };
                container.Children.Add(container.StartNode);
            }
            return container.StartNode;
        }

        /// <summary>取容器（或根）的结束圆，没有就建一个。</summary>
        public StateNode GetEnd(StateNode container)
        {
            if (container == null)
            {
                if (EndNode == null)
                {
                    EndNode = new StateNode { Key = "*end*", Label = "", Kind = StateKind.End };
                    Roots.Add(EndNode);
                }
                return EndNode;
            }
            if (container.EndNode == null)
            {
                container.EndNode = new StateNode { Key = "*end*" + container.Key, Label = "", Kind = StateKind.End, Parent = container };
                container.Children.Add(container.EndNode);
            }
            return container.EndNode;
        }

        private List<StateNode> Siblings(StateNode parent)
        {
            return parent == null ? Roots : parent.Children;
        }
    }
}
