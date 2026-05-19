using System;

namespace SourceGit.Controls
{
    public static class ExprEvaluator
    {
        public static bool Evaluate(ExprNode node, Func<string, bool> termEvaluator)
        {
            if (node == null) return true;

            switch (node.Op)
            {
                case ExprOp.Term:
                    return termEvaluator(node.Value);
                case ExprOp.Not:
                    return !Evaluate(node.Children?[0], termEvaluator);
                case ExprOp.And:
                    if (node.Children == null || node.Children.Count == 0) return true;
                    foreach (var child in node.Children)
                        if (!Evaluate(child, termEvaluator)) return false;
                    return true;
                case ExprOp.Or:
                    if (node.Children == null || node.Children.Count == 0) return true;
                    foreach (var child in node.Children)
                        if (Evaluate(child, termEvaluator)) return true;
                    return false;
                default:
                    return true;
            }
        }
    }
}
