using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.Models
{
    public interface IHistoryViewFilter
    {
        bool IsActive { get; }
        string Description { get; }
        List<Commit> Process(List<Commit> commits, Dictionary<string, Commit> map);
    }

    public class SoloFilter : ObservableObject, IHistoryViewFilter
    {
        public bool IsActive => _targets.Count > 0;
        public string Description
        {
            get
            {
                if (_targets.Count == 0) return string.Empty;
                var list = _targets.Select(x => x.Length > 7 ? x.Substring(0, 7) : x);
                return "Solo: " + string.Join(", ", list);
            }
        }

        public List<string> Targets
        {
            get => _targets;
            set
            {
                _targets = value;
                OnPropertyChanged(nameof(IsActive));
                OnPropertyChanged(nameof(Description));
            }
        }

        public List<Commit> Process(List<Commit> commits, Dictionary<string, Commit> map)
        {
            if (commits == null || commits.Count == 0 || _targets.Count == 0)
                return commits;

            var active = new bool[commits.Count];
            foreach (var target in _targets)
            {
                string sha = target;
                if (target.Equals("HEAD", StringComparison.OrdinalIgnoreCase))
                {
                    var head = commits.Find(x => x.IsCurrentHead);
                    if (head != null) sha = head.SHA;
                }

                if (map.TryGetValue(sha, out var commit) && commit.Index < commits.Count)
                {
                    var lineage = CommitGraph.GetCommitLineageFast(commits, map, commit, CommitLineageSearchMethod.FullLineage, (uint)commits.Count);
                    for (int i = 0; i < lineage.Length; i++)
                    {
                        if (lineage[i])
                            active[i] = true;
                    }
                }
            }

            var result = new List<Commit>();
            for (int i = 0; i < commits.Count; i++)
            {
                if (active[i])
                {
                    var c = commits[i].Clone();
                    c.IsCommitFilterHead = _targets.Any(t => t.Equals(c.SHA, StringComparison.OrdinalIgnoreCase) || 
                                                           (t.Equals("HEAD", StringComparison.OrdinalIgnoreCase) && c.IsCurrentHead));
                    result.Add(c);
                }
            }

            return result;
        }

        private List<string> _targets = [];
    }

    public class FoldingFilter : ObservableObject, IHistoryViewFilter
    {
        public bool IsActive => ViewModels.Preferences.Instance.EnableLinearCommitFolding;
        public string Description => "Linear Folding";

        public void NotifyStateChanged()
        {
            OnPropertyChanged(nameof(IsActive));
        }

        public List<Commit> Process(List<Commit> commits, Dictionary<string, Commit> map)
        {
            if (commits == null || commits.Count == 0)
                return commits;

            if (!IsActive)
            {
                foreach (var c in commits)
                    c.IsFolded = false;
                return commits;
            }

            var threshold = ViewModels.Preferences.Instance.MaxLinearCommitsToFold;
            if (threshold < 3)
            {
                foreach (var c in commits)
                    c.IsFolded = false;
                return commits;
            }

            var childrenCount = new Dictionary<string, int>();
            foreach (var c in commits)
            {
                foreach (var p in c.Parents)
                {
                    if (!childrenCount.TryAdd(p, 1))
                        childrenCount[p]++;
                }
            }

            var result = new List<Commit>();
            for (int i = 0; i < commits.Count; i++)
            {
                var start = commits[i];
                if (start.HasDecorators || start.Parents.Count != 1 || childrenCount.GetValueOrDefault(start.SHA, 0) > 1)
                {
                    start.IsFolded = false;
                    result.Add(start);
                    continue;
                }

                var segment = new List<Commit> { start };
                int j = i + 1;
                while (j < commits.Count)
                {
                    var next = commits[j];
                    if (next.HasDecorators || next.Parents.Count != 1 || childrenCount.GetValueOrDefault(next.SHA, 0) > 1 || !segment[^1].Parents[0].Equals(next.SHA))
                        break;

                    segment.Add(next);
                    j++;
                }

                if (segment.Count > threshold)
                {
                    var first = segment[0].Clone();
                    var last = segment[^1];
                    var middleIdx = segment.Count / 2;
                    var middle = segment[middleIdx].Clone();

                    first.IsFolded = false;
                    middle.IsFolded = true;
                    middle.FoldedCount = segment.Count - 3;

                    first.Parents = [middle.SHA];
                    middle.Parents = [last.SHA];

                    result.Add(first);
                    result.Add(middle);
                    result.Add(last);
                }
                else
                {
                    foreach (var c in segment)
                    {
                        c.IsFolded = false;
                        result.Add(c);
                    }
                }

                i = j - 1;
            }

            return result;
        }
    }
}
