using System.Collections.Generic;

namespace MarkdownKit.PlantUml
{
    /// <summary>参与者在图里画成什么形状。</summary>
    internal enum ParticipantKind
    {
        Participant,
        Actor,
        Boundary,
        Control,
        Entity,
        Database,
        Collections,
        Queue
    }

    /// <summary>注释相对参与者的位置。</summary>
    internal enum NotePlacement
    {
        Left,
        Right,
        Over
    }

    /// <summary>时序图里的一个参与者（一条生命线）。</summary>
    internal sealed class Participant
    {
        /// <summary>消息里引用的标识（participant X as Y 里的 Y）。</summary>
        public string Key;

        /// <summary>框里显示的文字（participant X as Y 里的 X）。</summary>
        public string Label;

        public ParticipantKind Kind;

        /// <summary>列序，0 起，按出现顺序排。</summary>
        public int Index;
    }

    /// <summary>时序图里的一条纵向内容，按出现顺序排。</summary>
    internal abstract class SeqItem
    {
    }

    /// <summary>一条消息箭头。From == To 时是自消息（回折箭头）。</summary>
    internal sealed class MessageItem : SeqItem
    {
        public int From;
        public int To;
        public string Text;             // 消息文字，可为 null
        public bool Dashed;             // -- 虚线
        public bool OpenHead;           // >> 空心箭头
        public bool LostHead;           // x 丢失箭头
        public bool ActivateTarget;     // 尾部 ++，激活接收方
        public bool DeactivateSource;   // 尾部 --，去激活发送方
        public int Number;              // autonumber 序号，0 表示无
    }

    /// <summary>activate / deactivate 一条生命线上的激活条起止。</summary>
    internal sealed class ActivateItem : SeqItem
    {
        public int Participant;
        public bool Deactivate;
    }

    /// <summary>一条注释。</summary>
    internal sealed class NoteItem : SeqItem
    {
        public NotePlacement Placement;
        public int A;
        public int B = -1;      // 仅 note over A, B 时有效
        public string Text;
    }

    /// <summary>一个分组框（alt / opt / loop / par / group / break / critical）。</summary>
    internal sealed class FragmentItem : SeqItem
    {
        public string Kind;
        public readonly List<FragmentSection> Sections = new List<FragmentSection>();
    }

    /// <summary>分组框里的一段（alt 的 else 会把框切成多段）。</summary>
    internal sealed class FragmentSection
    {
        public string Label;
        public readonly List<SeqItem> Items = new List<SeqItem>();
    }

    /// <summary>一条分隔线（== 文字 ==）。</summary>
    internal sealed class DividerItem : SeqItem
    {
        public string Text;
    }

    /// <summary>一段延时（... 文字 ...）。</summary>
    internal sealed class DelayItem : SeqItem
    {
        public string Text;
    }

    /// <summary>一张时序图解析出来的全部内容。</summary>
    internal sealed class SequenceDiagram
    {
        public string Title;

        /// <summary>内容像不像时序图。认不出的行多于认得出的行就判为不像（多半是别的图）。</summary>
        public bool LooksLikeSequence = true;

        /// <summary>认得出的行数与认不出的行数，供分发器跟组件图比，谁的得分高用谁。</summary>
        public int Known;
        public int Unknown;

        /// <summary>图例（legend ... endlegend）的正文，按行拆开；没有图例时为空。</summary>
        public readonly List<string> Legend = new List<string>();

        /// <summary>图例位置：bottom（默认）/ top / left / right / center。</summary>
        public string LegendPlacement = "bottom";

        /// <summary>参与者，按列序排。</summary>
        public readonly List<Participant> Participants = new List<Participant>();

        /// <summary>顶层内容，按出现顺序排。</summary>
        public readonly List<SeqItem> Items = new List<SeqItem>();

        /// <summary>按 key 找参与者，没有就新建一个排在末尾。消息里引用的名字会自动建参与者。</summary>
        public int Ensure(string key)
        {
            for (int i = 0; i < Participants.Count; i++)
            {
                if (Participants[i].Key == key)
                    return i;
            }
            Participants.Add(new Participant
            {
                Key = key,
                Label = key,
                Kind = ParticipantKind.Participant,
                Index = Participants.Count
            });
            return Participants.Count - 1;
        }
    }
}
