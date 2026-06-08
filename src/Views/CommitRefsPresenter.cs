using System;
using System.Collections.Generic;
using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SourceGit.Views
{
    public class CommitRefsIconCache
    {
        public static CommitRefsIconCache Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new CommitRefsIconCache();
                return _instance;
            }
        }

        public CommitRefsIconCache()
        {
            _head = LoadIcon("Icons.Head");
            _branch = LoadIcon("Icons.Branch");
            _remote = LoadIcon("Icons.Remote");
            _tag = LoadIcon("Icons.Tag");
        }

        public Geometry GetIcon(Models.DecoratorType type)
        {
            return type switch
            {
                Models.DecoratorType.CurrentBranchHead => _head,
                Models.DecoratorType.CurrentCommitHead => _head,
                Models.DecoratorType.LocalBranchHead => _branch,
                Models.DecoratorType.RemoteBranchHead => _remote,
                Models.DecoratorType.Tag => _tag,
                _ => null,
            };
        }

        private Geometry LoadIcon(string resourceKey)
        {
            var geo = App.Current.FindResource(resourceKey) as StreamGeometry;
            var drawGeo = geo!.Clone();
            var iconBounds = drawGeo.Bounds;
            var translation = Matrix.CreateTranslation(-(Vector)iconBounds.Position);
            var scale = Math.Min(10.0 / iconBounds.Width, 10.0 / iconBounds.Height);
            var transform = translation * Matrix.CreateScale(scale, scale);
            if (drawGeo.Transform == null || drawGeo.Transform.Value == Matrix.Identity)
                drawGeo.Transform = new MatrixTransform(transform);
            else
                drawGeo.Transform = new MatrixTransform(drawGeo.Transform.Value * transform);

            return drawGeo;
        }

        private static CommitRefsIconCache _instance = null;
        private Geometry _head = null;
        private Geometry _branch = null;
        private Geometry _remote = null;
        private Geometry _tag = null;
    }

    public class CommitRefsPresenter : Control
    {
        private class DecoratorRenderItem
        {
            public Geometry Icon { get; set; } = null;
            public FormattedText Label { get; set; } = null;
            public Models.Decorator Decorator { get; set; } = null;
            public bool IsHead { get; set; } = false;
            public bool IsTracked { get; set; } = false;
            public double Width { get; set; } = 0;
            public Rect Bounds { get; set; } = new Rect();
        }

        private class Pill
        {
            public List<DecoratorRenderItem> Locals { get; set; } = new();
            public List<DecoratorRenderItem> RemoteGroup { get; set; } = new();
            public string SharedBranchName { get; set; } = null;
            public DecoratorRenderItem Other { get; set; } = null;
            public IBrush Brush { get; set; } = null;
            public double Width { get; set; } = 0.0;
            public Rect Bounds { get; set; } = new Rect();
        }

        // --- Performance Cache ---
        private static readonly Dictionary<Models.DecoratorType, Geometry> ICON_CACHE = new();
        private static void EnsureIcons()
        {
            if (ICON_CACHE.Count > 0)
                return;
            ICON_CACHE[Models.DecoratorType.CurrentBranchHead] = CommitRefsIconCache.Instance.GetIcon(Models.DecoratorType.CurrentBranchHead);
            ICON_CACHE[Models.DecoratorType.CurrentCommitHead] = CommitRefsIconCache.Instance.GetIcon(Models.DecoratorType.CurrentCommitHead);
            ICON_CACHE[Models.DecoratorType.RemoteBranchHead] = CommitRefsIconCache.Instance.GetIcon(Models.DecoratorType.RemoteBranchHead);
            ICON_CACHE[Models.DecoratorType.Tag] = CommitRefsIconCache.Instance.GetIcon(Models.DecoratorType.Tag);
            ICON_CACHE[Models.DecoratorType.LocalBranchHead] = CommitRefsIconCache.Instance.GetIcon(Models.DecoratorType.LocalBranchHead);
        }

        private FormattedText _pipeText;
        private FormattedText _trackText;
        private FormattedText _colonText;
        private FormattedText _neqText;

        private List<Models.Branch> _lastBranchesRef = null;
        private Dictionary<string, Models.Branch> _branchLookup = null;
        private Dictionary<string, Models.Branch> GetBranchLookup(List<Models.Branch> branches)
        {
            if (branches == null)
                return null;
            if (ReferenceEquals(branches, _lastBranchesRef))
                return _branchLookup;

            var lookup = new Dictionary<string, Models.Branch>(branches.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var b in branches)
            {
                if (b.IsLocal)
                    lookup[b.Name] = b;
            }

            _lastBranchesRef = branches;
            _branchLookup = lookup;
            return lookup;
        }
        // -------------------------

        public static readonly StyledProperty<FontFamily> FontFamilyProperty =
            TextBlock.FontFamilyProperty.AddOwner<CommitRefsPresenter>();

        public FontFamily FontFamily
        {
            get => GetValue(FontFamilyProperty);
            set => SetValue(FontFamilyProperty, value);
        }

        public static readonly StyledProperty<double> FontSizeProperty =
           TextBlock.FontSizeProperty.AddOwner<CommitRefsPresenter>();

        public double FontSize
        {
            get => GetValue(FontSizeProperty);
            set => SetValue(FontSizeProperty, value);
        }

        public static readonly StyledProperty<IBrush> BackgroundProperty =
            AvaloniaProperty.Register<CommitRefsPresenter, IBrush>(nameof(Background), Brushes.Transparent);

        public IBrush Background
        {
            get => GetValue(BackgroundProperty);
            set => SetValue(BackgroundProperty, value);
        }

        public static readonly StyledProperty<IBrush> ForegroundProperty =
            AvaloniaProperty.Register<CommitRefsPresenter, IBrush>(nameof(Foreground), Brushes.White);

        public IBrush Foreground
        {
            get => GetValue(ForegroundProperty);
            set => SetValue(ForegroundProperty, value);
        }

        public static readonly StyledProperty<bool> UseCompactBranchNamesProperty =
            AvaloniaProperty.Register<CommitRefsPresenter, bool>(nameof(UseCompactBranchNames));

        public bool UseCompactBranchNames
        {
            get => GetValue(UseCompactBranchNamesProperty);
            set => SetValue(UseCompactBranchNamesProperty, value);
        }

        public static readonly StyledProperty<bool> UseGraphColorProperty =
            AvaloniaProperty.Register<CommitRefsPresenter, bool>(nameof(UseGraphColor));

        public bool UseGraphColor
        {
            get => GetValue(UseGraphColorProperty);
            set => SetValue(UseGraphColorProperty, value);
        }

        public static readonly StyledProperty<bool> AllowWrapProperty =
            AvaloniaProperty.Register<CommitRefsPresenter, bool>(nameof(AllowWrap));

        public bool AllowWrap
        {
            get => GetValue(AllowWrapProperty);
            set => SetValue(AllowWrapProperty, value);
        }

        public static readonly StyledProperty<bool> ShowTagsProperty =
            AvaloniaProperty.Register<CommitRefsPresenter, bool>(nameof(ShowTags), true);

        public bool ShowTags
        {
            get => GetValue(ShowTagsProperty);
            set => SetValue(ShowTagsProperty, value);
        }

        public static readonly StyledProperty<List<Models.Branch>> BranchesProperty =
            AvaloniaProperty.Register<CommitRefsPresenter, List<Models.Branch>>(nameof(Branches));

        public List<Models.Branch> Branches
        {
            get => GetValue(BranchesProperty);
            set => SetValue(BranchesProperty, value);
        }

        public static readonly StyledProperty<bool> HasSingleRemoteProperty =
            AvaloniaProperty.Register<CommitRefsPresenter, bool>(nameof(HasSingleRemote));

        public bool HasSingleRemote
        {
            get => GetValue(HasSingleRemoteProperty);
            set => SetValue(HasSingleRemoteProperty, value);
        }


        public Models.Decorator DecoratorAt(Point point)
        {
            foreach (var pill in _pills)
            {
                if (pill.Bounds.Contains(point))
                {
                    if (pill.Other != null)
                        return pill.Other.Decorator;
                    foreach (var l in pill.Locals)
                    {
                        if (l.Bounds.Contains(point))
                            return l.Decorator;
                    }
                    foreach (var r in pill.RemoteGroup)
                    {
                        if (r.Bounds.Contains(point))
                            return r.Decorator;
                    }
                }
            }

            return null;
        }

        public override void Render(DrawingContext context)
        {
            if (_pills.Count == 0)
                return;

            var fg = Foreground;
            var bg = Background;
            var x = 1.5;
            var y = 0.5;

            context.FillRectangle(Brushes.Transparent, Bounds);

            foreach (var pill in _pills)
            {
                if (AllowWrap && x > 1.5 && x + pill.Width > Bounds.Width)
                {
                    x = 1.5;
                    y += 20.0;
                }

                pill.Bounds = new Rect(x, y, pill.Width, 16);
                var entireRect = new RoundedRect(pill.Bounds, new CornerRadius(4));

                bool isHead = pill.Other != null && pill.Other.IsHead;
                if (!isHead)
                {
                    foreach (var local in pill.Locals)
                    {
                        if (local.IsHead)
                        {
                            isHead = true;
                            break;
                        }
                    }
                }
                if (isHead)
                {
                    if (UseGraphColor)
                    {
                        if (bg != null)
                            context.DrawRectangle(bg, null, entireRect);
                        using (context.PushOpacity(.6))
                            context.DrawRectangle(pill.Brush, null, entireRect);
                    }
                }
                else
                {
                    if (bg != null)
                        context.DrawRectangle(bg, null, entireRect);
                    using (context.PushOpacity(.2))
                        context.DrawRectangle(pill.Brush, null, entireRect);
                }

                context.DrawRectangle(null, new Pen(pill.Brush), entireRect);

                var curX = x;
                if (pill.Other != null)
                {
                    DrawItem(context, pill.Other, ref curX, y, fg);
                }
                else if (pill.RemoteGroup.Count > 0 && pill.Locals.Count == 0)
                {
                    // Case P3: [ Cloud R1 | Cloud R2 : branch ]
                    bool first = true;
                    foreach (var r in pill.RemoteGroup)
                    {
                        if (!first)
                        {
                            context.DrawText(_pipeText, new Point(curX + 4, y + 8.0 - _pipeText.Height * 0.5));
                            curX += _pipeText.Width + 8;
                        }

                        DrawItem(context, r, ref curX, y, fg);
                        first = false;
                    }

                    if (!string.IsNullOrEmpty(pill.SharedBranchName))
                    {
                        context.DrawText(_colonText, new Point(curX + 4, y + 8.0 - _colonText.Height * 0.5));
                        curX += _colonText.Width + 8;

                        var typeface = new Typeface(FontFamily);
                        var branchLabel = new FormattedText(pill.SharedBranchName, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, FontSize, fg);
                        context.DrawText(branchLabel, new Point(curX + 4, y + 8.0 - branchLabel.Height * 0.5));
                    }
                }
                else
                {
                    // Case P0/P1/P2: [ Local | Local ⇌ Cloud Tracked | ≠ Cloud Sibling ]
                    bool first = true;
                    foreach (var l in pill.Locals)
                    {
                        if (!first)
                        {
                            context.DrawText(_pipeText, new Point(curX + 4, y + 8.0 - _pipeText.Height * 0.5));
                            curX += _pipeText.Width + 8;
                        }

                        DrawItem(context, l, ref curX, y, fg);
                        first = false;
                    }

                    if (pill.RemoteGroup.Count > 0)
                    {
                        var hasTracked = false;
                        foreach (var remote in pill.RemoteGroup)
                        {
                            if (remote.IsTracked)
                            {
                                hasTracked = true;
                                break;
                            }
                        }
                        if (hasTracked)
                        {
                            context.DrawText(_trackText, new Point(curX + 4, y + 8.0 - _trackText.Height * 0.5));
                            curX += _trackText.Width + 8;
                        }

                        bool firstR = true;
                        foreach (var r in pill.RemoteGroup)
                        {
                            if (!firstR)
                            {
                                context.DrawText(_pipeText, new Point(curX + 4, y + 8.0 - _pipeText.Height * 0.5));
                                curX += _pipeText.Width + 8;
                            }

                            if (!r.IsTracked)
                            {
                                context.DrawText(_neqText, new Point(curX + 4, y + 8.0 - _neqText.Height * 0.5));
                                curX += _neqText.Width + 4;
                                DrawItem(context, r, ref curX, y, fg);
                            }
                            else
                            {
                                DrawItem(context, r, ref curX, y, fg);
                            }
                            firstR = false;
                        }
                    }
                }

                x += pill.Width + 4;
            }
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == FontFamilyProperty ||
                change.Property == FontSizeProperty ||
                change.Property == ForegroundProperty ||
                change.Property == UseGraphColorProperty ||
                change.Property == UseCompactBranchNamesProperty ||
                change.Property == BackgroundProperty ||
                change.Property == ShowTagsProperty ||
                change.Property == BranchesProperty ||
                change.Property == HasSingleRemoteProperty)
                InvalidateMeasure();
        }

        private void DrawItem(DrawingContext context, DecoratorRenderItem item, ref double x, double y, IBrush fg)
        {
            var icon = item.Icon;
            if (icon != null)
            {
                using (context.PushTransform(Matrix.CreateTranslation(x + 3, y + 3)))
                    context.DrawGeometry(fg, null, icon);
            }

            double labelX = x + (item.IsHead ? 16 : 20);
            context.DrawText(item.Label, new Point(labelX, y + 8.0 - item.Label.Height * 0.5));
            item.Bounds = new Rect(x, y, item.Width, 16);
            x += item.Width;
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            InvalidateMeasure();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            _pills.Clear();
            if (DataContext is not Models.Commit commit)
                return new Size(0, 0);

            var refs = commit.Decorators;
            if (refs == null || refs.Count == 0)
                return new Size(0, 0);
            EnsureIcons();
            var typeface = new Typeface(FontFamily);
            var typefaceBold = new Typeface(FontFamily, FontStyle.Normal, FontWeight.Bold);
            var fg = Foreground;
            var normalBG = UseGraphColor ? Models.CommitGraph.Pens[commit.Color].Brush : Brushes.Gray;
            var labelSize = FontSize;
            var requiredHeight = 16.0;

            if (!UseCompactBranchNames)
            {
                foreach (var decorator in refs)
                {
                    if (!ShowTags && decorator.Type == Models.DecoratorType.Tag)
                        continue;

                    var pill = new Pill { Brush = normalBG };
                    if (decorator.Type == Models.DecoratorType.Tag)
                        pill.Brush = Brushes.Gray;

                    var item = CreateRenderItem(decorator, typeface, typefaceBold, labelSize, fg, false, false);
                    pill.Other = item;
                    pill.Width = item.Width;
                    _pills.Add(pill);
                }

                var independentWidth = 0.0;
                var independentX = 0.0;
                foreach (var pill in _pills)
                {
                    if (AllowWrap && independentX + pill.Width > availableSize.Width && independentX > 0)
                    {
                        requiredHeight += 20.0;
                        independentX = 0;
                    }

                    independentX += pill.Width + 4;
                    independentWidth = Math.Max(independentWidth, independentX);
                }

                InvalidateVisual();
                return new Size(independentWidth, requiredHeight);
            }

            // --- Zero-allocation Separator Pre-calculation ---
            _pipeText = new FormattedText("|", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, labelSize, fg);
            _trackText = new FormattedText("⇌", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, labelSize, fg);
            _colonText = new FormattedText(":", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, labelSize, fg);
            _neqText = new FormattedText("≠", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, labelSize, fg);

            var pipeWidth = _pipeText.Width + 8;
            var trackWidth = _trackText.Width + 8;
            var colonWidth = _colonText.Width + 8;
            var neqWidth = _neqText.Width + 4;
            // -------------------------------------------------

            var branches = Branches;
            var branchLookup = GetBranchLookup(branches);
            var processed = new HashSet<Models.Decorator>();

            // Avoid LINQ categorization to reduce heap pressure
            var allRemotes = new List<Models.Decorator>();
            var allLocals = new List<Models.Decorator>();
            foreach (var r in refs)
            {
                if (r.Type == Models.DecoratorType.RemoteBranchHead)
                    allRemotes.Add(r);
                else if (r.Type is Models.DecoratorType.LocalBranchHead or Models.DecoratorType.CurrentBranchHead)
                    allLocals.Add(r);
            }

            // Pre-calculate tracked leaders with zero-allocation span comparison
            var trackLeaders = new Dictionary<Models.Decorator, List<Models.Decorator>>();
            if (branchLookup != null)
            {
                foreach (var l in allLocals)
                {
                    if (branchLookup.TryGetValue(l.Name, out var b) && !string.IsNullOrEmpty(b.Upstream))
                    {
                        var upstream = b.Upstream.AsSpan();
                        bool isFullRef = upstream.StartsWith("refs/remotes/", StringComparison.OrdinalIgnoreCase);
                        var upstreamCore = isFullRef ? upstream.Slice(13) : upstream;

                        Models.Decorator r = null;
                        foreach (var xr in allRemotes)
                        {
                            var xrName = xr.Name.AsSpan();
                            if (upstreamCore.Equals(xrName, StringComparison.OrdinalIgnoreCase))
                            {
                                r = xr;
                                break;
                            }
                        }

                        if (r != null)
                        {
                            if (!trackLeaders.ContainsKey(r))
                                trackLeaders[r] = new();
                            trackLeaders[r].Add(l);
                        }
                    }
                }
            }

            // 1. Process Tracking Groups (Tracking 1st)
            foreach (var kv in trackLeaders)
            {
                var r = kv.Key;
                var trackers = kv.Value;
                var pill = new Pill { Brush = normalBG };
                double w = 0;

                for (int i = 0; i < trackers.Count; i++)
                {
                    if (i > 0)
                        w += pipeWidth;
                    var lItem = CreateRenderItem(trackers[i], typeface, typefaceBold, labelSize, fg);
                    pill.Locals.Add(lItem);
                    processed.Add(trackers[i]);
                    w += lItem.Width;
                }

                w += trackWidth;

                // P0 Perfect Match logic
                bool perfect = true;
                var slashIdx = r.Name.IndexOf('/');
                if (slashIdx > 0)
                {
                    var suffix = r.Name.AsSpan(slashIdx + 1);
                    foreach (var l in trackers)
                    {
                        if (!l.Name.AsSpan().Equals(suffix, StringComparison.OrdinalIgnoreCase))
                        {
                            perfect = false;
                            break;
                        }
                    }
                }
                else
                {
                    perfect = false;
                }

                var compactRemoteName = UseCompactBranchNames && perfect;
                var rItem = CreateRenderItem(r, typeface, typefaceBold, labelSize, fg, true, compactRemoteName, compactRemoteName);
                rItem.IsTracked = true;
                pill.RemoteGroup.Add(rItem);
                processed.Add(r);
                w += rItem.Width;

                // Siblings with zero-allocation suffix check
                foreach (var ar in allRemotes)
                {
                    if (processed.Contains(ar) || trackLeaders.ContainsKey(ar))
                        continue;

                    var arName = ar.Name.AsSpan();
                    var arSlashIdx = arName.IndexOf('/');
                    var arSuffix = arSlashIdx >= 0 ? arName.Slice(arSlashIdx + 1) : arName;

                    if (arSuffix.Equals(trackers[0].Name.AsSpan(), StringComparison.OrdinalIgnoreCase))
                    {
                        w += pipeWidth + neqWidth;
                        var sItem = CreateRenderItem(ar, typeface, typefaceBold, labelSize, fg, true, UseCompactBranchNames, true);
                        sItem.IsTracked = false;
                        pill.RemoteGroup.Add(sItem);
                        processed.Add(ar);
                        w += sItem.Width;
                    }
                }

                pill.Width = w;
                _pills.Add(pill);
            }

            // 2. Process Remaining Locals
            foreach (var l in allLocals)
            {
                if (processed.Contains(l))
                    continue;

                var pill = new Pill { Brush = normalBG };
                var lItem = CreateRenderItem(l, typeface, typefaceBold, labelSize, fg);
                pill.Locals.Add(lItem);
                processed.Add(l);
                double w = lItem.Width;

                foreach (var ar in allRemotes)
                {
                    if (processed.Contains(ar) || trackLeaders.ContainsKey(ar))
                        continue;

                    var arName = ar.Name.AsSpan();
                    var arSlashIdx = arName.IndexOf('/');
                    var arSuffix = arSlashIdx >= 0 ? arName.Slice(arSlashIdx + 1) : arName;

                    if (arSuffix.Equals(l.Name.AsSpan(), StringComparison.OrdinalIgnoreCase))
                    {
                        w += pipeWidth + neqWidth;
                        var sItem = CreateRenderItem(ar, typeface, typefaceBold, labelSize, fg, true, UseCompactBranchNames, true);
                        sItem.IsTracked = false;
                        pill.RemoteGroup.Add(sItem);
                        processed.Add(ar);
                        w += sItem.Width;
                    }
                }

                pill.Width = w;
                _pills.Add(pill);
            }

            // 3. Tags & Common Divisor Merge
            foreach (var decorator in refs)
            {
                if (processed.Contains(decorator))
                    continue;
                if (!ShowTags && decorator.Type == Models.DecoratorType.Tag)
                    continue;

                var pill = new Pill { Brush = normalBG };
                if (decorator.Type == Models.DecoratorType.Tag)
                {
                    pill.Brush = Brushes.Gray;
                    var item = CreateRenderItem(decorator, typeface, typefaceBold, labelSize, fg);
                    pill.Other = item;
                    pill.Width = item.Width;
                    processed.Add(decorator);
                }
                else if (decorator.Type == Models.DecoratorType.CurrentCommitHead)
                {
                    var item = CreateRenderItem(decorator, typeface, typefaceBold, labelSize, fg);
                    pill.Other = item;
                    pill.Width = item.Width;
                    processed.Add(decorator);
                }
                else if (decorator.Type == Models.DecoratorType.RemoteBranchHead)
                {
                    var name = decorator.Name.AsSpan();
                    var slashIdx = name.IndexOf('/');
                    var suffix = slashIdx >= 0 ? name.Slice(slashIdx + 1) : name;

                    // Group same-named untracked remotes
                    var group = new List<Models.Decorator>();
                    foreach (var ur in allRemotes)
                    {
                        if (processed.Contains(ur))
                            continue;
                        var urName = ur.Name.AsSpan();
                        var urSlashIdx = urName.IndexOf('/');
                        var urSuffix = urSlashIdx >= 0 ? urName.Slice(urSlashIdx + 1) : urName;

                        if (urSuffix.Equals(suffix, StringComparison.OrdinalIgnoreCase))
                            group.Add(ur);
                    }

                    if (group.Count > 1)
                    {
                        double w = 0;
                        if (UseCompactBranchNames)
                        {
                            pill.SharedBranchName = suffix.ToString();
                            for (int i = 0; i < group.Count; i++)
                            {
                                if (i > 0)
                                    w += pipeWidth;
                                var rItem = CreateRenderItem(group[i], typeface, typefaceBold, labelSize, fg, true, true);
                                pill.RemoteGroup.Add(rItem);
                                processed.Add(group[i]);
                                w += rItem.Width;
                            }

                            var branchLabel = new FormattedText(pill.SharedBranchName, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, labelSize, fg);
                            w += colonWidth + branchLabel.Width + 8;
                        }
                        else
                        {
                            for (int i = 0; i < group.Count; i++)
                            {
                                if (i > 0)
                                    w += pipeWidth;
                                var rItem = CreateRenderItem(group[i], typeface, typefaceBold, labelSize, fg, true, false);
                                pill.RemoteGroup.Add(rItem);
                                processed.Add(group[i]);
                                w += rItem.Width;
                            }
                        }

                        pill.Width = w;
                    }
                    else
                    {
                        var item = CreateRenderItem(decorator, typeface, typefaceBold, labelSize, fg, false, false);
                        pill.Other = item;
                        pill.Width = item.Width;
                        processed.Add(decorator);
                    }
                }
                else
                {
                    var item = CreateRenderItem(decorator, typeface, typefaceBold, labelSize, fg, false, false);
                    pill.Other = item;
                    pill.Width = item.Width;
                    processed.Add(decorator);
                }

                _pills.Add(pill);
            }

            double requiredWidth = 0;
            double curX = 0;
            foreach (var pill in _pills)
            {
                if (AllowWrap && curX + pill.Width > availableSize.Width && curX > 0)
                {
                    requiredHeight += 20.0;
                    curX = 0;
                }
                curX += pill.Width + 4;
                requiredWidth = Math.Max(requiredWidth, curX);
            }

            InvalidateVisual();
            return new Size(requiredWidth, requiredHeight);
        }

        private DecoratorRenderItem CreateRenderItem(Models.Decorator decorator, Typeface typeface, Typeface typefaceBold, double labelSize, IBrush fg, bool forceBold = false, bool stripSuffix = false, bool hideNameForSingleRemote = false)
        {
            var isHead = decorator.Type is Models.DecoratorType.CurrentBranchHead or Models.DecoratorType.CurrentCommitHead;
            var useBold = isHead || forceBold;

            string displayName = decorator.Name;
            int slashIdx = displayName.IndexOf('/');
            if (stripSuffix && slashIdx > 0)
            {
                displayName = displayName.Substring(0, slashIdx);
            }

            // Upstream (1445b53f) only hides the remote name for remotes folded into a
            // local branch's compact pill, and only when the repo has a single remote.
            // Standalone remote branches must keep a label: strip the "remote/" prefix
            // and show the branch short name instead of erasing it entirely.
            if (HasSingleRemote && decorator.Type == Models.DecoratorType.RemoteBranchHead)
            {
                if (hideNameForSingleRemote)
                    return new DecoratorRenderItem
                    {
                        Icon = ICON_CACHE[decorator.Type],
                        Label = new FormattedText("", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, labelSize, fg),
                        Decorator = decorator,
                        IsHead = isHead,
                        Width = 20
                    };

                if (slashIdx > 0 && displayName.Length > slashIdx)
                    displayName = displayName.Substring(slashIdx + 1);
                slashIdx = -1;
            }

            var label = new FormattedText(
                displayName,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                useBold ? typefaceBold : typeface,
                useBold ? labelSize + 1 : labelSize,
                fg);

            if (!useBold && decorator.Type == Models.DecoratorType.RemoteBranchHead && slashIdx > 0)
            {
                label.SetFontWeight(FontWeight.Bold, 0, slashIdx);
                label.SetFontSize(labelSize + 1, 0, slashIdx);
            }

            return new DecoratorRenderItem
            {
                Icon = ICON_CACHE[decorator.Type],
                Label = label,
                Decorator = decorator,
                IsHead = isHead,
                Width = 16 + (isHead ? 0 : 4) + label.Width + 4
            };
        }

        private readonly List<Pill> _pills = new();
    }
}
