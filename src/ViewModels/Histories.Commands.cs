using System;
using System.Collections.Generic;
using System.Linq;
using SourceGit.Controls;

namespace SourceGit.ViewModels
{
    // ReSharper disable once PartialTypeWithSinglePart
    partial class Histories
    {
        private void SetupSlashCommands()
        {
            SearchSlashCommands.Add(new TokenSlashCommand
            {
                Name = "ui",
                Description = "视图控制：/ui <author|sha|time> [true|false|toggle]",
                Icon = "M 1.5 6.5 L 4.5 9.5 L 10.5 2.5",
                RequiresArgument = true,
                Suggest = SuggestUiSlashArguments,
                Execute = ExecuteUiSlashCommand,
            });
            SearchSlashCommands.Add(new TokenSlashCommand
            {
                Name = "st",
                Description = "设置快捷命令：/st <author|sha|time> [true|false|toggle]",
                Icon = "M 1.5 6.5 L 4.5 9.5 L 10.5 2.5",
                RequiresArgument = true,
                Suggest = SuggestUiSlashArguments,
                Execute = ExecuteUiSlashCommand,
            });
            SearchSlashCommands.Add(new TokenSlashCommand
            {
                Name = "goto",
                Description = "跳转：/goto <sha|tag|branch|head|commit|solo> [参数]",
                Icon = "M 1.5 6.5 L 4.5 9.5 L 10.5 2.5",
                RequiresArgument = true,
                Suggest = SuggestGotoArguments,
                Execute = ExecuteGotoCommand,
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

            if (name.Equals("time", StringComparison.OrdinalIgnoreCase) || name.Equals("datetime", StringComparison.OrdinalIgnoreCase))
            {
                IsDateTimeColumnVisible = !IsDateTimeColumnVisible;
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

            if (name.Equals("time", StringComparison.OrdinalIgnoreCase) || name.Equals("datetime", StringComparison.OrdinalIgnoreCase))
            {
                IsDateTimeColumnVisible = value;
                return true;
            }

            return false;
        }

        private static bool IsKnownUiField(string name)
        {
            return name.Equals("author", StringComparison.OrdinalIgnoreCase)
                || name.Equals("sha", StringComparison.OrdinalIgnoreCase)
                || name.Equals("time", StringComparison.OrdinalIgnoreCase)
                || name.Equals("datetime", StringComparison.OrdinalIgnoreCase);
        }

        private IEnumerable<TokenSuggestion> SuggestUiSlashArguments(TokenSlashSuggestionContext ctx)
        {
            static IEnumerable<TokenSuggestion> BuildFieldSuggestions(string pattern)
            {
                var fields = new[]
                {
                    new TokenSuggestion { Name = "author", Description = "作者列" },
                    new TokenSuggestion { Name = "sha", Description = "SHA 列" },
                    new TokenSuggestion { Name = "time", Description = "时间列" },
                };

                if (string.IsNullOrWhiteSpace(pattern))
                    return fields;

                return fields.Where(x => x.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase));
            }

            static IEnumerable<TokenSuggestion> BuildValueSuggestions(string field, string pattern)
            {
                var values = new[]
                {
                    new TokenSuggestion { Name = $"{field} true", Description = "显式开启" },
                    new TokenSuggestion { Name = $"{field} false", Description = "显式关闭" },
                    new TokenSuggestion { Name = $"{field} toggle", Description = "切换" },
                };

                if (string.IsNullOrWhiteSpace(pattern))
                    return values;

                return values.Where(x => x.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase));
            }

            var tokens = ctx.ArgumentTokens ?? [];
            var active = ctx.ActiveToken ?? string.Empty;

            if (ctx.ActiveTokenIndex <= 0)
                return BuildFieldSuggestions(active);

            var field = tokens.Count > 0 ? tokens[0] : string.Empty;
            if (string.IsNullOrWhiteSpace(field) || !IsKnownUiField(field))
                return BuildFieldSuggestions(field);

            return BuildValueSuggestions(field, active);
        }

        private bool ExecuteUiSlashCommand(TokenSlashExecuteContext ctx)
        {
            var tokens = ctx.ArgumentTokens ?? [];
            if (tokens.Count == 0)
                return false;

            var field = tokens[0];
            if (tokens.Count == 1)
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

        private IEnumerable<TokenSuggestion> SuggestGotoArguments(TokenSlashSuggestionContext ctx)
        {
            var tokens = ctx.ArgumentTokens;
            var endsWithSpace = ctx.EndsWithWhitespace;
            var active = ctx.ActiveToken ?? string.Empty;

            // No sub-command yet: suggest sub-commands
            if (tokens.Count == 0 || (tokens.Count == 1 && !endsWithSpace))
            {
                var subCommands = new[] { "sha", "tag", "branch", "head", "commit", "solo" };
                foreach (var cmd in subCommands)
                {
                    if (!string.IsNullOrEmpty(active) && !cmd.StartsWith(active, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var syntax = cmd switch
                    {
                        "sha" => "/goto sha <SHA>",
                        "tag" => "/goto tag <tagname>",
                        "branch" => "/goto branch <name>",
                        "head" => "/goto head",
                        "commit" => "/goto commit <keyword>",
                        "solo" => "/goto solo <SHA>",
                        _ => "",
                    };
                    yield return new TokenSuggestion { Name = cmd, Description = syntax };
                }
                yield break;
            }

            // Has sub-command: suggest values
            var subCmd = tokens[0].ToLowerInvariant();
            switch (subCmd)
            {
                case "head":
                    yield break;

                case "branch":
                    var branchNames = (_commits ?? [])
                        .SelectMany(c => c.Decorators)
                        .Where(d => d.Type is Models.DecoratorType.LocalBranchHead
                                or Models.DecoratorType.CurrentBranchHead
                                or Models.DecoratorType.RemoteBranchHead)
                        .Select(d => d.Name)
                        .Where(n => !string.IsNullOrEmpty(n))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Where(n => n.Contains(active, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(n => n)
                        .Take(20);
                    foreach (var name in branchNames)
                        yield return new TokenSuggestion
                        {
                            Name = name,
                            Value = $"branch {name}",
                            Description = "branch"
                        };
                    break;

                case "tag":
                    var tagNames = (_commits ?? [])
                        .SelectMany(c => c.Decorators)
                        .Where(d => d.Type == Models.DecoratorType.Tag)
                        .Select(d => d.Name)
                        .Where(n => !string.IsNullOrEmpty(n))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Where(n => n.Contains(active, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(n => n)
                        .Take(20);
                    foreach (var name in tagNames)
                        yield return new TokenSuggestion
                        {
                            Name = name,
                            Value = $"tag {name}",
                            Description = "tag"
                        };
                    break;

                case "sha":
                    var shaMatches = (_commits ?? [])
                        .Where(c => !string.IsNullOrWhiteSpace(c?.SHA) && c.SHA.StartsWith(active, StringComparison.OrdinalIgnoreCase))
                        .Take(10)
                        .Select(c =>
                        {
                            var sha = c.SHA[..Math.Min(10, c.SHA.Length)];
                            return new TokenSuggestion
                            {
                                Name = sha,
                                Value = $"sha {sha}",
                                Description = $"{c.Subject ?? ""} · {c.Author.Name}",
                            };
                        });
                    foreach (var s in shaMatches)
                        yield return s;
                    break;

                case "solo":
                    var soloTokens = SearchTokens
                        .Where(t => t.StartsWith("solo:", StringComparison.OrdinalIgnoreCase))
                        .Select(t => t["solo:".Length..])
                        .Where(s => !string.IsNullOrEmpty(s) && (string.IsNullOrEmpty(active) || s.StartsWith(active, StringComparison.OrdinalIgnoreCase)))
                        .Take(10);
                    foreach (var sha in soloTokens)
                        yield return new TokenSuggestion
                        {
                            Name = sha,
                            Value = $"solo {sha}",
                            Description = $"[Solo] {sha}"
                        };
                    break;

                case "commit":
                    var msgMatches = (_commits ?? [])
                        .Where(c => !string.IsNullOrWhiteSpace(c?.SHA) && !string.IsNullOrEmpty(c.Subject) && c.Subject.Contains(active, StringComparison.OrdinalIgnoreCase))
                        .Take(10)
                        .Select(c =>
                        {
                            var sha = c.SHA[..Math.Min(10, c.SHA.Length)];
                            return new TokenSuggestion
                            {
                                Name = $"{c.Subject ?? ""} · {c.Author.Name}",
                                Description = sha,
                            };
                        });
                    foreach (var s in msgMatches)
                        yield return s;
                    break;
            }
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
                    var query = string.Join(" ", tokens.Skip(1)).Trim().ToLowerInvariant();
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
    }
}
