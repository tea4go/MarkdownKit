using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace MarkdownKit.PlantUml
{
    /// <summary>一张时序图算好的全部几何。绘制那边只管照着画，不再做任何计算。</summary>
    internal sealed class SequenceLayout
    {
        public double Width;
        public double Height;

        public readonly List<BoxGeom> Boxes = new List<BoxGeom>();
        public readonly List<double> Lifelines = new List<double>();
        public double LifelineTop;
        public double LifelineBottom;

        /// <summary>参与者跨度（首尾生命线）。分隔线、延时画在这段上，不用整个画布宽度——
        /// 画布会因为自消息回折、注释伸出而变宽，中心会偏到参与者之外。</summary>
        public double SpanLeft;
        public double SpanRight;
        public double SpanCenter;

        public readonly List<BarGeom> Bars = new List<BarGeom>();
        public readonly List<ArrowGeom> Arrows = new List<ArrowGeom>();
        public readonly List<NoteGeom> Notes = new List<NoteGeom>();
        public readonly List<FrameGeom> Frames = new List<FrameGeom>();
        public readonly List<DividerGeom> Dividers = new List<DividerGeom>();
        public readonly List<DelayGeom> Delays = new List<DelayGeom>();
        public TitleGeom Title;

        /// <summary>图例框（legend ... endlegend），没有图例时为 null。</summary>
        public LegendGeom Legend;

        /// <summary>把所有几何整体右移，使最左边的元素落在 margin 处，并算出画布宽高。</summary>
        public void Normalize(double margin)
        {
            double minX = double.MaxValue;
            double maxX = double.MinValue;
            foreach (BoxGeom box in Boxes)
            {
                minX = Math.Min(minX, box.Rect.Left);
                maxX = Math.Max(maxX, box.Rect.Right);
            }
            foreach (BarGeom bar in Bars)
            {
                minX = Math.Min(minX, bar.Rect.Left);
                maxX = Math.Max(maxX, bar.Rect.Right);
            }
            foreach (NoteGeom note in Notes)
            {
                minX = Math.Min(minX, note.Rect.Left);
                maxX = Math.Max(maxX, note.Rect.Right);
            }
            foreach (FrameGeom frame in Frames)
            {
                minX = Math.Min(minX, frame.Rect.Left);
                maxX = Math.Max(maxX, frame.Rect.Right);
            }
            foreach (ArrowGeom arrow in Arrows)
            {
                minX = Math.Min(minX, arrow.MinX);
                maxX = Math.Max(maxX, arrow.MaxX);
            }
            if (Legend != null)
            {
                minX = Math.Min(minX, Legend.Rect.Left);
                maxX = Math.Max(maxX, Legend.Rect.Right);
            }
            if (minX > maxX)
            {
                minX = 0;
                maxX = 0;
            }

            double dx = margin - minX;
            foreach (BoxGeom box in Boxes)
                box.Rect = Shift(box.Rect, dx);
            foreach (BarGeom bar in Bars)
                bar.Rect = Shift(bar.Rect, dx);
            foreach (NoteGeom note in Notes)
                note.Rect = Shift(note.Rect, dx);
            foreach (FrameGeom frame in Frames)
                frame.Rect = Shift(frame.Rect, dx);
            for (int i = 0; i < Lifelines.Count; i++)
                Lifelines[i] += dx;
            foreach (ArrowGeom arrow in Arrows)
                arrow.Shift(dx);

            if (Lifelines.Count > 0)
            {
                SpanLeft = Lifelines[0];
                SpanRight = Lifelines[Lifelines.Count - 1];
                SpanCenter = (SpanLeft + SpanRight) / 2;
            }

            if (Legend != null)
            {
                Legend.Shift(dx, 0);
                Height = Math.Max(Height, Legend.Rect.Bottom);
            }
            Width = (maxX - minX) + margin * 2;
            Height = Height + margin;
            if (Title != null)
                Title.CenterX = Width / 2;
        }

        private static Rect Shift(Rect rect, double dx)
        {
            return new Rect(rect.Left + dx, rect.Top, rect.Width, rect.Height);
        }
    }

    internal sealed class BoxGeom
    {
        public Rect Rect;
        public FormattedText Text;
        public ParticipantKind Kind;
    }

    internal sealed class BarGeom
    {
        public Rect Rect;
    }

    /// <summary>一条消息箭头。From == To 时是自消息，用 SelfWidth/SelfHeight 画回折。</summary>
    internal sealed class ArrowGeom
    {
        public int From;
        public int To;
        public double Y;
        public bool Dashed;
        public bool Open;
        public bool Lost;
        public FormattedText Label;     // 可为 null
        public double LabelCenterX;     // 非自消息：标签中心；自消息：标签左边
        public double LabelTop;
        public double SelfWidth;
        public double SelfHeight;
        public double MinX;
        public double MaxX;

        public bool Self { get { return From == To; } }

        public void Shift(double dx)
        {
            LabelCenterX += dx;
            MinX += dx;
            MaxX += dx;
        }
    }

    internal sealed class NoteGeom
    {
        public Rect Rect;
        public FormattedText Text;
    }

    internal sealed class FrameGeom
    {
        public Rect Rect;
        public FormattedText Kind;
        public readonly List<FrameSectionGeom> Sections = new List<FrameSectionGeom>();
    }

    internal sealed class FrameSectionGeom
    {
        public double DividerY;       // 分隔线（else 那条虚线）的 y
        public double Y;              // 段标签文字顶部的 y
        public FormattedText Label;   // 可为 null
        public bool HasDivider;
    }

    internal sealed class DividerGeom
    {
        public double Y;
        public FormattedText Text;    // 可为 null
    }

    internal sealed class DelayGeom
    {
        public double Y;
        public FormattedText Text;    // 可为 null
    }

    internal sealed class TitleGeom
    {
        public FormattedText Text;
        public double CenterX;
        public double Top;
    }

    /// <summary>
    /// 由模型算出几何：参与者框、生命线、消息箭头、激活条、注释框、分组框的位置尺寸。
    /// 文本尺寸用 WPF 的 FormattedText 量，随字体字号变化。
    /// </summary>
    internal static class SequenceLayoutBuilder
    {
        public static SequenceLayout Build(SequenceDiagram diagram, double fontSize, double pixelsPerDip)
        {
            return new Impl(diagram, fontSize, pixelsPerDip).Build();
        }

        private sealed class Impl
        {
            private readonly SequenceDiagram _diagram;
            private readonly double _fs;
            private readonly double _ppd;
            private readonly Typeface _tf;
            private readonly Typeface _tfBold;
            private readonly Brush _text;
            private readonly SequenceLayout _layout = new SequenceLayout();
            private readonly List<Activation> _open = new List<Activation>();

            private double[] _xs;      // 各生命线的 x
            private double[] _boxW;    // 各参与者框宽
            private double[] _boxH;    // 各参与者框高（形状不同，顶部框底对齐、底部框顶对齐）

            private sealed class Activation
            {
                public int Participant;
                public double StartY;
                public int Depth;
            }

            public Impl(SequenceDiagram diagram, double fontSize, double pixelsPerDip)
            {
                _diagram = diagram;
                _fs = fontSize;
                _ppd = pixelsPerDip <= 0 ? 1.0 : pixelsPerDip;
                _tf = new Typeface(MarkdownTheme.DiagramFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                _tfBold = MarkdownTheme.BoldTypeface(MarkdownTheme.DiagramFamily);
                _text = MarkdownTheme.Markdown.Text;
            }

            public SequenceLayout Build()
            {
                int count = _diagram.Participants.Count;
                if (count == 0)
                    return _layout;

                MeasureBoxes(count);
                PlaceColumns(count);

                double margin = _fs * 0.36;
                double y = margin;

                if (!string.IsNullOrEmpty(_diagram.Title))
                {
                    var title = Text(_diagram.Title, _tfBold, _text);
                    _layout.Title = new TitleGeom { Text = title, Top = y, CenterX = 0 };
                    y += title.Height + _fs * 0.8;
                }

                // 参与者框统一顶对齐、统一高度，生命线才好从同一条横线往下走
                double boxesTop = y;
                double boxH = 0;
                for (int i = 0; i < count; i++)
                    boxH = Math.Max(boxH, _boxH[i]);
                for (int i = 0; i < count; i++)
                {
                    double w = _boxW[i];
                    // 形状不同、高度不同：顶部框底对齐，生命线才好从同一条横线起
                    double top = boxesTop + (boxH - _boxH[i]);
                    _layout.Boxes.Add(new BoxGeom
                    {
                        Rect = new Rect(_xs[i] - w / 2, top, w, _boxH[i]),
                        Text = Text(_diagram.Participants[i].Label, _tf, _text),
                        Kind = _diagram.Participants[i].Kind
                    });
                }

                _layout.LifelineTop = boxesTop + boxH;
                _layout.LifelineBottom = _layout.LifelineTop;
                y = _layout.LifelineTop + _fs * 1.6;

                LayoutItems(_diagram.Items, ref y);

                // 没被 deactivate 收尾的激活条，画到内容底部
                double bottom = y + _fs * 0.4;
                CloseOpenActivations(bottom);
                _layout.LifelineBottom = bottom;

                // 官方时序图在底部再画一份参与者框，上下对称
                for (int i = 0; i < count; i++)
                {
                    double w = _boxW[i];
                    _layout.Boxes.Add(new BoxGeom
                    {
                        Rect = new Rect(_xs[i] - w / 2, bottom, w, _boxH[i]),
                        Text = _layout.Boxes[i].Text,
                        Kind = _diagram.Participants[i].Kind
                    });
                }
                _layout.Height = bottom + boxH;

                for (int i = 0; i < count; i++)
                    _layout.Lifelines.Add(_xs[i]);

                LayoutLegend(margin);
                _layout.Normalize(margin);
                return _layout;
            }

            /// <summary>
            /// 摆图例（legend ... endlegend）。默认放在参与者下方居中，left/right/top 放到对应一侧，
            /// center 跟官方一样是"图内居中"，这里按下方居中处理——压在图上不好看。
            /// </summary>
            private void LayoutLegend(double margin)
            {
                LegendGeom geom = LegendBox.Build(_diagram.Legend, _tf, _text, _fs, _ppd);
                if (geom == null)
                    return;

                double minX = double.MaxValue;
                double maxX = double.MinValue;
                double minY = double.MaxValue;
                foreach (BoxGeom box in _layout.Boxes)
                {
                    minX = Math.Min(minX, box.Rect.Left);
                    maxX = Math.Max(maxX, box.Rect.Right);
                    minY = Math.Min(minY, box.Rect.Top);
                }
                if (minX > maxX)
                    minX = maxX = 0;
                if (minY == double.MaxValue)
                    minY = margin;

                Size size = geom.ContentSize();
                double w = size.Width + geom.Pad * 2;
                double h = size.Height + geom.Pad * 2;
                double gap = _fs * 0.8;
                double x;
                double y;
                switch (_diagram.LegendPlacement)
                {
                    case "top":
                        x = (minX + maxX - w) / 2;
                        // 要躲开图标题，它也在正文上方
                        y = (_layout.Title != null ? _layout.Title.Top : minY) - gap - h;
                        break;
                    case "left":
                        x = minX - gap - w;
                        y = (_layout.Height - h) / 2;
                        break;
                    case "right":
                        x = maxX + gap;
                        y = (_layout.Height - h) / 2;
                        break;
                    default:   // bottom / center
                        x = (minX + maxX - w) / 2;
                        y = _layout.Height + gap;
                        break;
                }
                geom.Rect = new Rect(x, y, w, h);
                _layout.Legend = geom;
            }

            // ── 列位置 ──────────────────────────────────────────────

            private void MeasureBoxes(int count)
            {
                _boxW = new double[count];
                _boxH = new double[count];
                for (int i = 0; i < count; i++)
                {
                    Participant p = _diagram.Participants[i];
                    FormattedText ft = Text(p.Label, _tf, _text);
                    if (p.Kind == ParticipantKind.Actor)
                    {
                        double stickW = _fs * 1.6;
                        _boxW[i] = Math.Max(ft.Width, stickW) + _fs * 0.4;
                        _boxH[i] = StickHeight() + ft.Height + _fs * 0.2;
                    }
                    else if (p.Kind == ParticipantKind.Database)
                    {
                        double dbW = _fs * 2.6;      // 圆柱宽（官方 36/14）
                        _boxW[i] = Math.Max(ft.Width, dbW) + _fs * 0.4;
                        _boxH[i] = CylinderHeight() + ft.Height + _fs * 0.2;
                    }
                    else
                    {
                        _boxW[i] = ft.Width + _fs;
                        _boxH[i] = ft.Height + _fs;
                    }
                }
            }

            private double StickHeight()
            {
                return _fs * 2.0;
            }

            private double CylinderHeight()
            {
                return _fs * 3.3;   // 圆柱高（官方 46/14）
            }

            private void PlaceColumns(int count)
            {
                double[] adjacentLabel = new double[count];
                double[] selfRoom = new double[count];
                AnalyzeMessages(_diagram.Items, adjacentLabel, selfRoom);

                _xs = new double[count];
                _xs[0] = _boxW[0] / 2;
                for (int i = 1; i < count; i++)
                {
                    // 相邻两框别贴住、中间的消息文字要放得下、左边参与者的自消息回折要有地方伸
                    double need = _boxW[i - 1] / 2 + _boxW[i] / 2 + _fs * 1.2;
                    need = Math.Max(need, adjacentLabel[i - 1] + _fs * 1.4);
                    need = Math.Max(need, selfRoom[i - 1]);
                    _xs[i] = _xs[i - 1] + Math.Max(_fs * 2.6, need);
                }
            }

            private void AnalyzeMessages(List<SeqItem> items, double[] adjacentLabel, double[] selfRoom)
            {
                foreach (SeqItem item in items)
                {
                    var message = item as MessageItem;
                    if (message != null)
                    {
                        if (message.From == message.To)
                        {
                            double labelW = message.Text == null ? 0 : Text(message.Text, _tf, _text).Width;
                            double room = _boxW[message.From] / 2 + _fs * 2.4 + labelW + _fs * 0.8;
                            selfRoom[message.From] = Math.Max(selfRoom[message.From], room);
                        }
                        else
                        {
                            int gap = Math.Min(message.From, message.To);
                            if (Math.Abs(message.From - message.To) == 1)
                            {
                                double labelW = message.Text == null ? 0 : Text(message.Text, _tf, _text).Width;
                                adjacentLabel[gap] = Math.Max(adjacentLabel[gap], labelW);
                            }
                        }
                        continue;
                    }
                    var fragment = item as FragmentItem;
                    if (fragment != null)
                    {
                        foreach (FragmentSection section in fragment.Sections)
                            AnalyzeMessages(section.Items, adjacentLabel, selfRoom);
                    }
                }
            }

            // ── 纵向排布 ────────────────────────────────────────────

            private void LayoutItems(List<SeqItem> items, ref double y)
            {
                foreach (SeqItem item in items)
                    LayoutItem(item, ref y);
            }

            private void LayoutItem(SeqItem item, ref double y)
            {
                var message = item as MessageItem;
                if (message != null)
                {
                    LayoutMessage(message, ref y);
                    return;
                }

                var note = item as NoteItem;
                if (note != null)
                {
                    LayoutNote(note, ref y);
                    return;
                }

                var activate = item as ActivateItem;
                if (activate != null)
                {
                    if (activate.Deactivate)
                        PopActivation(activate.Participant, y);
                    else
                        PushActivation(activate.Participant, y);
                    return;
                }

                var fragment = item as FragmentItem;
                if (fragment != null)
                {
                    LayoutFragment(fragment, ref y);
                    return;
                }

                var divider = item as DividerItem;
                if (divider != null)
                {
                    _layout.Dividers.Add(new DividerGeom
                    {
                        Y = y + _fs * 0.6,
                        Text = divider.Text == null ? null : Text(divider.Text, _tf, _text)
                    });
                    y += _fs * 1.8;
                    return;
                }

                var delay = item as DelayItem;
                if (delay != null)
                {
                    _layout.Delays.Add(new DelayGeom
                    {
                        Y = y + _fs * 0.4,
                        Text = delay.Text == null ? null : Text(delay.Text, _tf, _text)
                    });
                    y += _fs * 1.4;
                }
            }

            private void LayoutMessage(MessageItem message, ref double y)
            {
                string label = ComposeLabel(message);
                FormattedText ft = label == null ? null : Text(label, _tf, _text);
                double labelH = ft == null ? 0 : ft.Height;

                var arrow = new ArrowGeom
                {
                    From = message.From,
                    To = message.To,
                    Dashed = message.Dashed,
                    Open = message.OpenHead,
                    Lost = message.LostHead,
                    Label = ft
                };

                if (message.From == message.To)
                {
                    // 自消息：从生命线往右折一个矩形再回来，标签放在折线右侧
                    double top = y + _fs * 0.2;
                    arrow.Y = top;
                    arrow.SelfWidth = _fs * 2.2;
                    arrow.SelfHeight = _fs * 1.0;
                    arrow.LabelTop = top + arrow.SelfHeight - labelH / 2;
                    arrow.MinX = _xs[message.From];
                    arrow.MaxX = _xs[message.From] + arrow.SelfWidth + (ft == null ? 0 : ft.Width) + _fs * 0.4;
                    _layout.Arrows.Add(arrow);
                    y = top + arrow.SelfHeight + _fs * 0.8;
                }
                else
                {
                    arrow.Y = y + labelH + _fs * 0.35;
                    arrow.LabelCenterX = (_xs[message.From] + _xs[message.To]) / 2;
                    arrow.LabelTop = arrow.Y - labelH - _fs * 0.15;
                    arrow.MinX = Math.Min(_xs[message.From], _xs[message.To]);
                    arrow.MaxX = Math.Max(_xs[message.From], _xs[message.To]);
                    if (ft != null)
                    {
                        arrow.MinX = Math.Min(arrow.MinX, arrow.LabelCenterX - ft.Width / 2);
                        arrow.MaxX = Math.Max(arrow.MaxX, arrow.LabelCenterX + ft.Width / 2);
                    }
                    _layout.Arrows.Add(arrow);
                    y = arrow.Y + _fs * 1.15;
                }

                if (message.ActivateTarget)
                    PushActivation(message.To, arrow.Y);
                if (message.DeactivateSource)
                    PopActivation(message.From, arrow.Y);
            }

            /// <summary>autonumber 序号并到消息文字前，跟 PlantUML 的观感一致。</summary>
            private static string ComposeLabel(MessageItem message)
            {
                if (message.Number > 0)
                {
                    string prefix = message.Number.ToString(CultureInfo.InvariantCulture) + "  ";
                    return message.Text == null ? prefix.TrimEnd() : prefix + message.Text;
                }
                return message.Text;
            }

            private void LayoutNote(NoteItem note, ref double y)
            {
                FormattedText ft = Text(note.Text ?? "", _tf, _text);
                double padX = _fs * 0.6;
                double padY = _fs * 0.4;
                double w = ft.Width + padX * 2;
                double h = ft.Height + padY * 2;

                double left;
                if (note.Placement == NotePlacement.Left)
                {
                    left = _xs[note.A] - _fs * 0.6 - w;
                }
                else if (note.Placement == NotePlacement.Right)
                {
                    left = _xs[note.A] + _fs * 0.6;
                }
                else if (note.B >= 0)
                {
                    double a = _xs[note.A];
                    double b = _xs[note.B];
                    w = Math.Max(w, Math.Abs(b - a) + _fs * 1.0);
                    left = (a + b) / 2 - w / 2;
                }
                else
                {
                    left = _xs[note.A] - w / 2;
                }

                _layout.Notes.Add(new NoteGeom
                {
                    Rect = new Rect(left, y, w, h),
                    Text = ft
                });
                y += h + _fs * 0.5;
            }

            private void LayoutFragment(FragmentItem fragment, ref double y)
            {
                double frameTop = y;
                double headerH = _fs * 1.4;
                double pad = _fs * 0.5;

                FormattedText kind = Text(fragment.Kind, _tf, _text);
                var frame = new FrameGeom { Kind = kind };
                int noteStart = _layout.Notes.Count;

                double innerY = frameTop + headerH + _fs * 0.3;
                for (int s = 0; s < fragment.Sections.Count; s++)
                {
                    FragmentSection section = fragment.Sections[s];
                    var geom = new FrameSectionGeom
                    {
                        Label = section.Label == null ? null : Text("[" + section.Label + "]", _tf, _text)
                    };
                    if (s == 0)
                    {
                        geom.HasDivider = false;
                        geom.Y = frameTop + (_fs * 0.7 - (geom.Label == null ? 0 : geom.Label.Height / 2));
                    }
                    else
                    {
                        geom.HasDivider = true;
                        geom.DividerY = innerY;
                        geom.Y = innerY + _fs * 0.15;
                        innerY += _fs * 1.5;
                    }
                    frame.Sections.Add(geom);
                    LayoutItems(section.Items, ref innerY);
                }

                double frameBottom = innerY + _fs * 0.4;

                int min = int.MaxValue;
                int max = int.MinValue;
                InvolvedRange(fragment, ref min, ref max);
                if (min > max)
                {
                    min = 0;
                    max = _diagram.Participants.Count - 1;
                }
                double left = _xs[min] - _fs * 1.0;
                double right = _xs[max] + _fs * 1.0;
                // 框右端要放得下分区标签（第 0 段排在关键字标签页右边），否则会被裁掉
                double tabW = kind.Width + _fs * 1.2;
                for (int s = 0; s < frame.Sections.Count; s++)
                {
                    FormattedText lbl = frame.Sections[s].Label;
                    if (lbl == null)
                        continue;
                    double start = left + (s == 0 ? tabW : 0) + _fs * 0.6;
                    right = Math.Max(right, start + lbl.Width + _fs * 0.3);
                }
                // 框里的注释可能伸到参与者范围外，把框撑开一点免得压住
                for (int i = noteStart; i < _layout.Notes.Count; i++)
                {
                    left = Math.Min(left, _layout.Notes[i].Rect.Left - _fs * 0.3);
                    right = Math.Max(right, _layout.Notes[i].Rect.Right + _fs * 0.3);
                }

                frame.Rect = new Rect(left, frameTop, right - left, frameBottom - frameTop);
                _layout.Frames.Add(frame);
                y = frameBottom + _fs * 0.8;
            }

            /// <summary>分组框里涉及到的参与者序号范围，框就画到那几条生命线为止。</summary>
            private void InvolvedRange(SeqItem item, ref int min, ref int max)
            {
                var message = item as MessageItem;
                if (message != null)
                {
                    min = Math.Min(min, Math.Min(message.From, message.To));
                    max = Math.Max(max, Math.Max(message.From, message.To));
                    return;
                }
                var note = item as NoteItem;
                if (note != null)
                {
                    min = Math.Min(min, note.A);
                    max = Math.Max(max, note.A);
                    if (note.B >= 0)
                    {
                        min = Math.Min(min, note.B);
                        max = Math.Max(max, note.B);
                    }
                    return;
                }
                var activate = item as ActivateItem;
                if (activate != null)
                {
                    min = Math.Min(min, activate.Participant);
                    max = Math.Max(max, activate.Participant);
                    return;
                }
                var fragment = item as FragmentItem;
                if (fragment != null)
                {
                    foreach (FragmentSection section in fragment.Sections)
                    {
                        foreach (SeqItem child in section.Items)
                            InvolvedRange(child, ref min, ref max);
                    }
                }
            }

            // ── 激活条 ──────────────────────────────────────────────

            private void PushActivation(int participant, double y)
            {
                int depth = 0;
                foreach (Activation open in _open)
                {
                    if (open.Participant == participant)
                        depth++;
                }
                _open.Add(new Activation { Participant = participant, StartY = y, Depth = depth });
            }

            private void PopActivation(int participant, double y)
            {
                for (int i = _open.Count - 1; i >= 0; i--)
                {
                    if (_open[i].Participant != participant)
                        continue;
                    Activation open = _open[i];
                    _open.RemoveAt(i);
                    AddBar(open, y);
                    return;
                }
            }

            private void CloseOpenActivations(double y)
            {
                // 从后往前收，嵌套的激活条也是内层先结束
                for (int i = _open.Count - 1; i >= 0; i--)
                    AddBar(_open[i], y);
                _open.Clear();
            }

            private void AddBar(Activation activation, double endY)
            {
                double baseWidth = _fs * 0.55;
                double step = _fs * 0.18;
                double width = Math.Max(_fs * 0.2, baseWidth - activation.Depth * step);
                double x = _xs[activation.Participant] - width / 2;
                double top = Math.Min(activation.StartY, endY);
                double height = Math.Max(_fs * 0.2, Math.Abs(endY - activation.StartY));
                _layout.Bars.Add(new BarGeom { Rect = new Rect(x, top, width, height) });
            }

            // ── 文本 ────────────────────────────────────────────────

            private FormattedText Text(string text, Typeface typeface, Brush brush)
            {
                return new FormattedText(
                    text ?? string.Empty,
                    CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    _fs,
                    brush,
                    _ppd);
            }
        }
    }
}

