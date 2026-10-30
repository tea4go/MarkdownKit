using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace MarkdownKit.PlantUml
{
    /// <summary>一条泳道（`|名字|`）的几何。</summary>
    internal sealed class ActLane
    {
        public string Name;
        public FormattedText Title;
        public Rect Rect;
    }

    /// <summary>一张活动图算好的几何。</summary>
    internal sealed class ActivityLayout
    {
        public double Width;
        public double Height;
        public TitleGeom Title;

        /// <summary>要画的形状块（活动框、起止圆、菱形、同步条）。</summary>
        public readonly List<ActBlock> Nodes = new List<ActBlock>();

        /// <summary>所有连线。</summary>
        public readonly List<ActWire> Wires = new List<ActWire>();

        /// <summary>泳道；没写 `|泳道|` 时为空。</summary>
        public readonly List<ActLane> Lanes = new List<ActLane>();
    }

    /// <summary>
    /// 由块树算出几何。活动图是自上而下的流程图：
    /// Seq 把条目竖着摞起来，If 把两支并排摆在判断菱形下面、底下再汇合，
    /// Fork 把分支并排摆在两条同步粗条之间，While / Repeat 在侧面绕一条回边。
    /// 常量按官方 14 号字下的像素量出来，再经 U() 换算。
    /// </summary>
    internal static class ActivityLayoutBuilder
    {
        public static ActivityLayout Build(ActivityDiagram diagram, double fontSize, double pixelsPerDip)
        {
            return new Impl(diagram, fontSize, pixelsPerDip).Build();
        }

        private sealed class Impl
        {
            private readonly ActivityDiagram _diagram;
            private readonly double _fs;
            private readonly double _ppd;
            private readonly Typeface _tf;
            private readonly Typeface _tfBold;
            private readonly Brush _text;
            private readonly ActivityLayout _layout = new ActivityLayout();

            private double U(double v)
            {
                return v * _fs / 14.0;
            }

            /// <summary>上下相邻元素之间的空隙。</summary>
            private double VGap { get { return U(20); } }

            /// <summary>菱形和它下面那一层之间的空隙。</summary>
            private double DGap { get { return U(14); } }

            /// <summary>if 两支之间的横向空隙。</summary>
            private double HGapIf { get { return U(21); } }

            /// <summary>fork 分支之间的横向空隙。</summary>
            private double HGapFork { get { return U(28); } }

            /// <summary>fork 同步条两端留的边距。</summary>
            private double ForkPad { get { return U(12); } }

            /// <summary>同步条厚度。</summary>
            private double BarThick { get { return U(6); } }

            /// <summary>回边绕出去的横向余量。</summary>
            private double LoopGutter { get { return U(22); } }

            public Impl(ActivityDiagram diagram, double fontSize, double pixelsPerDip)
            {
                _diagram = diagram;
                _fs = fontSize;
                _ppd = pixelsPerDip <= 0 ? 1.0 : pixelsPerDip;
                _tf = new Typeface(MarkdownTheme.DiagramFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                _tfBold = MarkdownTheme.BoldTypeface(MarkdownTheme.DiagramFamily);
                _text = MarkdownTheme.Markdown.Text;
            }

            public ActivityLayout Build()
            {
                if (_diagram.Root == null)
                    return _layout;

                double margin = _fs * 0.8;
                double top = margin;
                if (!string.IsNullOrEmpty(_diagram.Title))
                {
                    FormattedText title = Text(_diagram.Title, _tfBold, _text);
                    _layout.Title = new TitleGeom { Text = title, Top = top, CenterX = 0 };
                    top += title.Height + _fs * 0.6;
                }

                ActBlock root = _diagram.Root;
                Measure(root);

                if (_diagram.Lanes.Count > 0)
                {
                    // 泳道模式：横向位置由泳道决定，纵向还是照流程往下排
                    MeasureLanes(root);
                    double flowTop = top + LaneTitleHeight + U(10);
                    Place(root, 0, flowTop);

                    double minX = double.MaxValue;
                    foreach (ActBlock node in _layout.Nodes)
                        minX = Math.Min(minX, node.X);
                    if (minX != double.MaxValue && minX < 0)
                    {
                        foreach (ActBlock node in _layout.Nodes)
                            node.X -= minX;
                        foreach (ActWire wire in _layout.Wires)
                        {
                            for (int i = 0; i < wire.Points.Count; i++)
                                wire.Points[i] = new Point(wire.Points[i].X - minX, wire.Points[i].Y);
                            wire.LabelAt = new Point(wire.LabelAt.X - minX, wire.LabelAt.Y);
                        }
                    }

                    double bottom = flowTop;
                    foreach (ActBlock node in _layout.Nodes)
                        bottom = Math.Max(bottom, node.Y + node.H);

                    // 泳道框：从标题下面一直包到内容底部
                    double acc = 0;
                    for (int i = 0; i < _diagram.Lanes.Count; i++)
                    {
                        _layout.Lanes.Add(new ActLane
                        {
                            Name = _diagram.Lanes[i],
                            Title = Text(_diagram.Lanes[i], _tfBold, _text),
                            Rect = new Rect(acc, top, _laneW[i], bottom + margin - top)
                        });
                        acc += _laneW[i];
                    }

                    _layout.Width = acc + margin * 2;
                    _layout.Height = bottom + margin * 2;
                    foreach (ActLane lane in _layout.Lanes)
                        lane.Rect = new Rect(lane.Rect.X + margin, lane.Rect.Y, lane.Rect.Width, lane.Rect.Height);
                    foreach (ActBlock node in _layout.Nodes)
                        node.X += margin;
                    foreach (ActWire wire in _layout.Wires)
                    {
                        for (int i = 0; i < wire.Points.Count; i++)
                            wire.Points[i] = new Point(wire.Points[i].X + margin, wire.Points[i].Y);
                        wire.LabelAt = new Point(wire.LabelAt.X + margin, wire.LabelAt.Y);
                    }
                    if (_layout.Title != null)
                        _layout.Title.CenterX = _layout.Width / 2;
                    return _layout;
                }

                double minX0 = double.MaxValue;
                double minY = double.MaxValue;
                CollectBounds(root, ref minX0, ref minY);
                if (minX0 == double.MaxValue)
                    minX0 = minY = 0;

                Place(root, margin - minX0, top - minY);

                _layout.Width = root.W + margin * 2;
                _layout.Height = top - minY + root.H + margin;
                if (_layout.Title != null)
                    _layout.Title.CenterX = _layout.Width / 2;
                return _layout;
            }

            // ── 泳道 ─────────────────────────────────────────────

            private double[] _laneW;
            private double[] _laneX;
            private double LaneTitleHeight { get { return U(22); } }

            /// <summary>泳道宽度取"泳道名"和"该泳道里最宽的节点"的较大者。</summary>
            private void MeasureLanes(ActBlock root)
            {
                int n = _diagram.Lanes.Count;
                _laneW = new double[n];
                for (int i = 0; i < n; i++)
                    _laneW[i] = Text(_diagram.Lanes[i], _tfBold, _text).Width + U(30);
                LaneWidths(root);
                _laneX = new double[n];
                double acc = 0;
                for (int i = 0; i < n; i++)
                {
                    _laneX[i] = acc;
                    acc += _laneW[i];
                }
            }

            private void LaneWidths(ActBlock b)
            {
                if (b.Kind == ActKind.Action || b.Kind == ActKind.Start || b.Kind == ActKind.Stop
                    || b.Kind == ActKind.End || b.Kind == ActKind.Detach)
                {
                    int lane = Math.Min(Math.Max(b.Lane, 0), _laneW.Length - 1);
                    _laneW[lane] = Math.Max(_laneW[lane], b.W + U(30));
                }
                foreach (ActBlock item in b.Items)
                    LaneWidths(item);
                if (b.Then != null)
                    LaneWidths(b.Then);
                if (b.Else != null)
                    LaneWidths(b.Else);
            }

            private double LaneCenter(int lane)
            {
                int i = Math.Min(Math.Max(lane, 0), _laneX.Length - 1);
                return _laneX[i] + _laneW[i] / 2;
            }

            // ── 量尺寸 ───────────────────────────────────────────

            private void Measure(ActBlock b)
            {
                MeasureShape(b);
                MeasureNote(b);
            }

            private void MeasureShape(ActBlock b)
            {
                switch (b.Kind)
                {
                    case ActKind.Action:
                        b.Ft = Text(b.Text ?? "", _tf, _text);
                        b.W = Math.Max(b.Ft.Width + U(15), U(20));
                        b.H = Math.Max(b.Ft.Height + U(19), U(26));
                        b.ShapeW = b.W;
                        SetInOut(b, b.W / 2, 0, b.W / 2, b.H);
                        return;

                    case ActKind.Start:
                        b.W = b.H = U(20);
                        b.ShapeW = b.W;
                        SetInOut(b, b.W / 2, b.H, b.W / 2, b.H);
                        return;

                    case ActKind.Stop:
                        b.W = b.H = U(22);
                        b.ShapeW = b.W;
                        SetInOut(b, b.W / 2, 0, b.W / 2, b.H);
                        return;

                    case ActKind.End:
                        b.W = U(28);
                        b.H = U(24);
                        b.ShapeW = b.W;
                        SetInOut(b, b.W / 2, 0, b.W / 2, b.H);
                        return;

                    case ActKind.Detach:
                        b.W = U(20);
                        b.H = U(8);
                        b.ShapeW = b.W;
                        SetInOut(b, b.W / 2, 0, b.W / 2, b.H);
                        return;

                    case ActKind.Seq:
                        MeasureSeq(b);
                        return;

                    case ActKind.If:
                        MeasureIf(b);
                        return;

                    case ActKind.Fork:
                        MeasureFork(b);
                        return;

                    case ActKind.While:
                        MeasureWhile(b);
                        return;

                    case ActKind.Repeat:
                        MeasureRepeat(b);
                        return;
                }
            }

            private void MeasureSeq(ActBlock b)
            {
                double w = 0;
                double h = 0;
                foreach (ActBlock item in b.Items)
                {
                    Measure(item);
                    w = Math.Max(w, item.W);
                    h += item.H;
                }
                h += VGap * Math.Max(0, b.Items.Count - 1);
                b.W = w;
                b.H = Math.Max(h, 0);
                SetInOut(b, b.W / 2, 0, b.W / 2, b.H);
            }

            private void MeasureIf(ActBlock b)
            {
                b.Ft = Text(b.Text ?? "", _tf, _text);
                b.LabelFt = Text(b.Label ?? "", _tf, _text);
                if (b.Else != null && !string.IsNullOrEmpty(b.Else.Label))
                    b.Else.LabelFt = Text(b.Else.Label, _tf, _text);

                if (b.Then == null)
                    b.Then = new ActBlock { Kind = ActKind.Seq };
                if (b.Else == null)
                    b.Else = new ActBlock { Kind = ActKind.Seq };
                Measure(b.Then);
                Measure(b.Else);

                double dH = U(34);
                double mW = U(46);
                double mH = U(24);
                double branchW = b.Then.W + HGapIf + b.Else.W;
                double center = branchW / 2;
                double offsetL = center - b.Then.W / 2;
                double offsetR = branchW - center - b.Else.W / 2;
                double dW = Math.Max(b.Ft.Width + U(24), Math.Max(offsetL, offsetR) * 2);

                b.W = Math.Max(branchW, Math.Max(dW, mW));
                b.H = dH + DGap + Math.Max(b.Then.H, b.Else.H) + DGap + mH;
                SetInOut(b, b.W / 2, 0, b.W / 2, b.H);
            }

            private void MeasureFork(ActBlock b)
            {
                double w = 0;
                double h = 0;
                foreach (ActBlock branch in b.Items)
                {
                    Measure(branch);
                    w += branch.W;
                    h = Math.Max(h, branch.H);
                }
                w += HGapFork * Math.Max(0, b.Items.Count - 1) + ForkPad * 2;
                b.W = w;
                b.H = BarThick * 2 + VGap * 2 + h;
                SetInOut(b, b.W / 2, 0, b.W / 2, b.H);
            }

            private void MeasureWhile(ActBlock b)
            {
                b.Ft = Text(b.Text ?? "", _tf, _text);
                b.LabelFt = Text(b.Label ?? "", _tf, _text);
                b.ExitFt = Text(b.ExitLabel ?? "", _tf, _text);
                if (b.Then == null)
                    b.Then = new ActBlock { Kind = ActKind.Seq };
                Measure(b.Then);

                double dH = U(34);
                double dW = Math.Max(b.Ft.Width + U(24), U(40));
                b.W = Math.Max(dW, b.Then.W) + LoopGutter * 2;
                // 菱形和循环体之间要留出"是"标签的高度，否则标签压在循环体上
                b.H = dH + DGap + LabelHeight(b.LabelFt) + b.Then.H + VGap;
                SetInOut(b, b.W / 2, 0, b.W / 2, b.H);
            }

            private void MeasureRepeat(ActBlock b)
            {
                b.Ft = Text(b.Text ?? "", _tf, _text);
                b.LabelFt = Text(b.Label ?? "", _tf, _text);
                b.ExitFt = Text(b.ExitLabel ?? "", _tf, _text);
                if (b.Then == null)
                    b.Then = new ActBlock { Kind = ActKind.Seq };
                Measure(b.Then);

                double mW = U(30);
                double mH = U(30);
                double dH = U(34);
                double dW = Math.Max(b.Ft.Width + U(24), U(40));
                b.W = Math.Max(Math.Max(mW, dW), b.Then.W) + LoopGutter * 2;
                b.H = mH + DGap + b.Then.H + DGap + dH + VGap;
                SetInOut(b, b.W / 2, 0, b.W / 2, b.H);
            }

            private void MeasureNote(ActBlock b)
            {
                if (string.IsNullOrEmpty(b.Note) || b.Kind == ActKind.Seq)
                    return;
                if (b.ShapeW <= 0)
                    b.ShapeW = b.W;
                b.NoteFt = Text(b.Note, _tf, _text);
                double w = b.NoteFt.Width + U(16);
                double h = b.NoteFt.Height + U(10);
                b.W = b.ShapeW + U(14) + w;
                b.H = Math.Max(b.H, h);
                // 入口出口仍落在活动框上，注释只占右边的位置
                SetInOut(b, b.ShapeW / 2, 0, b.ShapeW / 2, b.H);
            }

            private static void SetInOut(ActBlock b, double ix, double iy, double ox, double oy)
            {
                b.InX = ix;
                b.InY = iy;
                b.OutX = ox;
                b.OutY = oy;
            }

            /// <summary>标签的高度；没有标签就是 0。</summary>
            private static double LabelHeight(FormattedText label)
            {
                return label != null && label.Text.Length > 0 ? label.Height : 0;
            }

            private void CollectBounds(ActBlock b, ref double minX, ref double minY)
            {
                minX = Math.Min(minX, b.X);
                minY = Math.Min(minY, b.Y);
                foreach (ActBlock item in b.Items)
                    CollectBounds(item, ref minX, ref minY);
                if (b.Then != null)
                    CollectBounds(b.Then, ref minX, ref minY);
                if (b.Else != null)
                    CollectBounds(b.Else, ref minX, ref minY);
            }

            // ── 摆位置 + 连线 ────────────────────────────────────

            private void Place(ActBlock b, double x, double y)
            {
                b.X = x;
                b.Y = y;

                switch (b.Kind)
                {
                    case ActKind.Action:
                    case ActKind.Start:
                    case ActKind.Stop:
                    case ActKind.End:
                    case ActKind.Detach:
                        if (_laneW != null)
                            b.X = LaneCenter(b.Lane) - b.W / 2;
                        SetInOut(b, b.W / 2, 0, b.W / 2, b.H);
                        _layout.Nodes.Add(b);
                        return;

                    case ActKind.Seq:
                        PlaceSeq(b, x, y);
                        AddNoteNode(b, x, y);
                        return;

                    case ActKind.If:
                        PlaceIf(b, x, y);
                        AddNoteNode(b, x, y);
                        return;

                    case ActKind.Fork:
                        PlaceFork(b, x, y);
                        AddNoteNode(b, x, y);
                        return;

                    case ActKind.While:
                        PlaceWhile(b, x, y);
                        AddNoteNode(b, x, y);
                        return;

                    case ActKind.Repeat:
                        PlaceRepeat(b, x, y);
                        return;
                }
            }

            /// <summary>复合块（if / fork / while）自己不画形状，注释得单独挂一个占位节点。</summary>
            private void AddNoteNode(ActBlock b, double x, double y)
            {
                if (b.NoteFt == null)
                    return;
                _layout.Nodes.Add(new ActBlock
                {
                    Kind = ActKind.Note,
                    NoteFt = b.NoteFt,
                    ShapeW = b.ShapeW > 0 ? b.ShapeW : b.W,
                    X = x,
                    Y = y,
                    W = b.W,
                    H = b.H
                });
            }

            private void PlaceSeq(ActBlock b, double x, double y)
            {
                double cy = y;
                ActBlock prev = null;
                foreach (ActBlock item in b.Items)
                {
                    Place(item, x + (b.W - item.W) / 2, cy);
                    if (prev != null)
                        Wire(Exit(prev), Entry(item));
                    prev = item;
                    cy += item.H + VGap;
                }
                // 顺序块的出入口就是首尾两项的出入口（泳道模式下各项 x 不一样，得按实际位置取）
                if (b.Items.Count > 0)
                {
                    Point first = Entry(b.Items[0]);
                    Point last = Exit(b.Items[b.Items.Count - 1]);
                    SetInOut(b, first.X - b.X, first.Y - b.Y, last.X - b.X, last.Y - b.Y);
                }
                else
                {
                    SetInOut(b, b.W / 2, 0, b.W / 2, b.H);
                }
            }

            private void PlaceIf(ActBlock b, double x, double y)
            {
                double dW = Math.Max(b.Ft.Width + U(24), Math.Max(
                    (b.Then.W + HGapIf + b.Else.W) / 2 - b.Then.W / 2,
                    (b.Then.W + HGapIf + b.Else.W) / 2 - b.Else.W / 2) * 2);
                double dH = U(34);
                double cx = x + b.W / 2;
                bool lanes = _laneW != null;
                if (lanes)
                    cx = LaneCenter(b.Lane);

                var diamond = new ActBlock { Kind = ActKind.Diamond, Ft = b.Ft };
                diamond.W = dW;
                diamond.H = dH;
                diamond.X = cx - dW / 2;
                diamond.Y = y;
                _layout.Nodes.Add(diamond);

                double thenY = y + dH + DGap;
                double thenX = lanes ? 0 : x + (b.W - (b.Then.W + HGapIf + b.Else.W)) / 2;
                Place(b.Then, thenX, thenY);
                Place(b.Else, lanes ? 0 : thenX + b.Then.W + HGapIf, thenY);

                double mergeY = thenY + Math.Max(b.Then.H, b.Else.H) + DGap;
                double mW = U(46);
                double mH = U(24);
                var merge = new ActBlock { Kind = ActKind.Merge, W = mW, H = mH, X = cx - mW / 2, Y = mergeY };
                _layout.Nodes.Add(merge);
                SetInOut(b, cx - b.X, 0, cx - b.X, mergeY + mH - b.Y);

                // 菱形左右顶点 → 两支的入口（两支正好摆在对顶点下面，走直线）
                Point left = new Point(diamond.X, y + dH / 2);
                Point right = new Point(diamond.X + dW, y + dH / 2);
                Wire(left, Entry(b.Then));
                Wire(right, Entry(b.Else));

                // 分支标签贴在顶点外侧、菱形上半部
                AddLabel(b.LabelFt, left.X - U(2), y + U(2), true);
                AddLabel(b.Else.LabelFt, right.X + U(2), y + U(2), false);

                // 两支的出口 → 汇合菱形
                Wire(Exit(b.Then), new Point(cx, mergeY + mH / 2));
                Wire(Exit(b.Else), new Point(cx, mergeY + mH / 2));
            }

            private void PlaceFork(ActBlock b, double x, double y)
            {
                bool lanes = _laneW != null;
                if (lanes)
                {
                    x = _laneX[0];
                    b.W = _laneX[_laneX.Length - 1] + _laneW[_laneW.Length - 1] - _laneX[0];
                }
                var top = new ActBlock { Kind = ActKind.Bar, W = b.W, H = BarThick, X = x, Y = y };
                _layout.Nodes.Add(top);

                double branchY = y + BarThick + VGap;
                double maxH = 0;
                foreach (ActBlock branch in b.Items)
                    maxH = Math.Max(maxH, branch.H);

                double cursor = x + ForkPad;
                foreach (ActBlock branch in b.Items)
                {
                    Place(branch, cursor, branchY);
                    Wire(new Point(branch.X + branch.W / 2, y + BarThick),
                        new Point(branch.X + branch.W / 2, branchY));
                    cursor += branch.W + HGapFork;
                }

                double joinY = branchY + maxH + VGap;
                var bottom = new ActBlock { Kind = ActKind.Bar, W = b.W, H = BarThick, X = x, Y = joinY };
                _layout.Nodes.Add(bottom);
                SetInOut(b, x + b.W / 2 - b.X, 0, x + b.W / 2 - b.X, joinY + BarThick - b.Y);

                foreach (ActBlock branch in b.Items)
                    Wire(new Point(branch.X + branch.W / 2, branch.Y + branch.H),
                        new Point(branch.X + branch.W / 2, joinY));
            }

            private void PlaceWhile(ActBlock b, double x, double y)
            {
                double dW = Math.Max(b.Ft.Width + U(24), U(40));
                double dH = U(34);
                double cx = _laneW != null ? LaneCenter(b.Lane) : x + b.W / 2;
                SetInOut(b, cx - b.X, 0, cx - b.X, b.H);

                var diamond = new ActBlock { Kind = ActKind.Diamond, Ft = b.Ft, W = dW, H = dH, X = cx - dW / 2, Y = y };
                _layout.Nodes.Add(diamond);

                double bodyY = y + dH + DGap + LabelHeight(b.LabelFt);
                Place(b.Then, _laneW != null ? 0 : cx - b.Then.W / 2, bodyY);

                // 是：菱形底 → 循环体（标签摆在菱形和循环体之间）
                Wire(new Point(cx, y + dH), Entry(b.Then));
                AddLabel(b.LabelFt, cx + U(6), y + dH + U(3), false);

                // 回边：循环体底 → 右侧 → 菱形右顶点
                double rightX = x + b.W - U(8);
                double bodyBottom = bodyY + b.Then.H;
                Wire(Exit(b.Then), new Point(rightX, bodyBottom + U(8)));
                var back = new ActWire();
                back.Arrow = true;
                back.Points.Add(new Point(rightX, bodyBottom + U(8)));
                back.Points.Add(new Point(rightX, y + dH / 2));
                back.Points.Add(new Point(diamond.X + dW, y + dH / 2));
                _layout.Wires.Add(back);

                // 否：菱形左顶点 → 左侧 → 底部出口
                double leftX = x + U(8);
                var exit = new ActWire();
                exit.Points.Add(new Point(diamond.X, y + dH / 2));
                exit.Points.Add(new Point(leftX, y + dH / 2));
                exit.Points.Add(new Point(leftX, y + b.H));
                exit.Points.Add(new Point(cx, y + b.H));
                _layout.Wires.Add(exit);
                AddLabel(b.ExitFt, diamond.X - U(4), y + U(2), true);
            }

            private void PlaceRepeat(ActBlock b, double x, double y)
            {
                double mW = U(30);
                double mH = U(30);
                double cx = _laneW != null ? LaneCenter(b.Lane) : x + b.W / 2;
                SetInOut(b, cx - b.X, 0, cx - b.X, b.H);

                var entry = new ActBlock { Kind = ActKind.Merge, W = mW, H = mH, X = cx - mW / 2, Y = y };
                _layout.Nodes.Add(entry);

                double bodyY = y + mH + DGap;
                Place(b.Then, _laneW != null ? 0 : cx - b.Then.W / 2, bodyY);

                double dW = Math.Max(b.Ft.Width + U(24), U(40));
                double dH = U(34);
                double dY = bodyY + b.Then.H + DGap;
                var diamond = new ActBlock { Kind = ActKind.Diamond, Ft = b.Ft, W = dW, H = dH, X = cx - dW / 2, Y = dY };
                _layout.Nodes.Add(diamond);

                Wire(new Point(cx, y + mH), Entry(b.Then));
                Wire(Exit(b.Then), new Point(cx, dY));

                // 是：菱形右顶点 → 右侧 → 顶部菱形右顶点
                double rightX = x + b.W - U(8);
                var back = new ActWire();
                back.Points.Add(new Point(diamond.X + dW, dY + dH / 2));
                back.Points.Add(new Point(rightX, dY + dH / 2));
                back.Points.Add(new Point(rightX, y + mH / 2));
                back.Points.Add(new Point(entry.X + mW, y + mH / 2));
                _layout.Wires.Add(back);
                AddLabel(b.LabelFt, rightX + U(2), dY + dH / 2 - b.LabelFt.Height - U(2), false);

                // 否：菱形底 → 出口
                Wire(new Point(cx, dY + dH), new Point(cx, y + b.H));
                AddLabel(b.ExitFt, cx + U(4), dY + dH + U(2), false);
            }

            // ── 连线小工具 ───────────────────────────────────────

            private Point Entry(ActBlock b)
            {
                return new Point(b.X + b.InX, b.Y + b.InY);
            }

            private Point Exit(ActBlock b)
            {
                return new Point(b.X + b.OutX, b.Y + b.OutY);
            }

            /// <summary>两点之间连一条线：竖直就走直线，错开的走"下—横—下"。</summary>
            private void Wire(Point from, Point to)
            {
                var wire = new ActWire();
                wire.Points.Add(from);
                if (Math.Abs(from.X - to.X) < 0.5)
                {
                    wire.Points.Add(to);
                }
                else
                {
                    double midY = (from.Y + to.Y) / 2;
                    wire.Points.Add(new Point(from.X, midY));
                    wire.Points.Add(new Point(to.X, midY));
                    wire.Points.Add(to);
                }
                _layout.Wires.Add(wire);
            }

            private void AddLabel(FormattedText label, double x, double y, bool rightAlign)
            {
                if (label == null || label.Text.Length == 0)
                    return;
                var wire = new ActWire { Arrow = false, Label = label };
                wire.LabelAt = new Point(rightAlign ? x - label.Width : x, y);
                _layout.Wires.Add(wire);
            }

            private FormattedText Text(string text, Typeface typeface, Brush brush)
            {
                return new FormattedText(text ?? string.Empty, CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight, typeface, _fs, brush, _ppd);
            }
        }
    }
}
