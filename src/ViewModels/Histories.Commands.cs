using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SourceGit.Controls;

namespace SourceGit.ViewModels
{
    // ReSharper disable once PartialTypeWithSinglePart
    partial class Histories
    {
        private void SetupSlashCommands()
        {
            var fieldProvider = new StaticTokenSuggestionProvider("ui:", "列名", null, new[] { "author", "author_time", "commit_time", "sha" });
            var actionProvider =
                new StaticTokenSuggestionProvider("ui:", "动作", null, new[] { "true", "false", "toggle" });
            var uiArgs =
                new List<TokenArgumentDefinition> { new TokenArgumentDefinition { Name = "field", Description = "选择列名",
                                                                              SuggestionProvider = fieldProvider },
                                                new TokenArgumentDefinition { Name = "action", Description = "操作类型",
                                                                              IsRequired = false,
                                                                              SuggestionProvider = actionProvider } };

            SearchSlashCommands.Add(new TokenSlashCommand
            {
                Name = "ui",
                Description = "视图控制：/ui <author|sha|author_time|commit_time> [true|false|toggle]",
                Icon = "M 1.5 6.5 L 4.5 9.5 L 10.5 2.5",
                RequiresArgument = true,
                Arguments = uiArgs,
                Execute = ExecuteUiSlashCommand,
            });
            SearchSlashCommands.Add(new TokenSlashCommand
            {
                Name = "st",
                Description = "设置快捷命令：/st <author|sha|author_time|commit_time> [true|false|toggle]",
                Icon = "M 1.5 6.5 L 4.5 9.5 L 10.5 2.5",
                RequiresArgument = true,
                Arguments = uiArgs,
                Execute = ExecuteUiSlashCommand,
            });

            // GOTO Command Schema
            var gotoSubCommands = new[] { "sha", "tag", "branch", "head", "commit", "solo" };
            var gotoSubCmdProvider = new StaticTokenSuggestionProvider("goto:", "子命令", null, gotoSubCommands);

            SearchSlashCommands.Add(new TokenSlashCommand
            {
                Name = "goto",
                Description = "跳转：/goto <sha|tag|branch|head|commit|solo> [参数]",
                Icon = "M 1.5 6.5 L 4.5 9.5 L 10.5 2.5",
                RequiresArgument = true,
                Arguments =
                    new List<TokenArgumentDefinition> {
                    new TokenArgumentDefinition { Name = "type", Description = "跳转类型",
                                                  SuggestionProvider = gotoSubCmdProvider },
                    new TokenArgumentDefinition {
                        Name = "target", Description = "目标值",
                        DynamicProvider =
                            ctx =>
                        {
                            var subCmd =
                                ctx.ArgumentTokens.Count > 0 ? ctx.ArgumentTokens[0].ToLowerInvariant() : string.Empty;
                            return subCmd switch {
                                "branch" => new DelegateTokenSuggestionProvider(
                                    (pattern, ct) => Task.FromResult(
                                        (_commits ?? [])
                                            .SelectMany(c => c.Decorators)
                                            .Where(d => d.Type is Models.DecoratorType.LocalBranchHead or
                                                            Models.DecoratorType.CurrentBranchHead or
                                                                Models.DecoratorType.RemoteBranchHead)
                                            .Select(d => d.Name)
                                            .Distinct(StringComparer.OrdinalIgnoreCase)
                                            .Where(n => n.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                                            .OrderBy(n => n)
                                            .Take(20)
                                            .Select(n => new TokenSuggestion { Name = n, Value = n,
                                                                               Description = "branch" }))),
                                "tag" => new DelegateTokenSuggestionProvider(
                                    (pattern, ct) => Task.FromResult(
                                        (_commits ?? [])
                                            .SelectMany(c => c.Decorators)
                                            .Where(d => d.Type == Models.DecoratorType.Tag)
                                            .Select(d => d.Name)
                                            .Distinct(StringComparer.OrdinalIgnoreCase)
                                            .Where(n => n.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                                            .OrderBy(n => n)
                                            .Take(20)
                                            .Select(n => new TokenSuggestion { Name = n, Value = n,
                                                                               Description = "tag" }))),
                                "sha" => new DelegateTokenSuggestionProvider(
                                    (pattern, ct) => Task.FromResult(
                                        (_commits ?? [])
                                            .Where(c => !string.IsNullOrWhiteSpace(c?.SHA) &&
                                                        c.SHA.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                                            .Take(10)
                                            .Select(c => new TokenSuggestion {
                                                Name = c.SHA[..Math.Min(10, c.SHA.Length)], Value = c.SHA,
                                                Description = $"{c.Subject ?? ""} · {c.Author.Name}"
                                            }))),
                                "commit" => new DelegateTokenSuggestionProvider(
                                    (pattern, ct) => Task.FromResult(
                                        (_commits ?? [])
                                            .Where(c => !string.IsNullOrWhiteSpace(c?.SHA) &&
                                                        !string.IsNullOrEmpty(c.Subject) &&
                                                        c.Subject.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                                            .Take(10)
                                            .Select(
                                                c =>
                                                    new TokenSuggestion { Name = $"{c.Subject ?? ""} · {c.Author.Name}",
                                                                          Value = c.SHA, Description = c.SHA[..10] }))),
                                "solo" => new DelegateTokenSuggestionProvider(
                                    (pattern, ct) => Task.FromResult(
                                        SearchTokens
                                            .Where(t => t.Raw.StartsWith("solo:", StringComparison.OrdinalIgnoreCase))
                                            .Select(t => t.Raw["solo:".Length..])
                                            .Where(s => !string.IsNullOrEmpty(s) &&
                                                        s.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                                            .Take(10)
                                            .Select(s => new TokenSuggestion { Name = s, Value = s,
                                                                               Description = $"[Solo] {s}" }))),
                                _ => null
                            };
                        }
                    }
                    },
                Execute = ExecuteGotoCommand,
            });

            SearchSlashCommands.Add(new TokenSlashCommand
            {
                Name = "sort",
                Description = "排序：/sort <CommitDate|Topologically>",
                Icon = "M 1.5 6.5 L 4.5 9.5 L 10.5 2.5",
                RequiresArgument = true,
                Suggest = SuggestSortArguments,
                Execute = ExecuteSortCommand,
            });

            var decoraOptions = new[] { "0", "1", "2", "3", "none", "branch", "tag", "all" };
            var decoraProvider = new StaticTokenSuggestionProvider("decora:", "显示模式", null, decoraOptions);

            SearchSlashCommands.Add(new TokenSlashCommand
            {
                Name = "decora",
                Description = "设置装饰器显示：/decora <0|1|2|3|none|branch|tag|all>",
                Icon = "M 1.5 6.5 L 4.5 9.5 L 10.5 2.5",
                RequiresArgument = true,
                Arguments = new List<TokenArgumentDefinition> {
                    new TokenArgumentDefinition {
                        Name = "mode",
                        Description = "选择显示模式",
                        SuggestionProvider = decoraProvider
                    }
                },
                Execute = ExecuteDecoraCommand,
            });
        }

        private bool ToggleColumnByName(string name)
        {
            if (name.Equals("author", StringComparison.OrdinalIgnoreCase))
            {
                IsAuthorColumnVisible = !IsAuthorColumnVisible;
                return true;
            }

            if (name.Equals("sha", StringComparison.OrdinalIgnoreCase))
            {
                IsSHAColumnVisible = !IsSHAColumnVisible;
                return true;
            }

            if (name.Equals("author_time", StringComparison.OrdinalIgnoreCase))
            {
                IsAuthorTimeColumnVisible = !IsAuthorTimeColumnVisible;
                return true;
            }

            if (name.Equals("time", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("commit_time", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("datetime", StringComparison.OrdinalIgnoreCase))
            {
                IsCommitTimeColumnVisible = !IsCommitTimeColumnVisible;
                return true;
            }

            return false;
        }

        private bool SetColumnByName(string name, bool value)
        {
            if (name.Equals("author", StringComparison.OrdinalIgnoreCase))
            {
                IsAuthorColumnVisible = value;
                return true;
            }

            if (name.Equals("sha", StringComparison.OrdinalIgnoreCase))
            {
                IsSHAColumnVisible = value;
                return true;
            }

            if (name.Equals("author_time", StringComparison.OrdinalIgnoreCase))
            {
                IsAuthorTimeColumnVisible = value;
                return true;
            }

            if (name.Equals("time", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("commit_time", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("datetime", StringComparison.OrdinalIgnoreCase))
            {
                IsCommitTimeColumnVisible = value;
                return true;
            }

            return false;
        }

        private static bool IsKnownUiField(string name)
        {
            return name.Equals("author", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("sha", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("author_time", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("commit_time", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("time", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("datetime", StringComparison.OrdinalIgnoreCase);
        }

        private bool ExecuteUiSlashCommand(TokenSlashExecuteContext ctx)
        {
            var tokens = ctx.ArgumentTokens ?? [];
            if (tokens.Count == 0)
                return false;

            var field = tokens[0];
            if (!IsKnownUiField(field))
                return false;

            if (tokens.Count == 1)
                // toggle is default if no mode specified
                return ToggleColumnByName(field);

            var mode = tokens[1].Trim().ToLowerInvariant();
            return mode switch
            {
                "true" or "on" or "1" => SetColumnByName(field, true),
                "false" or "off" or "0" => SetColumnByName(field, false),
                "toggle" => ToggleColumnByName(field),
                _ => false,
            };
        }

        private bool ExecuteGotoCommand(TokenSlashExecuteContext ctx)
        {
            var tokens = ctx.ArgumentTokens?.ToList() ?? [];
            if (tokens.Count == 0)
                return false;

            var subCmd = tokens[0].Trim().ToLowerInvariant();
            switch (subCmd)
            {
                case "head":
                    _repo.NavigateToCommit("HEAD");
                    return true;

                case "branch":
                    if (tokens.Count < 2)
                        return false;
                    _repo.NavigateToBranch(tokens[1].Trim());
                    return true;

                case "tag":
                    if (tokens.Count < 2)
                        return false;
                    _repo.NavigateToTag(string.Join(" ", tokens.Skip(1)).Trim());
                    return true;

                case "sha":
                    if (tokens.Count < 2)
                        return false;
                    _repo.NavigateToCommit(tokens[1].Trim());
                    return true;

                case "solo":
                    if (tokens.Count < 2)
                        return false;
                    _repo.NavigateToCommit(tokens[1].Trim());
                    return true;

                case "commit":
                    if (tokens.Count < 2)
                        return false;

                    // Logic was: search for first subject matching query.
                    // Now improved: if suggestions provided a SHA value directly, NavigateToCommit would handle it better.
                    // But to maintain exact compatibility:
                    var query = string.Join(" ", tokens.Skip(1)).Trim().ToLowerInvariant();

                    // If the user picked a suggestion, the token[1] might already be a SHA (from our new schema).
                    // Try exact SHA match first:
                    if (_commitMap.ContainsKey(query))
                    {
                        _repo.NavigateToCommit(query);
                        return true;
                    }

                    var match = (_commits ?? [])
                                    .FirstOrDefault(c => c.Subject != null &&
                                                         c.Subject.Contains(query, StringComparison.OrdinalIgnoreCase));
                    if (match == null)
                        return false;
                    _repo.NavigateToCommit(match.SHA);
                    return true;

                default:
                    return false;
            }
        }

        private IEnumerable<TokenSuggestion> SuggestSortArguments(TokenSlashSuggestionContext ctx)
        {
            var active = ctx.ActiveToken ?? string.Empty;
            var options = new[] {
            new TokenSuggestion { Name = "Topologically", Description = "按拓扑顺序排序" },
            new TokenSuggestion { Name = "CommitDate", Description = "按提交日期排序" },
        };

            foreach (var opt in options)
            {
                if (!string.IsNullOrEmpty(active) && !opt.Name.Contains(active, StringComparison.OrdinalIgnoreCase))
                    continue;
                yield return opt;
            }
        }

        private bool ExecuteSortCommand(TokenSlashExecuteContext ctx)
        {
            var tokens = ctx.ArgumentTokens ?? [];
            if (tokens.Count == 0)
                return false;

            var value = string.Join(" ", tokens).Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(value))
                return false;

            switch (value)
            {
                case "topologically":
                case "topo":
                case "topological":
                    _repo.EnableTopoOrderInHistory = true;
                    return true;
                case "commit date":
                case "commitdate":
                case "time":
                case "date":
                    _repo.EnableTopoOrderInHistory = false;
                    return true;
                default:
                    return false;
            }
        }

        private bool ExecuteDecoraCommand(TokenSlashExecuteContext ctx)
        {
            var tokens = ctx.ArgumentTokens ?? [];
            if (tokens.Count == 0)
                return false;

            var value = tokens[0].Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(value))
                return false;

            int mode = -1;
            switch (value)
            {
                case "0":
                case "none":
                    mode = 0;
                    break;
                case "1":
                case "branch":
                case "branches":
                    mode = 1;
                    break;
                case "2":
                case "tag":
                case "tags":
                    mode = 2;
                    break;
                case "3":
                case "all":
                    mode = 3;
                    break;
                default:
                    return false;
            }

            Preferences.Instance.DecoratorDisplayMode = mode;
            return true;
        }
    }
}
