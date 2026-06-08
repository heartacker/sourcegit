using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SourceGit.Views
{
    public class CommitGraph : Control
    {
        public static readonly DirectProperty<CommitGraph, Models.CommitGraph> GraphProperty =
            AvaloniaProperty.RegisterDirect<CommitGraph, Models.CommitGraph>(
                nameof(Graph),
                static o => o.Graph,
                static (o, v) => o.Graph = v);

        public Models.CommitGraph Graph
        {
            get => _graph;
            set => SetAndRaise(GraphProperty, ref _graph, value);
        }

        public static readonly DirectProperty<CommitGraph, Models.CommitGraphLayout> LayoutProperty =
            AvaloniaProperty.RegisterDirect<CommitGraph, Models.CommitGraphLayout>(
                nameof(Layout),
                static o => o.Layout,
                static (o, v) => o.Layout = v);

        public Models.CommitGraphLayout Layout
        {
            get => _layout;
            set => SetAndRaise(LayoutProperty, ref _layout, value);
        }

        public static readonly StyledProperty<IBrush> DotBrushProperty =
            AvaloniaProperty.Register<CommitGraph, IBrush>(nameof(DotBrush), Brushes.Transparent);

        public IBrush DotBrush
        {
            get => GetValue(DotBrushProperty);
            set => SetValue(DotBrushProperty, value);
        }

        public static readonly StyledProperty<bool> OnlyHighlightedProperty =
            AvaloniaProperty.Register<CommitGraph, bool>(nameof(OnlyHighlighted), true);

        public bool OnlyHighlighted
        {
            get => GetValue(OnlyHighlightedProperty);
            set => SetValue(OnlyHighlightedProperty, value);
        }

        public static readonly StyledProperty<bool[]> HoveredLineageCommitsProperty =
            AvaloniaProperty.Register<CommitGraph, bool[]>(nameof(HoveredLineageCommits));

        public bool[] HoveredLineageCommits
        {
            get => GetValue(HoveredLineageCommitsProperty);
            set => SetValue(HoveredLineageCommitsProperty, value);
        }

        public static readonly StyledProperty<long> HoveredCommitIndexProperty =
            AvaloniaProperty.Register<CommitGraph, long>(nameof(HoveredCommitIndex), -1);

        public long HoveredCommitIndex
        {
            get => GetValue(HoveredCommitIndexProperty);
            set => SetValue(HoveredCommitIndexProperty, value);
        }


        public override void Render(DrawingContext context)
        {
            base.Render(context);

            if (_graph == null || _layout == null)
                return;

            var startY = _layout.StartY;
            var clipWidth = _layout.ClipWidth;
            var clipHeight = Bounds.Height;
            var rowHeight = _layout.RowHeight;
            var endY = startY + clipHeight + 28;

            using (context.PushClip(new Rect(0, 0, clipWidth, clipHeight)))
            using (context.PushTransform(Matrix.CreateTranslation(0, -startY)))
            {
                DrawCurves(context, _graph, startY, endY, rowHeight);
                DrawAnchors(context, _graph, startY, endY, rowHeight);
            }
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == GraphProperty ||
                change.Property == LayoutProperty ||
                change.Property == DotBrushProperty ||
                change.Property == OnlyHighlightedProperty ||
                change.Property == HoveredLineageCommitsProperty ||
                change.Property == HoveredCommitIndexProperty)
                InvalidateVisual();

            if (change.Property == GraphProperty ||
                change.Property == HoveredCommitIndexProperty ||
                change.Property == HoveredLineageCommitsProperty)
                UpdateHoveredRelated();
        }

        private void UpdateHoveredRelated()
        {
            var graph = Graph;
            if (graph == null)
                return;

            foreach (var line in graph.Paths)
                line.IsHoveredRelated = false;

            var hoveredLineage = HoveredLineageCommits;
            if (hoveredLineage != null)
            {
                foreach (var line in graph.Paths)
                {
                    if (line.StartCommitIndex >= 0 && line.EndCommitIndex >= 0 &&
                        line.StartCommitIndex < hoveredLineage.Length && line.EndCommitIndex < hoveredLineage.Length)
                    {
                        line.IsHoveredRelated = hoveredLineage[line.StartCommitIndex] &&
                                                hoveredLineage[line.EndCommitIndex];
                    }
                }
            }
        }

        private void DrawCurves(DrawingContext context, Models.CommitGraph graph, double top, double bottom, double rowHeight)
        {
            var hoverBold = 2.0;
            var onlyHighlighted = OnlyHighlighted;
            var grayedPen = new Pen(new SolidColorBrush(Colors.Gray, 0.4), Models.CommitGraph.Pens[0].Thickness);
            var hoveredLineage = HoveredLineageCommits;

            foreach (var link in graph.Links)
            {
                var startY = link.Start.Y * rowHeight;
                var endY = link.End.Y * rowHeight;

                if (endY < top)
                    continue;
                if (startY > bottom)
                    break;

                var isLinkInHoveredLineage = hoveredLineage != null &&
                    link.StartCommitIndex >= 0 && link.EndCommitIndex >= 0 &&
                    link.StartCommitIndex < hoveredLineage.Length && link.EndCommitIndex < hoveredLineage.Length &&
                    hoveredLineage[link.StartCommitIndex] &&
                    hoveredLineage[link.EndCommitIndex];

                var pen = Models.CommitGraph.Pens[link.Color];
                if (onlyHighlighted && !link.IsHighlighted)
                    pen = grayedPen;

                if (isLinkInHoveredLineage)
                    pen = new Pen(pen.Brush, pen.Thickness + hoverBold);

                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(new Point(link.Start.X, startY), false);
                    ctx.QuadraticBezierTo(new Point(link.Control.X, link.Control.Y * rowHeight), new Point(link.End.X, endY));
                }

                context.DrawGeometry(null, pen, geo);
            }

            foreach (var line in graph.Paths)
            {
                var last = new Point(line.Points[0].X, line.Points[0].Y * rowHeight);
                var size = line.Points.Count;
                var endY = line.Points[size - 1].Y * rowHeight;

                if (endY < top)
                    continue;
                if (last.Y > bottom)
                    break;

                var geo = new StreamGeometry();
                var pen = Models.CommitGraph.Pens[line.Color];
                if (onlyHighlighted && !line.IsHighlighted)
                    pen = grayedPen;

                if (line.IsHoveredRelated)
                    pen = new Pen(pen.Brush, pen.Thickness + hoverBold);

                using (var ctx = geo.Open())
                {
                    var started = false;
                    var ended = false;
                    for (int i = 1; i < size; i++)
                    {
                        var cur = new Point(line.Points[i].X, line.Points[i].Y * rowHeight);
                        if (cur.Y < top)
                        {
                            last = cur;
                            continue;
                        }

                        if (!started)
                        {
                            ctx.BeginFigure(last, false);
                            started = true;
                        }

                        if (cur.Y > bottom)
                        {
                            cur = new Point(cur.X, bottom);
                            ended = true;
                        }

                        if (cur.X > last.X)
                        {
                            ctx.QuadraticBezierTo(new Point(cur.X, last.Y), cur);
                        }
                        else if (cur.X < last.X)
                        {
                            if (i < size - 1)
                            {
                                var midY = (last.Y + cur.Y) / 2;
                                ctx.CubicBezierTo(new Point(last.X, midY + 4), new Point(cur.X, midY - 4), cur);
                            }
                            else
                            {
                                ctx.QuadraticBezierTo(new Point(last.X, cur.Y), cur);
                            }
                        }
                        else
                        {
                            ctx.LineTo(cur);
                        }

                        if (ended)
                            break;
                        last = cur;
                    }
                }

                context.DrawGeometry(null, pen, geo);
            }
        }

        private void DrawAnchors(DrawingContext context, Models.CommitGraph graph, double top, double bottom, double rowHeight)
        {
            var dotFill = DotBrush;
            var dotFillPen = new Pen(dotFill, 2);
            var onlyHighlighted = OnlyHighlighted;
            var grayedPen = new Pen(Brushes.Gray, Models.CommitGraph.Pens[0].Thickness);
            var hoveredLineage = HoveredLineageCommits;

            for (int i = 0; i < graph.Dots.Count; i++)
            {
                var dot = graph.Dots[i];
                var center = new Point(dot.Center.X, dot.Center.Y * rowHeight);

                if (center.Y < top)
                    continue;
                if (center.Y > bottom)
                    break;

                var isDotInHoveredLineage = hoveredLineage != null && i >= 0 && i < hoveredLineage.Length && hoveredLineage[i];

                var pen = Models.CommitGraph.Pens[dot.Color];
                if (!dot.IsHighlighted && onlyHighlighted)
                    pen = grayedPen;

                if (isDotInHoveredLineage)
                    pen = new Pen(pen.Brush, pen.Thickness + 0.8);

                switch (dot.Type)
                {
                    case Models.CommitGraph.DotType.Head:
                        context.DrawEllipse(dotFill, pen, center, 6, 6);
                        context.DrawEllipse(pen.Brush, null, center, 3, 3);
                        break;
                    case Models.CommitGraph.DotType.Merge:
                        context.DrawEllipse(pen.Brush, null, center, 6, 6);
                        context.DrawLine(dotFillPen, new Point(center.X, center.Y - 3), new Point(center.X, center.Y + 3));
                        context.DrawLine(dotFillPen, new Point(center.X - 3, center.Y), new Point(center.X + 3, center.Y));
                        break;
                    case Models.CommitGraph.DotType.Filter:
                        context.DrawEllipse(pen.Brush, null, center, 7, 7);
                        context.DrawLine(dotFillPen, new Point(center.X, center.Y - 5), new Point(center.X, center.Y + 5));
                        context.DrawLine(dotFillPen, new Point(center.X - 4, center.Y - 3), new Point(center.X + 4, center.Y + 3));
                        context.DrawLine(dotFillPen, new Point(center.X + 4, center.Y - 3), new Point(center.X - 4, center.Y + 3));
                        break;
                    default:
                        context.DrawEllipse(dotFill, pen, center, 3, 3);
                        break;
                }
            }
        }

        private Models.CommitGraph _graph = null;
        private Models.CommitGraphLayout _layout = null;
    }
}
