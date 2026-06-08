using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SourceGit.Controls;

// Token Filter Logic:
//   SearchTokens are parsed by QueryParser into groups by prefix.
//   Each group is either handled in-memory (UpdateDisplayCommits) or
//   triggers git log re-execution via HistoryFilters (BuildHistoryParams).
//
//   In-memory filters (a:, m:, is:, s:, since:, until:, f:, p:, ...):
//     evaluated per-commit against _rawCommits in UpdateDisplayCommits.
//
//   Persistent git log filters (b:/branch:, t:/tag:, r:/remote:, gitlog:):
//     SKIPPED in UpdateDisplayCommits (skip list at group processing).
//     Instead, their CollectionChanged handler creates HistoryFilter
//     entries in RepositoryUIStates.HistoryFilters, which feeds into
//     BuildHistoryParams() to rebuild git log arguments.
//
//   HistoryFilters flow:
//     SearchTokens -(b:/t:/r:)-> HistoryFilters -(BuildHistoryParams)-> git log
//     Sidebar UI ---(branch:/tag:/remote:)-> HistoryFilters -(same)->
//
//   Negation (-b:xxx):
//     TokenSearchBox.AddToken replaces opposite versions (-b:main <-> b:main).
//     Parser creates Excluded-mode HistoryFilter entries.
//     BuildHistoryParams uses ^prefix for excludes when includes exist,
//     or --exclude with --branches/--remotes/--tags for exclude-only.
//
//   Cancellation:
//     Same (pattern, type) with both Included and Excluded -> both removed.
//
//   ====== 数据流 / Data Flow ======
//   _rawCommits = git log 原始输出，仅受持久过滤器影响（b:, t:, r:, gitlog:）。
//                 这些持久过滤器通过重新执行 git log 来筛选（AND 关系），
//                 不受内存过滤器影响（a:, m:, is: 等）。
//   _commits    = _rawCommits 经过 UpdateDisplayCommits() 应用所有内存过滤器后的结果。
//                 这就是实际显示的提交列表。
//
//   ====== 过滤器层级 / Filter Hierarchy ======
//   过滤器分为三个层级，从硬到软：
//
//   ┌───────────────────────────────────┐
//   │ 层级1: 硬持久过滤器 (Hard Persistent)                                │
//   │   b:, t:, r:, gitlog:                                                │
//   │   通过 OnSearchTokensChanged → HistoryFilters → BuildHistoryParams │
//   │   直接重跑 git log，影响 _rawCommits 的内容。                        │
//   │   在 EvaluateToBitmap(QuerySpec) 的 skip-list 中跳过，               │
//   │   完全不参与内存过滤。                                               │
//   │   缓存 = _rawCommits（不受任何内存过滤器影响）。                     │
//   └───────────────────────────────────┘
//                          │
//                          ▼
//   ┌──────────────────────────────────┐
//   │ 层级2: 软全局过滤器 (Soft Global)                                  │
//   │   solo:                                                            │
//   │   不触发 git log 重跑，也不在 skip-list 中。                       │
//   │   在 EvaluateToBitmap(GroupSpec) 中有特殊的 lineage 计算分支：     │
//   │     计算目标提交的提交链（CommitGraph.GetCommitLineageFast），     │
//   │     然后对 _rawCommits 做 bitmap 筛选。                            │
//   │   此外 UpdateDisplayCommits 中还会通过 ExtractSoloValues 将目标    │
//   │     SHA 提取出来，再调用 FilterCommits 做二次过滤（lineage 合并）。│
//   │   注意：solo: 在 EvaluateToBitmap 和 FilterCommits 两处都被执行，  │
//   │     两处结果应一致（lineage 过滤），功能上正确但存在冗余计算。     │
//   │   缓存优先级: 1（仅次于硬持久过滤器），缓存受硬持久过滤器影响。    │
//   └──────────────────────────────────┘
//                          │
//                          ▼
//   ┌─────────────────────────────────┐
//   │ 层级3: 内存过滤器 (In-Memory)                                    │
//   │   is:, a:, c:, e:, s:, f:, p:, since:, until:, m:, ...           │
//   │   在 EvaluateToBitmap 中逐提交求值（EvalTerm），                 │
//   │   只影响 _commits 的显示，不触发 git log。                       │
//   │   缓存优先级: 11~101，按优先级链式缩小作用域。                   │
//   └─────────────────────────────────┘
//
//   ====== 建议器优先级缓存 / Suggester Priority Cache ======
//   每个建议器从 _suggestionCache[prefix] 读取，而非直接读 _commits。
//   _suggestionCache[P] = _rawCommits 经过优先级高于 P 的组筛选后的结果。
//   优先级数字越小优先级越高（0 = 最高）。
//
//   优先级体系:
//     0: b:, t:, r:, gitlog:   (硬持久，缓存 = _rawCommits)
//     1: solo:                 (软全局，缓存受硬持久影响)
//    11: is:                   (状态筛选)
//    31: a:, c:, e:            (身份)
//    41: s:, f:, p:            (搜索)
//    51: since:, until:        (时间)
//    61: S:, G:, change:, signed:, parent: (高级 git)
//   101: m:                    (消息 — 最低)
//   999: sort:                 (不是过滤器)
//
//   缓存算法:
//     cache[P] = _rawCommits
//     对每个其他组 G:
//       跳过自身和硬持久组(b:/t:/r:/gitlog:)
//       仅当 priority(G) < priority(P) 时（G 优先级更高）才应用
//       应用方式: ExprEvaluator.Evaluate(G.Expr, val => EvalTerm(G.prefix, c, val))
//     硬持久组(b:/t:/r:)缓存 = _rawCommits（nothing is higher priority）
//
//   示例 "a:acker m:fix"：
//     cache["a:"] = _rawCommits（没有优先级高于 a: 的内存组）
//     cache["m:"] = _rawCommits 经 a: 筛选（a: 优先级高于 m:）
//     → 编辑 a: 时显示所有作者，编辑 m: 时只显示 acker 的提交信息
//
//   ====== 括号分组 / Parentheses Grouping ======
//   输入 (a:acker m:bug) (a:bob m:fix) 实现跨前缀 OR。
//   QueryParser.PartitionByParentheses() 将 token 按 () 切分为子组。
//   每个子组内部 AND 连接，子组之间 OR 连接（Union DistinctBy SHA）。
//   ( 和 ) 作为运算符 Token，无删除按钮，气泡半透明粗体显示。
//
//   ====== 逻辑模式 / Logic Modes ======
//   AutoOr (同前缀 OR):
//     相同前缀的 token 之间是 OR 关系（并集）。
//     例如 "a:acker a:bob" → 显示 acker 或 bob 的提交。
//     适用前缀: a:, m:, b:, t:, r:, gitlog:
//   AutoAnd (同前缀 AND):
//     相同前缀的 token 之间是 AND 关系（交集）。
//     例如 "is:merged is:tag" → 既是合并提交又打了标签的提交。
//     适用前缀: is:
//   SingleReplace (同前缀替换):
//     只允许一个值，新值替换旧值。
//     适用前缀: sort:
//   不同前缀之间始终是 AND 关系，与各自模式无关：
//     例如 "a:acker m:fix" → 作者为 acker 且提交消息包含 fix
//
//   ====== AutoOr 建议作用域（由缓存保证） ======
//   _commits = (所有 a: OR) AND (所有 m: OR) AND (所有 is: AND) AND ...
//
//   建议器不再直接读 _commits，而是读 _suggestionCache[其前缀]。
//   缓存中已排除了自身同前缀的组，因此:
//     - AutoOr 前缀：建议范围被更高优先级前缀缩小，但不受自身同前缀影响
//     - AutoAnd 前缀：建议范围被更高优先级前缀缩小，也被自身同前缀缩小（因为 AND 会缩小结果）
//     - 持久前缀(b:/t:/r:/solo:)：缓存 = _rawCommits，完全不受内存过滤器影响

