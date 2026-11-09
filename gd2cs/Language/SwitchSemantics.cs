using gd2cs.Model;
using gd2cs.Parsing;

namespace gd2cs.Language;

// C# requires an explicit section exit; GD match exits implicitly. Keep that exit out of
// the model, but retain returns and loop continues. Never translate an early switch break
// into GD break: the latter would exit an enclosing loop, not the match.
internal static class SwitchSemantics
{
    public static bool EndsControlFlow(List<SyntaxElement> elements) =>
        elements.LastOrDefault(element => element is not (CommentElement or BlankLineElement)) switch
        {
            ReturnStatement or ContinueStatement => true,
            ConditionalStatement conditional => conditional.ElseElements is not null
                && conditional.Branches.All(branch => EndsControlFlow(branch.Elements))
                && EndsControlFlow(conditional.ElseElements),
            SwitchStatement statement => statement.Elements.OfType<SwitchSection>().Any(section => section.Labels.Contains(null))
                && statement.Elements.OfType<SwitchSection>().All(section => EndsControlFlow(section.Elements)),
            _ => false
        };

    public static void ValidateBreaks(List<SyntaxElement> elements)
    {
        foreach (var element in elements)
        {
            if (element is BreakStatement)
                throw SourceLocations.Error(element,
                    "Only a final top-level C# case break is supported; early switch breaks and GD loop breaks inside match require restructuring the branch.");
            if (element is ConditionalStatement conditional)
            {
                foreach (var branch in conditional.Branches)
                    ValidateBreaks(branch.Elements);
                if (conditional.ElseElements is not null)
                    ValidateBreaks(conditional.ElseElements);
            }
            // Nested loops own their breaks; nested switches validate their own sections.
        }
    }

    public static bool IsConstantPattern(Expression expression) => expression is ValueExpression
        or MemberAccessExpression or GodotEnumExpression or ScriptEnumExpression or GodotStaticMemberExpression
        || expression is UnaryExpression { Operator: "-" or "+" } unary && unary.Operand is ValueExpression;
}
