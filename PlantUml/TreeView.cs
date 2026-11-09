using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace MarkdownKit.PlantUml
{
    /// <summary>
    /// 画一张树形图（思维导图 / WBS / JSON / YAML）。
    /// 思维导图和 JSON/YAML 是横着长的树：圆角框，父子之间用一段平滑曲线连；
    /// WBS 是竖着长的树：矩形框，父子之间走折线（下 → 横 → 下）。
    /// JSON/YAML 的节点是"键 | 值"两列表格，嵌套对象从值格用虚线箭头连出去。
    /// </summary>
    internal sealed class TreeView : FrameworkElement, IZoomableView, ISelectableTextView
    {
        /// <summary>两侧要扣掉的宽度：预览区左右内边距加竖直滚动条。</summary>
        private const double SideMargin = 40;

        /// <summary>手动缩放的上下限，相对原始尺寸。下限放得很低，好让大文件的自适应倍率还能再缩小。</summary>
        private const double MinZoom = 0.01;
        private const double MaxZoom = 4;

        /// <summary>JSON / YAML 名称列最多占可用宽度的比例，超出折行。</summary>
        private const double KeyWidthRatio = 0.2;

        /// <summary>JSON / YAML 值列最多占可用宽度的比例，超出折行。</summary>
        private const double ValueWidthRatio = 0.3;

        /// <summary>布局按多宽算的（表格折行上限跟着它走）；宽度变了要重排。</summary>
        private const double RelayoutThreshold = 8;

        /// <summary>自绘选区的底色；半透明，压在文字下面也看得清。</summary>
        private static readonly Brush SelectionFill = CreateSelectionFill();

        private readonly TreeDiagram _diagram;
        private readonly double _fontSize;
        private TreeLayout _layout;
        private double _layoutLimit = double.NaN;
        private double _scale = 1;
        /// <summary>固定倍率（相对原始尺寸）；0 表示按预览区宽度自适应。</summary>
        private double _zoom;

        /// <summary>可选择的文字块，按版面从上到下、从左到右排好，供命中与复制使用。</summary>
        private readonly List<TextItem> _items = new List<TextItem>();
        /// <summary>所有文字块拼成的全文；块之间用制表符（同一行）或换行分隔。</summary>
        private string _flatText = string.Empty;
        /// <summary>选区两端在 _flatText 里的偏移；-1 表示没有选区。</summary>
        private int _selAnchor = -1;
        private int _selFocus = -1;
        private bool _selecting;

        private readonly Brush _fill;
        private readonly Brush _border;
        private readonly Pen _borderPen;
        private readonly Pen _linePen;

        /// <summary>一块可选择的文字：绘制原点、命中区域，以及在全文里的起始位置。</summary>
        private sealed class TextItem
        {
            public FormattedText Text;
            public Rect Area;
            public Point Origin;
            /// <summary>同一行 / 同一个节点共用组号：组内复制用制表符分隔，组间用换行。</summary>
            public int Group;
            public int Order;
            public int Start;
            public Rect[] CharRects;

            public int Length
            {
                get { return Text.Text.Length; }
            }
        }

        public static readonly DependencyProperty AvailableWidthProperty =
            DependencyProperty.Register(nameof(AvailableWidth), typeof(double), typeof(TreeView),
                new FrameworkPropertyMetadata(double.PositiveInfinity,
                    FrameworkPropertyMetadataOptions.AffectsMeasure));

        public double AvailableWidth
        {
            get { return (double)GetValue(AvailableWidthProperty); }
            set { SetValue(AvailableWidthProperty, value); }
        }

        public double Zoom
        {
            get { return _zoom; }
            set
            {
                double zoom = value <= 0
                    ? 0
                    : Math.Max(MinZoom, Math.Min(MaxZoom, value));
                if (Math.Abs(zoom - _zoom) < 1e-6)
                    return;
                _zoom = zoom;
                // 倍率只影响显示尺寸，不用重排：不动 _layout，只重新测量 / 绘制
                InvalidateMeasure();
                InvalidateVisual();
            }
        }

        public double ScalePercent
        {
            get { return _scale * 100; }
        }

        public double DisplayWidth
        {
            get { return _layout == null ? 0 : _layout.Width * _scale; }
        }

        public void ZoomIn()
        {
            Zoom = _scale * 1.25;
        }

        public void ZoomOut()
        {
            Zoom = _scale / 1.25;
        }

        public void ActualSize()
        {
            Zoom = 1;
        }

        public bool HasSelection
        {
            get
            {
                int lo;
                int hi;
                return GetSelectionRange(out lo, out hi);
            }
        }

        public string SelectedText
        {
            get
            {
                int lo;
                int hi;
                if (!GetSelectionRange(out lo, out hi))
                    return string.Empty;
                // 折行时插进去的零宽空格不应该跟着复制出去
                return _flatText.Substring(lo, hi - lo).Replace(Zws.ToString(), "");
            }
        }

        public void SelectAll()
        {
            if (_items.Count == 0 || _flatText.Length == 0)
                return;
            _selAnchor = 0;
            _selFocus = _flatText.Length;
            InvalidateVisual();
        }

        public void ClearSelection()
        {
            if (_selAnchor < 0 && _selFocus < 0)
                return;
            _selAnchor = -1;
            _selFocus = -1;
            InvalidateVisual();
        }

        public TreeView(TreeDiagram diagram, double fontSize, FrameworkElement host)
        {
            _diagram = diagram;
            _fontSize = fontSize;
            HorizontalAlignment = HorizontalAlignment.Center;
            // 自绘文字要能收到键盘和鼠标，才能像文档一样选中、复制
            Focusable = true;
            FocusVisualStyle = null;
            // 放大后树比预览区宽，获得焦点时外层滚动视图会把整棵树滚进视野，
            // 把刚点中的位置挪走；滚动交给外层滚动条，这里一律不接
            AddHandler(RequestBringIntoViewEvent,
                new RequestBringIntoViewEventHandler((s, e) => e.Handled = true), true);

            _fill = MarkdownTheme.Background;
            _border = MarkdownTheme.Markdown.Text;

            _borderPen = Frozen(new Pen(_border, 1.2));
            _linePen = Frozen(new Pen(_border, 1.2));

            if (host != null)
            {
                if (host.ActualWidth > 0)
                    AvailableWidth = host.ActualWidth;
                BindingOperations.SetBinding(this, AvailableWidthProperty,
                    new Binding("ActualWidth") { Source = host });
            }
        }

        private static Pen Frozen(Pen pen)
        {
            pen.Freeze();
            return pen;
        }

        private static Brush CreateSelectionFill()
        {
            Color color = SystemColors.HighlightColor;
            var brush = new SolidColorBrush(Color.FromArgb(0x64, color.R, color.G, color.B));
            brush.Freeze();
            return brush;
        }

        private double U(double v)
        {
            return v * _fontSize / 14.0;
        }

        private bool IsTable
        {
            get { return _diagram.Kind.IsTableKind(); }
        }

        private void EnsureLayout()
        {
            if (_layout == null)
                BuildLayout(_layoutLimit);
        }

        /// <summary>按给定可用宽度重排：表格的键 / 值列分别以 20% / 30% 封顶，超出折行。</summary>
        private void BuildLayout(double limit)
        {
            double ppd = 1.0;
            try
            {
                ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            }
            catch (Exception)
            {
                ppd = 1.0;
            }
            _layoutLimit = limit;
            bool capped = !double.IsInfinity(limit) && limit > 0;
            _layout = TreeLayoutBuilder.Build(_diagram, _fontSize, ppd,
                capped ? limit * KeyWidthRatio : double.PositiveInfinity,
                capped ? limit * ValueWidthRatio : double.PositiveInfinity);
            BuildTextItems();
        }

        /// <summary>
        /// 把版面里的文字块收成一份列表（并拼出全文），供命中测试、选区高亮和复制使用。
        /// 绘制也走这份列表，保证高亮和文字用的是同一套位置。
        /// </summary>
        private void BuildTextItems()
        {
            var items = new List<TextItem>();
            if (_layout.Kind.IsTableKind())
            {
                int group = 0;
                foreach (TreeGeom row in _layout.Rows)
                {
                    double padX = U(8);
                    AddTextItem(items, row.Text,
                        new Rect(row.Rect.X, row.Rect.Y, row.KeyW, row.Rect.Height),
                        TextAlignment.Right, padX, group, 0);
                    if (row.Note != null)
                    {
                        AddTextItem(items, row.Note,
                            new Rect(row.Rect.X + row.KeyW, row.Rect.Y, row.Rect.Width - row.KeyW, row.NoteH),
                            TextAlignment.Left, padX, group, 1);
                        AddTextItem(items, row.Value,
                            new Rect(row.Rect.X + row.KeyW, row.Rect.Y + row.NoteH,
                                row.Rect.Width - row.KeyW, row.Rect.Height - row.NoteH),
                            TextAlignment.Left, padX, group, 2);
                    }
                    else
                    {
                        AddTextItem(items, row.Value,
                            new Rect(row.Rect.X + row.KeyW, row.Rect.Y, row.Rect.Width - row.KeyW, row.Rect.Height),
                            TextAlignment.Left, padX, group, 1);
                    }
                    group++;
                }
            }
            else
            {
                int group = 0;
                foreach (TreeGeom node in _layout.Nodes)
                    AddTextItem(items, node.Text, node.Rect, TextAlignment.Center, 0, group++, 0);
            }

            items.Sort(CompareTextItems);

            var flat = new StringBuilder();
            for (int i = 0; i < items.Count; i++)
            {
                TextItem item = items[i];
                if (i > 0)
                    flat.Append(item.Group == items[i - 1].Group ? '\t' : '\n');
                item.Start = flat.Length;
                flat.Append(item.Text.Text);
            }

            _items.Clear();
            _items.AddRange(items);
            _flatText = flat.ToString();
            // 文字重排后旧的偏移作废
            _selAnchor = -1;
            _selFocus = -1;
        }

        private void AddTextItem(List<TextItem> items, FormattedText text, Rect area,
            TextAlignment align, double padX, int group, int order)
        {
            if (text == null || text.Text.Length == 0)
                return;
            double x;
            if (align == TextAlignment.Right)
            {
                // 键带 TextAlignment.Right：WPF 是在「MaxTextWidth 宽的对齐框」里靠右，
                // 所以起点要按对齐框算。按文字实际宽度算会把短键再往右推一截，压进值列
                double box = double.IsInfinity(text.MaxTextWidth) ? text.Width : text.MaxTextWidth;
                x = area.Right - padX - box;
            }
            else if (align == TextAlignment.Center)
            {
                x = area.X + (area.Width - text.Width) / 2;
            }
            else
            {
                x = area.X + padX;
            }
            items.Add(new TextItem
            {
                Text = text,
                Area = area,
                Origin = new Point(x, area.Y + (area.Height - text.Height) / 2),
                Group = group,
                Order = order
            });
        }

        private static int CompareTextItems(TextItem a, TextItem b)
        {
            int compare = a.Area.Top.CompareTo(b.Area.Top);
            if (compare != 0)
                return compare;
            compare = a.Area.Left.CompareTo(b.Area.Left);
            if (compare != 0)
                return compare;
            return a.Order.CompareTo(b.Order);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            double limit = double.PositiveInfinity;
            if (!double.IsInfinity(availableSize.Width))
                limit = availableSize.Width;
            if (!double.IsInfinity(AvailableWidth))
                limit = Math.Min(limit, Math.Max(0, AvailableWidth - SideMargin));

            // 表格的折行宽度取决于可用宽度，窗口缩放后要按新宽度重排；
            // 思维导图 / WBS 的几何跟宽度无关，只排一次，之后靠 _scale 缩放
            if (_layout == null || (IsTable && Math.Abs(limit - _layoutLimit) > RelayoutThreshold))
            {
                BuildLayout(limit);
                // 测量阶段换了布局要主动重绘：不加这一句，画面还停在换布局前的那一帧
                InvalidateVisual();
            }

            double width = _layout.Width;
            double height = _layout.Height;
            // 手动倍率优先；否则按可用宽度自适应（只缩不放，小图保持原始尺寸）
            _scale = _zoom > 0
                ? _zoom
                : (!double.IsInfinity(limit) && limit > 0 && width > limit ? limit / width : 1);
            return new Size(width * _scale, height * _scale);
        }

        protected override void OnRender(DrawingContext dc)
        {
            EnsureLayout();
            if (_scale != 1)
                dc.PushTransform(new ScaleTransform(_scale, _scale));

            DrawEdges(dc);
            if (_layout.Kind.IsTableKind())
                DrawTables(dc);
            else
            {
                foreach (TreeGeom geom in _layout.Nodes)
                    DrawNode(dc, geom);
            }

            // 选区压在底色 / 边线之上、文字之下
            DrawSelection(dc);
            foreach (TextItem item in _items)
                dc.DrawText(item.Text, item.Origin);

            if (_layout.Title != null)
            {
                FormattedText title = _layout.Title.Text;
                dc.DrawText(title, new Point(_layout.Title.CenterX - title.Width / 2, _layout.Title.Top));
            }

            if (_scale != 1)
                dc.Pop();
        }

        // ── 选择 ────────────────────────────────────────────────

        /// <summary>鼠标坐标换成布局坐标：绘制时整体乘过 _scale，命中要除掉。</summary>
        private Point ToLayout(Point point)
        {
            double scale = _scale <= 0 ? 1 : _scale;
            return new Point(point.X / scale, point.Y / scale);
        }

        private bool IsOverText(Point point)
        {
            Point layoutPoint = ToLayout(point);
            foreach (TextItem item in _items)
            {
                Rect area = item.Area;
                if (layoutPoint.X >= area.Left && layoutPoint.X <= area.Right
                    && layoutPoint.Y >= area.Top && layoutPoint.Y <= area.Bottom)
                    return true;
            }
            return false;
        }

        /// <summary>布局坐标 → 全文偏移。落在文字块外时取最近的一块，拖动时当夹取用。</summary>
        private int HitTestTextPosition(Point point)
        {
            if (_items.Count == 0)
                return -1;

            TextItem best = null;
            double bestDistance = double.MaxValue;
            foreach (TextItem item in _items)
            {
                Rect area = item.Area;
                if (point.X >= area.Left && point.X <= area.Right
                    && point.Y >= area.Top && point.Y <= area.Bottom)
                {
                    best = item;
                    break;
                }
                double dx = point.X < area.Left ? area.Left - point.X
                    : (point.X > area.Right ? point.X - area.Right : 0);
                double dy = point.Y < area.Top ? area.Top - point.Y
                    : (point.Y > area.Bottom ? point.Y - area.Bottom : 0);
                double distance = dx * dx + dy * dy;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = item;
                }
            }
            if (best == null)
                return -1;
            return best.Start + IndexInItem(best, point.X, point.Y);
        }

        /// <summary>块内坐标 → 块内字符下标：先找最近的一视觉行，再按字符中点找位置。</summary>
        private int IndexInItem(TextItem item, double x, double y)
        {
            Rect[] rects = CharRects(item);
            if (rects.Length == 0)
                return 0;

            double lineTop = 0;
            double bestDistance = double.MaxValue;
            for (int i = 0; i < rects.Length; i++)
            {
                Rect rect = rects[i];
                if (rect.Height <= 0)
                    continue;
                double dy = y < rect.Top ? rect.Top - y : (y > rect.Bottom ? y - rect.Bottom : 0);
                if (dy < bestDistance - 1e-6)
                {
                    bestDistance = dy;
                    lineTop = rect.Top;
                }
            }

            int lineStart = -1;
            int lineEnd = -1;
            for (int i = 0; i < rects.Length; i++)
            {
                Rect rect = rects[i];
                if (rect.Height <= 0 || Math.Abs(rect.Top - lineTop) > 0.5)
                    continue;
                if (lineStart < 0)
                    lineStart = i;
                lineEnd = i;
            }
            if (lineStart < 0)
                return rects.Length;

            for (int i = lineStart; i <= lineEnd; i++)
            {
                Rect rect = rects[i];
                if (x < rect.Left + rect.Width / 2)
                    return i;
            }
            return lineEnd + 1;
        }

        /// <summary>单个字符的矩形；改字号或重排后自动失效（随块重建）。</summary>
        private Rect[] CharRects(TextItem item)
        {
            if (item.CharRects != null)
                return item.CharRects;
            int length = item.Text.Text.Length;
            var rects = new Rect[length];
            for (int i = 0; i < length; i++)
            {
                Geometry geo = item.Text.BuildHighlightGeometry(item.Origin, i, 1);
                rects[i] = geo == null ? Rect.Empty : geo.Bounds;
            }
            item.CharRects = rects;
            return rects;
        }

        private TextItem ItemAt(int position)
        {
            foreach (TextItem item in _items)
            {
                if (position >= item.Start && position < item.Start + item.Length)
                    return item;
            }
            return null;
        }

        private void SelectWordAt(int position)
        {
            TextItem item = ItemAt(position);
            if (item == null)
                return;
            string text = item.Text.Text;
            int index = Math.Max(0, Math.Min(text.Length - 1, position - item.Start));
            if (index < 0)
                return;

            // 折行时插进字符之间的零宽空格不参与词边界判断，但选区仍要包含它们
            if (text[index] == Zws)
            {
                int left = index - 1;
                while (left >= 0 && text[left] == Zws)
                    left--;
                if (left >= 0)
                    index = left;
                else
                {
                    int right = index + 1;
                    while (right < text.Length && text[right] == Zws)
                        right++;
                    index = Math.Min(text.Length - 1, right);
                }
            }

            int start = index;
            int end = index + 1;
            if (IsWordChar(text[index]))
            {
                while (start > 0)
                {
                    int p = start - 1;
                    while (p >= 0 && text[p] == Zws)
                        p--;
                    if (p < 0 || !IsWordChar(text[p]))
                        break;
                    start = p;
                }
                while (end < text.Length)
                {
                    int p = end;
                    while (p < text.Length && text[p] == Zws)
                        p++;
                    if (p >= text.Length || !IsWordChar(text[p]))
                        break;
                    end = p + 1;
                }
            }
            _selAnchor = item.Start + start;
            _selFocus = item.Start + end;
            InvalidateVisual();
        }

        /// <summary>TreeLayout 折行时在字符间插的零宽空格。</summary>
        private const char Zws = (char)0x200B;

        private static bool IsWordChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_';
        }

        private bool GetSelectionRange(out int lo, out int hi)
        {
            lo = 0;
            hi = 0;
            if (_selAnchor < 0 || _selFocus < 0)
                return false;
            lo = Math.Min(_selAnchor, _selFocus);
            hi = Math.Max(_selAnchor, _selFocus);
            return hi > lo && hi <= _flatText.Length;
        }

        private void DrawSelection(DrawingContext dc)
        {
            int lo;
            int hi;
            if (!GetSelectionRange(out lo, out hi))
                return;
            foreach (TextItem item in _items)
            {
                int start = Math.Max(lo, item.Start);
                int end = Math.Min(hi, item.Start + item.Length);
                if (end <= start)
                    continue;
                Geometry geo = item.Text.BuildHighlightGeometry(item.Origin, start - item.Start, end - start);
                if (geo != null && !geo.Bounds.IsEmpty)
                    dc.DrawGeometry(SelectionFill, null, geo);
            }
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (e.Handled)
                return;

            // 先取坐标再聚焦：聚焦可能引起外层滚动，先取才是用户点的那一点
            Point point = e.GetPosition(this);
            Focus();
            if (!IsOverText(point))
            {
                ClearSelection();
                return;
            }

            int position = HitTestTextPosition(ToLayout(point));
            if (position < 0)
                return;

            if (e.ClickCount >= 2)
            {
                SelectWordAt(position);
                e.Handled = true;
                return;
            }

            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 && _selAnchor >= 0)
                _selFocus = position;
            else
            {
                _selAnchor = position;
                _selFocus = position;
            }
            _selecting = true;
            CaptureMouse();
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Point point = e.GetPosition(this);
            if (_selecting)
            {
                int position = HitTestTextPosition(ToLayout(point));
                if (position >= 0 && position != _selFocus)
                {
                    _selFocus = position;
                    InvalidateVisual();
                }
                return;
            }
            Cursor = IsOverText(point) ? Cursors.IBeam : Cursors.Arrow;
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (!_selecting)
                return;
            _selecting = false;
            ReleaseMouseCapture();
            // 只是点了一下没拖动：不留零长度选区
            if (_selAnchor == _selFocus)
                ClearSelection();
            e.Handled = true;
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            _selecting = false;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (ctrl && e.Key == Key.A)
            {
                SelectAll();
                e.Handled = true;
            }
            else if (ctrl && e.Key == Key.C)
            {
                CopySelection();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && HasSelection)
            {
                ClearSelection();
                e.Handled = true;
            }
        }

        private void CopySelection()
        {
            string text = SelectedText;
            if (text.Length == 0)
                return;
            // 剪贴板可能被别的程序短暂占用（CLIPBRD_E_CANT_OPEN）：
            // 重试几次，别把这个可恢复的意外抛给宿主
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    Clipboard.SetDataObject(text, true);
                    return;
                }
                catch (System.Runtime.InteropServices.ExternalException)
                {
                    if (attempt >= 4)
                        return;
                    System.Threading.Thread.Sleep(100);
                }
            }
        }

        // ── 连线 ────────────────────────────────────────────────

        private void DrawEdges(DrawingContext dc)
        {
            if (_layout.Kind == TreeKind.Wbs)
            {
                foreach (TreeEdgeGeom edge in _layout.Edges)
                {
                    double bus = (edge.From.Y + edge.To.Y) / 2;
                    var geo = new StreamGeometry();
                    using (StreamGeometryContext ctx = geo.Open())
                    {
                        ctx.BeginFigure(edge.From, false, false);
                        ctx.LineTo(new Point(edge.From.X, bus), true, false);
                        ctx.LineTo(new Point(edge.To.X, bus), true, false);
                        ctx.LineTo(edge.To, true, false);
                    }
                    geo.Freeze();
                    dc.DrawGeometry(null, _linePen, geo);
                }
                return;
            }

            bool table = _layout.Kind.IsTableKind();
            Pen pen = table ? _layout.EdgePen : _linePen;
            Brush arrowFill = table ? _layout.EdgeBrush : _border;
            foreach (TreeEdgeGeom edge in _layout.Edges)
            {
                Point from = edge.From;
                Point to = edge.To;
                double dx = Math.Max(U(12), Math.Abs(to.X - from.X) * 0.5);
                var geo = new StreamGeometry();
                using (StreamGeometryContext ctx = geo.Open())
                {
                    ctx.BeginFigure(from, false, false);
                    ctx.BezierTo(new Point(from.X + dx, from.Y), new Point(to.X - dx, to.Y), to, true, false);
                }
                geo.Freeze();
                dc.DrawGeometry(null, pen, geo);
                if (table)
                    DrawArrowHead(dc, to, from, pen, arrowFill);
            }
        }

        private void DrawArrowHead(DrawingContext dc, Point tip, Point from, Pen pen, Brush fill)
        {
            double dx = tip.X - from.X;
            double dy = tip.Y - from.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6)
                return;
            double ux = dx / len;
            double uy = dy / len;
            double px = -uy;
            double py = ux;
            double s = _fontSize * 0.6;
            double half = s * 0.4;
            var tri = new StreamGeometry();
            using (StreamGeometryContext ctx = tri.Open())
            {
                ctx.BeginFigure(tip, true, true);
                ctx.LineTo(new Point(tip.X - ux * s + px * half, tip.Y - uy * s + py * half), true, false);
                ctx.LineTo(new Point(tip.X - ux * s - px * half, tip.Y - uy * s - py * half), true, false);
            }
            tri.Freeze();
            dc.DrawGeometry(fill, pen, tri);
        }

        // ── 节点 ────────────────────────────────────────────────

        /// <summary>JSON / YAML：一个对象一张表，先铺底色、高亮行和格线；文字在选区之后统一画。</summary>
        private void DrawTables(DrawingContext dc)
        {
            TreeTableStyle style = _layout.Table;
            if (style == null)
                return;

            double corner = style.Corner;
            foreach (TreeGeom table in _layout.Nodes)
            {
                Rect r = table.Rect;
                dc.DrawRoundedRectangle(style.Fill, style.Border, r, corner, corner);
                dc.DrawRectangle(style.KeyFill, null, new Rect(r.X, r.Y, table.KeyW, r.Height));
                dc.DrawRoundedRectangle(null, style.Border, r, corner, corner);
            }

            // 高亮铺在整行（键格 + 值格）下面：官方也是整行换底色
            double highlightCorner = U(4);
            foreach (TreeGeom row in _layout.Rows)
            {
                if (row.RowBackground == null)
                    continue;
                Rect r = row.Rect;
                dc.DrawRoundedRectangle(row.RowBackground, null,
                    new Rect(r.X + 1.5, r.Y, Math.Max(0, r.Width - 3), r.Height),
                    highlightCorner, highlightCorner);
            }

            // 竖线必须压在高亮底色之上，否则高亮行整行铺色会把键值分隔线盖掉
            foreach (TreeGeom table in _layout.Nodes)
            {
                Rect r = table.Rect;
                dc.DrawLine(style.Border, new Point(r.X + table.KeyW, r.Top), new Point(r.X + table.KeyW, r.Bottom));
            }

            foreach (TreeGeom row in _layout.Rows)
            {
                Rect r = row.Rect;
                dc.DrawLine(style.Separator, new Point(r.X, r.Bottom), new Point(r.Right, r.Bottom));
                // 说明与值之间只画值列的分隔线，键格保持合并单元格的观感
                if (row.Note != null)
                {
                    double y = r.Y + row.NoteH;
                    dc.DrawLine(style.Separator, new Point(r.X + row.KeyW, y), new Point(r.Right, y));
                }
            }
        }

        private void DrawNode(DrawingContext dc, TreeGeom geom)
        {
            Rect r = geom.Rect;
            bool rounded = _layout.Kind == TreeKind.MindMap;
            if (rounded)
            {
                double corner = Math.Min(U(12), r.Height / 2);
                dc.DrawRoundedRectangle(_fill, _borderPen, r, corner, corner);
            }
            else
            {
                dc.DrawRectangle(_fill, _borderPen, r);
            }
        }
    }
}
