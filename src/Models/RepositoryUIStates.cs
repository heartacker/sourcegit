using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Avalonia.Collections;

namespace SourceGit.Models
{
    public class RepositoryUIStates
    {
        public HistoryShowFlags HistoryShowFlags
        {
            get;
            set;
        } = HistoryShowFlags.None;

        public bool IsAuthorColumnVisibleInHistory
        {
            get;
            set;
        } = true;

        public bool IsSHAColumnVisibleInHistory
        {
            get;
            set;
        } = true;

        public bool IsDateTimeColumnVisibleInHistory
        {
            get;
            set;
        } = true;

        public double AuthorColumnWidth
        {
            get;
            set;
        } = 120;

        public bool EnableTopoOrderInHistory
        {
            get;
            set;
        } = false;

        public CommitGraphHighlighting GraphHighlighting
        {
            get;
            set;
        } = CommitGraphHighlighting.All;

        public CommitLineageSearchMethod LineageSearchMethod
        {
            get;
            set;
        } = CommitLineageSearchMethod.FullLineage;

        public BranchSortMode LocalBranchSortMode
        {
            get;
            set;
        } = BranchSortMode.Name;

        public BranchSortMode RemoteBranchSortMode
        {
            get;
            set;
        } = BranchSortMode.Name;

        public bool ShowTagsAsTree
        {
            get;
            set;
        } = false;

        public TagSortMode TagSortMode
        {
            get;
            set;
        } = TagSortMode.CreatorDate;

        public bool ShowSubmodulesAsTree
        {
            get;
            set;
        } = false;

        public bool IncludeUntrackedInLocalChanges
        {
            get;
            set;
        } = true;

        public bool EnableForceOnFetch
        {
            get;
            set;
        } = false;

        public bool FetchAllRemotes
        {
            get;
            set;
        } = false;

        public bool FetchWithoutTags
        {
            get;
            set;
        } = false;

        public bool PreferRebaseInsteadOfMerge
        {
            get;
            set;
        } = true;

        public bool CheckSubmodulesOnPush
        {
            get;
            set;
        } = true;

        public bool PushAllTags
        {
            get;
            set;
        } = false;

        public bool CreateAnnotatedTag
        {
            get;
            set;
        } = true;

        public bool PushToRemoteWhenCreateTag
        {
            get;
            set;
        } = true;

        public bool PushToRemoteWhenDeleteTag
        {
            get;
            set;
        } = false;

        public bool CheckoutBranchOnCreateBranch
        {
            get;
            set;
        } = true;

        public bool EnableSignOffForCommit
        {
            get;
            set;
        } = false;

        public bool NoVerifyOnCommit
        {
            get;
            set;
        } = false;

        public bool IncludeUntrackedWhenStash
        {
            get;
            set;
        } = true;

        public bool OnlyStagedWhenStash
        {
            get;
            set;
        } = false;

        public int ChangesAfterStashing
        {
            get;
            set;
        } = 0;

        public bool IsLocalBranchesExpandedInSideBar
        {
            get;
            set;
        } = true;

        public bool IsRemotesExpandedInSideBar
        {
            get;
            set;
        } = false;

        public bool IsTagsExpandedInSideBar
        {
            get;
            set;
        } = false;

        public bool IsSubmodulesExpandedInSideBar
        {
            get;
            set;
        } = false;

        public bool IsWorktreeExpandedInSideBar
        {
            get;
            set;
        } = false;

        public List<string> ExpandedBranchNodesInSideBar
        {
            get;
            set;
        } = [];

        public string LastCommitMessage
        {
            get;
            set;
        } = string.Empty;

        public AvaloniaList<HistoryFilter> HistoryFilters
        {
            get;
            set;
        } = [];

        public static RepositoryUIStates Load(string gitDir)
        {
            var fileInfo = new FileInfo(Path.Combine(gitDir, "sourcegit.uistates"));
            var fullpath = fileInfo.FullName;

            RepositoryUIStates states;
            if (!File.Exists(fullpath))
            {
                states = new RepositoryUIStates();
            }
            else
            {
                try
                {
                    using var stream = File.OpenRead(fullpath);
                    states = JsonSerializer.Deserialize(stream, JsonCodeGen.Default.RepositoryUIStates);
                }
                catch
                {
                    states = new RepositoryUIStates();
                }
            }

            states._file = fullpath;
            return states;
        }

