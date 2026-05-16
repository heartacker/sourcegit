using System.Collections.Generic;
using System.Linq;

namespace SourceGit.Models
{
    public interface IHistoryViewFilter
    {
        List<Commit> Process(List<Commit> commits, Dictionary<string, Commit> map);
    }

    public class SoloFilter : IHistoryViewFilter
    {
        public List<string> Targets { get; set; } = [];

        public List<Commit> Process(List<Commit> commits, Dictionary<string, Commit> map)
        {
            if (commits == null || commits.Count == 0 || Targets.Count == 0)
                return commits;

            var active = new bool[commits.Count];
            foreach (var target in Targets)
            {
                if (map.TryGetValue(target, out var commit) && commit.Index < commits.Count)
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
                    c.IsCommitFilterHead = Targets.Contains(c.SHA);
                    result.Add(c);
                }
            }

            return result;
        }
    }

    public class FoldingFilter : IHistoryViewFilter
    {
        public List<Commit> Process(List<Commit> commits, Dictionary<string, Commit> map)
        {
            if (commits == null || commits.Count == 0)
                return commits;

            if (!ViewModels.Preferences.Instance.EnableLinearCommitFolding)
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

            // Build local map for children count since the input commits might be already filtered by SoloFilter
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
