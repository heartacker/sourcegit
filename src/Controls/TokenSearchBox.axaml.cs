using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace SourceGit.Controls
{
    /// <summary>
    ///     A search box that supports tokenized filters (chips), providing a GitHub-style filtering experience.
    /// </summary>
    [TemplatePart("PART_TextPresenter", typeof(TextBox))]
    [TemplatePart("PART_TokensList", typeof(ListBox))]
    [TemplatePart("PART_SuggestionsPopup", typeof(Popup))]
    [TemplatePart("PART_SuggestionsList", typeof(ListBox))]
    public class TokenSearchBox : TemplatedControl
    {
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

        public static readonly StyledProperty<double> MaxListHeightProperty =
            AvaloniaProperty.Register<TokenSearchBox, double>(nameof(MaxListHeight), 96.0);

        public static readonly StyledProperty<ICommand> SearchCommandProperty =
            AvaloniaProperty.Register<TokenSearchBox, ICommand>(nameof(SearchCommand));

        public static readonly StyledProperty<ICommand> TokenDoubleClickCommandProperty =
            AvaloniaProperty.Register<TokenSearchBox, ICommand>(nameof(TokenDoubleClickCommand));

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
                SetCurrentValue(MaxListHeightProperty, MaxRows * 32.0);
            }
        }

        private TextBox _textBox;
        private ListBox _tokensList;
        private Popup _popup;
        private ListBox _suggestionList;
        private CancellationTokenSource _cts;

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            base.OnApplyTemplate(e);
            _textBox = e.NameScope.Find<TextBox>("PART_TextPresenter");
            if (_textBox != null)
            {
                _textBox.KeyDown += OnTextBoxKeyDown;
                _textBox.PropertyChanged += OnTextBoxPropertyChanged;
                _textBox.GotFocus += (s, e) =>
                {
                    if (_tokensList != null)
                        _tokensList.SelectedIndex = -1;
                    _ = UpdateSuggestionsAsync(Text ?? string.Empty);
                };
                _textBox.LostFocus += (s, e) =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (_popup == null)
                            return;

                        // Keep popup open when focus moves from textbox into suggestion list.
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
        }

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
            if (provider.Aliases != null)
            {
                foreach (var alias in provider.Aliases)
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
            if (provider.Aliases != null)
            {
                foreach (var alias in provider.Aliases)
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
                // Value selected for a prefix -> Complete token
                var prefixPart = isNegated ? "!" + matchedPrefix : matchedPrefix;
                AddToken(prefixPart + suggestion.Name);
            }
            else
            {
                // Prefix selected -> Append to textbox and keep typing
                var prefixPart = isNegated ? "!" + suggestion.Name : suggestion.Name;
                SetCurrentValue(TextProperty, prefixPart);
                if (_textBox != null)
                {
                    _textBox.Focus();
                    _textBox.CaretIndex = _textBox.Text.Length;
                }
                return; // Do not close popup, let PropertyChanged trigger new suggestions
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

                // GitHub style: Only trigger space commit if it's a FULL token (prefix + value) or an operator
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

                // If not committed, update suggestions
                if (string.IsNullOrEmpty(val) && SelectedTokens.Count > 0)
                {
                    // Text cleared while tokens exist → close popup and focus last token
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
                .OrderByDescending(g => g.Key != null) // Groups with instances first
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
                    if (p.Aliases != null && p.Aliases.Length > 0)
                        desc = $"{desc} ({string.Join(", ", p.Aliases)})";

                    flatList.Add(new TokenSuggestion { Name = p.Prefix, Description = desc, Icon = p.Icon });
                }
            }

            if (!token.IsCancellationRequested)
            {
                if (flatList.Count > 0)
                {
                    _suggestionList.ItemsSource = flatList;
                    _popup.IsOpen = true;

                    // Auto-select first suggestion (skip headers) when user has typed something
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
            // Keyboard navigation for suggestions
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

            // Normal text box logic
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
                // Close popup first to avoid focus conflicts
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

            var matchedProvider = token != "|" && token != "&"
                ? MatchProvider(Providers, checkStr, out var _)
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
                else if (matchedProvider.LogicMode == TokenLogicMode.AutoOr)
                {
                    bool hasSamePrefix = false;
                    foreach (var existing in SelectedTokens)
                    {
                        var existingCheck = existing.StartsWith("!") ? existing.Substring(1) : existing;
                        if (MatchProvider(Providers, existingCheck, out var _) == matchedProvider)
                        {
                            hasSamePrefix = true;
                            break;
                        }
                    }
                    if (hasSamePrefix)
                    {
                        var last = SelectedTokens.LastOrDefault();
                        if (last == "&")
                        {
                            // Enforce "Cannot AND" rule for AutoOr: Correct illegal '&' to '|'
                            SelectedTokens[SelectedTokens.Count - 1] = "|";
                        }
                        else if (last != null && last != "|")
                        {
                            SelectedTokens.Add("|");
                        }
                    }
                }
            }

            if (!SelectedTokens.Contains(token))
            {
                SelectedTokens.Add(token);
            }
            SetCurrentValue(TextProperty, string.Empty);
            if (_popup != null)
                _popup.IsOpen = false;
        }

        public bool InsertToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return false;

            AddToken(token.Trim());
            return true;
        }

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

        public void RemoveToken(string token)
        {
            SelectedTokens.Remove(token);
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
    }
}