        public void Save()
        {
            try
            {
                var content = JsonSerializer.Serialize(this, JsonCodeGen.Default.RepositoryUIStates);
                var tmpfile = $"{_file}.tmp";
                File.WriteAllText(tmpfile, content);
                File.Move(tmpfile, _file, true);
            }
            catch
            {
                // Ignore save errors
            }
        }

        public FilterMode GetHistoryFilterMode(string pattern = null)
        {
            if (string.IsNullOrEmpty(pattern))
                return HistoryFilters.Count == 0 ? FilterMode.None : HistoryFilters[0].Mode;

            foreach (var filter in HistoryFilters)
            {
                if (filter.Pattern.Equals(pattern, StringComparison.Ordinal))
                    return filter.Mode;
            }

            return FilterMode.None;
        }

        public Dictionary<string, FilterMode> GetHistoryFiltersMap()
        {
            var map = new Dictionary<string, FilterMode>();
            foreach (var filter in HistoryFilters)
                map.Add(filter.Pattern, filter.Mode);
            return map;
        }

        public bool UpdateHistoryFilters(string pattern, FilterType type, FilterMode mode)
        {
            // Clear all filters when there's a filter that has different mode.
            if (mode != FilterMode.None)
            {
                var clear = false;
                foreach (var filter in HistoryFilters)
                {
                    if (filter.Mode != mode)
                    {
                        clear = true;
                        break;
                    }
                }

                if (clear)
                {
                    HistoryFilters.Clear();
                    HistoryFilters.Add(new HistoryFilter(pattern, type, mode));
                    return true;
                }
            }
            else
            {
                for (int i = 0; i < HistoryFilters.Count; i++)
                {
                    var filter = HistoryFilters[i];
                    if (filter.Type == type && filter.Pattern.Equals(pattern, StringComparison.Ordinal))
                    {
                        HistoryFilters.RemoveAt(i);
                        return true;
                    }
                }

                return false;
            }

            foreach (var filter in HistoryFilters)
            {
                if (filter.Type != type)
                    continue;

                if (filter.Pattern.Equals(pattern, StringComparison.Ordinal))
                    return false;
            }

            HistoryFilters.Add(new HistoryFilter(pattern, type, mode));
            return true;
        }

        public void RemoveHistoryFilter(string pattern, FilterType type)
        {
            foreach (var filter in HistoryFilters)
            {
                if (filter.Type == type && filter.Pattern.Equals(pattern, StringComparison.Ordinal))
                {
                    HistoryFilters.Remove(filter);
                    break;
                }
            }
        }

        public void RenameBranchFilter(string oldName, string newName)
        {
            foreach (var filter in HistoryFilters)
            {
                if (filter.Type == FilterType.LocalBranch &&
                    filter.Pattern.Equals(oldName, StringComparison.Ordinal))
                {
                    filter.Pattern = $"refs/heads/{newName}";
                    break;
                }
            }
        }

        public void RemoveBranchFiltersByPrefix(string pattern)
        {
            var dirty = new List<HistoryFilter>();
            var prefix = $"{pattern}/";

            foreach (var filter in HistoryFilters)
            {
                if (filter.Type == FilterType.Tag)
                    continue;

                if (filter.Pattern.StartsWith(prefix, StringComparison.Ordinal))
                    dirty.Add(filter);
            }

            foreach (var filter in dirty)
                HistoryFilters.Remove(filter);
        }