namespace SourceGit.ViewModels
{
    // ReSharper disable once PartialTypeWithSinglePart
    partial class Histories
    {
        private static List<string> BuildGitOptionTokens(Models.HistoryShowFlags flags)
        {
            var tokens = new List<string>();
            if (flags.HasFlag(Models.HistoryShowFlags.Reflog))
                tokens.Add("gitlog:reflog");
            if (flags.HasFlag(Models.HistoryShowFlags.FirstParentOnly))
                tokens.Add("gitlog:1st-p");
            if (flags.HasFlag(Models.HistoryShowFlags.SimplifyByDecoration))
                tokens.Add("gitlog:decora");
            return tokens;
        }

        private void SyncGitTokensFromFlags(Models.HistoryShowFlags flags)
        {
            var desired = BuildGitOptionTokens(flags);
            var current = SearchTokens.Where(t => t.Raw.StartsWith("gitlog:", StringComparison.OrdinalIgnoreCase)).ToList();

            bool same = desired.Count == current.Count;
            if (same)
            {
                foreach (var token in desired)
                {
                    if (!current.Any(c => c.Raw.Equals(token, StringComparison.OrdinalIgnoreCase)))
                    {
                        same = false;
                        break;
                    }
                }
            }

            if (same)
                return;

            _suppressSearchTokenCollectionChanged = true;
            try
            {
                foreach (var old in current)
                    SearchTokens.Remove(old);

                foreach (var token in desired)
                    SearchTokens.Add(CreateTokenInstance(token));
            }
            finally
            {
                _suppressSearchTokenCollectionChanged = false;
            }
        }

        public void AddFilter(string token)
        {
            if (string.IsNullOrEmpty(token))
                return;

            _suppressSearchTokenCollectionChanged = true;
            try
            {
                var isNegated = token.StartsWith("-");
                var checkStr = isNegated ? token.Substring(1) : token;

                IsStoreProviderToken(token, out var provider, out _);
                var comp = (provider?.CaseSensitive == true) ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

                // [双语注释 / Bilingual Comment]
                // 动态提取 provider 的 CaseSensitive 属性来决定是否区分大小写去重，默认不区分。
                // Dynamically extract provider's CaseSensitive property to decide case-sensitivity for deduplication, defaulting to false.
                var current = SearchTokens.FirstOrDefault(x => x.Raw.Equals(token, comp));
                if (current != null)
                    return;

                var opposite = isNegated ? checkStr : $"-{checkStr}";
                var old = SearchTokens.FirstOrDefault(x => x.Raw.Equals(opposite, comp));
                if (old != null)
                {
                    SearchTokens.Remove(old);
                }

                SearchTokens.Add(CreateTokenInstance(token));
            }
            finally
            {
                _suppressSearchTokenCollectionChanged = false;
            }

            UpdateDisplayCommits();
        }

