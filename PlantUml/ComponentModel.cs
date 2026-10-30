using System.Collections.Generic;

namespace MarkdownKit.PlantUml
{
    /// <summary>组件/部署图里一个元素的种类。带 { } 体的算容器，其余是叶子。</summary>
    internal enum ElementKind
    {
        Node,
        Package,
        Folder,
        Frame,
        Cloud,
        Rectangle,
        Storage,
        Stack,
        Component,
        Database,
        Queue,
        Artifact,
        Card,
        File,
        Usecase,
        Actor,
        Agent,
        Boundary,
        Control,
        Entity,
        Collections,
        Interface,
        Circle,
        Hexagon,
        Person,
        Label,
        Action,
        Process,

        // ── 类图 / ER 图 ──
        /// <summary>class：名字左边一个绿色 C 圆标，下面按隔间列字段/方法。</summary>
        Class,
        /// <summary>abstract class：名字斜体。</summary>
        AbstractClass,
        /// <summary>enum。</summary>
        Enum,
        /// <summary>annotation。</summary>
        Annotation,
        /// <summary>struct。</summary>
        Struct,
        /// <summary>object（对象图），名字带下划线。</summary>
        Object,
        /// <summary>ER 图的实体（跟组件图里的 entity 边界形状不是一回事）。</summary>
        ErEntity
    }

    /// <summary>组件图里组件元素画成什么形状（skinparam componentStyle）。</summary>
    internal enum ComponentStyle
    {
        /// <summary>UML2 组件图标（矩形 + 右上角小方块及凸耳），官方默认。</summary>
        Uml2,

        /// <summary>UML1 组件（左右两侧带两个小凸耳）。</summary>
        Uml1,

        /// <summary>普通矩形。</summary>
        Rectangle
    }

    /// <summary>类 / ER 框里的一个隔间（名字下面那几块）。</summary>
    internal sealed class Compartment
    {
        /// <summary>里面的每一行。</summary>
        public readonly List<string> Lines = new List<string>();
    }

    /// <summary>组件图里一个元素（容器或叶子）。</summary>
    internal sealed class Element
    {
        /// <summary>关系里引用的别名（xxx as Yyy 里的 Yyy）。</summary>
        public string Key;

        /// <summary>框里显示的文字。</summary>
        public string Label;

        public ElementKind Kind;

        /// <summary>构造型（&lt;&lt;xxx&gt;&gt; 里的 xxx），没有则为 null。</summary>
        public string Stereotype;

        /// <summary>
        /// 挂在自己身上的关系标签里最宽的那条（布局用）。标签画在边旁会横向伸出去，
        /// 同层排列时要按这个给元素留位，不然标签会压到隔壁节点上。
        /// </summary>
        public double LabelW;

        /// <summary>带 { } 体的是容器，能装子元素。</summary>
        public bool IsContainer;

        /// <summary>
        /// 类 / ER 框：名字下面按隔间列字段、方法、字段列。空表示不是这种框。
        /// </summary>
        public readonly List<Compartment> Compartments = new List<Compartment>();

        /// <summary>名字左边画个圆标（类图的 C / I / E）。</summary>
        public bool ShowIcon = true;

        /// <summary>透明容器（together { }）：只把子元素凑一组排布，不画边框和标题。</summary>
        public bool Transparent;

        public Element Parent;

        public readonly List<Element> Children = new List<Element>();

        // 下面几个是布局算出来的，绘制直接用
        public double X;
        public double Y;
        public double W;
        public double H;

        /// <summary>从根到自己的深度，0 起。</summary>
        public int Depth
        {
            get
            {
                int d = 0;
                for (Element p = Parent; p != null; p = p.Parent)
                    d++;
                return d;
            }
        }

        /// <summary>是否在容器 c 内（含自身就是 c 的情况）。</summary>
        public bool Inside(Element c)
        {
            for (Element e = this; e != null; e = e.Parent)
            {
                if (e == c)
                    return true;
            }
            return false;
        }
    }

    /// <summary>关系箭头端的装饰。</summary>
    internal enum EndArrow
    {
        None,
        Open,           // >
        Triangle,       // |&gt; 空心三角（继承）
        DiamondFilled,  // * 实心菱形（组合）
        DiamondHollow   // o 空心菱形（聚合）
    }

    /// <summary>ER 图（鸦爪记号）那一端的基数。</summary>
    internal enum ErEnd
    {
        None,

        /// <summary>`||` 正好一个。</summary>
        One,

        /// <summary>`o|` 零或一个。</summary>
        ZeroOne,

        /// <summary>`|&lt;` 一个或多个。</summary>
        OneMany,

        /// <summary>`o{` 零或多个。</summary>
        ZeroMany,

        /// <summary>`&lt;` 多个。</summary>
        Many
    }

