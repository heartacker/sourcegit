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
using Avalonia.VisualTree;

namespace SourceGit.Controls {
    /// <summary>
    ///     A search box that supports tokenized filters (chips), providing a GitHub-style filtering experience.
    /// </summary>
    [TemplatePart("PART_TextPresenter", typeof(TextBox))]
    [TemplatePart("PART_TokensList", typeof(ListBox))]
    [TemplatePart("PART_SuggestionsPopup", typeof(Popup))]
    [TemplatePart("PART_SuggestionsList", typeof(ListBox))]
    public class TokenSearchBox : TemplatedControl {
        public static readonly StyledProperty<string> TextProperty =
            AvaloniaProperty.Register<TokenSearchBox, string>(nameof(Text), defaultBindingMode: BindingMode.TwoWay);

        public static readonly StyledProperty<ObservableCollection<string>> SelectedTokensProperty =
            AvaloniaProperty.Register<TokenSearchBox, ObservableCollection<string>>(nameof(SelectedTokens));

        public static readonly StyledProperty<ObservableCollection<ITokenSuggestionProvider>> ProvidersProperty =
            AvaloniaProperty.Register<TokenSearchBox, ObservableCollection<ITokenSuggestionProvider>>(nameof(Providers));

        public static readonly StyledProperty<string> WatermarkProperty =
            AvaloniaProperty.Register<TokenSearchBox, string>(nameof(Watermark));

        public static readonly StyledProperty<ICommand> SearchCommandProperty =
            AvaloniaProperty.Register<TokenSearchBox, ICommand>(nameof(SearchCommand));

        public string Text {
            get => GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public ObservableCollection<string> SelectedTokens {
            get => GetValue(SelectedTokensProperty);
            set => SetValue(SelectedTokensProperty, value);
        }

        public ObservableCollection<ITokenSuggestionProvider> Providers {
            get => GetValue(ProvidersProperty);
            set => SetValue(ProvidersProperty, value);
        }

        public string Watermark {
            get => GetValue(WatermarkProperty);
            set => SetValue(WatermarkProperty, value);
        }

        public ICommand SearchCommand {
            get => GetValue(SearchCommandProperty);
            set => SetValue(SearchCommandProperty, value);
        }

        public TokenSearchBox() {
            SelectedTokens = new ObservableCollection<string>();
            Providers = new ObservableCollection<ITokenSuggestionProvider>();
        }

        private TextBox _textBox;
        private ListBox _tokensList;
        private Popup _popup;
        private ListBox _suggestionList;
        private CancellationTokenSource _cts;

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
            base.OnApplyTemplate(e);
            _textBox = e.NameScope.Find<TextBox>("PART_TextPresenter");
            if (_textBox != null) {
                _textBox.KeyDown += OnTextBoxKeyDown;
                _textBox.PropertyChanged += OnTextBoxPropertyChanged;
                _textBox.GotFocus += (s, e) => {
                    if (_tokensList != null) _tokensList.SelectedIndex = -1;
                    _ = UpdateSuggestionsAsync(Text ?? string.Empty);
                };
            }

            _tokensList = e.NameScope.Find<ListBox>("PART_TokensList");
            if (_tokensList != null) {
                _tokensList.KeyDown += OnTokensListKeyDown;
            }

            _popup = e.NameScope.Find<Popup>("PART_SuggestionsPopup");
            _suggestionList = e.NameScope.Find<ListBox>("PART_SuggestionsList");
            if (_suggestionList != null) {
                _suggestionList.PointerReleased += OnSuggestionPointerReleased;
            }
        }

