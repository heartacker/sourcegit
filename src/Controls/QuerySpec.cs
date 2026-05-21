using System.Collections.Generic;

namespace SourceGit.Controls
{
    public enum ExprOp
    {
        Term,
        And,
        Or,
        Not
    }

    public class ExprNode
    {
        public ExprOp Op { get; set; }
        public List<ExprNode> Children { get; set; }
        public string Prefix { get; set; }
        public string Value { get; set; }

        public override string ToString()
        {
            if (Op == ExprOp.Term)
                return string.IsNullOrEmpty(Prefix) ? Value : $"{Prefix}:{Value}";
            if (Op == ExprOp.Not)
                return $"-( {Children?[0]} )";
            var joiner = Op == ExprOp.And ? " AND " : " OR ";
            return $"( {string.Join(joiner, Children ?? [])} )";
        }
    }

    public class GroupSpec
    {
        public string ProviderPrefix { get; set; }
        public ExprNode Expr { get; set; }
    }

    public class QuerySpec
    {
        public List<GroupSpec> Groups { get; set; } = new List<GroupSpec>();
        public List<string> FallbackTerms { get; set; } = new List<string>();
        public List<string> FallbackNotTerms { get; set; } = new List<string>();
        public List<QuerySpec> SubGroups { get; set; } = new List<QuerySpec>();

        public bool HasSubGroups => SubGroups.Count > 0;
    }
}
