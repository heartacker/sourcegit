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
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
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
        public IReadOnlyList<TokenInstance> SelectedTokens { get; init; }
        public IReadOnlyList<TokenInstance> PersistentTokens { get; init; }
    }

    public class TokenSlashExecuteContext
    {
        public TokenSearchBox SearchBox { get; init; }
        public string CommandName { get; init; }
        public string RawArgument { get; init; }
        public IReadOnlyList<string> ArgumentTokens { get; init; }
        public IReadOnlyList<TokenInstance> SelectedTokens { get; init; }
        public IReadOnlyList<TokenInstance> PersistentTokens { get; init; }
    }

    public class TokenSlashCommand
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Icon { get; set; }
        public bool RequiresArgument { get; set; }
        public List<TokenArgumentDefinition> Arguments { get; set; } = new();
        public Func<TokenSlashSuggestionContext, IEnumerable<TokenSuggestion>> Suggest { get; set; }
        public Func<TokenSlashExecuteContext, bool> Execute { get; set; }
    }

    /// <summary>
    /// ==========================================================================================
    /// 【TokenSearchBox 用户交互与视觉表现白皮书 (User-Centric Interaction Specification)】
    /// ==========================================================================================
    /// 本控件是 SourceGit 核心过滤控制中心（Filter Command Center），负责支持结构化标签（Token）搜索、
    /// 斜杠智能命令（Slash Commands）、级联参数提示以及高级图形化日期过滤。
    /// 为了向未来的开发者传递最极致、最流畅的交互打字体验，特在此系统化归纳其用户视角的视觉与交互行为规范：
    ///
    /// 一、 用户视角：视觉呈现规范（User Visuals & Presentation）：
    /// 1. 现代磨砂输入容器（Modern Border）：
    ///    - 输入框采用精致的圆角微边框设计。当失去焦点时边框淡化，与背景融为一体；
    ///      一旦聚焦（:focus-within），边框平滑过渡为系统强调色，为用户提供清晰的视觉焦点输入反馈。
    /// 2. 胶囊式彩色标签（Capsule Tokens）：
    ///    - 每一个打包好的过滤标签都以优雅的胶囊形状呈现。胶囊的背景色与边框色通过 Converter 基于前缀动态渲染
    ///      （例如分支、作者、哈希、时间等不同维度采用柔和、淡雅的互补色系），消除单调感，提高数据可读性。
    /// 3. 标签高亮选中状态（Selection Highlight）：
    ///    - 当用户使用键盘方向键导航到某个标签，或用鼠标单击标签时，该标签立即进入“选中高亮”状态，
    ///      其边框线变粗、颜色加深。此时，胶囊右端会渐显轻微透明的“X”物理删除小按钮。
    /// 4. 智能悬浮下拉窗（Suggestions Popup）：
    ///    - 紧贴输入框下沿展开，配备了柔和的立体投影（Drop Shadow）。列表左侧展示匹配类型小图标，
    ///      中间展示候选文字，右侧则为支持直发（不依赖额外参数）的命令贴心地配备了一键执行的“闪电小按钮”。
    /// 5. 紧凑/展开自动切换（Auto Compact）：
    ///    - 失去焦点且无输入时，相邻同类标签以紧凑贴合的模式压缩展示，节省宝贵的纵向屏幕高度；
    ///      重新聚焦时平滑展开为独立的高亮交互卡片，呈现完美的视觉张力。
    ///
    /// 二、 用户视角：极客键盘流与快捷键操作白皮书（Geek Keyboard-Driven Interactions）：
    /// 1. 【左/右方向键】─ 跨层级焦点平滑穿梭（Left/Right Navigation）：
    ///    - 当输入框为空且光标处于最左端（CaretIndex == 0）时，按 Left 键，焦点将神奇地脱离输入文本流，
    ///      直接进入左侧的 Token 序列，并将最右端的一个 Token 标为高亮选中。
    ///    - 在 Token 被高亮选中时，按 Left/Right 键可在 Token 集合中左右高亮横移。
    ///    - 当最右端的一个 Token 被选中时，再次按下 Right 键，焦点无感地“回落”到普通 TextBox 输入流，高亮自动消失。
    /// 2. 【退格键 (Backspace)】─ 二次编辑与回退解包（Unpack & Edit Mode）：
    ///    - 当输入框没有文本时，按一次 Backspace 会自动选中并高亮最末尾的最后一个 Token 胶囊。
    ///    - 【核心回退规范】：当某个 Token 胶囊被高亮选中时，按下 Backspace 键不会执行无情的物理删除。
    ///      根据用户反悔与二次微调的人性化考量（如拼错过滤参数），Backspace 将会把当前 Token 完好地“溶化/解包”，
    ///      原样退回为原始文本并写入输入框，同时光标自动跳跃到该段文本末端。用户只需按键微调即可，无需从头输入！
    /// 3. 【删除键 (Delete)】─ 物理硬删除（Physical Force Delete）：
    ///    - 只有 Delete 键是唯一的硬删除指令。当某个 Token 胶囊被高亮选中时，按下 Delete 键，该标签将直接从集合中
    ///      被物理抹除。随后，右侧剩余的 Tokens 自动向左滑移对齐补位，且高亮状态自动顺延到相邻的新 Token 上。
    /// 4. 【回车键 (Enter)】─ 多情景智能研判直发（Enter Commit / Execute）：
    ///    - 场景 A（高亮选中建议项）：若该建议是一键命令，直接运行并关闭弹窗；若需要更多参数，则先补全前缀，继续等待输入。
    ///    - 场景 B（输入框有文本但建议未高亮）：将纯文本作为 Token 标签上屏打包并清空输入框，强行闭合悬浮弹窗。
    ///    - 场景 C（输入框为空且无高亮标签）：直接向后台分发全局 SearchCommand 物理搜索，刷新整个提交记录面板。
    /// 5. 【Tab 键】─ 强力文本补全（AutoComplete / Insert）：
    ///    - 仅用于将当前建议高亮项文本拼写补全到文本框中，绝不做直发执行（让用户有二次确认的机会）。
    ///    - 拦截 Avalonia 默认的 Tab 切焦行为，确保极客键盘打字焦点牢牢锁定在输入框中。
    /// 6. 【Esc 键】─ 取消高亮与重置（Escape Reset）：
    ///    - 选中 Token 时按 Esc 键可瞬间取消选中高亮，光标复位到输入框尾部。
    ///    - 弹窗展开时按 Esc 键直接关闭提示弹窗，清空所有提示项。
    /// 7. 【上/下方向键】─ 候选列表项导航（Up/Down Navigation）：
    ///    - 上下键直接在 _suggestionList 的有效候选条目（跳过 Header 分组组头）中高亮穿梭，并支持 ListBox.ScrollIntoView
    ///    滚动跟随。
    ///
    /// 三、 用户视角：鼠标与触控手势逻辑（Mouse & Touch Gestures）：
    /// 1. 空间聚焦锁定（Focus Locking）：
    ///    - 当用户点击 Token 容器内部的任何没有互动元素的空白处，焦点都会瞬间锁定回真实的文本输入框 _textBox，
    ///      彻底解决由于空白点击导致的输入焦点断流和焦点竞争问题。
    /// 2. 单击胶囊选中（Single Click Selection）：
    ///    - 用鼠标左键单击任意 Token 胶囊，焦点自动转移回输入框，同时该胶囊立即呈现高亮选中状态。
    /// 3. 双击胶囊解包（Double Click to Edit）：
    ///    - 双击任意非操作符 Token 胶囊，等同于触发 Backspace 机制：该 Token 瞬间移出集合并被“原样解包”写回文本输入框，
    ///      输入框获得焦点且光标定位到该文本尾部，供用户快速微调。
    /// 4. 悬浮 X 按钮物理删除（Click to Remove）：
    ///    - 鼠标悬停在 Token 胶囊上或胶囊被高亮选中时，点击胶囊上置的“X”图标按钮（RemoveTokenCommand），
    ///      能够瞬间、干净利索地将该 Token 物理删除。
    /// 5. 建议项点击与闪电 Action 执行（Suggestion Item Actions）：
    ///    - 单击智能下拉列表里的条目即可将其填入输入框补全；若点击条目最右端的小闪电 Action 按钮，
    ///      则不经过前缀填充，直接一键物理执行该命令（例如直接 solo 该分支）并顺畅地闭合弹窗。
    ///
    /// 四、 技术实现：提示防死循环状态锁与自闭合（Dismissal & Locks）：
    /// 1. 防自我提示死循环锁（_isCommittingSuggestion）：
    ///    - 当用户选择候选建议上屏时，输入框 of Text 会重设。为避免引发 Text 发生改变事件（OnTextBoxPropertyChanged）
    ///      从而再次重新计算建议、导致第一项补全建议重复被选中高亮的死循环，引入状态安全锁。
    /// 2. 交互终点主动闭合（Manual Dismissal）：
    ///    - 在锁生效的期间，由于建议计算逻辑被提前 return 拦截，导致“文本变为空时自动关闭弹窗”的逻辑错失触发契机。
    ///      因此，在回车、鼠标释放点击、一键 Action 执行三大场景触发的最终执行点，代码都进行了强行、主动的
    ///      `_popup.IsOpen = false;` 和 `_suggestionList.SelectedItem = null;` 复位操作，确保提示框绝不残留悬空。
    /// ==========================================================================================
    /// </summary>
    [TemplatePart("PART_TextPresenter", typeof(TextBox))]
    [TemplatePart("PART_TokensList", typeof(WrapPanel))]
    [TemplatePart("PART_SuggestionsPopup", typeof(Popup))]
    [TemplatePart("PART_SuggestionsList", typeof(ListBox))]
    [TemplatePart("PART_RootBorder", typeof(Border))]
    public class TokenSearchBox : TemplatedControl
    {
        #region Dependency Properties
        public static readonly StyledProperty<string> TextProperty =
            AvaloniaProperty.Register<TokenSearchBox, string>(nameof(Text), defaultBindingMode: BindingMode.TwoWay);

        public static readonly StyledProperty<ObservableCollection<TokenInstance>> SelectedTokensProperty =
            AvaloniaProperty.Register<TokenSearchBox, ObservableCollection<TokenInstance>>(nameof(SelectedTokens));

        public static readonly StyledProperty<ObservableCollection<TokenInstance>> PersistentTokensProperty =
            AvaloniaProperty.Register<TokenSearchBox, ObservableCollection<TokenInstance>>(nameof(PersistentTokens));

        public static readonly StyledProperty<ObservableCollection<ITokenSuggestionProvider>> ProvidersProperty =
            AvaloniaProperty.Register<TokenSearchBox, ObservableCollection<ITokenSuggestionProvider>>(nameof(Providers));

        public static readonly StyledProperty<ObservableCollection<TokenSlashCommand>> SlashCommandsProperty =
            AvaloniaProperty.Register<TokenSearchBox, ObservableCollection<TokenSlashCommand>>(nameof(SlashCommands));

        public static readonly StyledProperty<string> PlaceholderTextProperty =
            AvaloniaProperty.Register<TokenSearchBox, string>(nameof(PlaceholderText));

        public static readonly StyledProperty<int> MaxRowsProperty =
            AvaloniaProperty.Register<TokenSearchBox, int>(nameof(MaxRows), 3);

        public static readonly StyledProperty<bool> AutoGroupingProperty =
            AvaloniaProperty.Register<TokenSearchBox, bool>(nameof(AutoGrouping), true);

        public static readonly StyledProperty<bool> AutoCompactProperty =
            AvaloniaProperty.Register<TokenSearchBox, bool>(nameof(AutoCompact), true);

        public static readonly StyledProperty<double> MaxListHeightProperty =
            AvaloniaProperty.Register<TokenSearchBox, double>(nameof(MaxListHeight), 120.0);

        public static readonly StyledProperty<ICommand> SearchCommandProperty =
            AvaloniaProperty.Register<TokenSearchBox, ICommand>(nameof(SearchCommand));

        public static readonly StyledProperty<ICommand> TokenDoubleClickCommandProperty =
            AvaloniaProperty.Register<TokenSearchBox, ICommand>(nameof(TokenDoubleClickCommand));

        public static readonly StyledProperty<ICommand> SuggestionActionCommandProperty =
            AvaloniaProperty.Register<TokenSearchBox, ICommand>(nameof(SuggestionActionCommand));

        public static readonly StyledProperty<ICommand> RemoveTokenCommandProperty =
            AvaloniaProperty.Register<TokenSearchBox, ICommand>(nameof(RemoveTokenCommand));

        public static readonly StyledProperty<int> SelectedTokenIndexProperty =
            AvaloniaProperty.Register<TokenSearchBox, int>(nameof(SelectedTokenIndex), -1);
        #endregion

        #region Properties
        public string Text
        {
            get => GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public ObservableCollection<TokenInstance> SelectedTokens
        {
            get => GetValue(SelectedTokensProperty);
            set => SetValue(SelectedTokensProperty, value);
        }

        public ObservableCollection<TokenInstance> PersistentTokens
        {
            get => GetValue(PersistentTokensProperty);
            set => SetValue(PersistentTokensProperty, value);
        }

        public int SelectedTokenIndex
        {
            get => GetValue(SelectedTokenIndexProperty);
            set => SetValue(SelectedTokenIndexProperty, value);
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

        public ICommand RemoveTokenCommand
        {
            get => GetValue(RemoveTokenCommandProperty);
            set => SetValue(RemoveTokenCommandProperty, value);
        }
        #endregion

        public TokenSearchBox()
        {
            SetCurrentValue(SelectedTokensProperty, new ObservableCollection<TokenInstance>());
            SetCurrentValue(PersistentTokensProperty, new ObservableCollection<TokenInstance>());
            SetCurrentValue(ProvidersProperty, new ObservableCollection<ITokenSuggestionProvider>());
            SetCurrentValue(SlashCommandsProperty, new ObservableCollection<TokenSlashCommand>());
            SetCurrentValue(SuggestionActionCommandProperty,
                            new App.Command(parameter =>
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
                SetCurrentValue(MaxListHeightProperty, MaxRows * 32.0);
            }
            else if (change.Property == SelectedTokenIndexProperty)
            {
                var oldIdx = change.GetOldValue<int>();
                var newIdx = change.GetNewValue<int>();
                UpdateSelectedTokenHighlight(oldIdx, newIdx);
            }
        }

        private TextBox _textBox;
        private WrapPanel _tokensList;
        private IDataTemplate _tokenChipTemplate;
        private readonly List<ContentPresenter> _tokenContainers = new();
        private Popup _popup;
        private ListBox _suggestionList;
        private ContentControl _customEditorPresenter;
        private Border _rootBorder;
        private Button _clearButton;
        private CancellationTokenSource _cts;
        private bool _isCommittingSuggestion;
        private bool _isNavigatingSuggestions;
        private string _originalUserText = string.Empty;

        private string _slashCommandName;
        private readonly List<string> _slashCommandArgs = new();
        // [双语注释 / Bilingual Comment]
        // 采用 Ordinal 严格比对以支持大小写区分的持久过滤 Token 集合判定，避免 a:acker 与 a:Acker 等在持久状态中冲突。
        // Use strict Ordinal comparer to support case-sensitive persistent filter tokens collection, preventing conflicts between a:acker and a:Acker.
        private readonly HashSet<string> _persistentTokenSet = new(StringComparer.Ordinal);

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            base.OnApplyTemplate(e);
            _textBox = e.NameScope.Find<TextBox>("PART_TextPresenter");
            if (_textBox != null)
            {
                _textBox.AddHandler(KeyDownEvent, OnTextBoxKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
                _textBox.PropertyChanged += OnTextBoxPropertyChanged;
                _textBox.GotFocus += (s, ev) =>
                {
                    SetCurrentValue(SelectedTokenIndexProperty, -1);
                    if (!string.IsNullOrEmpty(Text))
                        _ = UpdateSuggestionsAsync(Text);
                };
                _textBox.LostFocus += (s, ev) =>
                {
                    Dispatcher.UIThread.Post(
                        () =>
                        {
                            if (_popup == null)
                                return;

                            if ((_textBox?.IsKeyboardFocusWithin ?? false) ||
                                (_suggestionList?.IsKeyboardFocusWithin ?? false) ||
                                (_customEditorPresenter?.IsKeyboardFocusWithin ?? false) ||
                                (_popup?.IsPointerOver ?? false) ||
                                (_suggestionList?.IsPointerOver ?? false))
                                return;

                            _popup.IsOpen = false;
                            SetCurrentValue(SelectedTokenIndexProperty, -1);
                        },
                        DispatcherPriority.Input);
                };
            }

            _tokensList = e.NameScope.Find<WrapPanel>("PART_TokensList");
            if (_tokensList != null)
            {
                _tokensList.PointerPressed += OnTokensListPointerPressed;
                _tokensList.DoubleTapped += OnTokensListDoubleTapped;
            }

            _tokenChipTemplate = this.TryFindResource("TokenChipTemplate", out var tmpl) ? (IDataTemplate)tmpl : null;

            _popup = e.NameScope.Find<Popup>("PART_SuggestionsPopup");
            _suggestionList = e.NameScope.Find<ListBox>("PART_SuggestionsList");
            if (_suggestionList != null)
            {
                _suggestionList.AddHandler(PointerPressedEvent, OnSuggestionPointerPressed, Avalonia.Interactivity.RoutingStrategies.Bubble, true);
            }

            _customEditorPresenter = e.NameScope.Find<ContentControl>("PART_CustomEditorPresenter");
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
                {
                    SelectedTokens.CollectionChanged += (_, _) =>
                    {
                        UpdateClearButtonVisibility();
                        RebuildTokenChips();
                        if (SelectedTokenIndex >= SelectedTokens.Count)
                        {
                            SetCurrentValue(SelectedTokenIndexProperty, -1);
                        }
                        else
                        {
                            ClearAllTokenHighlights();
                            if (SelectedTokenIndex >= 0 && SelectedTokenIndex < _tokenContainers.Count)
                            {
                                var container = _tokenContainers[SelectedTokenIndex];
                                if (container != null)
                                    container.Classes.Add("selected");
                            }
                        }
                    };
                }
                if (PersistentTokens != null)
                    PersistentTokens.CollectionChanged += (_, _) => UpdateClearButtonVisibility();
                UpdateClearButtonVisibility();
            }

            RebuildTokenChips();

            // [备注] 用 handledEventsToo = true 注册路由事件监听器，
            // 确保当用户点击控件内部任何地方，只要不是清空按钮或下拉建议弹窗，
            // 都能自动将焦点转移/锁定在输入文本框 _textBox。这样既能实现点击任何地方极速聚焦，
            // 又能彻底免除焦点竞争。
            AddHandler(PointerPressedEvent,
                       (s, ev) =>
                       {
                           var src = ev.Source as Visual;
                           if (src == null)
                               return;

                           if (_clearButton != null &&
                               (src == _clearButton || src.GetVisualAncestors().Contains(_clearButton)))
                               return;

                           if (_popup != null && (src == _popup || src.GetVisualAncestors().Contains(_popup)))
                               return;

                           Dispatcher.UIThread.Post(() =>
                                                    { _textBox?.Focus(); },
                                                    DispatcherPriority.Input);
                       },
                       Avalonia.Interactivity.RoutingStrategies.Bubble, true);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            _textBox?.Focus();
        }

        #region Public API
        private static StringComparison GetComparison(ITokenSuggestionProvider provider)
        {
            // [双语注释 / Bilingual Comment]
            // 如果 provider 配置了 CaseSensitive = true，则采用 Ordinal 严格比对以支持大小写区分；否则默认不区分大小写 (OrdinalIgnoreCase)。
            // If the provider has CaseSensitive = true, use Ordinal comparison for strict case-sensitivity; otherwise, default to case-insensitive comparison (OrdinalIgnoreCase).
            return (provider?.CaseSensitive == true) ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        }

        public void RemoveToken(TokenInstance token)
        {
            if (token == null)
                return;

            SelectedTokens.Remove(token);
            if (_persistentTokenSet.Contains(token.Raw))
            {
                _persistentTokenSet.Remove(token.Raw);
                // [双语注释 / Bilingual Comment]
                // 移除持久 Token 时，根据对应 Provider 的大小写敏感度配置执行匹配。
                // Use the matching comparison mode configured in the provider when removing persistent tokens.
                var comp = GetComparison(token.Provider);
                var p = PersistentTokens.FirstOrDefault(x => x.Raw.Equals(token.Raw, comp));
                if (p != null)
                    PersistentTokens.Remove(p);
            }
        }

        public void AddToken(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return;

            var instance = CreateTokenInstance(raw);
            var token = instance.Raw;

            var isStoreToken = instance.Provider?.IsPersistent == true;
            if (isStoreToken && _persistentTokenSet.Add(token))
                PersistentTokens.Add(instance);

            if (instance.Provider != null && instance.Provider.LogicMode == TokenLogicMode.SingleReplace)
            {
                for (int i = 0; i < SelectedTokens.Count; i++)
                {
                    if (SelectedTokens[i].Provider == instance.Provider)
                    {
                        SelectedTokens[i] = instance;
                        SetCurrentValue(TextProperty, string.Empty);
                        if (_popup != null)
                            _popup.IsOpen = false;
                        return;
                    }
                }
            }

            var oppositeRaw = instance.IsNegated ? token[1..] : $"-{token}";
            // [双语注释 / Bilingual Comment]
            // 根据 Provider 对应的大小写敏感度属性配置比对方式，清除相反状态的互斥 Token。
            // Clean up opposite tokens with mutual exclusivity using the casing comparison rule configured in the provider.
            var comp = GetComparison(instance.Provider);
            var opposite =
                SelectedTokens.FirstOrDefault(t => t.Raw.Equals(oppositeRaw, comp));
            if (opposite != null)
                SelectedTokens.Remove(opposite);

            // [双语注释 / Bilingual Comment]
            // 根据大小写比对方式智能检测去重，决定是否将该 Token 加入集合。
            // Check for duplication dynamically based on the provider's casing comparison rules before adding.
            if (SelectedTokens.All(t => !t.Raw.Equals(token, comp)))
            {
                if (AutoGrouping && instance.Provider != null)
                {
                    int insertAt = -1;
                    string targetPrefix = instance.Provider.Prefix;
                    var persistentBoundary = SelectedTokens.Count(t => IsPersistentToken(t.Raw));
                    var rangeStart = isStoreToken ? 0 : persistentBoundary;
                    var rangeEnd = isStoreToken ? persistentBoundary : SelectedTokens.Count;

                    for (int i = rangeEnd - 1; i >= rangeStart; i--)
                    {
                        var t = SelectedTokens[i];
                        if (t.IsOperator)
                            continue;
                        if (t.Provider?.Prefix == targetPrefix)
                        {
                            insertAt = i + 1;
                            if (insertAt < SelectedTokens.Count && SelectedTokens[insertAt].IsOperator)
                                insertAt++;
                            break;
                        }
                    }

                    if (insertAt >= 0)
                        SelectedTokens.Insert(insertAt, instance);
                    else
                        SelectedTokens.Insert(rangeEnd, instance);
                }
                else
                {
                    if (isStoreToken)
                    {
                        var boundary = SelectedTokens.Count(t => IsPersistentToken(t.Raw));
                        SelectedTokens.Insert(boundary, instance);
                    }
                    else
                        SelectedTokens.Add(instance);
                }
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

        public bool DeleteToken(string raw, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
        {
            var target = SelectedTokens.FirstOrDefault(t => t.Raw.Equals(raw, comparison));
            if (target != null)
            {
                RemoveToken(target);
                return true;
            }
            return false;
        }

        public void FocusSearchTextBox(NavigationMethod navigationMethod = NavigationMethod.Directional)
        {
            if (_textBox != null)
                _textBox.Focus(navigationMethod);
            else
                Focus(navigationMethod);
        }

        public int DeleteTokensByPrefix(string prefix, bool includeNegated = true,
                                        StringComparison comparison = StringComparison.OrdinalIgnoreCase)
        {
            if (string.IsNullOrWhiteSpace(prefix) || SelectedTokens == null || SelectedTokens.Count == 0)
                return 0;

            var toRemove = SelectedTokens
                               .Where(t =>
                                      {
                                          var check = t.IsNegated && includeNegated ? t.Raw[1..] : t.Raw;
                                          return check.StartsWith(prefix, comparison);
                                      })
                               .ToList();

            foreach (var t in toRemove)
                RemoveToken(t);

            return toRemove.Count;
        }

        public IReadOnlyList<TokenInstance> QueryTokens(string prefix = null, bool includeNegated = true,
                                                        StringComparison comparison = StringComparison.OrdinalIgnoreCase)
        {
            if (SelectedTokens == null || SelectedTokens.Count == 0)
                return Array.Empty<TokenInstance>();

            if (string.IsNullOrWhiteSpace(prefix))
                return SelectedTokens.ToList();

            return SelectedTokens
                .Where(t =>
                       {
                           var check = t.IsNegated && includeNegated ? t.Raw[1..] : t.Raw;
                           return check.StartsWith(prefix, comparison);
                       })
                .ToList();
        }

        public void RestorePersistentTokens(IEnumerable<string> raws, bool clearExisting = true)
        {
            if (clearExisting)
            {
                foreach (var old in PersistentTokens.ToList())
                {
                    _persistentTokenSet.Remove(old.Raw);
                    SelectedTokens.Remove(old);
                }
                PersistentTokens.Clear();
            }

            if (raws == null)
                return;

            foreach (var raw in raws)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;
                var instance = CreateTokenInstance(raw.Trim());
                if (_persistentTokenSet.Add(instance.Raw))
                {
                    PersistentTokens.Add(instance);
                    if (SelectedTokens.All(t => t.Raw != instance.Raw))
                    {
                        var boundary = SelectedTokens.Count(t => IsPersistentToken(t.Raw));
                        SelectedTokens.Insert(boundary, instance);
                    }
                }
            }
        }

        public TokenInstance CreateTokenInstance(string raw)
        {
            var isNegated = raw.StartsWith("-");
            var checkStr = isNegated ? raw.Substring(1) : raw;

            string matchedPrefix = null;
            var matchedProvider = !IsOperatorToken(raw) ? MatchProvider(Providers, checkStr, out matchedPrefix) : null;

            if (AutoCompact && matchedProvider != null && matchedPrefix != matchedProvider.Prefix)
            {
                var val = checkStr.Substring(matchedPrefix.Length);
                raw = (isNegated ? "-" : "") + matchedProvider.Prefix + val;
                checkStr = matchedProvider.Prefix + val;
                matchedPrefix = matchedProvider.Prefix;
            }

            var instance = new TokenInstance
            {
                Raw = raw,
                IsNegated = isNegated,
                IsOperator = IsOperatorToken(raw),
                Provider = matchedProvider,
                DisplayText = raw
            };

            if (matchedProvider is IAdvancedTokenProvider advanced)
            {
                var val = checkStr.Substring(matchedPrefix.Length);
                var unescapedVal = TokenValueHelper.Unescape(val);
                instance.Value = advanced.ValueConverter?.ToValue(unescapedVal) ?? unescapedVal;
                instance.DisplayText =
                    (isNegated ? "-" : "") + matchedPrefix + (advanced.ValueConverter?.ToDisplay(instance.Value) ?? unescapedVal);
            }

            return instance;
        }
        #endregion

        #region Internal Logic
        private void OnTokensListDoubleTapped(object sender, TappedEventArgs e)
        {
            var item = (e.Source as Visual)?.GetVisualAncestors().OfType<ContentPresenter>().FirstOrDefault();
            if (item?.DataContext is TokenInstance token)
            {
                if (TokenDoubleClickCommand?.CanExecute(token.Raw) == true)
                    TokenDoubleClickCommand.Execute(token.Raw);

                BeginEditToken(token);
                e.Handled = true;
            }
        }

        private void OnTokensListPointerPressed(object sender, PointerPressedEventArgs e)
        {
            var src = e.Source as Visual;
            if (src != null)
            {
                // [备注] 如果鼠标直接点击的是胶囊右端的“X”删除悬浮按钮（PART_DeleteButton）或其内部的子元素，
                // 我们不在此处无条件吃掉事件，而是将按下事件完美留给按钮自身去消费，以便顺畅地触发 RemoveTokenCommand 物理删除。
                var isDeleteButton = src is Button btn && btn.Name == "PART_DeleteButton";
                if (!isDeleteButton)
                {
                    isDeleteButton = src.GetVisualAncestors().OfType<Button>().Any(b => b.Name == "PART_DeleteButton");
                }

                if (isDeleteButton)
                    return;
            }

            var item = (e.Source as Visual)?.GetVisualAncestors().OfType<ContentPresenter>().FirstOrDefault();
            if (item != null)
            {
                var index = _tokenContainers.IndexOf(item);
                if (index >= 0)
                {
                    SetCurrentValue(SelectedTokenIndexProperty, index);
                    _textBox?.Focus();
                    e.Handled = true;
                }
            }
        }

        private void UpdateSelectedTokenHighlight(int oldIndex, int newIndex)
        {
            if (oldIndex >= 0 && oldIndex < _tokenContainers.Count)
            {
                var container = _tokenContainers[oldIndex];
                if (container != null)
                {
                    container.Classes.Remove("selected");
                }
            }

            if (newIndex >= 0 && newIndex < _tokenContainers.Count)
            {
                var container = _tokenContainers[newIndex];
                if (container != null)
                {
                    container.Classes.Add("selected");
                }
            }
        }

        private void ClearAllTokenHighlights()
        {
            for (int i = 0; i < _tokenContainers.Count; i++)
            {
                var container = _tokenContainers[i];
                if (container != null)
                {
                    container.Classes.Remove("selected");
                }
            }
        }

        private void RebuildTokenChips()
        {
            if (_tokensList == null || _textBox == null || _tokenChipTemplate == null)
                return;

            // Remove old token chip ContentPresenters (keep the TextBox)
            for (int i = _tokensList.Children.Count - 1; i >= 0; i--)
            {
                if (_tokensList.Children[i] is ContentPresenter)
                    _tokensList.Children.RemoveAt(i);
            }

            _tokenContainers.Clear();

            var converter = new TokenBubblePositionConverter();
            // Add new token chips before the TextBox
            for (int i = 0; i < SelectedTokens.Count; i++)
            {
                var token = SelectedTokens[i];
                var tag = converter.Convert(
                    new object[] { token.Raw, SelectedTokens, SelectedTokens.Count },
                    typeof(object), null, System.Globalization.CultureInfo.InvariantCulture);
                var cp = new ContentPresenter
                {
                    Content = token,
                    ContentTemplate = _tokenChipTemplate,
                    DataContext = token,
                    Tag = tag,
                };
                _tokensList.Children.Insert(i, cp);
                _tokenContainers.Add(cp);
            }
        }

        private void BeginEditToken(TokenInstance token)
        {
            if (token == null)
                return;
            RemoveToken(token);

            if (_textBox != null)
            {
                _textBox.Focus();
            }

            // [备注] 时序终极防竞争机制：先使 TextBox 稳定获得焦点，在其完成 GotFocus 内置清零后，
            // 再在 Input 优先级队列中安全写入 Raw 文本并强力推高 Caret 坐标至末尾。
            // 如此彻底阻断了系统底层焦点转移与异步更新时光标归零的竞争，实现完美的输入连贯性。
            Dispatcher.UIThread.Post(() =>
            {
                if (_textBox != null)
                {
                    _textBox.Text = token.Raw;
                    _textBox.CaretIndex = _textBox.Text?.Length ?? 0;
                }
            }, DispatcherPriority.Input);
        }

        private void OnSuggestionPointerPressed(object sender, PointerPressedEventArgs e)
        {
            var sourceVisual = e.Source as Visual;
            var item = sourceVisual?.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
            if (item?.DataContext is TokenSuggestion suggestion)
            {
                var fromActionButton = sourceVisual is Button btn && btn.Name == "PART_SuggestionActionButton";
                if (!fromActionButton && sourceVisual != null)
                {
                    fromActionButton = sourceVisual.GetVisualAncestors().OfType<Button>().Any(
                        b => b.Name == "PART_SuggestionActionButton");
                }

                // [备注] 高优先级智能交互协议决策流：
                // 如果用户点击的是右侧 Action 按钮，或者该建议项本身就是 Execute 类型的值（如 bob, main 等），
                // 我们直接设置 requestExecute = true 强行触发 Token 封包上屏。
                // 如果只是点击 a: 或参数占位符（Insert 类型），则维持 requestExecute = false 仅作为文本补全，并级联唤醒下级建议。
                bool requestExecute = fromActionButton || (suggestion.ActionType == TokenSuggestionActionType.Execute);

                CommitSuggestion(suggestion, requestExecute);
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

        private static ITokenSuggestionProvider MatchProvider(IEnumerable<ITokenSuggestionProvider> providers, string text,
                                                              out string matchedPrefix)
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
            var result = new List<string>();
            var i = 0;
            while (i < rawArgument.Length)
            {
                if (char.IsWhiteSpace(rawArgument[i]))
                {
                    i++;
                    continue;
                }
                var start = i;
                bool inQuotes = false;
                while (i < rawArgument.Length)
                {
                    if (rawArgument[i] == '\\' && i + 1 < rawArgument.Length && rawArgument[i + 1] == '"')
                    {
                        i += 2;
                        continue;
                    }
                    if (rawArgument[i] == '"')
                    {
                        inQuotes = !inQuotes;
                    }

                    if (!inQuotes && char.IsWhiteSpace(rawArgument[i]))
                    {
                        break;
                    }
                    i++;
                }
                if (i > start)
                    result.Add(rawArgument[start..i]);
            }
            return result;
        }

        private void SyncSlashCommandState(string commandName, string argument)
        {
            _slashCommandName = commandName ?? string.Empty;
            var raw = argument ?? string.Empty;
            var args = SplitSlashArguments(raw);
            var endsWithWhitespace = raw.EndsWith(' ');
            _slashCommandArgs.Clear();
            if (endsWithWhitespace)
                _slashCommandArgs.AddRange(args);
            else if (args.Count > 0)
                _slashCommandArgs.AddRange(args.Take(args.Count - 1));
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
            var existing = SlashCommands.FirstOrDefault(
                c => string.Equals(c.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
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
            var existing = SlashCommands.FirstOrDefault(
                c => string.Equals(c.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
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
                Description = "按前缀批量移除 Token",
                RequiresArgument = true,
            };
            if (SlashCommands == null)
                yield break;
            foreach (var cmd in SlashCommands)
            {
                if (cmd == null || string.IsNullOrWhiteSpace(cmd.Name))
                    continue;
                var normalizedName = cmd.Name.Trim().TrimStart('/');
                yield return new TokenSlashCommand
                {
                    Name = normalizedName,
                    Description = cmd.Description,
                    Icon = cmd.Icon,
                    RequiresArgument = cmd.RequiresArgument,
                    Arguments = cmd.Arguments,
                    Suggest = cmd.Suggest,
                    Execute = cmd.Execute,
                };
            }
        }

        private TokenSlashCommand GetSlashCommand(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;
            if (name == "-")
                return new TokenSlashCommand
                {
                    Name = "-",
                    Description = "移除已存在 Token",
                    RequiresArgument = true,
                };
            if (name == "-/")
                return new TokenSlashCommand
                {
                    Name = "-/",
                    Description = "按前缀批量移除 Token",
                    RequiresArgument = true,
                };
            return SlashCommands?.FirstOrDefault(
                c => string.Equals(c?.Name?.Trim().TrimStart('/'), name, StringComparison.OrdinalIgnoreCase));
        }

        private bool ExecuteSlashCommand(string commandName, string argument)
        {
            if (string.IsNullOrWhiteSpace(commandName))
                return false;
            if (string.Equals(commandName, "-", StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(argument))
                    return false;
                var exact = SelectedTokens.FirstOrDefault(
                    t => !t.IsOperator && string.Equals(t.Raw, argument.Trim(), StringComparison.OrdinalIgnoreCase));
                if (exact != null)
                    RemoveToken(exact);
                return true;
            }

            if (string.Equals(commandName, "-/", StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(argument))
                    return false;
                DeleteTokensByPrefixCommand(argument);
                return true;
            }

            var command = GetSlashCommand(commandName);
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
                if (token.IsOperator)
                    continue;
                var check = token.IsNegated ? token.Raw[1..] : token.Raw;
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
            var temporary = SelectedTokens.Where(t => !IsPersistentToken(t.Raw)).ToList();
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
            if (raw.Equals("temp", StringComparison.OrdinalIgnoreCase))
                return DeleteTemporaryTokensByCommand();
            if (raw.Equals("persistent", StringComparison.OrdinalIgnoreCase))
                return DeletePersistentTokensByCommand();

            var knownPrefixes = GetKnownTokenPrefixes();
            var matchedPrefixes =
                knownPrefixes.Where(p => p.Equals(raw, StringComparison.OrdinalIgnoreCase)).Select(p => p + ":").ToList();
            if (matchedPrefixes.Count == 0)
                matchedPrefixes.Add(raw + ":");

            var toRemove = SelectedTokens.Where(t => !t.IsOperator)
                               .Where(t =>
                                      {
                                          var check = t.IsNegated ? t.Raw[1..] : t.Raw;
                                          return matchedPrefixes.Any(
                                              prefix => check.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
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
                    if (alias.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                        return true;
            }
            return false;
        }

        private string BuildSlashSuggestionReplacementText(TokenSuggestion suggestion)
        {
            var cmd = suggestion?.SlashCommandName?.Trim() ?? string.Empty;
            var arg = suggestion?.SlashCommandArgument?.Trim() ?? string.Empty;
            if (TryParseSlashCommandSegment(Text ?? string.Empty, out var currentCmd, out var currentArg))
            {
                var meta = GetSlashCommand(cmd);
                string replacement;
                if (string.Equals(cmd, "-", StringComparison.Ordinal))
                    replacement = string.IsNullOrWhiteSpace(arg) ? "/- " : $"/-{arg}";
                else if (string.Equals(cmd, "-/", StringComparison.Ordinal))
                    replacement = string.IsNullOrWhiteSpace(arg) ? "/-/ " : $"/-/{arg}";
                else if (string.IsNullOrWhiteSpace(arg))
                    replacement = meta?.RequiresArgument == true ? $"/{cmd} " : $"/{cmd}";
                else
                    replacement = $"/{cmd} {arg}";
                return BuildTextWithCurrentSegmentReplaced(Text ?? string.Empty, replacement);
            }
            return Text ?? string.Empty;
        }

        private void CommitSuggestion(TokenSuggestion suggestion, bool requestExecute = false, bool forceAsCompletion = false)
        {
            // [备注] 设置状态锁，防止在程序内更新文本（TextProperty）时，
            // 触发 OnTextBoxPropertyChanged 中的 UpdateSuggestionsAsync，从而导致“自我推荐”状态死循环。
            _isCommittingSuggestion = true;
            try
            {
                var currentText = Text ?? string.Empty;
                if (suggestion?.IsSlashCommand == true)
                {
                    var cmd = suggestion.SlashCommandName?.Trim();
                    var arg = suggestion.SlashCommandArgument?.Trim() ?? string.Empty;
                    if (forceAsCompletion || !requestExecute || !suggestion.CanExecuteDirectly)
                    {
                        var replacement = BuildSlashSuggestionReplacementText(suggestion);
                        // [备注] 如果命令参数无法直接执行（如 /goto branch），且当前补全后末尾没有空格，
                        // 自动追加一个空格，以便于立即触发下一个命令参数的智能推荐。
                        if (!suggestion.CanExecuteDirectly && !replacement.EndsWith(' '))
                            replacement += " ";
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
                    if (ExecuteSlashCommand(cmd, arg))
                    {
                        // [备注] 命令执行成功后清空文本框。
                        // 【全场景弹窗自闭合机制】
                        // 此处虽然调用了 SetCurrentValue(TextProperty, string.Empty)，但因为当前整个建议提交方法包裹在
                        // _isCommittingSuggestion = true 的状态安全锁中，导致 OnTextBoxPropertyChanged 会在第 1490 行被直接
                        // return 拦截， 错失了由“文本变为空”而自动关闭建议弹窗的唯一契机。
                        // 因此，为了完美覆盖“键盘回车、鼠标释放点击、以及建议项右侧 Action 按钮点击”三大全场景交互流，
                        // 我们必须在此处显式且强行地重置下拉列表选中状态并彻底闭合 _popup
                        // 弹窗，防止提示框残留悬空在屏幕上。
                        SetCurrentValue(TextProperty, string.Empty);
                        if (_textBox != null)
                        {
                            _textBox.Focus();
                            _textBox.CaretIndex = _textBox.Text.Length;
                        }
                        _suggestionList.SelectedItem = null;
                        if (_popup != null)
                            _popup.IsOpen = false;
                    }
                    else
                    {
                        var finalReplacement = BuildSlashSuggestionReplacementText(suggestion);
                        if (!finalReplacement.EndsWith(' '))
                            finalReplacement += " ";
                        SetCurrentValue(TextProperty, finalReplacement);
                        if (_textBox != null)
                        {
                            _textBox.Focus();
                            _textBox.CaretIndex = _textBox.Text.Length;
                        }
                        _suggestionList.SelectedItem = null;
                        _ = UpdateSuggestionsAsync(Text ?? string.Empty);
                    }
                    return;
                }

                var segment = GetCurrentSegment(currentText);
                var isNegated = segment.StartsWith("-");
                var checkStr = isNegated ? segment.Substring(1) : segment;
                var insertValue = suggestion?.InsertValue ?? suggestion?.Name ?? string.Empty;
                insertValue = TokenValueHelper.Escape(insertValue);
                var matchedProvider = MatchProvider(Providers, checkStr, out var matchedPrefix);
                string replacementText;
                if (matchedProvider != null)
                {
                    if (insertValue.StartsWith(matchedPrefix, StringComparison.OrdinalIgnoreCase))
                        replacementText = BuildTextWithCurrentSegmentReplaced(currentText, (isNegated ? "-" : "") + insertValue);
                    else
                        replacementText = BuildTextWithCurrentSegmentReplaced(currentText, (isNegated ? "-" : "") + matchedPrefix + insertValue);
                }
                else
                    replacementText =
                        BuildTextWithCurrentSegmentReplaced(currentText, (isNegated ? "-" : "") + insertValue);

                if (!forceAsCompletion && (requestExecute || suggestion.CanExecuteDirectly) && suggestion.CanExecuteDirectly)
                {
                    CommitTextAsToken(NormalizeInlineExpressionForControl(replacementText));
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

                // 【智能前缀研判与下一级提示级联唤醒机制】
                // 如果当前补全完的文本刚好是某个过滤前缀（如 b:, t:, r:, gitlog: 等），
                // 说明用户刚进入“前缀参数等待录入”阶段，必须保持弹窗开启，并主动重新拉起该前缀专属的子项补全列表。
                // 否则，如果是普通的值补全（如完整分支名），则维持原作者设计：直接关闭建议弹窗，避免高亮自我匹配冲突。
                var isProviderPrefix = false;
                if (Providers != null)
                {
                    isProviderPrefix =
                        Providers.Any(p => !string.IsNullOrEmpty(p.Prefix) &&
                                           (replacementText.Equals(p.Prefix, StringComparison.OrdinalIgnoreCase) ||
                                            replacementText.Equals("-" + p.Prefix, StringComparison.OrdinalIgnoreCase)));
                }

                if (isProviderPrefix)
                {
                    _ = UpdateSuggestionsAsync(replacementText);
                }
                else
                {
                    if (_popup != null)
                        _popup.IsOpen = false;
                }
            }
            finally
            {
                _isCommittingSuggestion = false;
                _isNavigatingSuggestions = false;
            }
        }

        private void ShowRemoveTokenSuggestions(string pattern)
        {
            if (_popup == null || _suggestionList == null)
                return;
            var normalized = pattern?.Trim() ?? string.Empty;
            if (TryParsePrefixClassRemoveArgument(normalized, out var prefixFilter))
            {
                var prefixGroups = SelectedTokens.Where(t => !t.IsOperator)
                                       .Select(t => NormalizePrefixKey(t.Raw))
                                       .Where(p => !string.IsNullOrEmpty(p))
                                       .GroupBy(p => p)
                                       .Select(g => new { Prefix = g.Key, Count = g.Count() })
                                       .Where(x => string.IsNullOrEmpty(prefixFilter) ||
                                                   x.Prefix.Contains(prefixFilter, StringComparison.OrdinalIgnoreCase))
                                       .OrderBy(x => x.Prefix, StringComparer.OrdinalIgnoreCase)
                                       .ToList();
                if (prefixGroups.Count == 0)
                {
                    _popup.IsOpen = false;
                    return;
                }
                var grouped = new List<object> { new TokenSuggestionHeader { Name = "移除 Token 前缀类 (/-/)" } };
                foreach (var g in prefixGroups)
                    grouped.Add(new TokenSuggestion
                    {
                        Name = g.Prefix,
                        Description = $"{g.Count} 项 · 回车/点击批量删除",
                        IsSlashCommand = true,
                        SlashCommandName = "-",
                        SlashCommandArgument = $"prefix:{g.Prefix}",
                    });
                _suggestionList.ItemsSource = grouped;
                _popup.IsOpen = true;
                _suggestionList.SelectedIndex = grouped.Count > 1 ? 1 : -1;
                return;
            }
            var tokens = SelectedTokens.Where(t => !t.IsOperator)
                             .DistinctBy(t => t.Raw)
                             .Where(t => string.IsNullOrEmpty(normalized) ||
                                         t.Raw.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                             .OrderByDescending(t => IsPersistentToken(t.Raw))
                             .ThenBy(t => t.Raw, StringComparer.OrdinalIgnoreCase)
                             .ToList();
            if (tokens.Count == 0)
            {
                _popup.IsOpen = false;
                return;
            }
            var flatList = new List<object> { new TokenSuggestionHeader { Name = "移除 Token (/-)" } };
            foreach (var token in tokens)
                flatList.Add(new TokenSuggestion
                {
                    Name = token.Raw,
                    Description = IsPersistentToken(token.Raw) ? "持久 Token" : "临时 Token",
                    IsSlashCommand = true,
                    SlashCommandName = "-",
                    SlashCommandArgument = token.Raw,
                    CanExecuteDirectly = true,
                    ActionType = TokenSuggestionActionType.Execute,
                });
            _suggestionList.ItemsSource = flatList;
            _popup.IsOpen = true;
            _suggestionList.SelectedIndex = flatList.Count > 1 ? 1 : -1;
        }

        private void ShowRemovePrefixSuggestions(string pattern)
        {
            if (_popup == null || _suggestionList == null)
                return;
            var normalized = (pattern ?? string.Empty).Trim().TrimStart('/').Trim().TrimEnd(':').Trim();
            var optionSuggestions = new List<TokenSuggestion>();
            if (string.IsNullOrEmpty(normalized) || "temp".Contains(normalized, StringComparison.OrdinalIgnoreCase))
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
            if (string.IsNullOrEmpty(normalized) || "persistent".Contains(normalized, StringComparison.OrdinalIgnoreCase))
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
            var prefixes = GetKnownTokenPrefixes()
                               .Where(p => string.IsNullOrEmpty(normalized) ||
                                           p.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                               .ToList();
            if (prefixes.Count == 0 && optionSuggestions.Count == 0)
            {
                _popup.IsOpen = false;
                return;
            }
            var flatList = new List<object> { new TokenSuggestionHeader { Name = "批量移除前缀 (/-/)" } };
            foreach (var option in optionSuggestions)
                flatList.Add(option);
            foreach (var prefix in prefixes)
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
            _suggestionList.ItemsSource = flatList;
            _popup.IsOpen = true;
            _suggestionList.SelectedIndex = flatList.Count > 1 ? 1 : -1;
        }

        private async Task ShowSlashCommandSuggestionsAsync(string commandPattern, string argument, CancellationToken ct)
        {
            if (_popup == null || _suggestionList == null)
                return;
            var normalizedPattern = commandPattern?.Trim() ?? string.Empty;

            if (string.Equals(normalizedPattern, "-/", StringComparison.Ordinal))
            {
                ShowRemovePrefixSuggestions(argument);
                return;
            }

            if (string.Equals(normalizedPattern, "-", StringComparison.Ordinal))
            {
                ShowRemoveTokenSuggestions(argument);
                return;
            }

            var allCommands = EnumerateSlashCommands().ToList();
            var exactCommand = allCommands.FirstOrDefault(
                c => string.Equals(c.Name, normalizedPattern, StringComparison.OrdinalIgnoreCase));
            if (exactCommand != null)
            {
                var context = BuildSlashSuggestionContext(exactCommand.Name, argument);
                List<TokenSuggestion> argSuggestions = null;
                if (exactCommand.Arguments != null && exactCommand.Arguments.Count > context.ActiveTokenIndex)
                {
                    var argDef = exactCommand.Arguments[context.ActiveTokenIndex];
                    var provider =
                        argDef.DynamicProvider != null ? argDef.DynamicProvider(context) : argDef.SuggestionProvider;
                    if (provider != null)
                    {
                        var results = await provider.GetSuggestionsAsync(context.ActiveToken, ct);
                        if (ct.IsCancellationRequested)
                            return;
                        argSuggestions = results?.ToList();
                    }
                }
                if (argSuggestions == null && exactCommand.Suggest != null)
                    argSuggestions = exactCommand.Suggest(context)?.ToList();
                if (argSuggestions != null && argSuggestions.Count > 0)
                {
                    var argList =
                        new List<object> { new TokenSuggestionHeader { Name = $"命令参数 (/{exactCommand.Name})" } };
                    foreach (var argSuggestion in argSuggestions)
                    {
                        if (argSuggestion == null || string.IsNullOrWhiteSpace(argSuggestion.DisplayName))
                            continue;

                        var displayArg = argSuggestion.DisplayName;
                        var displayPrefix = string.Join(" ", context.ArgumentTokens.Take(context.ActiveTokenIndex));
                        var fullDisplay =
                            string.IsNullOrWhiteSpace(displayPrefix) ? displayArg : $"{displayPrefix} {displayArg}";

                        // Fix: SlashCommandArgument should be the FULL argument string for execution and replacement.
                        var argValue = string.IsNullOrWhiteSpace(displayPrefix)
                                           ? argSuggestion.InsertValue
                                           : $"{displayPrefix} {argSuggestion.InsertValue}";

                        // Smarter CanExecuteDirectly:
                        // 1. If more required arguments follow, cannot execute.
                        // 2. If this is the last argument, can execute.
                        bool hasMoreRequired =
                            exactCommand.Arguments.Skip(context.ActiveTokenIndex + 1).Any(a => a.IsRequired);
                        bool canExecute = !hasMoreRequired;
                        if (string.Equals(exactCommand.Name, "goto", StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(argSuggestion.InsertValue, "head", StringComparison.OrdinalIgnoreCase))
                        {
                            canExecute = true;
                        }

                        argList.Add(new TokenSuggestion
                        {
                            Name = $"/{exactCommand.Name} {fullDisplay}",
                            Description = string.IsNullOrWhiteSpace(argSuggestion.Description) ? "回车/点击执行命令"
                                                                                               : argSuggestion.Description,
                            Icon = argSuggestion.Icon == null ? exactCommand.Icon : argSuggestion.Icon,
                            IsSlashCommand = true,
                            SlashCommandName = exactCommand.Name,
                            SlashCommandArgument = argValue,
                            CanExecuteDirectly = canExecute,
                            ActionType = canExecute ? TokenSuggestionActionType.Execute : TokenSuggestionActionType.Insert,
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
                               .Where(c => string.IsNullOrEmpty(normalizedPattern) ||
                                           c.Name.StartsWith(normalizedPattern, StringComparison.OrdinalIgnoreCase))
                               .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                               .ToList();
            if (commands.Count == 0)
            {
                _popup.IsOpen = false;
                return;
            }
            var flatList = new List<object> { new TokenSuggestionHeader { Name = "命令工具 (/)" } };
            foreach (var cmd in commands)
                flatList.Add(new TokenSuggestion
                {
                    Name = $"/{cmd.Name}",
                    Description = string.IsNullOrWhiteSpace(cmd.Description) ? "回车/点击执行命令" : cmd.Description,
                    Icon = cmd.Icon,
                    IsSlashCommand = true,
                    SlashCommandName = cmd.Name,
                    SlashCommandArgument = argument,
                    CanExecuteDirectly = cmd.RequiresArgument == false,
                    ActionType =
                        cmd.RequiresArgument ? TokenSuggestionActionType.Insert : TokenSuggestionActionType.Execute,
                });
            _suggestionList.ItemsSource = flatList;
            _popup.IsOpen = true;
            _suggestionList.SelectedIndex = flatList.Count > 1 ? 1 : -1;
        }

        private async Task UpdateSuggestionsAsync(string text)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
            }
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            if (string.IsNullOrEmpty(text))
            {
                ShowDefaultProviders("", token);
                return;
            }
            if (TryParseSlashCommandSegment(text, out var slashCommandName, out var slashArgument))
            {
                SyncSlashCommandState(slashCommandName, slashArgument);
                await ShowSlashCommandSuggestionsAsync(slashCommandName, slashArgument, token);
                return;
            }
            var checkStr = GetCurrentSegment(text);
            var isNegated = checkStr.StartsWith("-");
            if (isNegated)
                checkStr = checkStr[1..];
            var matchedProvider = MatchProvider(Providers, checkStr, out var matchedPrefix);
            if (matchedProvider != null)
            {
                if (matchedProvider is IAdvancedTokenProvider advanced && advanced.EditorType == TokenEditorType.Date &&
                    _customEditorPresenter != null)
                {
                    if (!(_customEditorPresenter.Content is Calendar))
                    {
                        var calendar = new Calendar { Margin = new Thickness(4) };
                        calendar.SelectedDatesChanged += (s, ev) =>
                        {
                            if (calendar.SelectedDate.HasValue)
                            {
                                var dateStr = calendar.SelectedDate.Value.ToString("yyyy-MM-dd");
                                var currentText = Text ?? string.Empty;
                                var seg = GetCurrentSegment(currentText);
                                var isNeg = seg.StartsWith("-");
                                var prefixPart = isNeg ? "-" + matchedPrefix : matchedPrefix;
                                SetCurrentValue(TextProperty,
                                                BuildTextWithCurrentSegmentReplaced(currentText, prefixPart + dateStr));
                                // [备注] 极客交互回流：当鼠标点击图形日历选中日期后，
                                // 异步将键盘焦点自动、强力地拉回输入框末端，使用户不用多点一次鼠标，可以直接继续打字录入！
                                Dispatcher.UIThread.Post(() =>
                                {
                                    if (_textBox != null)
                                    {
                                        _textBox.Focus();
                                        _textBox.CaretIndex = _textBox.Text?.Length ?? 0;
                                    }
                                }, DispatcherPriority.Input);
                            }
                        };
                        _customEditorPresenter.Content = calendar;
                    }
                    _customEditorPresenter.IsVisible = true;
                }
                else if (_customEditorPresenter != null)
                    _customEditorPresenter.IsVisible = false;
                var pattern = checkStr.Substring(matchedPrefix.Length);
                try
                {
                    var suggestions = await matchedProvider.GetSuggestionsAsync(pattern, token);
                    if (token.IsCancellationRequested)
                        return;
                    ShowSuggestions(suggestions);
                }
                catch
                {
                    if (!token.IsCancellationRequested)
                        _popup.IsOpen = false;
                }
            }
            else
            {
                if (_customEditorPresenter != null)
                    _customEditorPresenter.IsVisible = false;
                ShowDefaultProviders(checkStr, token);
            }
        }

        private void CommitTextAsToken(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;
            var parenParts = SplitParenthesizedExpression(text.Trim());
            foreach (var part in parenParts)
            {
                if (part == "(" || part == ")")
                    AddToken(part);
                else
                    CommitTextPartWithOperators(part);
            }
        }

        private void CommitTextPartWithOperators(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;
            var segments = new List<string>();
            var operators = new List<string>();
            var start = 0;
            for (int i = 0; i < text.Length - 1; i++)
            {
                string foundOp = null;
                if (text[i] == '|' && text[i + 1] == '|')
                    foundOp = "||";
                else if (text[i] == '&' && text[i + 1] == '&')
                    foundOp = "&&";
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
            if (segments.Count == 0)
                return;
            if (segments.Count == 1 && operators.Count == 0)
            {
                AddToken(text);
                return;
            }
            var basePrefix = GetPrefixFromToken(segments[0]);
            for (int i = 0; i < segments.Count; i++)
            {
                var token = segments[i];
                var neg = token.StartsWith("-", StringComparison.Ordinal);
                var raw = neg ? token[1..] : token;
                if (!raw.Contains(':'))
                {
                    var prefixToUse = !string.IsNullOrEmpty(basePrefix) ? basePrefix : "m:";
                    segments[i] = neg ? $"-{prefixToUse}{raw}" : $"{prefixToUse}{raw}";
                }
            }
            var needParens = new bool[segments.Count];
            if (operators.Count > 0)
            {
                for (int i = 0; i < operators.Count; i++)
                {
                    var p1 = GetPrefixFromToken(segments[i]);
                    var p2 = GetPrefixFromToken(segments[i + 1]);
                    if (!string.Equals(p1, p2, StringComparison.OrdinalIgnoreCase) && operators[i] == "||")
                    {
                        needParens[i] = true;
                        needParens[i + 1] = true;
                    }
                }
            }
            for (int i = 0; i < segments.Count; i++)
            {
                if (needParens[i])
                    AddToken("(");
                AddToken(segments[i]);
                if (needParens[i])
                    AddToken(")");
                if (i < operators.Count)
                {
                    var p1 = GetPrefixFromToken(segments[i]);
                    var p2 = GetPrefixFromToken(segments[i + 1]);
                    var op = operators[i];
                    var isCrossPrefix = !string.Equals(p1, p2, StringComparison.OrdinalIgnoreCase);
                    var provider = MatchProvider(Providers, p1 ?? "m:", out _);
                    var defaultOp = (provider?.LogicMode == TokenLogicMode.AutoAnd) ? "&&" : "||";
                    if (isCrossPrefix || op != defaultOp || needParens[i])
                        AddToken(op);
                }
            }
        }

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
            if (segments.Count == 0)
                return [];
            var basePrefix = GetPrefixFromToken(segments[0]);
            for (int i = 0; i < segments.Count; i++)
            {
                var token = segments[i];
                var neg = token.StartsWith("-", StringComparison.Ordinal);
                var raw = neg ? token[1..] : token;
                if (raw.Contains(':'))
                    continue;
                var prefixToUse = !string.IsNullOrEmpty(basePrefix) ? basePrefix : "m:";
                segments[i] = neg ? $"-{prefixToUse}{raw}" : $"{prefixToUse}{raw}";
            }
            return segments;
        }

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
                bool inQuotes = false;
                while (i < text.Length)
                {
                    if (text[i] == '\\' && i + 1 < text.Length && text[i + 1] == '"')
                    {
                        i += 2;
                        continue;
                    }
                    if (text[i] == '"')
                    {
                        inQuotes = !inQuotes;
                    }

                    if (!inQuotes && (char.IsWhiteSpace(text[i]) || text[i] == '(' || text[i] == ')'))
                    {
                        break;
                    }
                    i++;
                }
                if (i > start)
                    result.Add(text[start..i]);
            }
            return result;
        }

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

        private bool IsPersistentToken(string raw) => _persistentTokenSet.Contains(raw);

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
            var temporary = SelectedTokens.Where(t => !IsPersistentToken(t.Raw)).ToList();
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
                if (!string.IsNullOrEmpty(Text))
                    SetCurrentValue(SelectedTokenIndexProperty, -1);
                UpdateClearButtonVisibility();
                // [备注] 如果当前更改是由用户点击/确认了某个补全项导致的，
                // 则不自动在此处重新拉起建议检索，避免触发重复自动选中高亮的死循环。
                if (_isCommittingSuggestion)
                    return;
                _isNavigatingSuggestions = false;
                var val = Text ?? string.Empty;
                if (string.IsNullOrEmpty(val))
                {
                    if (_popup != null)
                        _popup.IsOpen = false;
                    return;
                }
                await UpdateSuggestionsAsync(val);
            }
        }

        private async void OnTextBoxKeyDown(object sender, KeyEventArgs e)
        {
            var isControlActive = (e.KeyModifiers & KeyModifiers.Control) != 0;
            if (e.Key == Key.Space && isControlActive)
            {
                _ = UpdateSuggestionsAsync(Text ?? string.Empty);
                e.Handled = true;
                return;
            }

            // [备注] 如果当前有选中/高亮的 Token，并且按下的不是导航、删除或 Esc 键，
            // 我们应自动清除 Token 选中状态（SelectedTokenIndex = -1），让键盘输入流能够流畅、无干扰地输入进 TextBox 中。
            if (SelectedTokenIndex >= 0)
            {
                if (e.Key != Key.Left && e.Key != Key.Right && e.Key != Key.Back && e.Key != Key.Delete &&
                    e.Key != Key.Escape)
                {
                    SetCurrentValue(SelectedTokenIndexProperty, -1);
                }
            }

            if (e.Key == Key.Enter)
            {
                if ((e.KeyModifiers & KeyModifiers.Control) != 0)
                {
                    // [备注] Ctrl+Enter 终极物理强制流：直接强行提取当前输入框的原生文本进行提交或执行，
                    // 彻底无视和跳过任何高亮提示项，帮助用户强行提交主观手打的数据！
                    var forceRaw = Text?.Trim() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(forceRaw))
                    {
                        if (TryParseSlashCommandSegment(forceRaw, out var forceCmd, out var forceArg))
                        {
                            ExecuteSlashCommand(forceCmd, forceArg);
                        }
                        else
                        {
                            CommitTextAsToken(forceRaw);
                        }
                        SetCurrentValue(TextProperty, string.Empty);
                        _suggestionList.SelectedItem = null;
                        if (_popup != null)
                            _popup.IsOpen = false;
                    }
                    e.Handled = true;
                    return;
                }

                if (_popup != null && _popup.IsOpen && _suggestionList?.SelectedItem is TokenSuggestion suggestion)
                {
                    var isShiftPressed = (e.KeyModifiers & KeyModifiers.Shift) != 0;
                    CommitSuggestion(suggestion, !isShiftPressed, isShiftPressed);
                    e.Handled = true;
                    return;
                }

                var raw = Text?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(raw))
                {
                    SearchCommand?.Execute(null);
                    e.Handled = true;
                    return;
                }
                if (TryParseSlashCommandSegment(raw, out var cmd, out var arg))
                {
                    if (ExecuteSlashCommand(cmd, arg))
                    {
                        // [备注] 用户手动输入完整个斜杠命令（不经过下拉高亮选中）并直接敲击回车执行。
                        // 执行成功后清空文本框，并强行将下拉建议列表的选中状态复位、关闭弹窗，防止提示框残留悬空。
                        SetCurrentValue(TextProperty, string.Empty);
                        _suggestionList.SelectedItem = null;
                        if (_popup != null)
                            _popup.IsOpen = false;
                    }
                    e.Handled = true;
                    return;
                }
                var isCtrlPressed = (e.KeyModifiers & KeyModifiers.Control) != 0;
                if (!isCtrlPressed)
                {
                    var normalized = NormalizeInlineExpressionForControl(raw);
                    if (!string.Equals(normalized, raw, StringComparison.Ordinal))
                    {
                        SetCurrentValue(TextProperty, normalized);
                        if (_textBox != null)
                            _textBox.CaretIndex = _textBox.Text.Length;
                        e.Handled = true;
                        return;
                    }
                }
                // [备注] 用户输入普通纯文本并直接敲击回车（将其作为标签上屏并清空输入框）。
                // 为确保整个键盘输入交互流畅，此处同样必须清空建议项选中状态并彻底关闭提示下拉弹窗。
                CommitTextAsToken(raw);
                SetCurrentValue(TextProperty, string.Empty);
                _suggestionList.SelectedItem = null;
                if (_popup != null)
                    _popup.IsOpen = false;
                e.Handled = true;
            }
            else if (SelectedTokenIndex >= 0 && (e.Key == Key.Delete || e.Key == Key.Back))
            {
                if (e.Key == Key.Delete)
                {
                    // [备注] 根据交互规范，只有 Delete 键才是真正的物理硬删除标签。
                    var idx = SelectedTokenIndex;
                    RemoveToken(SelectedTokens[idx]);
                    if (SelectedTokens.Count > 0)
                    {
                        SetCurrentValue(SelectedTokenIndexProperty, Math.Min(idx, SelectedTokens.Count - 1));
                    }
                    else
                    {
                        SetCurrentValue(SelectedTokenIndexProperty, -1);
                    }
                }
                else if (e.Key == Key.Back)
                {
                    // [备注] 根据交互规范，Backspace 退回编辑状态（拆解放回输入框），而非物理删除。
                    BeginEditToken(SelectedTokens[SelectedTokenIndex]);
                    SetCurrentValue(SelectedTokenIndexProperty, -1);
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Back && string.IsNullOrEmpty(Text) && SelectedTokens.Count > 0)
            {
                // [备注] 根据交互规范，在没有任何字符输入时，按下 Backspace 默认高亮最后一个 Token。
                SetCurrentValue(SelectedTokenIndexProperty, SelectedTokens.Count - 1);
                e.Handled = true;
            }
            else if (e.Key == Key.Left && string.IsNullOrEmpty(Text) && SelectedTokens.Count > 0)
            {
                if (SelectedTokenIndex < 0)
                    SetCurrentValue(SelectedTokenIndexProperty, SelectedTokens.Count - 1);
                else if (SelectedTokenIndex > 0)
                    SetCurrentValue(SelectedTokenIndexProperty, SelectedTokenIndex - 1);
                e.Handled = true;
            }
            else if (e.Key == Key.Right && string.IsNullOrEmpty(Text) && SelectedTokenIndex >= 0)
            {
                if (SelectedTokenIndex < SelectedTokens.Count - 1)
                    SetCurrentValue(SelectedTokenIndexProperty, SelectedTokenIndex + 1);
                else
                    SetCurrentValue(SelectedTokenIndexProperty, -1);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                if (SelectedTokenIndex >= 0)
                {
                    SetCurrentValue(SelectedTokenIndexProperty, -1);
                    e.Handled = true;
                }
                else if (_isNavigatingSuggestions)
                {
                    // [备注] 高级人机交互流：如果正处于键盘高亮即时穿梭预览状态，按下 Esc
                    // 不关闭弹窗，而是将输入框的 Text 瞬间完美回滚还原到用户最初键入的原汁原味状态，并重置预览状态锁，退回一级！
                    _isCommittingSuggestion = true;
                    try
                    {
                        SetCurrentValue(TextProperty, _originalUserText);
                        if (_textBox != null)
                        {
                            _textBox.Focus();
                            _textBox.CaretIndex = _textBox.Text?.Length ?? 0;
                        }
                    }
                    finally
                    {
                        _isCommittingSuggestion = false;
                    }
                    _isNavigatingSuggestions = false;
                    e.Handled = true;
                }
                else if (_popup != null && _popup.IsOpen)
                {
                    _popup.IsOpen = false;
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Up || e.Key == Key.Down)
            {
                if (_popup != null && _popup.IsOpen && _suggestionList != null)
                {
                    var count = _suggestionList.ItemsSource?.Cast<object>().Count() ?? 0;
                    if (count > 0)
                    {
                        // 1. 如果这是按上下键的第一帧，光速且牢固地把最初的用户手工键入原生态文本备份下来！
                        if (!_isNavigatingSuggestions)
                        {
                            _originalUserText = Text ?? string.Empty;
                            _isNavigatingSuggestions = true;
                        }

                        var dir = (e.Key == Key.Up) ? -1 : 1;
                        var next = _suggestionList.SelectedIndex + dir;
                        while (next >= 0 && next < count)
                        {
                            var item = _suggestionList.ItemsSource.Cast<object>().ElementAt(next);
                            if (item is TokenSuggestion suggestion)
                            {
                                _suggestionList.SelectedIndex = next;
                                _suggestionList.ScrollIntoView(item);

                                // 2. 即时生成高亮预览映射串 (Live Preview Translation)
                                string previewText = string.Empty;
                                if (suggestion.IsSlashCommand)
                                {
                                    previewText = BuildSlashSuggestionReplacementText(suggestion);
                                }
                                else
                                {
                                    var segment = GetCurrentSegment(_originalUserText);
                                    var isNegated = segment.StartsWith("-");
                                    var checkStr = isNegated ? segment.Substring(1) : segment;
                                    var insertValue = suggestion.InsertValue ?? suggestion.Name ?? string.Empty;
                                    var matchedProvider = MatchProvider(Providers, checkStr, out var matchedPrefix);
                                    if (matchedProvider != null)
                                    {
                                        if (insertValue.StartsWith(matchedPrefix, StringComparison.OrdinalIgnoreCase))
                                        {
                                            previewText = BuildTextWithCurrentSegmentReplaced(_originalUserText,
                                                (isNegated ? "-" : "") + insertValue);
                                        }
                                        else
                                        {
                                            previewText = BuildTextWithCurrentSegmentReplaced(_originalUserText,
                                                (isNegated ? "-" : "") + matchedPrefix + insertValue);
                                        }
                                    }
                                    else
                                    {
                                        previewText = BuildTextWithCurrentSegmentReplaced(_originalUserText,
                                            (isNegated ? "-" : "") + insertValue);
                                    }
                                }

                                // 3. 安全更新输入框，并强力防死锁屏蔽，让 Caret 回位！
                                _isCommittingSuggestion = true;
                                try
                                {
                                    SetCurrentValue(TextProperty, previewText);
                                    if (_textBox != null)
                                    {
                                        _textBox.CaretIndex = _textBox.Text?.Length ?? 0;
                                    }
                                }
                                finally
                                {
                                    _isCommittingSuggestion = false;
                                }

                                break;
                            }
                            next += dir;
                        }
                        e.Handled = true;
                    }
                }
            }
            else if (e.Key == Key.Tab)
            {
                if (_popup != null && _popup.IsOpen && _suggestionList?.SelectedItem is TokenSuggestion suggestion)
                {
                    CommitSuggestion(suggestion, false);
                    e.Handled = true;
                }
            }
        }

        private void ShowDefaultProviders(string pattern, CancellationToken ct)
        {
            if (_popup == null || _suggestionList == null || Providers == null)
                return;
            var list = new List<object>();
            var grouped = Providers.Where(p => MatchesPattern(p, pattern))
                              .OrderBy(p => p.Priority)
                              .GroupBy(p => p.Group?.Name ?? "其它")
                              .ToList();
            foreach (var g in grouped)
            {
                list.Add(new TokenSuggestionHeader { Name = g.Key });
                foreach (var p in g)
                    list.Add(new TokenSuggestion
                    {
                        Name = p.Prefix,
                        Description = p.Description,
                        Icon = p.Icon,
                        ActionType = TokenSuggestionActionType.Insert
                    });
            }
            if (list.Count > 0)
            {
                _suggestionList.ItemsSource = list;
                _popup.IsOpen = true;
                _suggestionList.SelectedIndex = 1;
            }
            else
                _popup.IsOpen = false;
        }

        private void ShowSuggestions(IEnumerable<TokenSuggestion> suggestions)
        {
            if (_popup == null || _suggestionList == null)
                return;
            var list = suggestions?.ToList() ?? [];
            if (list.Count > 0)
            {
                var flat = new List<object> { new TokenSuggestionHeader { Name = "建议" } };
                foreach (var s in list)
                    flat.Add(s);
                _suggestionList.ItemsSource = flat;
                _popup.IsOpen = true;
                _suggestionList.SelectedIndex = 1;
            }
            else
                _popup.IsOpen = false;
        }

        private void UpdateClearButtonVisibility()
        {
            if (_clearButton != null)
                _clearButton.IsVisible = !string.IsNullOrEmpty(Text) || SelectedTokens.Count > 0;
        }
        #endregion
    }
}