    /// <summary>两个元素之间的一条关系。Left/Right 按源码书写顺序。</summary>
    internal sealed class Relation
    {
        public Element Left;
        public Element Right;
        public string Label;
        public bool Dashed;

        /// <summary>
        /// 连接符里横线/点的个数，对应官方 Link.getLength()。官方把它当 dot 的 minlen 用
        /// （minlen = length - 1）：-> 是 1（两端同层），--> / -- / ..> 是 2（目标下沉一层）。
        /// </summary>
        public int Length = 2;
        public EndArrow LeftArrow;
        public EndArrow RightArrow;

        /// <summary>两端的重数标签（类图里的 "1" / "1..*" / "0..1"）。</summary>
        public string LeftMultiplicity;
        public string RightMultiplicity;

        /// <summary>ER 图两端的鸦爪记号。</summary>
        public ErEnd LeftEr;
        public ErEnd RightEr;

        /// <summary>挂在关系线上的注释（note on link），可为 null。</summary>
        public string Note;

        /// <summary>箭头指向的那一端（画箭头用）。两边都没箭头时按左→右。</summary>
        public Element Target
        {
            get
            {
                if (RightArrow != EndArrow.None)
                    return Right;
                if (LeftArrow != EndArrow.None)
                    return Left;
                return Right;
            }
        }

        /// <summary>箭尾那一端。</summary>
        public Element Source
        {
            get { return Target == Left ? Right : Left; }
        }
    }

    /// <summary>挂在元素旁的注释（note left/right/top/bottom of X）。</summary>
    internal sealed class DiagramNote
    {
        /// <summary>目标元素的 key。</summary>
        public string Target;

        /// <summary>left / right / top / bottom。</summary>
        public string Placement;

        public string Text;
    }

    /// <summary>一张组件/部署图解析出来的全部内容。</summary>
    internal sealed class ComponentDiagram
    {
        public string Title;

        /// <summary>true 表示 left to right direction，分层方向由竖改横。</summary>
        public bool LeftToRight;

        /// <summary>skinparam componentStyle，默认 UML2。</summary>
        public ComponentStyle ComponentStyle = ComponentStyle.Uml2;

        /// <summary>源码里出现过 [X] 组件简写。这是组件图独占语法，判定时优先于时序图。</summary>
        public bool HasBracketSyntax;

        /// <summary>类图 / ER 图：`hide members` 之类的开关。</summary>
        public bool HideMembers;
        public bool HideMethods;
        public bool HideFields;
        public bool HideCircle;
        public bool HideEmptyMembers;

        /// <summary>true 表示这是一张类图或 ER 图（按隔间画框）。</summary>
        public bool IsClassDiagram;

        /// <summary>图例（legend ... endlegend）的正文，按行拆开；没有图例时为空。</summary>
        public readonly List<string> Legend = new List<string>();

        /// <summary>图例位置：bottom（默认）/ top / left / right / center。</summary>
        public string LegendPlacement = "bottom";

        /// <summary>认得出的行数与认不出的行数，供分发器判断该用哪套渲染器。</summary>
        public int Known;
        public int Unknown;

        public readonly List<Element> Roots = new List<Element>();
        public readonly List<Relation> Relations = new List<Relation>();
        public readonly List<DiagramNote> Notes = new List<DiagramNote>();

        private readonly Dictionary<string, Element> _byKey = new Dictionary<string, Element>();

        public Element Find(string key)
        {
            Element e;
            return key != null && _byKey.TryGetValue(key, out e) ? e : null;
        }

        /// <summary>声明一个元素。已经存在（关系里先引用过）就复用并补全信息。</summary>
        public Element Declare(string key, string label, ElementKind kind, bool container, Element parent)
        {
            Element e = Find(key);
            if (e == null)
            {
                e = new Element { Key = key };
                _byKey[key] = e;
                Siblings(parent).Add(e);
            }
            else
            {
                // 之前挂在别处（多半是被关系提前创建在根上），挪到正确的父下
                List<Element> old = e.Parent == null ? Roots : e.Parent.Children;
                List<Element> now = Siblings(parent);
                if (old != now)
                {
                    old.Remove(e);
                    now.Add(e);
                }
            }
            e.Label = label;
            e.Kind = kind;
            e.IsContainer = container;
            e.Parent = parent;
            return e;
        }

        /// <summary>关系里引用到的名字，没声明过就补一个默认元素。</summary>
        public Element Ensure(string key)
        {
            Element e = Find(key);
            if (e != null)
                return e;
            e = new Element { Key = key, Label = key, Kind = ElementKind.Component };
            _byKey[key] = e;
            Roots.Add(e);
            return e;
        }

        private List<Element> Siblings(Element parent)
        {
            return parent == null ? Roots : parent.Children;
        }
    }
}