        private void SetupSearchProviders()
        {
            var groupGit = new TokenSuggestionGroup("git", "Git 选项", 0);
            var groupFilters = new TokenSuggestionGroup("filters", "常规过滤", 1);
            var groupView = new TokenSuggestionGroup("view", "视图控制", 2);
            var groupAdvanced = new TokenSuggestionGroup("advanced", "高级检索", 3);

            Func<string, CancellationToken, Task<IEnumerable<TokenSuggestion>>> authorSuggester = async (pattern, ct) =>
            {
                var source = _suggestionCache.GetValueOrDefault("a:") ?? _commits;
                return await Task.Run(
                    () =>
                    {
                        var results =
                            source.Select(c => c.Author)
                                .DistinctBy(a => a.Name)
                                .Where(a => string.IsNullOrEmpty(pattern) ||
                                            a.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase) ||
                                            a.Email.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                                .Take(10)
                                .Select(a => new TokenSuggestion
                                {
                                    Name = a.Name,
                                    Value = a.Name,
                                    Description = a.Email,
                                    CanExecuteDirectly = true,
                                    ActionType = TokenSuggestionActionType.Execute
                                });
                        return results;
                    },
                    ct);
            };

            // [双语注释 / Bilingual Comment]
            // 作者建议器 (a:) 设为 caseSensitive: true，启用大小写严格区分。
            // Author suggestion provider (a:) is configured with caseSensitive: true to enable strict case-sensitivity.
            SearchProviders.Add(new StaticTokenSuggestionProvider(
                "a:", "作者", groupFilters, authorSuggester,
                alias: new[] { "author:" },
                icon: App.GetIcon("Icons.User"),
                priority: 31,
                caseSensitive: true));

            Func<string, CancellationToken, Task<IEnumerable<TokenSuggestion>>> messageSuggester = async (pattern, ct) =>
            {
                var source = _suggestionCache.GetValueOrDefault("m:") ?? _commits;
                return await Task.Run(
                    () =>
                    {
                        var results =
                            source.Select(c => c.Subject)
                                .Where(s => !string.IsNullOrEmpty(s))
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .Where(s => string.IsNullOrEmpty(pattern) ||
                                            s.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                                .Take(10)
                                .Select(s => new TokenSuggestion
                                {
                                    Name = s,
                                    Value = s,
                                    CanExecuteDirectly = true,
                                    ActionType = TokenSuggestionActionType.Execute
                                });
                        return results;
                    },
                    ct);
            };

            SearchProviders.Add(new StaticTokenSuggestionProvider(
                "m:", "提交信息", groupFilters, messageSuggester,
                alias: new[] { "message:" },
                icon: App.GetIcon("Icons.Message"),
                priority: 101));

             // b:（分支）建议器 — 持久过滤器 | Suggester: Local & Remote Branches
             // 需求：b: 必须同时包含本地分支和远程分支的建议。
             // 本地分支建议 Name（不带 refs/heads/），远程分支建议 FriendlyName（如 origin/main）。
             // Requirements: b: must suggest both local branches (short Name) and remote branches (FriendlyName).
             Func<string, CancellationToken, Task<IEnumerable<TokenSuggestion>>> branchSuggester = async (pattern, ct) =>
             {
                 return await Task.Run(
                     () =>
                     {
                         var results =
                             _repo.Branches
                                 .Select(b => b.IsLocal ? b.Name : b.FriendlyName)
                                 .Where(n => !string.IsNullOrEmpty(n))
                                 .Distinct(StringComparer.OrdinalIgnoreCase)
                                 .Where(n => string.IsNullOrEmpty(pattern) ||
                                             n.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                                 .OrderBy(n => n)
                                 .Take(20)
                                 .Select(n => new TokenSuggestion
                                 {
                                     Name = n,
                                     Value = n,
                                     CanExecuteDirectly = true,
                                     ActionType = TokenSuggestionActionType.Execute
                                 });
                         return results;
                     },
                     ct);
             };
 
             // [双语注释 / Bilingual Comment]
             // 分支建议器 (b:) 设为 caseSensitive: true，启用大小写严格区分（在 Git 中分支名区分大小写）。
             // Branch suggestion provider (b:) is configured with caseSensitive: true to enable strict case-sensitivity as branch names are case-sensitive in Git.
             SearchProviders.Add(
                 new StaticTokenSuggestionProvider(
                 "b:", "分支", groupFilters, branchSuggester, alias: new[] { "branch:" },
                 icon: App.GetIcon("Icons.Branch"), priority: 0, isPersistent: true, caseSensitive: true));

            // r:（远程分支/远程名）建议器 — 持久过滤器
            // Suggester: Remote Branches + Remote Names
            // 数据来源: _repo.Branches（取所有 IsLocal==false 的分支）+
            //           _repo.Remotes（取远程名本身，如 "origin"）
            // 建议列表包含两类条目：
            //   1) 裸远程名（如 "origin"）→ 选择后走 RemoteBranchFolder 分支，
            //      生成 --remotes=origin/* 过滤该远程下所有分支。
            //   2) 完整远程分支（如 "origin/main"、"origin/gitlab/Github"）→
            //      选择后走 RemoteBranch 分支，精确匹配 refs/remotes/origin/main。
            // 与旧实现不同：旧方案从 _commits.Decorators 的 RemoteBranchHead 中提取 Name，
            // 只能拿到远程分支的短名（如 "main"），且只有已加载的提交上出现的分支才会显示。
            Func<string, CancellationToken, Task<IEnumerable<TokenSuggestion>>> remoteBranchSuggester =
                async (pattern, ct) =>
            {
                return await Task.Run(
                    () =>
                    {
                        var branchNames = _repo.Branches
                            .Where(b => !b.IsLocal)
                            .Select(b => b.FriendlyName);

                        var remoteNames = _repo.Remotes
                            .Select(r => r.Name);

                        var results = branchNames.Concat(remoteNames)
                            .Where(n => !string.IsNullOrEmpty(n))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Where(n => string.IsNullOrEmpty(pattern) ||
                                        n.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                            .OrderBy(n => n)
                            .Take(20)
                            .Select(n => new TokenSuggestion
                            {
                                Name = n,
                                Value = n,
                                CanExecuteDirectly = true,
                                ActionType = TokenSuggestionActionType.Execute
                            });
                        return results;
                    },
                    ct);
            };

            // [双语注释 / Bilingual Comment]
            // 远程分支建议器 (r:) 设为 caseSensitive: true，启用大小写严格区分（Git 中的远程分支名区分大小写）。
            // Remote branch suggestion provider (r:) is configured with caseSensitive: true to enable strict case-sensitivity as remote branch names are case-sensitive in Git.
            SearchProviders.Add(new StaticTokenSuggestionProvider(
                "r:", "远程分支", groupFilters, remoteBranchSuggester,
                alias: new[] { "remote:" },
                icon: App.GetIcon("Icons.Branch"),
                priority: 0,
                isPersistent: true,
                caseSensitive: true));

            // t:（标签）建议器 — 持久过滤器 | Suggester: Tags
            // 数据来源: _repo.Tags（Repository 中的标签列表）
            // 与旧实现不同：旧方案从 _commits.Decorators 的 Tag 中提取，
            // 只有已加载提交上的标签才会显示；新方案覆盖仓库中所有标签。
            Func<string, CancellationToken, Task<IEnumerable<TokenSuggestion>>> tagSuggester = async (pattern, ct) =>
            {
                return await Task.Run(
                    () =>
                    {
                        var results =
                            _repo.Tags
                                .Select(t => t.Name)
                                .Where(n => !string.IsNullOrEmpty(n))
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .Where(n => string.IsNullOrEmpty(pattern) ||
                                            n.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                                .OrderBy(n => n)
                                .Take(20)
                                .Select(n => new TokenSuggestion
                                {
                                    Name = n,
                                    Value = n,
                                    CanExecuteDirectly = true,
                                    ActionType = TokenSuggestionActionType.Execute
                                });
                        return results;
                    },
                    ct);
            };

            // [双语注释 / Bilingual Comment]
            // 标签建议器 (t:) 设为 caseSensitive: true，启用大小写严格区分（Git 中的标签名区分大小写）。
            // Tag suggestion provider (t:) is configured with caseSensitive: true to enable strict case-sensitivity as tags are case-sensitive in Git.
            SearchProviders.Add(new StaticTokenSuggestionProvider(
                "t:", "标签", groupFilters, tagSuggester,
                alias: new[] { "tag:" },
                icon: App.GetIcon("Icons.Tag"),
                priority: 0,
                isPersistent: true,
                caseSensitive: true));

            SearchProviders.Add(new StaticTokenSuggestionProvider(
                "s:", "哈希", groupFilters, alias: new[] { "sha:" },
                icon: App.GetIcon("Icons.Hash"),
                priority: 41));

            Func<string, CancellationToken, Task<IEnumerable<TokenSuggestion>>> isSuggester =
                (string pattern, CancellationToken ct) =>
            {
                var options = new[] { "merged", "unmerged", "tag", "branch", "merge", "cherrypick", "head", "folded" };
                var filtered = options
                                   .Where(x => string.IsNullOrEmpty(pattern) ||
                                               x.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                                   .Select(x => new TokenSuggestion
                                   {
                                       Name = x,
                                       Value = x,
                                       CanExecuteDirectly = true,
                                       ActionType = TokenSuggestionActionType.Execute
                                   });
                return Task.FromResult(filtered);
            };
            SearchProviders.Add(new StaticTokenSuggestionProvider(
                "is:", "状态过滤", groupAdvanced, isSuggester,
                TokenLogicMode.AutoAnd, icon: App.GetIcon("Icons.Filter"),
                priority: 11));

            var dateConverter = new Controls.DateValueConverter();
            SearchProviders.Add(new StaticTokenSuggestionProvider(
                "since:", "起始时间", groupFilters, alias: new[] { "after:" },
                icon: App.GetIcon("Icons.DateTime"),
                priority: 51,
                valueConverter: dateConverter,
                editorType: TokenEditorType.Date));
            SearchProviders.Add(new StaticTokenSuggestionProvider(
                "until:", "结束时间", groupFilters, alias: new[] { "before:" },
                icon: App.GetIcon("Icons.DateTime"),
                priority: 51,
                valueConverter: dateConverter,
                editorType: TokenEditorType.Date));

            SearchProviders.Add(new StaticTokenSuggestionProvider(
                "f:", "修改文件", groupFilters, alias: new[] { "file:" },
                icon: App.GetIcon("Icons.File"),
                priority: 41));
            SearchProviders.Add(new StaticTokenSuggestionProvider(
                "p:", "修改路径", groupFilters, alias: new[] { "path:" },
                icon: App.GetIcon("Icons.Folder"),
                priority: 41));

            // [双语注释 / Bilingual Comment]
            // 提交者 (c:) 建议器：自动从当前提交中动态提取并去重获取提交者建议项。
            // Committer (c:) suggester: automatically extract and distinct committer suggestions from the current commits.
            Func<string, CancellationToken, Task<IEnumerable<TokenSuggestion>>> committerSuggester = async (pattern, ct) =>
            {
                var source = _suggestionCache.GetValueOrDefault("c:") ?? _commits;
                return await Task.Run(
                    () =>
                    {
                        var results =
                            source.Select(c => c.Committer)
                                .DistinctBy(co => co.Name)
                                .Where(co => string.IsNullOrEmpty(pattern) ||
                                             co.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase) ||
                                             co.Email.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                                .Take(10)
                                .Select(co => new TokenSuggestion
                                {
                                    Name = co.Name,
                                    Value = co.Name,
                                    Description = co.Email,
                                    CanExecuteDirectly = true,
                                    ActionType = TokenSuggestionActionType.Execute
                                });
                        return results;
                    },
                    ct);
            };

            // [双语注释 / Bilingual Comment]
            // 邮箱 (e:) 建议器：提取作者和提交者邮箱进行智能提示，解决旧版本中输入 "e:" 无任何提示的问题。
            // Email (e:) suggester: extract and distinct emails from both authors and committers to provide search suggestion, resolving the bug where typing "e:" gave no suggestions.
            Func<string, CancellationToken, Task<IEnumerable<TokenSuggestion>>> emailSuggester = async (pattern, ct) =>
            {
                var source = _suggestionCache.GetValueOrDefault("e:") ?? _commits;
                return await Task.Run(
                    () =>
                    {
                        var authorEmails = source.Select(c => c.Author.Email);
                        var committerEmails = source.Select(c => c.Committer.Email);
                        var results = authorEmails.Concat(committerEmails)
                            .Where(email => !string.IsNullOrEmpty(email))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Where(email => string.IsNullOrEmpty(pattern) ||
                                            email.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                            .Take(10)
                            .Select(email => new TokenSuggestion
                            {
                                Name = email,
                                Value = email,
                                CanExecuteDirectly = true,
                                ActionType = TokenSuggestionActionType.Execute
                            });
                        return results;
                    },
                    ct);
            };

            // [双语注释 / Bilingual Comment]
            // 提交者建议器 (c:) 设为 caseSensitive: true，启用大小写严格区分。
            // Committer suggestion provider (c:) is configured with caseSensitive: true to enable strict case-sensitivity.
            SearchProviders.Add(new StaticTokenSuggestionProvider(
                "c:", "提交者", groupAdvanced, committerSuggester,
                alias: new[] { "committer:" },
                icon: App.GetIcon("Icons.User"),
                priority: 31,
                caseSensitive: true));
            SearchProviders.Add(new StaticTokenSuggestionProvider(
                "e:", "邮箱", groupAdvanced, emailSuggester,
                alias: new[] { "email:" },
                icon: App.GetIcon("Icons.Email"),
                priority: 31));

            Func<string, CancellationToken, Task<IEnumerable<TokenSuggestion>>> gitLogSuggester =
                (string pattern, CancellationToken ct) =>
            {
                var options = new[] { "reflog", "1st-p", "decora" };
                var filtered = options
                                   .Where(x => string.IsNullOrEmpty(pattern) ||
                                               x.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                                   .Select(x => new TokenSuggestion
                                   {
                                       Name = x,
                                       Value = x,
                                       CanExecuteDirectly = true,
                                       ActionType = TokenSuggestionActionType.Execute
                                   });
                return Task.FromResult(filtered);
            };

            SearchProviders.Add(new StaticTokenSuggestionProvider(
                "gitlog:", "git 解析选项", groupGit, gitLogSuggester,
                icon: App.GetIcon("Icons.Code"), priority: 0,
                isPersistent: true));

            Func<string, CancellationToken, Task<IEnumerable<TokenSuggestion>>> soloSuggester = async (pattern, ct) =>
            {
                var query = pattern?.Trim() ?? string.Empty;
                var soloSource = _suggestionCache.GetValueOrDefault("solo:") ?? _commits;
                return await Task.Run(
                    () =>
                    {
                        var results =
                            soloSource.Where(c => !string.IsNullOrWhiteSpace(c?.SHA))
                                .Where(c => string.IsNullOrEmpty(query) ||
                                            (!string.IsNullOrEmpty(c.Subject) &&
                                             c.Subject.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                                            c.SHA.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                                .Take(100)
                                .Select(c => new TokenSuggestion
                                {
                                    Name = string.IsNullOrWhiteSpace(c.Subject) ? c.SHA[..Math.Min(10, c.SHA.Length)]
                                                                                : c.Subject,
                                    Value = c.SHA,
                                    Description = $"{c.SHA[..Math.Min(10, c.SHA.Length)]} · {c.Author.Name}",
                                    CanExecuteDirectly = true,
                                    ActionType = TokenSuggestionActionType.Execute
                                });

                        var head = soloSource.FirstOrDefault(x => x.IsCurrentHead);
                        if (head != null &&
                            (string.IsNullOrEmpty(query) || "HEAD".Contains(query, StringComparison.OrdinalIgnoreCase) ||
                             (!string.IsNullOrEmpty(head.Subject) &&
                              head.Subject.Contains(query, StringComparison.OrdinalIgnoreCase))))
                        {
                            var list = results.ToList();
                            list.Insert(0, new TokenSuggestion
                            {
                                Name = "HEAD",
                                Value = "HEAD",
                                Description = string.IsNullOrWhiteSpace(head.Subject) ? "当前分支头提交" : head.Subject,
                                CanExecuteDirectly = true,
                                ActionType = TokenSuggestionActionType.Execute
                            });
                            return list.DistinctBy(c => c.Value ?? c.Name);
                        }
                        return results.DistinctBy(c => c.Value ?? c.Name);
                    },
                    ct);
            };

            SearchProviders.Add(new StaticTokenSuggestionProvider(
                "solo:", "Solo 提交链过滤", groupView, soloSuggester,
                alias: new[] { "sole:" },
                icon: App.GetIcon("Icons.Commit"),
                priority: 1));

            // [双语注释 / Bilingual Comment]
            // 初始化静态字典 _providersMap，以便在静态方法 EvalTerm 中高效地以 O(1) 复杂度访问对应前缀的 CaseSensitive 属性。
            // Initialize the static dictionary _providersMap to efficiently access the CaseSensitive property for a prefix in the static EvalTerm method with O(1) complexity.
            _providersMap.Clear();
            foreach (var p in SearchProviders)
            {
                _providersMap[p.Prefix] = p;
                if (p.FullPrefix != null)
                {
                    foreach (var alias in p.FullPrefix)
                        _providersMap[alias] = p;
                }
            }
        }

        private void OnSearchTokensChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (_suppressSearchTokenCollectionChanged)
                return;

            var gitOptions = SearchTokens.Where(t => t.Raw.StartsWith("gitlog:", StringComparison.OrdinalIgnoreCase))
                                 .Select(t => t.Raw.Substring(t.Raw.IndexOf(':') + 1).Trim())
                                 .Where(t => !string.IsNullOrEmpty(t))
                                 .ToList();

            var refreshGitLog = false;
            if (gitOptions.Count > 0)
            {
                var flags = Models.HistoryShowFlags.None;
                if (gitOptions.Any(o => o.Equals("reflog", StringComparison.OrdinalIgnoreCase)))
                    flags |= Models.HistoryShowFlags.Reflog;
                if (gitOptions.Any(o => o.Equals("1st-p", StringComparison.OrdinalIgnoreCase)))
                    flags |= Models.HistoryShowFlags.FirstParentOnly;
                if (gitOptions.Any(o => o.Equals("decora", StringComparison.OrdinalIgnoreCase)))
                    flags |= Models.HistoryShowFlags.SimplifyByDecoration;

                if (_repo.HistoryShowFlags != flags)
                {
                    _syncingRepoFlagsFromTokens = true;
                    _repo.HistoryShowFlags = flags;
                    _syncingRepoFlagsFromTokens = false;
                    refreshGitLog = true;
                }
                _gitOptionsDrivenByTokens = true;
            }
            else if (_gitOptionsDrivenByTokens)
            {
                if (_repo.HistoryShowFlags != Models.HistoryShowFlags.None)
                {
                    _syncingRepoFlagsFromTokens = true;
                    _repo.HistoryShowFlags = Models.HistoryShowFlags.None;
                    _syncingRepoFlagsFromTokens = false;
                    refreshGitLog = true;
                }
                _gitOptionsDrivenByTokens = false;
            }

            var newSearchFilters = new List<Models.HistoryFilter>();
            foreach (var token in SearchTokens)
            {
                var isNeg = token.Raw.StartsWith("-", StringComparison.Ordinal);
                var check = isNeg ? token.Raw[1..] : token.Raw;
                var mode = isNeg ? Models.FilterMode.Excluded : Models.FilterMode.Included;

                // b: (分支检索) - 动态识别并支持本地/远程分支，纠正 FCC 强行转为 LocalBranch 的硬编码 Bug
                // b: (Branch Search) - Dynamically identify and support both local and remote branches
                if (check.StartsWith("b:", StringComparison.OrdinalIgnoreCase) ||
                    check.StartsWith("branch:", StringComparison.OrdinalIgnoreCase))
                {
                    var val = check[(check.IndexOf(':') + 1)..].Trim();
                    if (val.Length > 0)
                    {
                        // 1. 判断是否是远程分支 FriendlyName (e.g. origin/main) 或者是带有远程前缀斜杠的局部输入
                        // 1. Check if it matches a remote branch FriendlyName or contains a slash with a known remote name
                        bool isRemote = _repo.Branches.Any(b => !b.IsLocal && b.FriendlyName.Equals(val, StringComparison.Ordinal));
                        if (!isRemote)
                        {
                            var slashIdx = val.IndexOf('/');
                            if (slashIdx > 0)
                            {
                                var remotePrefix = val.Substring(0, slashIdx);
                                if (_repo.Remotes.Any(r => r.Name.Equals(remotePrefix, StringComparison.OrdinalIgnoreCase)))
                                {
                                    isRemote = true;
                                }
                            }
                        }

                        // 2. 根据识别结果，动态绑定到 LocalBranch (refs/heads/) 或 RemoteBranch (refs/remotes/)
                        // 2. Bind dynamically to LocalBranch or RemoteBranch based on identification
                        if (isRemote)
                        {
                            newSearchFilters.Add(
                                new Models.HistoryFilter($"refs/remotes/{val}", Models.FilterType.RemoteBranch, mode));
                        }
                        else
                        {
                            newSearchFilters.Add(
                                new Models.HistoryFilter($"refs/heads/{val}", Models.FilterType.LocalBranch, mode));
                        }
                    }
                }
                else if (check.StartsWith("t:", StringComparison.OrdinalIgnoreCase) ||
                         check.StartsWith("tag:", StringComparison.OrdinalIgnoreCase))
                {
                    var val = check[(check.IndexOf(':') + 1)..].Trim();
                    if (val.Length > 0)
                        newSearchFilters.Add(new Models.HistoryFilter(val, Models.FilterType.Tag, mode));
                }
                // r: (远程检索) - 支持远程主机名（RemoteBranchFolder）以及完整远程分支（RemoteBranch）
                // r: (Remote Search) - Support remote host folder and remote branches
                else if (check.StartsWith("r:", StringComparison.OrdinalIgnoreCase) ||
                         check.StartsWith("remote:", StringComparison.OrdinalIgnoreCase))
                {
                    var val = check[(check.IndexOf(':') + 1)..].Trim();
                    if (val.Length > 0)
                    {
                        var type = val.Contains('/') ? Models.FilterType.RemoteBranch : Models.FilterType.RemoteBranchFolder;
                        newSearchFilters.Add(new Models.HistoryFilter($"refs/remotes/{val}", type, mode));
                    }
                }
            }

            var seen = new Dictionary<(string, Models.FilterType), Models.FilterMode>();
            var toCancel = new HashSet<(string, Models.FilterType)>();
            foreach (var f in newSearchFilters)
            {
                var key = (f.Pattern, f.Type);
                if (seen.TryGetValue(key, out var existing))
                {
                    if (existing != f.Mode)
                        toCancel.Add(key);
                }
                else
                    seen[key] = f.Mode;
            }
            newSearchFilters.RemoveAll(f => toCancel.Contains((f.Pattern, f.Type)));

            var changed = newSearchFilters.Count != _searchDrivenFilters.Count;
            if (!changed)
            {
                for (int i = 0; i < newSearchFilters.Count; i++)
                {
                    var a = newSearchFilters[i];
                    var b = _searchDrivenFilters[i];
                    if (a.Pattern != b.Pattern || a.Type != b.Type || a.Mode != b.Mode)
                    {
                        changed = true;
                        break;
                    }
                }
            }

            if (changed)
            {
                _suppressHistoryFiltersCollectionChanged = true;
                try
                {
                    foreach (var old in _searchDrivenFilters)
                        _repo.UIStates.HistoryFilters.Remove(old);
                    _searchDrivenFilters.Clear();
                    foreach (var nf in newSearchFilters)
                    {
                        var exists = _repo.UIStates.HistoryFilters.Any(f => f.Pattern == nf.Pattern && f.Type == nf.Type &&
                                                                            f.Mode == nf.Mode);
                        if (!exists)
                        {
                            _repo.UIStates.HistoryFilters.Add(nf);
                            _searchDrivenFilters.Add(nf);
                        }
                    }
                }
                finally
                {
                    _suppressHistoryFiltersCollectionChanged = false;
                }
                if (!refreshGitLog)
                {
                    _repo.RefreshCommits();
                    refreshGitLog = true;
                }
            }

            UpdateDisplayCommits();
        }

        private Models.HistoryFilter ConvertTokenToHistoryFilter(string token, ITokenSuggestionProvider provider,
                                                                 string matchedPrefix)
        {
            var isNegated = token.StartsWith("-", StringComparison.Ordinal);
            var val = token.Substring(matchedPrefix.Length + (isNegated ? 1 : 0)).Trim();
            if (string.IsNullOrEmpty(val))
                return null;

            var type = provider.Prefix switch
            {
                "b:" => Models.FilterType.LocalBranch,
                "r:" => Models.FilterType.RemoteBranch,
                "t:" => Models.FilterType.Tag,
                _ => Models.FilterType.LocalBranch,
            };

            return new Models.HistoryFilter
            {
                Pattern = val,
                Type = type,
                Mode = isNegated ? Models.FilterMode.Excluded : Models.FilterMode.Included
            };
        }

        private void SyncGitTokensFromFlags_Legacy(Models.HistoryShowFlags flags)
        {
            if (_gitOptionsDrivenByTokens)
                return;

            _suppressSearchTokenCollectionChanged = true;
            try
            {
                var existing =
                    SearchTokens.Where(t => t.Raw.StartsWith("gitlog:", StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var t in existing)
                    SearchTokens.Remove(t);

                if (flags.HasFlag(Models.HistoryShowFlags.FirstParentOnly))
                    SearchTokens.Add(CreateTokenInstance("gitlog:1st-p"));
            }
            finally
            {
                _suppressSearchTokenCollectionChanged = false;
            }
        }

        private void ExtractSoloValues(Controls.ExprNode node, List<string> targets)
        {
            if (node == null)
                return;
            if (node.Op == Controls.ExprOp.Term)
            {
                if (!string.IsNullOrWhiteSpace(node.Value))
                    targets.Add(node.Value);
            }
            else if (node.Children != null)
            {
                foreach (var child in node.Children)
                    ExtractSoloValues(child, targets);
            }
        }

        private BitArray EvaluateToBitmap(Controls.GroupSpec group)
        {
            var totalCount = _rawCommits.Count;
            var bits = new BitArray(totalCount, true);

            // solo: 软全局过滤器——计算目标提交的完整祖先链（lineage），
            // 然后保留该链上所有提交。不触发 git log 重跑。
            // 注意：此处存在冗余计算——在 UpdateDisplayCommits 中，
            // solo: 还会通过 ExtractSoloValues + FilterCommits 再执行一次
            // lineage 过滤。两处结果一致，功能正确但可优化。
            if (group.ProviderPrefix == "solo:")
            {
                var rawCommitMap = new Dictionary<string, Models.Commit>(_rawCommits.Count);
                for (int i = 0; i < _rawCommits.Count; i++)
                {
                    _rawCommits[i].Index = i;
                    rawCommitMap[_rawCommits[i].SHA] = _rawCommits[i];
                }

                var lineageCaches = new Dictionary<string, bool[]>();
                var soloTargets = new List<string>();
                ExtractSoloValues(group.Expr, soloTargets);

                foreach (var target in soloTargets)
                {
                    Models.Commit commit = null;
                    if (target.Equals("HEAD", StringComparison.OrdinalIgnoreCase))
                    {
                        commit = _rawCommits.Find(x => x.IsCurrentHead);
                    }
                    else if (rawCommitMap.TryGetValue(target, out var exact))
                    {
                        commit = exact;
                    }
                    else if (target.Length >= 4)
                    {
                        commit = _rawCommits.Find(x => x.SHA.StartsWith(target, StringComparison.OrdinalIgnoreCase));
                    }

                    if (commit != null)
                    {
                        var lineage = Models.CommitGraph.GetCommitLineageFast(_rawCommits, rawCommitMap, commit,
                                                                              Models.CommitLineageSearchMethod.FullLineage, (uint)_rawCommits.Count);
                        lineageCaches[target] = lineage;
                    }
                }

                if (lineageCaches.Count > 0)
                {
                    var soloGroupBits = new BitArray(totalCount);
                    for (int i = 0; i < totalCount; i++)
                    {
                        soloGroupBits[i] = Controls.ExprEvaluator.Evaluate(
                            group.Expr, node => lineageCaches.TryGetValue(node.Value, out var b) && b[i]);
                    }
                    bits.And(soloGroupBits);
                    return bits;
                }

                return bits;
            }

            var groupBits = new BitArray(totalCount);
            for (int i = 0; i < totalCount; i++)
            {
                groupBits[i] = Controls.ExprEvaluator.Evaluate(
                    group.Expr, node => EvalTerm(group.ProviderPrefix, _rawCommits[i], node));
            }
            bits.And(groupBits);

            return bits;
        }

        private BitArray EvaluateToBitmap(Controls.QuerySpec spec)
        {
            var totalCount = _rawCommits.Count;
            var bits = new BitArray(totalCount, true);

            foreach (var group in spec.Groups)
            {
                // 硬持久过滤器（gitlog:/b:/t:/r:）不在内存中求值，
                // 它们通过 OnSearchTokensChanged → HistoryFilters → BuildHistoryParams
                // 重跑 git log 来影响 _rawCommits。
                // solo: 不在此跳过——它是软全局过滤器，在 EvaluateToBitmap(GroupSpec)
                // 中有独立的 lineage 计算分支。但它也会在 UpdateDisplayCommits 中
                // 通过 ExtractSoloValues + FilterCommits 再执行一次，存在冗余。
                if (group.ProviderPrefix is "gitlog:" or "b:" or "t:" or "r:")
                    continue;
                bits.And(EvaluateToBitmap(group));
            }

            if (spec.FallbackTerms.Count > 0 || spec.FallbackNotTerms.Count > 0)
            {
                var fallbackBits = new BitArray(totalCount);
                for (int i = 0; i < totalCount; i++)
                {
                    var commit = _rawCommits[i];
                    var matches = true;
                    foreach (var term in spec.FallbackTerms)
                    {
                        if (!(commit.Subject?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) &&
                            !commit.SHA.Contains(term, StringComparison.OrdinalIgnoreCase))
                        {
                            matches = false;
                            break;
                        }
                    }
                    if (matches)
                    {
                        foreach (var term in spec.FallbackNotTerms)
                        {
                            if ((commit.Subject?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                                commit.SHA.Contains(term, StringComparison.OrdinalIgnoreCase))
                            {
                                matches = false;
                                break;
                            }
                        }
                    }
                    fallbackBits[i] = matches;
                }
                bits.And(fallbackBits);
            }

            return bits;
        }

        public TokenInstance CreateTokenInstance(string raw)
        {
            var isNegated = raw.StartsWith("-");
            var checkStr = isNegated ? raw.Substring(1) : raw;

            string matchedPrefix = null;
            var matchedProvider =
                !IsOperatorToken(raw) ? MatchProvider(SearchProviders, checkStr, out matchedPrefix) : null;

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
                var unescapedVal = Controls.TokenValueHelper.Unescape(val);
                instance.Value = advanced.ValueConverter?.ToValue(unescapedVal) ?? unescapedVal;
                instance.DisplayText =
                    (isNegated ? "-" : "") + matchedPrefix + (advanced.ValueConverter?.ToDisplay(instance.Value) ?? unescapedVal);
            }

            return instance;
        }

        private bool IsStoreProviderToken(string token, out ITokenSuggestionProvider provider, out string matchedPrefix)
        {
            provider = null;
            matchedPrefix = null;
            if (string.IsNullOrWhiteSpace(token))
                return false;
            var check = token.StartsWith("-", StringComparison.Ordinal) ? token[1..] : token;
            provider = MatchProvider(SearchProviders, check, out matchedPrefix);
            return provider?.IsPersistent == true;
        }

        private static bool IsOperatorToken(string token)
        {
            return token == "||" || token == "&&" || token == "|" || token == "&" || token == "(" || token == ")";
        }

        private static ITokenSuggestionProvider MatchProvider(IEnumerable<ITokenSuggestionProvider> providers, string text,
                                                              out string matchedPrefix)
        {
            foreach (var p in providers)
            {
                if (text.StartsWith(p.Prefix, StringComparison.OrdinalIgnoreCase))
                {
                    matchedPrefix = p.Prefix;
                    return p;
                }
                if (p.FullPrefix != null)
                {
                    foreach (var alias in p.FullPrefix)
                    {
                        if (text.StartsWith(alias, StringComparison.OrdinalIgnoreCase))
                        {
                            matchedPrefix = alias;
                            return p;
                        }
                    }
                }
            }
            matchedPrefix = null;
            return null;
        }
    }
}
