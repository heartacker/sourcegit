using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace SourceGit.Controls
{
    /// <summary>
    ///     提供类似 GitHub 风格的智能过滤搜索框，支持 Token 芯片化显示。
    ///     核心特性：
    ///     1. 自动归类 (AutoGrouping)：相同前缀的 Token 会自动排在一起。
    ///     2. 流体气泡 (Merged Bubble)：同类项在静默状态下视觉合并为胶囊。
    ///     3. 两段式删除：Backspace 首次按下选中 Token，再次按下删除/编辑。
    ///     4. 异步建议：支持通过 Provider 异步获取增量搜索建议。
    /// </summary>
    [TemplatePart("PART_TextPresenter", typeof(TextBox))]
    [TemplatePart("PART_TokensList", typeof(ListBox))]
    [TemplatePart("PART_SuggestionsPopup", typeof(Popup))]
    [TemplatePart("PART_SuggestionsList", typeof(ListBox))]
    [TemplatePart("PART_RootBorder", typeof(Border))]
    public class TokenSearchBox : TemplatedControl
    {
        #region Dependency Properties
        public static readonly StyledProperty<string> TextProperty =
            AvaloniaProperty.Register<TokenSearchBox, string>(nameof(Text), defaultBindingMode: BindingMode.TwoWay);

        public static readonly StyledProperty<ObservableCollection<string>> SelectedTokensProperty =
            AvaloniaProperty.Register<TokenSearchBox, ObservableCollection<string>>(nameof(SelectedTokens));

        public static readonly StyledProperty<ObservableCollection<ITokenSuggestionProvider>> ProvidersProperty =
            AvaloniaProperty.Register<TokenSearchBox, ObservableCollection<ITokenSuggestionProvider>>(nameof(Providers));

        public static readonly StyledProperty<string> WatermarkProperty =
            AvaloniaProperty.Register<TokenSearchBox, string>(nameof(Watermark));

        public static readonly StyledProperty<int> MaxRowsProperty =
            AvaloniaProperty.Register<TokenSearchBox, int>(nameof(MaxRows), 3);

        /// <summary>
        ///     是否开启自动归类：开启后，相同前缀的 Token 将被自动排列在一起。
        /// </summary>
        public static readonly StyledProperty<bool> AutoGroupingProperty =
            AvaloniaProperty.Register<TokenSearchBox, bool>(nameof(AutoGrouping), true);

        /// <summary>
        ///     内部用于动态计算 ScrollViewer 的最大高度。
        /// </summary>
        public static readonly StyledProperty<double> MaxListHeightProperty =
            AvaloniaProperty.Register<TokenSearchBox, double>(nameof(MaxListHeight), 96.0);

        public static readonly StyledProperty<ICommand> SearchCommandProperty =
            AvaloniaProperty.Register<TokenSearchBox, ICommand>(nameof(SearchCommand));

        public static readonly StyledProperty<ICommand> TokenDoubleClickCommandProperty =
            AvaloniaProperty.Register<TokenSearchBox, ICommand>(nameof(TokenDoubleClickCommand));
        #endregion

        #region Properties
        public string Text
        {
            get => GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public ObservableCollection<string> SelectedTokens
        {
            get => GetValue(SelectedTokensProperty);
            set => SetValue(SelectedTokensProperty, value);
        }

        public ObservableCollection<ITokenSuggestionProvider> Providers
        {
            get => GetValue(ProvidersProperty);
            set => SetValue(ProvidersProperty, value);
        }

        public string Watermark
        {
            get => GetValue(WatermarkProperty);
            set => SetValue(WatermarkProperty, value);
        }

        public int MaxRows
        {
            get => GetValue(MaxRowsProperty);
            set => SetValue(MaxRowsProperty, value);
        }

        public bool AutoGrouping
        {
            get => GetValue(AutoGroupingProperty);
            set => SetValue(AutoGroupingProperty, value);
        }

        public double MaxListHeight
        {
            get => GetValue(MaxListHeightProperty);
            set => SetValue(MaxListHeightProperty, value);
        }

        public ICommand SearchCommand
        {
            get => GetValue(SearchCommandProperty);
            set => SetValue(SearchCommandProperty, value);
        }

        public ICommand TokenDoubleClickCommand
        {
            get => GetValue(TokenDoubleClickCommandProperty);
            set => SetValue(TokenDoubleClickCommandProperty, value);
        }
        #endregion

        public TokenSearchBox()
        {
            SetCurrentValue(SelectedTokensProperty, new ObservableCollection<string>());
            SetCurrentValue(ProvidersProperty, new ObservableCollection<ITokenSuggestionProvider>());
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == MaxRowsProperty)
            {
                // 根据 MaxRows 动态计算最大像素高度，每行约 32px
                SetCurrentValue(MaxListHeightProperty, MaxRows * 32.0);
            }
        }

        private TextBox _textBox;
        private ListBox _tokensList;
        private Popup _popup;
        private ListBox _suggestionList;
        private Border _rootBorder;
        private Button _clearButton;
        private CancellationTokenSource _cts;

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            base.OnApplyTemplate(e);
            _textBox = e.NameScope.Find<TextBox>("PART_TextPresenter");
            if (_textBox != null)
            {
                _textBox.KeyDown += OnTextBoxKeyDown;
                _textBox.PropertyChanged += OnTextBoxPropertyChanged;
                _textBox.GotFocus += (s, ev) =>
                {
                    if (_tokensList != null)
                        _tokensList.SelectedIndex = -1;
                    _ = UpdateSuggestionsAsync(Text ?? string.Empty);
                };
                _textBox.LostFocus += (s, ev) =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (_popup == null)
                            return;

                        // 只有当焦点真的离开了整个搜索控件（且没进入建议列表）时才关闭
                        if ((_textBox?.IsKeyboardFocusWithin ?? false) || (_suggestionList?.IsKeyboardFocusWithin ?? false))
                            return;

                        _popup.IsOpen = false;
                    }, DispatcherPriority.Input);
                };
            }

            _tokensList = e.NameScope.Find<ListBox>("PART_TokensList");
            if (_tokensList != null)
            {
                _tokensList.KeyDown += OnTokensListKeyDown;
                _tokensList.DoubleTapped += OnTokensListDoubleTapped;
            }

            _popup = e.NameScope.Find<Popup>("PART_SuggestionsPopup");
            _suggestionList = e.NameScope.Find<ListBox>("PART_SuggestionsList");
            if (_suggestionList != null)
            {
                _suggestionList.PointerReleased += OnSuggestionPointerReleased;
            }

            _rootBorder = e.NameScope.Find<Border>("PART_RootBorder");
            if (_rootBorder != null)
            {
                _rootBorder.PointerPressed += (s, ev) =>
                {
                    // 点击搜索框任何空白区域都自动聚焦 TextBox
                    _textBox?.Focus();
                    ev.Handled = true;
                };
            }

            _clearButton = e.NameScope.Find<Button>("PART_ClearButton");
            if (_clearButton != null)
            {
                _clearButton.Click += (s, ev) =>
                {
                    SelectedTokens?.Clear();
                    _textBox?.Focus();
                };

                if (SelectedTokens != null)
                    SelectedTokens.CollectionChanged += (_, _) => UpdateClearButtonVisibility();
                UpdateClearButtonVisibility();
            }
        }

        /// <summary>
        ///     基础点击支持：确保点击边框区域也能获取焦点。
        /// </summary>
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            _textBox?.Focus();
            e.Handled = true;
        }

        #region Public API
        /// <summary>
        ///     直接移除指定的 Token 字符串。
        /// </summary>
        public void RemoveToken(string token)
        {
            SelectedTokens.Remove(token);
        }

        /// <summary>
        ///     通过代码向搜索框插入一个 Token。
        /// </summary>
        public bool InsertToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return false;

            AddToken(token.Trim());
            return true;
        }

        /// <summary>
        ///     删除具有指定内容的 Token。
        /// </summary>
        public bool DeleteToken(string token, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
        {
            if (string.IsNullOrWhiteSpace(token) || SelectedTokens == null || SelectedTokens.Count == 0)
                return false;

            for (int i = 0; i < SelectedTokens.Count; i++)
            {
                if (SelectedTokens[i].Equals(token, comparison))
                {
                    SelectedTokens.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        ///     按前缀批量删除 Token（例如重置所有作者过滤）。
        /// </summary>
        public int DeleteTokensByPrefix(string prefix, bool includeNegated = true, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
        {
            if (string.IsNullOrWhiteSpace(prefix) || SelectedTokens == null || SelectedTokens.Count == 0)
                return 0;

            var count = 0;
            for (int i = SelectedTokens.Count - 1; i >= 0; i--)
            {
                var token = SelectedTokens[i];
                var check = token;
                if (includeNegated && check.StartsWith("!", StringComparison.Ordinal))
                    check = check[1..];

                if (check.StartsWith(prefix, comparison))
                {
                    SelectedTokens.RemoveAt(i);
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        ///     查询当前所有匹配特定前缀的 Token。
        /// </summary>
        public IReadOnlyList<string> QueryTokens(string prefix = null, bool includeNegated = true, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
        {
            if (SelectedTokens == null || SelectedTokens.Count == 0)
                return Array.Empty<string>();

            if (string.IsNullOrWhiteSpace(prefix))
                return SelectedTokens.ToList();

            return SelectedTokens
                .Where(t =>
                {
                    var check = t;
                    if (includeNegated && check.StartsWith("!", StringComparison.Ordinal))
                        check = check[1..];

                    return check.StartsWith(prefix, comparison);
                })
                .ToList();
        }
        #endregion

        #region Internal Logic
        private void OnTokensListKeyDown(object sender, KeyEventArgs e)
        {
            if (_tokensList == null)
                return;

            if (e.Key == Key.Delete)
            {
                if (_tokensList.SelectedIndex >= 0 && _tokensList.SelectedIndex < SelectedTokens.Count)
                {
                    var idx = _tokensList.SelectedIndex;
                    SelectedTokens.RemoveAt(idx);

                    if (SelectedTokens.Count > 0)
                    {
                        _tokensList.SelectedIndex = Math.Min(idx, SelectedTokens.Count - 1);
                        _tokensList.Focus();
                    }
                    else
                    {
                        _tokensList.SelectedIndex = -1;
                        _textBox?.Focus();
                    }
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Back)
            {
                // 两段式删除：第二次按退格进入编辑模式
                if (_tokensList.SelectedIndex >= 0 && _tokensList.SelectedIndex < SelectedTokens.Count)
                {
                    BeginEditToken(SelectedTokens[_tokensList.SelectedIndex]);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Left)
            {
                if (_tokensList.SelectedIndex > 0)
                {
                    _tokensList.SelectedIndex--;
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Right)
            {
                if (_tokensList.SelectedIndex < SelectedTokens.Count - 1)
                {
                    _tokensList.SelectedIndex++;
                }
                else
                {
                    _tokensList.SelectedIndex = -1;
                    _textBox?.Focus();
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                _tokensList.SelectedIndex = -1;
                _textBox?.Focus();
                e.Handled = true;
            }
        }

        private void OnTokensListDoubleTapped(object sender, TappedEventArgs e)
        {
            var item = (e.Source as Visual)?.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
            var token = item?.DataContext as string;
            if (string.IsNullOrEmpty(token))
                return;

            if (TokenDoubleClickCommand?.CanExecute(token) == true)
                TokenDoubleClickCommand.Execute(token);

            BeginEditToken(token);
            e.Handled = true;
        }

        private void OnSuggestionPointerReleased(object sender, PointerReleasedEventArgs e)
        {
            var item = (e.Source as Visual)?.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
            if (item?.DataContext is TokenSuggestion suggestion)
            {
                CommitSuggestion(suggestion);
                e.Handled = true;
            }
        }

        private static string GetMatchedPrefix(ITokenSuggestionProvider provider, string text)
        {
            if (text.StartsWith(provider.Prefix, StringComparison.OrdinalIgnoreCase))
                return provider.Prefix;
            if (provider.FullPrefix != null)
            {
                foreach (var alias in provider.FullPrefix)
                {
                    if (text.StartsWith(alias, StringComparison.OrdinalIgnoreCase))
                        return alias;
                }
            }
            return null;
        }

        private static ITokenSuggestionProvider MatchProvider(IEnumerable<ITokenSuggestionProvider> providers, string text, out string matchedPrefix)
        {
            foreach (var p in providers)
            {
                var prefix = GetMatchedPrefix(p, text);
                if (prefix != null)
                {
                    matchedPrefix = prefix;
                    return p;
                }
            }
            matchedPrefix = null;
            return null;
        }

        private static bool MatchesPattern(ITokenSuggestionProvider provider, string pattern)
        {
            if (provider.Prefix.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
            if (provider.FullPrefix != null)
            {
                foreach (var alias in provider.FullPrefix)
                {
                    if (alias.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }

        private void CommitSuggestion(TokenSuggestion suggestion)
        {
            var currentText = Text ?? string.Empty;
            var isNegated = currentText.StartsWith("!");
            var checkStr = isNegated ? currentText.Substring(1) : currentText;

            var matchedProvider = MatchProvider(Providers, checkStr, out var matchedPrefix);

            if (matchedProvider != null)
            {
                var prefixPart = isNegated ? "!" + matchedPrefix : matchedPrefix;
                AddToken(prefixPart + suggestion.Name);
            }
            else
            {
                var prefixPart = isNegated ? "!" + suggestion.Name : suggestion.Name;
                SetCurrentValue(TextProperty, prefixPart);
                if (_textBox != null)
                {
                    _textBox.Focus();
                    _textBox.CaretIndex = _textBox.Text.Length;
                }
                return;
            }

            if (_popup != null)
                _popup.IsOpen = false;
            _suggestionList.SelectedItem = null;
            _textBox?.Focus();
        }

        private async void OnTextBoxPropertyChanged(object sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == TextBox.TextProperty)
            {
                if (_tokensList != null && !string.IsNullOrEmpty(Text))
                {
                    _tokensList.SelectedIndex = -1;
                }

                var val = Text ?? string.Empty;

                if (val.EndsWith(" ") && val.Trim().Length > 0)
                {
                    var trimmed = val.Trim();
                    if (trimmed == "|" || trimmed == "&")
                    {
                        AddToken(trimmed);
                        return;
                    }

                    var isNegated = trimmed.StartsWith("!");
                    var checkStr = isNegated ? trimmed.Substring(1) : trimmed;

                    var matchedProvider = MatchProvider(Providers, checkStr, out var matchedPrefix);
                    if (matchedProvider != null && checkStr.Length > matchedPrefix.Length)
                    {
                        AddToken(trimmed);
                        return;
                    }
                }

                if (string.IsNullOrEmpty(val) && SelectedTokens.Count > 0)
                {
                    if (_popup != null)
                        _popup.IsOpen = false;
                    _tokensList.SelectedIndex = SelectedTokens.Count - 1;
                }
                else
                {
                    await UpdateSuggestionsAsync(val.TrimEnd());
                }
            }
        }

        private async Task UpdateSuggestionsAsync(string text)
        {
            if (_popup == null || _suggestionList == null)
                return;

            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            if (string.IsNullOrEmpty(text))
            {
                ShowDefaultProviders("", token);
                return;
            }

            var isNegated = text.StartsWith("!");
            var checkStr = isNegated ? text.Substring(1) : text;

            var matchedProvider = MatchProvider(Providers, checkStr, out var matchedPrefix);

            if (matchedProvider != null)
            {
                var pattern = checkStr.Substring(matchedPrefix.Length);
                try
                {
                    var suggestions = await matchedProvider.GetSuggestionsAsync(pattern, token);
                    if (token.IsCancellationRequested)
                        return;

                    var list = new List<TokenSuggestion>(suggestions);
                    if (list.Count > 0)
                    {
                        _suggestionList.ItemsSource = list;
                        _popup.IsOpen = true;
                    }
                    else
                    {
                        _popup.IsOpen = false;
                    }
                }
                catch
                {
                    if (!token.IsCancellationRequested)
                    {
                        _popup.IsOpen = false;
                    }
                }
            }
            else
            {
                ShowDefaultProviders(checkStr, token);
            }
        }

        private void ShowDefaultProviders(string pattern, CancellationToken token)
        {
            var flatList = new List<object>();

            var groups = Providers
                .Where(p => string.IsNullOrEmpty(pattern) || MatchesPattern(p, pattern))
                .GroupBy(p => p.Group)
                .OrderByDescending(g => g.Key != null)
                .ThenBy(g => g.Key?.Id);

            foreach (var g in groups)
            {
                if (g.Key != null)
                {
                    flatList.Add(new TokenSuggestionHeader { Name = g.Key.Name });
                }
                foreach (var p in g)
                {
                    var desc = p.Description;
                    if (p.FullPrefix != null && p.FullPrefix.Length > 0)
                        desc = $"{desc} ({string.Join(", ", p.FullPrefix)})";

                    flatList.Add(new TokenSuggestion { Name = p.Prefix, Description = desc, Icon = p.Icon });
                }
            }

            if (!token.IsCancellationRequested)
            {
                if (flatList.Count > 0)
                {
                    _suggestionList.ItemsSource = flatList;
                    _popup.IsOpen = true;

                    if (!string.IsNullOrEmpty(pattern))
                    {
                        var firstSuggestion = -1;
                        for (int i = 0; i < flatList.Count; i++)
                        {
                            if (flatList[i] is TokenSuggestion)
                            {
                                firstSuggestion = i;
                                break;
                            }
                        }
                        _suggestionList.SelectedIndex = firstSuggestion;
                    }
                }
                else
                {
                    _popup.IsOpen = false;
                }
            }
        }

        private void OnTextBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (_popup?.IsOpen == true && _suggestionList != null)
            {
                if (e.Key == Key.Down)
                {
                    int next = _suggestionList.SelectedIndex + 1;
                    while (next < _suggestionList.ItemCount && _suggestionList.Items.Cast<object>().ElementAt(next) is TokenSuggestionHeader)
                        next++;
                    if (next < _suggestionList.ItemCount)
                        _suggestionList.SelectedIndex = next;
                    e.Handled = true;
                    return;
                }
                else if (e.Key == Key.Up)
                {
                    int prev = _suggestionList.SelectedIndex - 1;
                    while (prev >= 0 && _suggestionList.Items.Cast<object>().ElementAt(prev) is TokenSuggestionHeader)
                        prev--;
                    if (prev >= 0)
                        _suggestionList.SelectedIndex = prev;
                    e.Handled = true;
                    return;
                }
                else if (e.Key == Key.Enter || e.Key == Key.Tab)
                {
                    if (_suggestionList.SelectedItem is TokenSuggestion suggestion)
                    {
                        CommitSuggestion(suggestion);
                        e.Handled = true;
                        return;
                    }
                }
                else if (e.Key == Key.Escape)
                {
                    _popup.IsOpen = false;
                    e.Handled = true;
                    return;
                }
            }

            if (e.Key == Key.Enter)
            {
                if (!string.IsNullOrEmpty(Text))
                {
                    AddToken(Text.Trim());
                }
                SearchCommand?.Execute(null);
                if (_popup != null)
                    _popup.IsOpen = false;
                e.Handled = true;
            }
            else if (e.Key == Key.Back && SelectedTokens.Count > 0)
            {
                if (_popup != null)
                    _popup.IsOpen = false;

                if (string.IsNullOrEmpty(Text))
                {
                    if (_tokensList != null)
                    {
                        if (_tokensList.SelectedIndex >= 0 && _tokensList.SelectedIndex < SelectedTokens.Count)
                        {
                            BeginEditToken(SelectedTokens[_tokensList.SelectedIndex]);
                        }
                        else
                        {
                            _tokensList.SelectedIndex = SelectedTokens.Count - 1;
                        }
                    }
                    else
                    {
                        SelectedTokens.RemoveAt(SelectedTokens.Count - 1);
                    }
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Left && string.IsNullOrEmpty(Text) && SelectedTokens.Count > 0)
            {
                if (_tokensList != null)
                {
                    if (_tokensList.SelectedIndex < 0)
                    {
                        _tokensList.SelectedIndex = SelectedTokens.Count - 1;
                    }
                    else if (_tokensList.SelectedIndex > 0)
                    {
                        _tokensList.SelectedIndex--;
                    }
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Right && string.IsNullOrEmpty(Text) && _tokensList?.SelectedIndex >= 0)
            {
                if (_tokensList.SelectedIndex < SelectedTokens.Count - 1)
                {
                    _tokensList.SelectedIndex++;
                }
                else
                {
                    _tokensList.SelectedIndex = -1;
                }
                e.Handled = true;
            }
        }

        public void AddToken(string token)
        {
            var isNegated = token.StartsWith("!");
            var checkStr = isNegated ? token.Substring(1) : token;

            string matchedPrefix = null;
            var matchedProvider = token != "|" && token != "&"
                ? MatchProvider(Providers, checkStr, out matchedPrefix)
                : null;

            if (matchedProvider != null)
            {
                if (matchedProvider.LogicMode == TokenLogicMode.SingleReplace)
                {
                    for (int i = 0; i < SelectedTokens.Count; i++)
                    {
                        var existing = SelectedTokens[i];
                        var existingCheck = existing.StartsWith("!") ? existing.Substring(1) : existing;
                        if (MatchProvider(Providers, existingCheck, out var _) == matchedProvider)
                        {
                            SelectedTokens[i] = token;
                            SetCurrentValue(TextProperty, string.Empty);
                            if (_popup != null)
                                _popup.IsOpen = false;
                            return;
                        }
                    }
                }
            }

            if (!SelectedTokens.Contains(token))
            {
                if (AutoGrouping && matchedProvider != null)
                {
                    int insertAt = -1;
                    string targetPrefix = matchedPrefix;

                    for (int i = SelectedTokens.Count - 1; i >= 0; i--)
                    {
                        var t = SelectedTokens[i];
                        if (t == "|" || t == "&")
                            continue;

                        var tn = t.StartsWith("!") ? t.Substring(1) : t;
                        MatchProvider(Providers, tn, out var p);
                        if (p == targetPrefix)
                        {
                            insertAt = i + 1;
                            if (insertAt < SelectedTokens.Count && (SelectedTokens[insertAt] == "|" || SelectedTokens[insertAt] == "&"))
                            {
                                insertAt++;
                            }
                            break;
                        }
                    }

                    if (insertAt >= 0)
                        SelectedTokens.Insert(insertAt, token);
                    else
                        SelectedTokens.Add(token);
                }
                else
                {
                    SelectedTokens.Add(token);
                }
            }

            SetCurrentValue(TextProperty, string.Empty);
            if (_popup != null)
                _popup.IsOpen = false;
        }

        private void BeginEditToken(string token)
        {
            if (string.IsNullOrEmpty(token))
                return;

            var idx = SelectedTokens.IndexOf(token);
            if (idx >= 0)
                SelectedTokens.RemoveAt(idx);

            if (_tokensList != null)
                _tokensList.SelectedIndex = -1;

            SetCurrentValue(TextProperty, token);
            _textBox?.Focus();
            if (_textBox != null)
                _textBox.CaretIndex = _textBox.Text?.Length ?? 0;
        }

        private void UpdateClearButtonVisibility()
        {
            if (_clearButton == null)
                return;
            _clearButton.IsVisible = SelectedTokens is { Count: > 0 };
        }
        #endregion
    }
}