        private void OnTokensListKeyDown(object sender, KeyEventArgs e) {
            if (_tokensList == null) return;

            if (e.Key == Key.Back || e.Key == Key.Delete || e.Key == Key.Enter) {
                if (_tokensList.SelectedIndex >= 0 && _tokensList.SelectedIndex < SelectedTokens.Count) {
                    var idx = _tokensList.SelectedIndex;
                    SelectedTokens.RemoveAt(idx);
                    
                    if (SelectedTokens.Count > 0) {
                        _tokensList.SelectedIndex = Math.Min(idx, SelectedTokens.Count - 1);
                        _tokensList.Focus();
                    } else {
                        _tokensList.SelectedIndex = -1;
                        _textBox?.Focus();
                    }
                    e.Handled = true;
                }
            } else if (e.Key == Key.Left) {
                if (_tokensList.SelectedIndex > 0) {
                    _tokensList.SelectedIndex--;
                }
                e.Handled = true;
            } else if (e.Key == Key.Right) {
                if (_tokensList.SelectedIndex < SelectedTokens.Count - 1) {
                    _tokensList.SelectedIndex++;
                } else {
                    _tokensList.SelectedIndex = -1;
                    _textBox?.Focus();
                }
                e.Handled = true;
            } else if (e.Key == Key.Escape) {
                _tokensList.SelectedIndex = -1;
                _textBox?.Focus();
                e.Handled = true;
            }
        }

        private void OnSuggestionPointerReleased(object sender, PointerReleasedEventArgs e) {
            var item = (e.Source as Visual)?.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
            if (item?.DataContext is TokenSuggestion suggestion) {
                CommitSuggestion(suggestion);
                e.Handled = true;
            }
        }

        private void CommitSuggestion(TokenSuggestion suggestion) {
            var currentText = Text ?? string.Empty;
            var isNegated = currentText.StartsWith("!");
            var checkStr = isNegated ? currentText.Substring(1) : currentText;
            
            ITokenSuggestionProvider matchedProvider = null;
            foreach (var p in Providers) {
                if (checkStr.StartsWith(p.Prefix, StringComparison.OrdinalIgnoreCase)) {
                    matchedProvider = p;
                    break;
                }
            }

            if (matchedProvider != null) {
                // Value selected for a prefix -> Complete token
                var prefixPart = isNegated ? "!" + matchedProvider.Prefix : matchedProvider.Prefix;
                AddToken(prefixPart + suggestion.Name);
            } else {
                // Prefix selected -> Append to textbox and keep typing
                var prefixPart = isNegated ? "!" + suggestion.Name : suggestion.Name;
                SetCurrentValue(TextProperty, prefixPart);
                if (_textBox != null) {
                    _textBox.Focus();
                    _textBox.CaretIndex = _textBox.Text.Length;
                }
                return; // Do not close popup, let PropertyChanged trigger new suggestions
            }
            
            if (_popup != null) _popup.IsOpen = false;
            _suggestionList.SelectedItem = null;
            _textBox?.Focus();
        }

        private async void OnTextBoxPropertyChanged(object sender, AvaloniaPropertyChangedEventArgs e) {
            if (e.Property == TextBox.TextProperty) {
                var val = Text ?? string.Empty;
                
                // GitHub style: Only trigger space commit if it's a FULL token (prefix + value) or an operator
                if (val.EndsWith(" ") && val.Trim().Length > 0) {
                    var trimmed = val.Trim();
                    if (trimmed == "|" || trimmed == "&") {
                        AddToken(trimmed);
                        return;
                    }

                    var isNegated = trimmed.StartsWith("!");
                    var checkStr = isNegated ? trimmed.Substring(1) : trimmed;

                    foreach (var provider in Providers) {
                        // Check if it starts with prefix AND has some value after it
                        if (checkStr.StartsWith(provider.Prefix, StringComparison.OrdinalIgnoreCase) && checkStr.Length > provider.Prefix.Length) {
                            AddToken(trimmed);
                            return;
                        }
                    }
                }

                // If not committed, update suggestions
                await UpdateSuggestionsAsync(val.TrimEnd());
            }
        }

