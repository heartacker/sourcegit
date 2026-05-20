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
    public class TokenSlashSuggestionContext
    {
        public TokenSearchBox SearchBox { get; init; }
        public string CommandName { get; init; }
        public string RawArgument { get; init; }
        public IReadOnlyList<string> ArgumentTokens { get; init; }
        public int ActiveTokenIndex { get; init; }
        public string ActiveToken { get; init; }
        public bool EndsWithWhitespace { get; init; }
        public IReadOnlyList<string> SelectedTokens { get; init; }
        public IReadOnlyList<string> PersistentTokens { get; init; }
    }

    public class TokenSlashExecuteContext
    {
        public TokenSearchBox SearchBox { get; init; }
        public string CommandName { get; init; }
        public string RawArgument { get; init; }
        public IReadOnlyList<string> ArgumentTokens { get; init; }
        public IReadOnlyList<string> SelectedTokens { get; init; }
        public IReadOnlyList<string> PersistentTokens { get; init; }
    }

    public class TokenSlashCommand
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Icon { get; set; }
        public bool RequiresArgument { get; set; }
        public Func<TokenSlashSuggestionContext, IEnumerable<TokenSuggestion>> Suggest { get; set; }
        public Func<TokenSlashExecuteContext, bool> Execute { get; set; }
    }

    /// <summary>
    ///     提供类似 GitHub 风格的智能过滤搜索框，支持 Token 芯片化显示。
    ///
    ///     ===== 交互协议（维护约定） =====
    ///     该控件的行为由“建议上屏”和“Token 提交”两条路径组成，二者必须严格分离：
    ///
    ///     1) 建议弹窗优先级
    ///        - 当建议弹窗打开时，Enter/Tab 的含义是“提交建议到输入框（上屏）”，而不是直接生成 Token。
    ///        - 这样用户可以连续编辑，例如：先选出 a:acker，再继续输入 ||bo。
    ///
    ///     2) 普通 Enter 提交规则（弹窗关闭时）
    ///        - 若当前输入可以被规范化（例如 a:acker||bob -> a:acker||a:bob），本次 Enter 只做规范化改写，不提交。
    ///        - 若当前输入已经是规范形式（例如 a:acker 或 a:acker||a:bob），本次 Enter 直接提交为 Token。
    ///        - 这保证了“能一步提交就一步提交；需要补全前缀时先修正再提交”的用户体验。
    ///
    ///     3) Ctrl+Enter 强制提交规则
    ///        - Ctrl+Enter 永远按原样提交，不做 ||/&& 拆解。
    ///        - 例如 a:acker||bob 会整体作为一个 Token 提交。
    ///
    ///     4) || / && 后的建议继承规则
    ///        - 在 a:acker||... 或 a:acker&&... 中，后续段默认继承前一段的 provider。
    ///        - 继承不仅在空输入生效，也必须在非空输入生效。
    ///        - 例如输入 a:acker||bo 时，建议应继续来自作者 provider，并以 bo 作为过滤词。
    ///
    ///     5) 规范化仅属于控件层
    ///        - 控件层会为了交互体验做前缀补全与拆解（ExpandInlineSegmentsForControl）。
    ///        - 外部查询解析仍保留字面量语义，不应被隐式拆解污染。
    ///
    ///     6) 清除按钮三段优先级
    ///        - 第一步：清空输入文本。
    ///        - 第二步：删除临时 Token。
    ///        - 第三步：删除持久 Token。
    ///
    ///     7) 持久 Token 约束
    ///        - provider 可通过 IsPersistent 声明其 Token 为持久。
    ///        - 持久 Token 受 PersistentTokens / _persistentTokenSet 统一维护。
    ///        - 在 AutoGrouping 下，持久 Token 位于临时 Token 之前。
    ///
    ///     8) 当前控件的基础能力
    ///        - 自动归类 (AutoGrouping)：相同前缀 Token 自动排在一起。
    ///        - 流体气泡 (Merged Bubble)：同类项静默态视觉合并。
    ///        - 两段式删除：Backspace 首次选中 Token，再次删除/编辑。
    ///        - 异步建议：支持通过 Provider 增量查询建议。
    ///
    ///     9) 取反替换规则（Negation Replacement）
    ///        - AddToken 时自动检测是否存在相反版本（-b:main ↔ b:main）。
    ///        - 若存在则移除旧版本，确保同一值不会同时存在正负两种形态。
    ///        - 适用于所有前缀（a:, b:, t:, r:, m:, is: 等），对 ||/&& 运算符无效。
    ///
    ///     10) 建议交互机制（CommitSuggestion）
    ///        - 普通 Token 建议：点击/回车仅上屏到 TextBox，不直接 AddToken。
    ///        - Slash 命令建议：点击/回车直接执行命令分支（内置或外部回调）。
    ///        - 对声明 RequiresArgument 的外部命令，若参数为空则进入“参数输入态”。
    ///          例如选中 /ui 后会变成 "/ui " 并继续弹出参数建议，而不是直接执行。
    ///
    ///     11) Token 删除按钮（PART_DeleteButton）
    ///        - 每个 Token 气泡右侧悬停显示 × 删除按钮。
    ///        - 仅在 hover 或 selected 时可见。
    ///        - 运算符 Token（||, &&）强制隐藏删除按钮。
    ///
    ///     12) 运算符 Token（||, &&）
    ///        - 作为独立 Token 芯片插入，不可被删除按钮删除。
    ///        - 视觉上加粗淡化（FontWeight Bold + Opacity 0.5）。
    ///        - 排序时与普通 Token 互斥，不能出现在 AutoGrouping 的同类块中。
    ///
    ///     13) Backspace 交互协议
    ///        - 输入为空时按 Backspace：若选中了 Token 则删除，否则选中末尾 Token。
    ///        - 已选中 Token 时按 Backspace：直接删除该 Token。
    ///        - Ctrl+Backspace：无论是否选中，直接删除末尾 Token。
    ///        - 删除持久 Token 时会同步更新 PersistentTokens 和 _persistentTokenSet。
    ///
    ///     14) 气泡合并与视觉状态（Merged Bubble）
    ///        - 相邻同 provider 的 Token 在非交互状态下合并为一个连续气泡。
    ///        - 合并时按位置分 Start / Middle / Operator / End 四种圆角状态。
    ///        - TokenSearchBox:not(:pointerover):not(:focus-within) 时触发合并。
    ///        - hover 或 focus 时气泡展开为独立圆角，便于定位和删除。
    ///
    ///     15) Provider 视觉定制
    ///        - 每个 provider 可配置图标（Icon）、颜色（由 TokenToColorConverter 映射）。
    ///        - 持久 Token 使用实心背景色（TokenToBackgroundConverter），临时 Token 仅边框色。
    ///        - 颜色映射为语义哈希：相同前缀的 Token 颜色一致。
    ///
    ///     16) 建议弹窗（PART_SuggestionsPopup）
    ///        - Provider 可返回 TokenSuggestionHeader（分组标题）和 TokenSuggestion（可选条目）。
    ///        - header 显示分组名（粗体+小字+灰色），条目显示图标+名称+描述。
    ///        - 弹窗支持键盘导航（↑↓ 选择，Enter/Tab 上屏）。
    ///        - 点击弹窗外区域自动关闭（IsLightDismissEnabled）。
    ///
    ///     17) 清除按钮三段优先级
    ///        - 输入框非空时：清除文本。
    ///        - 文本为空时：删除最后一个临时 Token。
    ///        - 无临时 Token 时：删除最后一个持久 Token。
    ///        - 搜索图标与清除按钮共享位置，根据状态切换可见性。
    ///
    ///     18) 点击外部处理（PointerPressed / LightDismiss）
    ///        - 点击控件外部区域自动关闭弹窗并取消 Token 选中状态。
    ///        - 点击控件本身聚焦输入框但不拦截事件传递。
    ///
    ///     19) AutoCompact 自动精简前缀
    ///        - 开启后，输入 alias 前缀（如 branch:xxx）自动转为短前缀（b:xxx）。
    ///        - 作用于 AddToken 入口，确保 SelectedTokens 中始终使用最短规范前缀。
    ///        - 默认 true，可通过 AutoCompactProperty 关闭。
    ///
    ///     20) 斜杠命令系统（/）
    ///        - 输入 / 进入命令模式，建议列表展示可执行命令。
    ///        - /-  是内置命令：用于删除当前已存在 Token（支持 /-a:、/-main 过滤）。
    ///        - /-/ 是内置命令：用于删除当前已存在某一类 Token（支持 /-/a 过滤，过滤项为 prefix）。
    ///        - /-/temp：清理所有临时 Token；/-/persistent：清理所有持久 Token。
    ///        - 外部可通过 SlashCommands 或 AddSlashCommand 注册 /hl、/st 等命令。
    ///        - 无参数命令：选中或回车可直接执行，执行后清空输入。
    ///        - 需要参数的命令：若命令声明 RequiresArgument 且参数为空，先进入 "/cmd " 参数输入态。
    ///        - 外部命令补全由 Suggest(context) 提供，支持二级/三级参数分段智能提示。
    ///        - Enter/Tab/点击建议项在命令模式下不会新增普通 Token。
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

        public static readonly StyledProperty<ObservableCollection<string>> PersistentTokensProperty =
            AvaloniaProperty.Register<TokenSearchBox, ObservableCollection<string>>(nameof(PersistentTokens));

        public static readonly StyledProperty<ObservableCollection<ITokenSuggestionProvider>> ProvidersProperty =
            AvaloniaProperty.Register<TokenSearchBox, ObservableCollection<ITokenSuggestionProvider>>(nameof(Providers));

        public static readonly StyledProperty<ObservableCollection<TokenSlashCommand>> SlashCommandsProperty =
            AvaloniaProperty.Register<TokenSearchBox, ObservableCollection<TokenSlashCommand>>(nameof(SlashCommands));

        // 1. 定义 PlaceholderText 属性 (类型是 string)
        public static readonly StyledProperty<string> PlaceholderTextProperty =
            AvaloniaProperty.Register<TokenSearchBox, string>(nameof(PlaceholderText));


        public static readonly StyledProperty<int> MaxRowsProperty =
            AvaloniaProperty.Register<TokenSearchBox, int>(nameof(MaxRows), 3);

        /// <summary>
        ///     是否开启自动归类：开启后，相同前缀的 Token 将被自动排列在一起。
        /// </summary>
        public static readonly StyledProperty<bool> AutoGroupingProperty =
            AvaloniaProperty.Register<TokenSearchBox, bool>(nameof(AutoGrouping), true);

        /// <summary>
        ///     是否开启自动精简前缀：开启后，输入 branch:xxx 自动转为 b:xxx。
        /// </summary>
        public static readonly StyledProperty<bool> AutoCompactProperty =
            AvaloniaProperty.Register<TokenSearchBox, bool>(nameof(AutoCompact), true);

        /// <summary>
        ///     内部用于动态计算 ScrollViewer 的最大高度。
        /// </summary>
        public static readonly StyledProperty<double> MaxListHeightProperty =
            AvaloniaProperty.Register<TokenSearchBox, double>(nameof(MaxListHeight), 96.0);

        public static readonly StyledProperty<ICommand> SearchCommandProperty =
            AvaloniaProperty.Register<TokenSearchBox, ICommand>(nameof(SearchCommand));

        public static readonly StyledProperty<ICommand> TokenDoubleClickCommandProperty =
            AvaloniaProperty.Register<TokenSearchBox, ICommand>(nameof(TokenDoubleClickCommand));

        public static readonly StyledProperty<ICommand> SuggestionActionCommandProperty =
            AvaloniaProperty.Register<TokenSearchBox, ICommand>(nameof(SuggestionActionCommand));
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

        public ObservableCollection<string> PersistentTokens
        {
            get => GetValue(PersistentTokensProperty);
            set => SetValue(PersistentTokensProperty, value);
        }

        public ObservableCollection<ITokenSuggestionProvider> Providers
        {
            get => GetValue(ProvidersProperty);
            set => SetValue(ProvidersProperty, value);
        }

        public ObservableCollection<TokenSlashCommand> SlashCommands
        {
            get => GetValue(SlashCommandsProperty);
            set => SetValue(SlashCommandsProperty, value);
        }

        public string PlaceholderText
        {
            get => GetValue(PlaceholderTextProperty);
            set => SetValue(PlaceholderTextProperty, value);
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

        public bool AutoCompact
        {
            get => GetValue(AutoCompactProperty);
            set => SetValue(AutoCompactProperty, value);
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

        public ICommand SuggestionActionCommand
        {
            get => GetValue(SuggestionActionCommandProperty);
            set => SetValue(SuggestionActionCommandProperty, value);
        }
        #endregion

        public TokenSearchBox()
        {
            SetCurrentValue(SelectedTokensProperty, new ObservableCollection<string>());
            SetCurrentValue(PersistentTokensProperty, new ObservableCollection<string>());
            SetCurrentValue(ProvidersProperty, new ObservableCollection<ITokenSuggestionProvider>());
            SetCurrentValue(SlashCommandsProperty, new ObservableCollection<TokenSlashCommand>());
            SetCurrentValue(SuggestionActionCommandProperty, new App.Command(parameter =>
            {
                if (parameter is TokenSuggestion suggestion)
                    CommitSuggestion(suggestion, true);
            }));
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
        private readonly HashSet<string> _persistentTokenSet = new(StringComparer.OrdinalIgnoreCase);

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
                _tokensList.LostFocus += (s, ev) =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        // 仅当焦点离开整个控件时清除选中，避免失焦后残留“放大”视觉。
                        if ((_textBox?.IsKeyboardFocusWithin ?? false) || (_tokensList?.IsKeyboardFocusWithin ?? false) || (_suggestionList?.IsKeyboardFocusWithin ?? false))
                            return;

                        _tokensList.SelectedIndex = -1;
                    }, DispatcherPriority.Input);
                };
            }

            _popup = e.NameScope.Find<Popup>("PART_SuggestionsPopup");
            _suggestionList = e.NameScope.Find<ListBox>("PART_SuggestionsList");
            if (_suggestionList != null)
            {
                _suggestionList.PointerReleased += OnSuggestionPointerReleased;
            }

            _rootBorder = e.NameScope.Find<Border>("PART_RootBorder");

            _clearButton = e.NameScope.Find<Button>("PART_ClearButton");
            if (_clearButton != null)
            {
                _clearButton.Click += (s, ev) =>
                {
                    ClearByPriority();
                    _textBox?.Focus();
                };

                if (SelectedTokens != null)
                    SelectedTokens.CollectionChanged += (_, _) => UpdateClearButtonVisibility();
                if (PersistentTokens != null)
                    PersistentTokens.CollectionChanged += (_, _) => UpdateClearButtonVisibility();
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
        }

        #region Public API
        /// <summary>
        ///     直接移除指定的 Token 字符串。
        /// </summary>
        public void RemoveToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return;

            SelectedTokens.Remove(token);
            if (_persistentTokenSet.Contains(token))
            {
                _persistentTokenSet.Remove(token);
                PersistentTokens.Remove(token);
            }
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
                    var removed = SelectedTokens[i];
                    SelectedTokens.RemoveAt(i);
                    if (_persistentTokenSet.Contains(removed))
                    {
                        _persistentTokenSet.Remove(removed);
                        PersistentTokens.Remove(removed);
                    }
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        ///     将焦点设置到内部输入框，确保可立即输入关键字。
        /// </summary>
        public void FocusSearchTextBox(NavigationMethod navigationMethod = NavigationMethod.Directional)
        {
            if (_textBox != null)
            {
                _textBox.Focus(navigationMethod);
                return;
            }

            Focus(navigationMethod);
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
                if (includeNegated && check.StartsWith("-", StringComparison.Ordinal))
                    check = check[1..];

                if (check.StartsWith(prefix, comparison))
                {
                    var removed = SelectedTokens[i];
                    SelectedTokens.RemoveAt(i);
                    if (_persistentTokenSet.Contains(removed))
                    {
                        _persistentTokenSet.Remove(removed);
                        PersistentTokens.Remove(removed);
                    }
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
                    if (includeNegated && check.StartsWith("-", StringComparison.Ordinal))
                        check = check[1..];

                    return check.StartsWith(prefix, comparison);
                })
                .ToList();
        }

        public IReadOnlyList<string> QueryPersistentTokens(string prefix = null, bool includeNegated = true, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
        {
            if (PersistentTokens == null || PersistentTokens.Count == 0)
                return Array.Empty<string>();

            if (string.IsNullOrWhiteSpace(prefix))
                return PersistentTokens.ToList();

            return PersistentTokens
                .Where(t =>
                {
                    var check = t;
                    if (includeNegated && check.StartsWith("-", StringComparison.Ordinal))
                        check = check[1..];

                    return check.StartsWith(prefix, comparison);
                })
                .ToList();
        }

        public void RestorePersistentTokens(IEnumerable<string> tokens, bool clearExisting = true)
        {
            if (clearExisting)
            {
                foreach (var old in PersistentTokens.ToList())
                {
                    _persistentTokenSet.Remove(old);
                    SelectedTokens.Remove(old);
                }
                PersistentTokens.Clear();
            }

            if (tokens == null)
                return;

            foreach (var token in tokens)
            {
                if (string.IsNullOrWhiteSpace(token))
                    continue;

                var trimmed = token.Trim();
                if (_persistentTokenSet.Add(trimmed))
                {
                    PersistentTokens.Add(trimmed);
                    if (!SelectedTokens.Contains(trimmed))
                        SelectedTokens.Insert(0, trimmed);
                }
            }
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
                else
                {
                    _tokensList.SelectedIndex = -1;
                    _textBox?.Focus();
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
            else
            {
                _textBox?.Focus();
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
            var sourceVisual = e.Source as Visual;
            var item = sourceVisual?.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
            if (item?.DataContext is TokenSuggestion suggestion)
            {
                var fromActionButton = sourceVisual is Button btn && btn.Name == "PART_SuggestionActionButton";
                if (!fromActionButton && sourceVisual != null)
                {
                    fromActionButton = sourceVisual
                        .GetVisualAncestors()
                        .OfType<Button>()
                        .Any(b => b.Name == "PART_SuggestionActionButton");
                }

                if (!fromActionButton)
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

        private static bool IsOperatorToken(string token)
        {
            return token == "||" || token == "&&" || token == "|" || token == "&" || token == "(" || token == ")";
        }

        private static bool TryFindLastOperator(string text, out int opStart, out int opLength)
        {
            opStart = -1;
            opLength = 0;

            if (string.IsNullOrEmpty(text))
                return false;

            var idxOr = text.LastIndexOf("||", StringComparison.Ordinal);
            var idxAnd = text.LastIndexOf("&&", StringComparison.Ordinal);

            if (idxOr < 0 && idxAnd < 0)
                return false;

            if (idxOr >= idxAnd)
            {
                opStart = idxOr;
                opLength = 2;
            }
            else
            {
                opStart = idxAnd;
                opLength = 2;
            }

            return true;
        }

        private static string GetCurrentSegment(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            if (TryFindLastOperator(text, out var start, out var len))
                return text[(start + len)..].TrimStart();

            return text.TrimStart();
        }

        private static string NormalizePrefixKey(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            var s = raw.Trim();
            if (s.StartsWith("-", StringComparison.Ordinal))
                s = s[1..];
            if (s.StartsWith("/", StringComparison.Ordinal))
                s = s[1..];

            var idx = s.IndexOf(':');
            if (idx >= 0)
                return s[..(idx + 1)].ToLowerInvariant();

            return (s + ":").ToLowerInvariant();
        }

        private static bool TryParsePrefixClassRemoveArgument(string arg, out string prefixKey)
        {
            prefixKey = string.Empty;
            if (string.IsNullOrWhiteSpace(arg))
                return false;

            var trimmed = arg.Trim();
            if (!trimmed.StartsWith("/", StringComparison.Ordinal))
                return false;

            prefixKey = NormalizePrefixKey(trimmed);
            return !string.IsNullOrEmpty(prefixKey);
        }

        private static bool TryParseSlashCommandSegment(string text, out string commandName, out string argument)
        {
            commandName = null;
            argument = string.Empty;

            var segment = GetCurrentSegment(text);
            if (!segment.StartsWith("/", StringComparison.Ordinal))
                return false;

            if (segment.StartsWith("/-/", StringComparison.Ordinal))
            {
                commandName = "-/";
                argument = segment.Length > 3 ? segment[3..].Trim() : string.Empty;
                return true;
            }

            if (segment.StartsWith("/-", StringComparison.Ordinal))
            {
                commandName = "-";
                argument = segment.Length > 2 ? segment[2..].Trim() : string.Empty;
                return true;
            }

            var body = segment.Length > 1 ? segment[1..] : string.Empty;
            if (string.IsNullOrWhiteSpace(body))
            {
                commandName = string.Empty;
                return true;
            }

            var spaceIdx = body.IndexOf(' ');
            if (spaceIdx < 0)
            {
                commandName = body.Trim();
                return true;
            }

            commandName = body[..spaceIdx].Trim();
            argument = body[(spaceIdx + 1)..].TrimStart();
            return true;
        }

        private static List<string> SplitSlashArguments(string rawArgument)
        {
            if (string.IsNullOrWhiteSpace(rawArgument))
                return [];

            return rawArgument
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }

        private TokenSlashSuggestionContext BuildSlashSuggestionContext(string commandName, string rawArgument)
        {
            var raw = rawArgument ?? string.Empty;
            var tokens = SplitSlashArguments(raw);
            var endsWithWhitespace = raw.EndsWith(' ');

            int activeTokenIndex;
            string activeToken;
            if (tokens.Count == 0)
            {
                activeTokenIndex = 0;
                activeToken = string.Empty;
            }
            else if (endsWithWhitespace)
            {
                activeTokenIndex = tokens.Count;
                activeToken = string.Empty;
            }
            else
            {
                activeTokenIndex = tokens.Count - 1;
                activeToken = tokens[^1];
            }

            return new TokenSlashSuggestionContext
            {
                SearchBox = this,
                CommandName = commandName,
                RawArgument = raw,
                ArgumentTokens = tokens,
                ActiveTokenIndex = activeTokenIndex,
                ActiveToken = activeToken,
                EndsWithWhitespace = endsWithWhitespace,
                SelectedTokens = SelectedTokens?.ToList() ?? [],
                PersistentTokens = PersistentTokens?.ToList() ?? [],
            };
        }

        private TokenSlashExecuteContext BuildSlashExecuteContext(string commandName, string rawArgument)
        {
            var raw = rawArgument ?? string.Empty;
            return new TokenSlashExecuteContext
            {
                SearchBox = this,
                CommandName = commandName,
                RawArgument = raw,
                ArgumentTokens = SplitSlashArguments(raw),
                SelectedTokens = SelectedTokens?.ToList() ?? [],
                PersistentTokens = PersistentTokens?.ToList() ?? [],
            };
        }

        public bool AddSlashCommand(TokenSlashCommand command, bool replaceExisting = false)
        {
            if (command == null || string.IsNullOrWhiteSpace(command.Name))
                return false;

            var normalizedName = command.Name.Trim().TrimStart('/');
            if (string.Equals(normalizedName, "-", StringComparison.Ordinal) ||
                string.Equals(normalizedName, "-/", StringComparison.Ordinal))
                return false;

            var existing = SlashCommands.FirstOrDefault(c => string.Equals(c.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                if (!replaceExisting)
                    return false;

                SlashCommands.Remove(existing);
            }

            command.Name = normalizedName;
            SlashCommands.Add(command);
            return true;
        }

        public bool RemoveSlashCommand(string commandName)
        {
            if (string.IsNullOrWhiteSpace(commandName))
                return false;

            var normalizedName = commandName.Trim().TrimStart('/');
            var existing = SlashCommands.FirstOrDefault(c => string.Equals(c.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
                return false;

            SlashCommands.Remove(existing);
            return true;
        }

        private IEnumerable<TokenSlashCommand> EnumerateSlashCommands()
        {
            yield return new TokenSlashCommand
            {
                Name = "-",
                Description = "移除已存在 Token",
                RequiresArgument = true,
            };

            yield return new TokenSlashCommand
            {
                Name = "-/",
                Description = "按前缀批量移除 Token（示例：/-/a）",
                RequiresArgument = true,
            };

            if (SlashCommands == null)
                yield break;

            foreach (var cmd in SlashCommands)
            {
                if (cmd == null || string.IsNullOrWhiteSpace(cmd.Name))
                    continue;

                var normalizedName = cmd.Name.Trim().TrimStart('/');
                if (string.Equals(normalizedName, "-", StringComparison.Ordinal))
                    continue;

                yield return new TokenSlashCommand
                {
                    Name = normalizedName,
                    Description = cmd.Description,
                    Icon = cmd.Icon,
                    RequiresArgument = cmd.RequiresArgument,
                    Suggest = cmd.Suggest,
                    Execute = cmd.Execute,
                };
            }
        }

        private bool ExecuteExternalSlashCommand(string commandName, string argument)
        {
            var command = SlashCommands?.FirstOrDefault(
                c => string.Equals(c?.Name?.Trim().TrimStart('/'),
                commandName, StringComparison.OrdinalIgnoreCase));

            if (command?.Execute == null)
                return false;

            try
            {
                return command.Execute(BuildSlashExecuteContext(commandName, argument));
            }
            catch
            {
                return false;
            }
        }

        private TokenSlashCommand GetExternalSlashCommand(string commandName)
        {
            if (string.IsNullOrWhiteSpace(commandName))
                return null;

            return SlashCommands?.FirstOrDefault(
                c => string.Equals(c?.Name?.Trim().TrimStart('/'),
                    commandName, StringComparison.OrdinalIgnoreCase));
        }

        private List<string> GetKnownTokenPrefixes()
        {
            var prefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (Providers != null)
            {
                foreach (var provider in Providers)
                {
                    if (!string.IsNullOrWhiteSpace(provider?.Prefix))
                    {
                        var p = provider.Prefix.Trim().TrimEnd(':');
                        if (!string.IsNullOrWhiteSpace(p))
                            prefixes.Add(p);
                    }

                    if (provider?.FullPrefix != null)
                    {
                        foreach (var alias in provider.FullPrefix)
                        {
                            var p = alias?.Trim().TrimEnd(':');
                            if (!string.IsNullOrWhiteSpace(p))
                                prefixes.Add(p);
                        }
                    }
                }
            }

            foreach (var token in SelectedTokens)
            {
                if (string.IsNullOrWhiteSpace(token) || IsOperatorToken(token))
                    continue;

                var check = token.StartsWith("-", StringComparison.Ordinal) ? token[1..] : token;
                var idx = check.IndexOf(':');
                if (idx > 0)
                {
                    var p = check[..idx].Trim();
                    if (!string.IsNullOrWhiteSpace(p))
                        prefixes.Add(p);
                }
            }

            return prefixes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private int DeleteTemporaryTokensByCommand()
        {
            var temporary = SelectedTokens
                .Where(t => !_persistentTokenSet.Contains(t))
                .ToList();

            foreach (var token in temporary)
                SelectedTokens.Remove(token);

            return temporary.Count;
        }

        private int DeletePersistentTokensByCommand()
        {
            var persistent = PersistentTokens.ToList();
            foreach (var token in persistent)
                SelectedTokens.Remove(token);

            PersistentTokens.Clear();
            _persistentTokenSet.Clear();
            return persistent.Count;
        }

        private int DeleteTokensByPrefixCommand(string argument)
        {
            var raw = (argument ?? string.Empty).Trim().TrimStart('/').Trim();
            raw = raw.TrimEnd(':').Trim();
            if (string.IsNullOrEmpty(raw))
                return 0;

            if (raw.Equals("temp", StringComparison.OrdinalIgnoreCase) ||
                raw.Equals("temporary", StringComparison.OrdinalIgnoreCase))
                return DeleteTemporaryTokensByCommand();

            if (raw.Equals("persistent", StringComparison.OrdinalIgnoreCase) ||
                raw.Equals("persist", StringComparison.OrdinalIgnoreCase))
                return DeletePersistentTokensByCommand();

            var knownPrefixes = GetKnownTokenPrefixes();
            var matchedPrefixes = knownPrefixes
                .Where(p => p.Equals(raw, StringComparison.OrdinalIgnoreCase))
                .Select(p => p + ":")
                .ToList();

            if (matchedPrefixes.Count == 0)
                matchedPrefixes.Add(raw + ":");

            var toRemove = SelectedTokens
                .Where(t => !IsOperatorToken(t))
                .Where(t =>
                {
                    var check = t.StartsWith("-", StringComparison.Ordinal) ? t[1..] : t;
                    return matchedPrefixes.Any(prefix => check.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                })
                .ToList();

            foreach (var token in toRemove)
                RemoveToken(token);

            return toRemove.Count;
        }

        private static string BuildTextWithCurrentSegmentReplaced(string originalText, string newSegment)
        {
            if (string.IsNullOrEmpty(originalText))
                return newSegment;

            if (TryFindLastOperator(originalText, out var start, out var len))
            {
                var left = originalText[..(start + len)];
                return left + newSegment;
            }

            return newSegment;
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

        private string BuildSlashSuggestionReplacementText(TokenSuggestion suggestion)
        {
            var cmd = suggestion?.SlashCommandName?.Trim() ?? string.Empty;
            var arg = suggestion?.SlashCommandArgument?.Trim() ?? string.Empty;

            string replacement;
            if (!string.IsNullOrWhiteSpace(suggestion?.Name) && suggestion.Name.StartsWith("/", StringComparison.Ordinal))
            {
                replacement = suggestion.Name;
            }
            else if (string.Equals(cmd, "-", StringComparison.Ordinal))
            {
                replacement = string.IsNullOrWhiteSpace(arg) ? "/-" : $"/-{arg}";
            }
            else if (string.Equals(cmd, "-/", StringComparison.Ordinal))
            {
                replacement = string.IsNullOrWhiteSpace(arg) ? "/-/" : $"/-/{arg}";
            }
            else
            {
                var external = GetExternalSlashCommand(cmd);
                if (string.IsNullOrWhiteSpace(arg))
                    replacement = external?.RequiresArgument == true ? $"/{cmd} " : $"/{cmd}";
                else
                    replacement = $"/{cmd} {arg}";
            }

            return BuildTextWithCurrentSegmentReplaced(Text ?? string.Empty, replacement);
        }

        // 普通点击=上屏；动作按钮点击=执行。
        private void CommitSuggestion(TokenSuggestion suggestion, bool requestExecute = false)
        {
            var currentText = Text ?? string.Empty;

            if (suggestion?.IsSlashCommand == true)
            {
                if (!requestExecute || !suggestion.CanExecuteDirectly)
                {
                    var replacement = BuildSlashSuggestionReplacementText(suggestion);
                    SetCurrentValue(TextProperty, replacement);
                    if (_textBox != null)
                    {
                        _textBox.Focus();
                        _textBox.CaretIndex = _textBox.Text.Length;
                    }

                    _suggestionList.SelectedItem = null;
                    _ = UpdateSuggestionsAsync(Text ?? string.Empty);
                    return;
                }

                var cmd = suggestion.SlashCommandName?.Trim();
                if (string.Equals(cmd, "-", StringComparison.Ordinal))
                {
                    var tokenToRemove = suggestion.SlashCommandArgument?.Trim();
                    if (!string.IsNullOrWhiteSpace(tokenToRemove))
                    {
                        if (tokenToRemove.StartsWith("prefix:", StringComparison.OrdinalIgnoreCase))
                        {
                            var key = tokenToRemove[7..].Trim();
                            if (!string.IsNullOrWhiteSpace(key))
                                DeleteTokensByPrefix(key, includeNegated: true);
                        }
                        else
                        {
                            RemoveToken(tokenToRemove);
                        }

                        SetCurrentValue(TextProperty, string.Empty);
                        if (_textBox != null)
                        {
                            _textBox.Focus();
                            _textBox.CaretIndex = _textBox.Text.Length;
                        }

                        _suggestionList.SelectedItem = null;
                        if (_popup != null)
                            _popup.IsOpen = false;
                        return;
                    }

                    // Picking '/-' from command palette switches into remove mode.
                    SetCurrentValue(TextProperty, "/-");
                    if (_textBox != null)
                    {
                        _textBox.Focus();
                        _textBox.CaretIndex = _textBox.Text.Length;
                    }

                    _suggestionList.SelectedItem = null;
                    _ = UpdateSuggestionsAsync(Text ?? string.Empty);
                    return;
                }
                else if (string.Equals(cmd, "-/", StringComparison.Ordinal))
                {
                    var removed = DeleteTokensByPrefixCommand(suggestion.SlashCommandArgument);
                    if (removed > 0)
                    {
                        SetCurrentValue(TextProperty, string.Empty);
                        if (_textBox != null)
                        {
                            _textBox.Focus();
                            _textBox.CaretIndex = _textBox.Text.Length;
                        }

                        _suggestionList.SelectedItem = null;
                        if (_popup != null)
                            _popup.IsOpen = false;
                        return;
                    }

                    // Picking '/-/' from command palette switches into prefix-remove mode.
                    SetCurrentValue(TextProperty, "/-/");
                    if (_textBox != null)
                    {
                        _textBox.Focus();
                        _textBox.CaretIndex = _textBox.Text.Length;
                    }

                    _suggestionList.SelectedItem = null;
                    _ = UpdateSuggestionsAsync(Text ?? string.Empty);
                    return;
                }
                else if (!string.IsNullOrWhiteSpace(cmd))
                {
                    var external = GetExternalSlashCommand(cmd);
                    var arg = suggestion.SlashCommandArgument?.Trim() ?? string.Empty;
                    if (external?.RequiresArgument == true && string.IsNullOrEmpty(arg))
                    {
                        // Command requires arguments: enter argument mode first.
                        SetCurrentValue(TextProperty, $"/{cmd} ");
                        if (_textBox != null)
                        {
                            _textBox.Focus();
                            _textBox.CaretIndex = _textBox.Text.Length;
                        }

                        _suggestionList.SelectedItem = null;
                        _ = UpdateSuggestionsAsync(Text ?? string.Empty);
                        return;
                    }

                    ExecuteExternalSlashCommand(cmd, suggestion.SlashCommandArgument);
                }

                SetCurrentValue(TextProperty, string.Empty);
                if (_textBox != null)
                {
                    _textBox.Focus();
                    _textBox.CaretIndex = _textBox.Text.Length;
                }

                _suggestionList.SelectedItem = null;
                if (_popup != null)
                    _popup.IsOpen = false;
                return;
            }

            var segment = GetCurrentSegment(currentText);
            var isNegated = segment.StartsWith("-");
            var checkStr = isNegated ? segment.Substring(1) : segment;
            var insertValue = suggestion?.InsertValue ?? suggestion?.Name ?? string.Empty;

            var matchedProvider = MatchProvider(Providers, checkStr, out var matchedPrefix);

            string replacementText;
            if (matchedProvider != null)
            {
                var prefixPart = isNegated ? "-" + matchedPrefix : matchedPrefix;
                replacementText = BuildTextWithCurrentSegmentReplaced(currentText, prefixPart + insertValue);
            }
            else
            {
                var prefixPart = isNegated ? "-" + insertValue : insertValue;
                replacementText = BuildTextWithCurrentSegmentReplaced(currentText, prefixPart);
            }

            if (requestExecute && suggestion.CanExecuteDirectly)
            {
                var executeText = replacementText;
                var normalized = NormalizeInlineExpressionForControl(executeText);
                if (!string.Equals(normalized, executeText, StringComparison.Ordinal))
                    executeText = normalized;

                CommitTextAsToken(executeText);
                SetCurrentValue(TextProperty, string.Empty);
                if (_textBox != null)
                {
                    _textBox.Focus();
                    _textBox.CaretIndex = _textBox.Text.Length;
                }

                _suggestionList.SelectedItem = null;
                if (_popup != null)
                    _popup.IsOpen = false;
                return;
            }

            SetCurrentValue(TextProperty, replacementText);
            if (_textBox != null)
            {
                _textBox.Focus();
                _textBox.CaretIndex = _textBox.Text.Length;
            }

            _suggestionList.SelectedItem = null;
            // 不主动关闭弹窗，让 OnTextBoxPropertyChanged 触发 UpdateSuggestionsAsync
            // 按“上屏后的新文本”重新拉取建议（比如从 provider 列表切到作者列表）。
        }

        private void ShowRemoveTokenSuggestions(string pattern)
        {
            if (_popup == null || _suggestionList == null)
                return;

            var normalized = pattern?.Trim() ?? string.Empty;

            if (TryParsePrefixClassRemoveArgument(normalized, out var prefixFilter))
            {
                var prefixGroups = SelectedTokens
                    .Where(t => !IsOperatorToken(t))
                    .Select(t => NormalizePrefixKey(t))
                    .Where(p => !string.IsNullOrEmpty(p))
                    .GroupBy(p => p)
                    .Select(g => new { Prefix = g.Key, Count = g.Count() })
                    .Where(x => string.IsNullOrEmpty(prefixFilter) || x.Prefix.Contains(prefixFilter, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x.Prefix, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (prefixGroups.Count == 0)
                {
                    _popup.IsOpen = false;
                    return;
                }

                var grouped = new List<object>
                {
                    new TokenSuggestionHeader { Name = "移除 Token 前缀类 (/-/)" }
                };

                foreach (var g in prefixGroups)
                {
                    grouped.Add(new TokenSuggestion
                    {
                        Name = g.Prefix,
                        Description = $"{g.Count} 项 · 回车/点击批量删除",
                        IsSlashCommand = true,
                        SlashCommandName = "-",
                        SlashCommandArgument = $"prefix:{g.Prefix}",
                    });
                }

                _suggestionList.ItemsSource = grouped;
                _popup.IsOpen = true;
                _suggestionList.SelectedIndex = grouped.Count > 1 ? 1 : -1;
                return;
            }

            var tokens = SelectedTokens
                .Where(t => !IsOperatorToken(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(t => string.IsNullOrEmpty(normalized) || t.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(t => IsPersistentToken(t))
                .ThenBy(t => t, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (tokens.Count == 0)
            {
                _popup.IsOpen = false;
                return;
            }

            var flatList = new List<object>
            {
                new TokenSuggestionHeader { Name = "移除 Token (/-)" }
            };

            foreach (var token in tokens)
            {
                var desc = IsPersistentToken(token) ? "持久 Token · 回车/点击删除" : "临时 Token · 回车/点击删除";
                flatList.Add(new TokenSuggestion
                {
                    Name = token,
                    Description = desc,
                    IsSlashCommand = true,
                    SlashCommandName = "-",
                    SlashCommandArgument = token,
                    CanExecuteDirectly = true,
                    ActionType = TokenSuggestionActionType.Execute,
                });
            }

            _suggestionList.ItemsSource = flatList;
            _popup.IsOpen = true;
            _suggestionList.SelectedIndex = flatList.Count > 1 ? 1 : -1;
        }

        private void ShowRemovePrefixSuggestions(string pattern)
        {
            if (_popup == null || _suggestionList == null)
                return;

            var normalized = (pattern ?? string.Empty).Trim().TrimStart('/').Trim();
            normalized = normalized.TrimEnd(':').Trim();

            var optionSuggestions = new List<TokenSuggestion>();
            if (string.IsNullOrEmpty(normalized) || "temp".Contains(normalized, StringComparison.OrdinalIgnoreCase))
            {
                optionSuggestions.Add(new TokenSuggestion
                {
                    Name = "/-/temp",
                    Description = "清理全部临时 Token",
                    IsSlashCommand = true,
                    SlashCommandName = "-/",
                    SlashCommandArgument = "temp",
                    CanExecuteDirectly = true,
                    ActionType = TokenSuggestionActionType.Execute,
                });
            }

            if (string.IsNullOrEmpty(normalized) || "persistent".Contains(normalized, StringComparison.OrdinalIgnoreCase))
            {
                optionSuggestions.Add(new TokenSuggestion
                {
                    Name = "/-/persistent",
                    Description = "清理全部持久 Token",
                    IsSlashCommand = true,
                    SlashCommandName = "-/",
                    SlashCommandArgument = "persistent",
                    CanExecuteDirectly = true,
                    ActionType = TokenSuggestionActionType.Execute,
                });
            }

            var prefixes = GetKnownTokenPrefixes()
                .Where(p => string.IsNullOrEmpty(normalized) || p.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (prefixes.Count == 0 && optionSuggestions.Count == 0)
            {
                _popup.IsOpen = false;
                return;
            }

            var flatList = new List<object>
            {
                new TokenSuggestionHeader { Name = "批量移除前缀 (/-/)" }
            };

            foreach (var option in optionSuggestions)
                flatList.Add(option);

            foreach (var prefix in prefixes)
            {
                flatList.Add(new TokenSuggestion
                {
                    Name = $"/-/{prefix}",
                    Description = $"删除前缀 {prefix}: 的所有 Token",
                    IsSlashCommand = true,
                    SlashCommandName = "-/",
                    SlashCommandArgument = prefix,
                    CanExecuteDirectly = true,
                    ActionType = TokenSuggestionActionType.Execute,
                });
            }

            _suggestionList.ItemsSource = flatList;
            _popup.IsOpen = true;
            _suggestionList.SelectedIndex = flatList.Count > 1 ? 1 : -1;
        }

        private void ShowSlashCommandSuggestions(string commandPattern, string argument)
        {
            if (_popup == null || _suggestionList == null)
                return;

            if (string.Equals(commandPattern, "-/", StringComparison.Ordinal) ||
                (commandPattern?.StartsWith("-/", StringComparison.Ordinal) ?? false))
            {
                var prefixPattern = string.Equals(commandPattern, "-/", StringComparison.Ordinal)
                    ? argument
                    : commandPattern[2..] + (string.IsNullOrEmpty(argument) ? string.Empty : $" {argument}");
                ShowRemovePrefixSuggestions(prefixPattern);
                return;
            }

            if (string.Equals(commandPattern, "-", StringComparison.Ordinal) ||
                (commandPattern?.StartsWith("-", StringComparison.Ordinal) ?? false))
            {
                var removePattern = string.Equals(commandPattern, "-", StringComparison.Ordinal)
                    ? argument
                    : commandPattern[1..] + (string.IsNullOrEmpty(argument) ? string.Empty : $" {argument}");
                ShowRemoveTokenSuggestions(removePattern);
                return;
            }

            var normalizedPattern = commandPattern?.Trim() ?? string.Empty;

            var allCommands = EnumerateSlashCommands().ToList();
            var exactCommand = allCommands.FirstOrDefault(c => string.Equals(c.Name, normalizedPattern, StringComparison.OrdinalIgnoreCase));
            if (exactCommand?.Suggest != null)
            {
                var context = BuildSlashSuggestionContext(exactCommand.Name, argument);
                var argSuggestions = exactCommand.Suggest(context)?.ToList() ?? [];
                if (argSuggestions.Count > 0)
                {
                    var argList = new List<object>
                    {
                        new TokenSuggestionHeader { Name = $"命令参数 (/{exactCommand.Name})" }
                    };

                    foreach (var argSuggestion in argSuggestions)
                    {
                        if (string.IsNullOrWhiteSpace(argSuggestion?.Name))
                            continue;

                        argList.Add(new TokenSuggestion
                        {
                            Name = $"/{exactCommand.Name} {argSuggestion.Name}",
                            Description = string.IsNullOrWhiteSpace(argSuggestion.Description)
                                ? "回车/点击执行命令"
                                : argSuggestion.Description,
                            Icon = string.IsNullOrWhiteSpace(argSuggestion.Icon) ? exactCommand.Icon : argSuggestion.Icon,
                            IsSlashCommand = true,
                            SlashCommandName = exactCommand.Name,
                            SlashCommandArgument = argSuggestion.Name,
                            CanExecuteDirectly = true,
                            ActionType = TokenSuggestionActionType.Execute,
                        });
                    }

                    if (argList.Count > 1)
                    {
                        _suggestionList.ItemsSource = argList;
                        _popup.IsOpen = true;
                        _suggestionList.SelectedIndex = 1;
                        return;
                    }
                }
            }

            var commands = EnumerateSlashCommands()
                .Where(c => string.IsNullOrEmpty(normalizedPattern)
                    || c.Name.StartsWith(normalizedPattern, StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (commands.Count == 0)
            {
                _popup.IsOpen = false;
                return;
            }

            var flatList = new List<object>
            {
                new TokenSuggestionHeader { Name = "命令工具 (/)" }
            };

            foreach (var cmd in commands)
            {
                var commandName = cmd.Name?.Trim() ?? string.Empty;
                var desc = string.IsNullOrWhiteSpace(cmd.Description)
                    ? "回车/点击执行命令"
                    : cmd.Description;

                flatList.Add(new TokenSuggestion
                {
                    Name = $"/{commandName}",
                    Description = desc,
                    Icon = cmd.Icon,
                    IsSlashCommand = true,
                    SlashCommandName = commandName,
                    SlashCommandArgument = argument,
                    CanExecuteDirectly = cmd.RequiresArgument == false,
                    ActionType = cmd.RequiresArgument ? TokenSuggestionActionType.Insert : TokenSuggestionActionType.Execute,
                });
            }

            _suggestionList.ItemsSource = flatList;
            _popup.IsOpen = true;
            _suggestionList.SelectedIndex = flatList.Count > 1 ? 1 : -1;
        }

        private string BuildSuggestionReplacementText(TokenSuggestion suggestion)
        {
            var currentText = Text ?? string.Empty;
            var segment = GetCurrentSegment(currentText);
            var isNegated = segment.StartsWith("-");
            var checkStr = isNegated ? segment.Substring(1) : segment;
            var insertValue = suggestion?.InsertValue ?? suggestion?.Name ?? string.Empty;

            var matchedProvider = MatchProvider(Providers, checkStr, out var matchedPrefix);
            string replacement;

            if (matchedProvider != null)
            {
                var prefixPart = isNegated ? "-" + matchedPrefix : matchedPrefix;
                replacement = prefixPart + insertValue;
            }
            else
            {
                replacement = isNegated ? "-" + insertValue : insertValue;
            }

            return BuildTextWithCurrentSegmentReplaced(currentText, replacement);
        }

        // 普通提交：允许对 || / && 做控制层拆解，并将每段作为独立 Token。
        // 根据 provider 的 LogicMode 智能决定是否保留运算符 Token：
        //   - 运算符与 LogicMode 默认一致（AutoOr + ||, AutoAnd + &&）→ 直接拆分不留 Token
        //   - 运算符与 LogicMode 不一致（AutoOr + &&, AutoAnd + ||）→ 保留运算符 Token
        private void CommitTextAsToken(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            var parenParts = SplitParenthesizedExpression(text.Trim());
            foreach (var part in parenParts)
            {
                if (part == "(" || part == ")")
                {
                    AddToken(part);
                }
                else
                {
                    CommitTextPartWithOperators(part);
                }
            }
        }

        // 按 ||/&& 拆分，在运算符与 LogicMode 不一致时保留运算符 Token
        private void CommitTextPartWithOperators(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            // Split into segments and track operators between them
            var segments = new List<string>();
            var operators = new List<string>();
            var start = 0;

            for (int i = 0; i < text.Length - 1; i++)
            {
                string foundOp = null;
                if (text[i] == '|' && text[i + 1] == '|') foundOp = "||";
                else if (text[i] == '&' && text[i + 1] == '&') foundOp = "&&";

                if (foundOp != null)
                {
                    var part = text[start..i].Trim();
                    if (!string.IsNullOrEmpty(part))
                        segments.Add(part);
                    operators.Add(foundOp);
                    start = i + 2;
                    i++;
                }
            }

            var tail = text[start..].Trim();
            if (!string.IsNullOrEmpty(tail))
                segments.Add(tail);

            if (segments.Count <= 1)
            {
                AddToken(text);
                return;
            }

            // With explicit operators, each segment stands independently (no prefix inheritance).
            // Without operators, inherit the first segment's prefix for bare values.
            if (operators.Count == 0)
            {
                var basePrefix = GetPrefixFromToken(segments[0]);
                for (int i = 0; i < segments.Count; i++)
                {
                    var token = segments[i];
                    var neg = token.StartsWith("-", StringComparison.Ordinal);
                    var raw = neg ? token[1..] : token;
                    if (raw.Contains(':'))
                        continue;

                    if (!string.IsNullOrEmpty(basePrefix))
                        segments[i] = neg ? $"-{basePrefix}{raw}" : $"{basePrefix}{raw}";
                }
            }

            // Determine which segments need paren wrapping (cross-prefix || groups)
            var needParens = new bool[segments.Count];
            for (int i = 0; i < segments.Count; i++)
            {
                var curPrefix = GetPrefixFromToken(segments[i]);
                if (curPrefix == null) continue;

                if (i > 0 && i - 1 < operators.Count && operators[i - 1] == "||")
                {
                    var prevPrefix = GetPrefixFromToken(segments[i - 1]);
                    if (prevPrefix != null && !string.Equals(prevPrefix, curPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        needParens[i] = true;
                        needParens[i - 1] = true;
                    }
                }

                if (i < operators.Count && operators[i] == "||")
                {
                    var nextPrefix = GetPrefixFromToken(segments[i + 1]);
                    if (nextPrefix != null && !string.Equals(curPrefix, nextPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        needParens[i] = true;
                        needParens[i + 1] = true;
                    }
                }
            }

            // Add tokens with paren wrapping for cross-prefix groups, conditionally inserting operator tokens
            for (int i = 0; i < segments.Count; i++)
            {
                if (needParens[i]) AddToken("(");
                AddToken(segments[i]);
                if (needParens[i]) AddToken(")");

                if (i < operators.Count)
                {
                    var curPrefix = GetPrefixFromToken(segments[i]);
                    var nextPrefix = GetPrefixFromToken(segments[i + 1]);

                    if (curPrefix != null && nextPrefix != null)
                    {
                        var isSamePrefix = string.Equals(curPrefix, nextPrefix, StringComparison.OrdinalIgnoreCase);

                        if (isSamePrefix)
                        {
                            // Same prefix: preserve operator only when it overrides LogicMode default
                            var provider = MatchProvider(Providers, curPrefix, out _);
                            if (provider != null)
                            {
                                var defaultOp = provider.LogicMode == TokenLogicMode.AutoAnd ? "&&" : "||";
                                if (operators[i] != defaultOp)
                                    AddToken(operators[i]);
                            }
                        }
                        else if (operators[i] == "||")
                        {
                            // Different prefixes with || overrides default AND → preserve
                            AddToken(operators[i]);
                        }
                        // && between different prefixes matches default AND → skip
                    }
                    else if (operators[i] == "||" && (curPrefix != null) != (nextPrefix != null))
                    {
                        // One side has prefix, other doesn't → cross-type OR
                        AddToken(operators[i]);
                    }
                }
            }
        }

        // 强制提交：不做 ||/&& 拆解（Ctrl+Enter），但仍拆分括号 Token。
        private void CommitTextAsTokenNoExpand(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            var parenParts = SplitParenthesizedExpression(text.Trim());
            foreach (var part in parenParts)
            {
                AddToken(part.Trim());
            }
        }

        // 控件内规范化：
        // a:acker||bob -> [a:acker, a:bob]
        // a:acker&&bob -> [a:acker, a:bob]
        // 仅用于 UI 交互与入框规范，不改变外部 parser 的“字面量”语义。
        private static List<string> ExpandInlineSegmentsForControl(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return [];

            var segments = new List<string>();
            var start = 0;
            for (int i = 0; i < text.Length - 1; i++)
            {
                if ((text[i] == '|' && text[i + 1] == '|') || (text[i] == '&' && text[i + 1] == '&'))
                {
                    var part = text[start..i].Trim();
                    if (!string.IsNullOrEmpty(part))
                        segments.Add(part);
                    start = i + 2;
                    i++;
                }
            }

            var tail = text[start..].Trim();
            if (!string.IsNullOrEmpty(tail))
                segments.Add(tail);

            if (segments.Count <= 1)
                return [text];

            var basePrefix = GetPrefixFromToken(segments[0]);
            for (int i = 0; i < segments.Count; i++)
            {
                var token = segments[i];
                var neg = token.StartsWith("-", StringComparison.Ordinal);
                var raw = neg ? token[1..] : token;
                if (raw.Contains(':'))
                {
                    segments[i] = token;
                    continue;
                }

                if (!string.IsNullOrEmpty(basePrefix))
                    segments[i] = neg ? $"-{basePrefix}{raw}" : $"{basePrefix}{raw}";
            }

            return segments;
        }

        // 将含括号的表达式拆分为独立 Token：(a:acker m:bug) → (, a:acker, m:bug, )
        private static List<string> SplitParenthesizedExpression(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return [];

            var result = new List<string>();
            var i = 0;
            while (i < text.Length)
            {
                if (char.IsWhiteSpace(text[i]))
                {
                    i++;
                    continue;
                }

                if (text[i] == '(' || text[i] == ')')
                {
                    result.Add(text[i].ToString());
                    i++;
                    continue;
                }

                var start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '(' && text[i] != ')')
                    i++;

                if (i > start)
                    result.Add(text[start..i]);
            }

            return result;
        }

        // 返回规范化后的显示文本。
        // 若输入与规范化结果不同，表示用户仍在”修正阶段”，本次 Enter 只改写文本不提交。
        private static string NormalizeInlineExpressionForControl(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var expanded = ExpandInlineSegmentsForControl(text.Trim());
            if (expanded.Count <= 1)
                return text.Trim();

            var hasAnd = text.Contains("&&", StringComparison.Ordinal);
            var op = hasAnd ? "&&" : "||";
            return string.Join(op, expanded);
        }

        private static string GetPrefixFromToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return null;

            var check = token.StartsWith("-", StringComparison.Ordinal) ? token[1..] : token;
            var idx = check.IndexOf(':');
            if (idx <= 0)
                return null;

            return check[..(idx + 1)];
        }

        private bool IsPersistentToken(string token)
        {
            return _persistentTokenSet.Contains(token);
        }

        private bool IsStoreProviderToken(string token, out ITokenSuggestionProvider provider, out string matchedPrefix)
        {
            provider = null;
            matchedPrefix = null;

            if (string.IsNullOrWhiteSpace(token))
                return false;

            var check = token.StartsWith("-", StringComparison.Ordinal) ? token[1..] : token;
            provider = MatchProvider(Providers, check, out matchedPrefix);
            return provider?.IsPersistent == true;
        }

        private void ClearByPriority()
        {
            if (!string.IsNullOrEmpty(Text))
            {
                SetCurrentValue(TextProperty, string.Empty);
                return;
            }

            var temporary = SelectedTokens.Where(t => !_persistentTokenSet.Contains(t)).ToList();
            if (temporary.Count > 0)
            {
                foreach (var t in temporary)
                    SelectedTokens.Remove(t);
                return;
            }

            if (PersistentTokens.Count > 0)
            {
                foreach (var p in PersistentTokens.ToList())
                    SelectedTokens.Remove(p);

                PersistentTokens.Clear();
                _persistentTokenSet.Clear();
            }
        }

        private async void OnTextBoxPropertyChanged(object sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == TextBox.TextProperty)
            {
                if (_tokensList != null && !string.IsNullOrEmpty(Text))
                {
                    _tokensList.SelectedIndex = -1;
                }

                UpdateClearButtonVisibility();

                var val = Text ?? string.Empty;
                // 只在用户主动清空输入时重置
                if (string.IsNullOrEmpty(val) && SelectedTokens.Count > 0)
                {
                    if (_popup != null)
                        _popup.IsOpen = false;
                    _tokensList.SelectedIndex = SelectedTokens.Count - 1;
                }
                else
                {
                    await UpdateSuggestionsAsync(val);
                }
            }
        }

        // 按当前输入段更新建议。
        // 关键规则：在 ||/&& 后，即使当前段非空，也继承前段 provider 做过滤。
        // 例如 a:acker||bo 会继续走作者 provider 并用 bo 过滤。
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

            if (TryParseSlashCommandSegment(text, out var slashCommandName, out var slashArgument))
            {
                ShowSlashCommandSuggestions(slashCommandName, slashArgument);
                return;
            }

            var segment = GetCurrentSegment(text);
            var isNegated = segment.StartsWith("-");
            var checkStr = isNegated ? segment.Substring(1) : segment;

            // When typing ( or ), show all providers so user can pick a prefix for the parenthesized expression
            if (checkStr is "(" or ")")
            {
                ShowDefaultProviders("", token);
                return;
            }

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
                    foreach (var item in list)
                    {
                        item.CanExecuteDirectly = true;
                        item.ActionType = TokenSuggestionActionType.Execute;
                    }
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
                if (TryFindLastOperator(text, out var opStart, out _))
                {
                    var previousText = text[..opStart].TrimEnd();
                    var previousSegment = GetCurrentSegment(previousText);
                    var prevNeg = previousSegment.StartsWith("-");
                    var prevCheck = prevNeg ? previousSegment.Substring(1) : previousSegment;
                    var inheritedProvider = MatchProvider(Providers, prevCheck, out var prevPrefix);
                    if (inheritedProvider != null && prevCheck.Length > prevPrefix.Length)
                    {
                        try
                        {
                            // 用当前段 checkStr 作为过滤词，而不是空串。
                            var suggestions = await inheritedProvider.GetSuggestionsAsync(checkStr, token);
                            if (token.IsCancellationRequested)
                                return;

                            var list = new List<TokenSuggestion>(suggestions);
                            foreach (var item in list)
                            {
                                item.CanExecuteDirectly = true;
                                item.ActionType = TokenSuggestionActionType.Execute;
                            }
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
                                _popup.IsOpen = false;
                        }
                        return;
                    }
                }

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
                foreach (var p in g.OrderBy(p => p.Priority))
                {
                    var desc = p.Description;
                    if (p.FullPrefix != null && p.FullPrefix.Length > 0)
                        desc = $"{desc} ({string.Join(", ", p.FullPrefix)})";

                    flatList.Add(new TokenSuggestion { Name = p.Prefix, Description = desc, Icon = p.Icon });
                    if (flatList[^1] is TokenSuggestion suggestion)
                    {
                        suggestion.CanExecuteDirectly = false;
                        suggestion.ActionType = TokenSuggestionActionType.Insert;
                    }
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

        // 键盘交互总入口：
        // 1) 建议弹窗打开时 Enter/Tab 优先上屏建议
        // 2) 非弹窗场景 Enter：先规范化，若已规范则提交
        // 3) Ctrl+Enter：跳过规范化，直接整段提交
        private void OnTextBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && TryParseSlashCommandSegment(Text ?? string.Empty, out var slashCommandName, out var slashArgument))
            {
                var normalizedArg = slashArgument?.Trim() ?? string.Empty;
                var selectedSlashSuggestion = _suggestionList?.SelectedItem as TokenSuggestion;

                // If the user has already typed a complete slash command, Enter should execute it
                // even when suggestions are visible.
                if (string.Equals(slashCommandName, "-/", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(normalizedArg))
                {
                    DeleteTokensByPrefixCommand(slashArgument);
                    SetCurrentValue(TextProperty, string.Empty);
                    if (_popup != null)
                        _popup.IsOpen = false;
                    e.Handled = true;
                    return;
                }

                if (string.Equals(slashCommandName, "-", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(normalizedArg))
                {
                    var removePattern = slashArgument?.Trim() ?? string.Empty;
                    if (TryParsePrefixClassRemoveArgument(removePattern, out var prefixKey))
                    {
                        DeleteTokensByPrefix(prefixKey, includeNegated: true);
                    }
                    else
                    {
                        var exact = SelectedTokens.FirstOrDefault(t =>
                            !IsOperatorToken(t) && string.Equals(t, removePattern, StringComparison.OrdinalIgnoreCase));
                        if (!string.IsNullOrEmpty(exact))
                            RemoveToken(exact);
                    }

                    SetCurrentValue(TextProperty, string.Empty);
                    if (_popup != null)
                        _popup.IsOpen = false;
                    e.Handled = true;
                    return;
                }

                if (!string.IsNullOrWhiteSpace(slashCommandName))
                {
                    var external = GetExternalSlashCommand(slashCommandName);
                    var canExecuteDirectly = external != null &&
                                             (external.RequiresArgument == false || !string.IsNullOrWhiteSpace(normalizedArg));
                    if (canExecuteDirectly)
                    {
                        ExecuteExternalSlashCommand(slashCommandName, slashArgument);
                        SetCurrentValue(TextProperty, string.Empty);
                        if (_popup != null)
                            _popup.IsOpen = false;
                        e.Handled = true;
                        return;
                    }
                }

                if (selectedSlashSuggestion?.IsSlashCommand == true)
                {
                    CommitSuggestion(selectedSlashSuggestion);
                }
                else if (string.Equals(slashCommandName, "-/", StringComparison.Ordinal))
                {
                    DeleteTokensByPrefixCommand(slashArgument);
                    SetCurrentValue(TextProperty, string.Empty);
                    if (_popup != null)
                        _popup.IsOpen = false;
                }
                else if (string.Equals(slashCommandName, "-", StringComparison.Ordinal))
                {
                    var removePattern = slashArgument?.Trim() ?? string.Empty;
                    if (TryParsePrefixClassRemoveArgument(removePattern, out var prefixKey))
                    {
                        DeleteTokensByPrefix(prefixKey, includeNegated: true);
                    }
                    else
                    {
                        var exact = SelectedTokens.FirstOrDefault(t =>
                            !IsOperatorToken(t) && string.Equals(t, removePattern, StringComparison.OrdinalIgnoreCase));
                        if (!string.IsNullOrEmpty(exact))
                            RemoveToken(exact);
                    }

                    SetCurrentValue(TextProperty, string.Empty);
                    if (_popup != null)
                        _popup.IsOpen = false;
                }
                else if (!string.IsNullOrWhiteSpace(slashCommandName))
                {
                    var external = GetExternalSlashCommand(slashCommandName);
                    var arg = slashArgument?.Trim() ?? string.Empty;
                    if (external?.RequiresArgument == true && string.IsNullOrEmpty(arg))
                    {
                        // Keep user in command-argument flow instead of executing empty command.
                        SetCurrentValue(TextProperty, $"/{slashCommandName} ");
                        _ = UpdateSuggestionsAsync(Text ?? string.Empty);
                        if (_textBox != null)
                        {
                            _textBox.Focus();
                            _textBox.CaretIndex = _textBox.Text.Length;
                        }
                        e.Handled = true;
                        return;
                    }

                    ExecuteExternalSlashCommand(slashCommandName, slashArgument);
                    SetCurrentValue(TextProperty, string.Empty);
                    if (_popup != null)
                        _popup.IsOpen = false;
                }
                else
                {
                    if (_popup != null)
                        _popup.IsOpen = false;
                }

                e.Handled = true;
                return;
            }

            if (_popup?.IsOpen == true && _suggestionList != null)
            {
                if (e.Key == Key.Enter)
                {
                    if (_suggestionList.SelectedItem is TokenSuggestion suggestion)
                    {
                        CommitSuggestion(suggestion);
                        e.Handled = true;
                        return;
                    }
                }
                else if (e.Key == Key.Down)
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
                else if (e.Key == Key.Tab)
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
                bool isCtrlPressed = e.KeyModifiers.HasFlag(KeyModifiers.Control);

                if (!string.IsNullOrEmpty(Text))
                {
                    if (isCtrlPressed)
                    {
                        // 用户显式要求“按原样提交”。
                        CommitTextAsTokenNoExpand(Text);
                        SetCurrentValue(TextProperty, string.Empty);
                    }
                    else
                    {
                        var normalized = NormalizeInlineExpressionForControl(Text);
                        if (!string.Equals(normalized, Text, StringComparison.Ordinal))
                        {
                            // 发现可规范化内容：本次只改写输入，不生成 Token。
                            SetCurrentValue(TextProperty, normalized);
                            if (_textBox != null)
                                _textBox.CaretIndex = _textBox.Text?.Length ?? 0;
                        }
                        else
                        {
                            // 已经是规范表达：单次 Enter 直接提交为 Token。
                            CommitTextAsToken(Text);
                            SetCurrentValue(TextProperty, string.Empty);
                        }
                    }
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
                            _tokensList.Focus();
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
                    _tokensList.Focus();
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Right && string.IsNullOrEmpty(Text) && _tokensList?.SelectedIndex >= 0)
            {
                if (_tokensList.SelectedIndex < SelectedTokens.Count - 1)
                {
                    _tokensList.SelectedIndex++;
                    _tokensList.Focus();
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
            var isNegated = token.StartsWith("-");
            var checkStr = isNegated ? token.Substring(1) : token;

            string matchedPrefix = null;
            var matchedProvider = !IsOperatorToken(token)
                ? MatchProvider(Providers, checkStr, out matchedPrefix)
                : null;

            // Auto-compact: normalize alias prefixes to short form (branch: -> b:)
            if (AutoCompact && matchedProvider != null && matchedPrefix != matchedProvider.Prefix)
            {
                var value = checkStr.Substring(matchedPrefix.Length);
                token = (isNegated ? "-" : "") + matchedProvider.Prefix + value;
                checkStr = matchedProvider.Prefix + value;
                matchedPrefix = matchedProvider.Prefix;
            }

            var isStoreToken = IsStoreProviderToken(token, out var storeProvider, out var _);
            if (isStoreToken && _persistentTokenSet.Add(token))
                PersistentTokens.Add(token);

            if (matchedProvider != null)
            {
                if (matchedProvider.LogicMode == TokenLogicMode.SingleReplace)
                {
                    for (int i = 0; i < SelectedTokens.Count; i++)
                    {
                        var existing = SelectedTokens[i];
                        var existingCheck = existing.StartsWith("-") ? existing.Substring(1) : existing;
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

            // Replace existing token when its negated/non-negated version is added
            var opposite = isNegated ? checkStr : $"-{checkStr}";
            var oppositeIdx = SelectedTokens.IndexOf(opposite);
            if (oppositeIdx >= 0)
                SelectedTokens.RemoveAt(oppositeIdx);

            if (!SelectedTokens.Contains(token))
            {
                if (AutoGrouping && matchedProvider != null)
                {
                    int insertAt = -1;
                    string targetPrefix = matchedPrefix;
                    var persistentBoundary = SelectedTokens.Count(t => IsPersistentToken(t));
                    var rangeStart = isStoreToken ? 0 : persistentBoundary;
                    var rangeEnd = isStoreToken ? persistentBoundary : SelectedTokens.Count;

                    for (int i = rangeEnd - 1; i >= rangeStart; i--)
                    {
                        var t = SelectedTokens[i];
                        if (IsOperatorToken(t))
                            continue;

                        var tn = t.StartsWith("-") ? t.Substring(1) : t;
                        MatchProvider(Providers, tn, out var p);
                        if (p == targetPrefix)
                        {
                            insertAt = i + 1;
                            if (insertAt < SelectedTokens.Count && IsOperatorToken(SelectedTokens[insertAt]))
                            {
                                insertAt++;
                            }
                            break;
                        }
                    }

                    if (insertAt >= 0)
                        SelectedTokens.Insert(insertAt, token);
                    else
                        SelectedTokens.Insert(rangeEnd, token);
                }
                else
                {
                    if (isStoreToken)
                    {
                        var boundary = SelectedTokens.Count(t => IsPersistentToken(t));
                        SelectedTokens.Insert(boundary, token);
                    }
                    else
                    {
                        SelectedTokens.Add(token);
                    }
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

            if (_persistentTokenSet.Contains(token))
            {
                _persistentTokenSet.Remove(token);
                PersistentTokens.Remove(token);
            }

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
            _clearButton.IsVisible = !string.IsNullOrEmpty(Text)
                || (SelectedTokens is { Count: > 0 })
                || (PersistentTokens is { Count: > 0 });
        }
        #endregion

        #region Syntax Highlighting
        private void HighlightLogicalOperators()
        {
            // Example logic for highlighting logical operators
            foreach (var token in SelectedTokens)
            {
                if (token.Contains("||") || token.Contains("&&"))
                {
                    // Apply highlighting logic here
                    // This could involve changing the token's style or adding visual cues
                }
            }
        }
        #endregion

        // Call the highlighting method at appropriate places, e.g., after tokenization or input changes
        // HighlightLogicalOperators();
    }
}