        public string BuildHistoryParams()
        {
            var includedRefs = new List<string>();
            var excludedRevisions = new List<string>();
            var hasExcludeBranches = false;
            var hasExcludeRemotes = false;
            var hasExcludeTags = false;
            var excludeArgs = new System.Text.StringBuilder();

            foreach (var filter in HistoryFilters)
            {
                if (filter.Type == FilterType.LocalBranch)
                {
                    if (filter.Mode == FilterMode.Included)
                    {
                        includedRefs.Add(filter.Pattern);
                    }
                    else if (filter.Mode == FilterMode.Excluded)
                    {
                        excludedRevisions.Add($"^{filter.Pattern}");
                        excludeArgs.Append($"--exclude=\"{filter.Pattern.AsSpan(11)}\" --decorate-refs-exclude=\"{filter.Pattern}\" ");
                        hasExcludeBranches = true;
                    }
                }
                else if (filter.Type == FilterType.LocalBranchFolder)
                {
                    if (filter.Mode == FilterMode.Included)
                    {
                        includedRefs.Add($"--branches={filter.Pattern.AsSpan(11)}/*");
                    }
                    else if (filter.Mode == FilterMode.Excluded)
                    {
                        var glob = $"{filter.Pattern.AsSpan(11)}/*";
                        excludedRevisions.Add($"--exclude=refs/heads/{glob}");
                        excludeArgs.Append($"--exclude=\"{glob}\" --decorate-refs-exclude=\"{filter.Pattern}/*\" ");
                        hasExcludeBranches = true;
                    }
                }
                else if (filter.Type == FilterType.RemoteBranch)
                {
                    if (filter.Mode == FilterMode.Included)
                    {
                        includedRefs.Add(filter.Pattern);
                    }
                    else if (filter.Mode == FilterMode.Excluded)
                    {
                        excludedRevisions.Add($"^{filter.Pattern}");
                        excludeArgs.Append($"--exclude=\"{filter.Pattern.AsSpan(13)}\" --decorate-refs-exclude=\"{filter.Pattern}\" ");
                        hasExcludeRemotes = true;
                    }
                }
                else if (filter.Type == FilterType.RemoteBranchFolder)
                {
                    if (filter.Mode == FilterMode.Included)
                    {
                        includedRefs.Add($"--remotes={filter.Pattern.AsSpan(13)}/*");
                    }
                    else if (filter.Mode == FilterMode.Excluded)
                    {
                        var glob = $"{filter.Pattern.AsSpan(13)}/*";
                        excludedRevisions.Add($"--exclude=refs/remotes/{glob}");
                        excludeArgs.Append($"--exclude=\"{glob}\" --decorate-refs-exclude=\"{filter.Pattern}/*\" ");
                        hasExcludeRemotes = true;
                    }
                }
                else if (filter.Type == FilterType.Tag)
                {
                    if (filter.Mode == FilterMode.Included)
                    {
                        includedRefs.Add($"refs/tags/{filter.Pattern}");
                    }
                    else if (filter.Mode == FilterMode.Excluded)
                    {
                        excludedRevisions.Add($"^refs/tags/{filter.Pattern}");
                        excludeArgs.Append($"--exclude=\"{filter.Pattern}\" --decorate-refs-exclude=\"refs/tags/{filter.Pattern}\" ");
                        hasExcludeTags = true;
                    }
                }
            }

            var builder = new StringBuilder();

            if (EnableTopoOrderInHistory)
                builder.Append("--topo-order ");
            else
                builder.Append("--date-order ");

            if (HistoryShowFlags.HasFlag(HistoryShowFlags.Reflog))
                builder.Append("--reflog ");

            if (HistoryShowFlags.HasFlag(HistoryShowFlags.FirstParentOnly))
                builder.Append("--first-parent ");

            if (HistoryShowFlags.HasFlag(HistoryShowFlags.SimplifyByDecoration))
                builder.Append("--simplify-by-decoration ");

            var hasIncluded = includedRefs.Count > 0;
            var hasExcluded = excludedRevisions.Count > 0;

            if (hasIncluded || hasExcluded)
            {
                if (hasIncluded)
                {
                    foreach (var r in includedRefs)
                    {
                        builder.Append(r);
                        builder.Append(' ');
                    }
                }

                if (hasExcluded)
                {
                    if (hasIncluded)
                    {
                        // Combined includes + excludes: use ^prefix for revision-level pruning
                        foreach (var rev in excludedRevisions)
                        {
                            builder.Append(rev);
                            builder.Append(' ');
                        }
                    }
                    else
                    {
                        // Exclude-only: use --exclude with --branches/--remotes/--tags
                        builder.Append(excludeArgs);

                        if (hasExcludeBranches)
                            builder.Append("--exclude=HEAD --branches ");

                        if (hasExcludeRemotes)
                            builder.Append("--exclude=origin/HEAD --remotes ");

                        if (hasExcludeTags)
                            builder.Append("--tags ");
                    }
                }
            }
            else
            {
                builder.Append("--branches --remotes --tags HEAD");
            }

            return builder.ToString();
        }

        private string _file = string.Empty;
    }
}