        private async Task UpdateSuggestionsAsync(string text) {
            if (_popup == null || _suggestionList == null) return;

            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            if (string.IsNullOrEmpty(text)) {
                ShowDefaultProviders("", token);
                return;
            }

            var isNegated = text.StartsWith("!");
            var checkStr = isNegated ? text.Substring(1) : text;

            ITokenSuggestionProvider matchedProvider = null;
            foreach (var p in Providers) {
                if (checkStr.StartsWith(p.Prefix, StringComparison.OrdinalIgnoreCase)) {
                    matchedProvider = p;
                    break;
                }
            }

            if (matchedProvider != null) {
                var pattern = checkStr.Substring(matchedProvider.Prefix.Length);
                try {
                    var suggestions = await matchedProvider.GetSuggestionsAsync(pattern, token);
                    if (token.IsCancellationRequested) return;

                    var list = new List<TokenSuggestion>(suggestions);
                    if (list.Count > 0) {
                        _suggestionList.ItemsSource = list;
                        _popup.IsOpen = true;
                    } else {
                        _popup.IsOpen = false;
                    }
                } catch {
                    if (!token.IsCancellationRequested) {
                        _popup.IsOpen = false;
                    }
                }
            } else {
                ShowDefaultProviders(checkStr, token);
            }
        }

        private void ShowDefaultProviders(string pattern, CancellationToken token) {
            var defaultSuggestions = new List<TokenSuggestion>();
            foreach (var p in Providers) {
                if (string.IsNullOrEmpty(pattern) || p.Prefix.StartsWith(pattern, StringComparison.OrdinalIgnoreCase)) {
                    defaultSuggestions.Add(new TokenSuggestion { Name = p.Prefix, Description = p.Description });
                }
            }

            if (!token.IsCancellationRequested) {
                if (defaultSuggestions.Count > 0) {
                    _suggestionList.ItemsSource = defaultSuggestions;
                    _popup.IsOpen = true;
                } else {
                    _popup.IsOpen = false;
                }
            }
        }

        private void OnTextBoxKeyDown(object sender, KeyEventArgs e) {
            // Keyboard navigation for suggestions
            if (_popup?.IsOpen == true && _suggestionList != null) {
                if (e.Key == Key.Down) {
                    _suggestionList.SelectedIndex = Math.Min(_suggestionList.SelectedIndex + 1, _suggestionList.ItemCount - 1);
                    e.Handled = true;
                    return;
                } else if (e.Key == Key.Up) {
                    _suggestionList.SelectedIndex = Math.Max(_suggestionList.SelectedIndex - 1, 0);
                    e.Handled = true;
                    return;
                } else if (e.Key == Key.Enter) {
                    if (_suggestionList.SelectedItem is TokenSuggestion suggestion) {
                        CommitSuggestion(suggestion);
                        e.Handled = true;
                        return;
                    }
                } else if (e.Key == Key.Escape) {
                    _popup.IsOpen = false;
                    e.Handled = true;
                    return;
                }
            }

            // Normal text box logic
            if (e.Key == Key.Enter) {
                if (!string.IsNullOrEmpty(Text)) {
                    AddToken(Text.Trim());
                }
                SearchCommand?.Execute(null);
                if (_popup != null) _popup.IsOpen = false;
                e.Handled = true;
            } else if (e.Key == Key.Back && string.IsNullOrEmpty(Text) && SelectedTokens.Count > 0) {
                if (_tokensList != null) {
                    _tokensList.SelectedIndex = SelectedTokens.Count - 1;
                    _tokensList.Focus();
                } else {
                    SelectedTokens.RemoveAt(SelectedTokens.Count - 1);
                }
                if (_popup != null) _popup.IsOpen = false;
                e.Handled = true;
            }
        }

        public void AddToken(string token) {
            if (!SelectedTokens.Contains(token)) {
                SelectedTokens.Add(token);
            }
            SetCurrentValue(TextProperty, string.Empty);
            if (_popup != null) _popup.IsOpen = false;
        }

        public void RemoveToken(string token) {
            SelectedTokens.Remove(token);
        }
    }
}