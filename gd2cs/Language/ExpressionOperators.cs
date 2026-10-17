using gd2cs.Model;

namespace gd2cs.Language;

// GDScript puts bitwise operators above comparisons; C# puts them below comparisons.
internal static class ExpressionOperators
{
    public static int Precedence(string operation, ScriptLanguage language) => operation switch
    {
        "or" => 0,
        "and" => 1,
        "|" => language == ScriptLanguage.CSharp ? 2 : 4,
        "^" => language == ScriptLanguage.CSharp ? 3 : 5,
        "&" => language == ScriptLanguage.CSharp ? 4 : 6,
        "==" or "!=" => language == ScriptLanguage.CSharp ? 5 : 2,
        "<" or "<=" or ">" or ">=" => language == ScriptLanguage.CSharp ? 6 : 3,
        "+" or "-" => 7,
        "*" or "/" or "%" => 8,
        _ => -1
    };

    // Preserve the expression tree, including right-hand grouping and GDScript's low-precedence as casts.
    public static bool NeedsParentheses(Expression child, string parent, bool right, ScriptLanguage language) =>
        language == ScriptLanguage.GdScript && child is VariantCastExpression
        || child is BinaryExpression binary && (Precedence(binary.Operator, language) < Precedence(parent, language)
            || right && Precedence(binary.Operator, language) == Precedence(parent, language));

    // Only layout-free, required grouping is a dialect detail. Explicit multiline grouping remains trivia.
    public static Expression NormalizeOperand(Expression child, string parent, bool right) => child is
        ParenthesizedExpression { OpeningLineBreaks: 0, ClosingLineBreaks: 0 } grouped
        && !HasLineBreaks(grouped.Expression)
        && (NeedsParentheses(grouped.Expression, parent, right, ScriptLanguage.CSharp)
            || NeedsParentheses(grouped.Expression, parent, right, ScriptLanguage.GdScript))
                ? grouped.Expression : child;

    private static bool HasLineBreaks(Expression expression) => expression switch
    {
        BinaryExpression binary => binary.LineBreaksBeforeOperator > 0 || binary.LineBreaksAfterOperator > 0
            || HasLineBreaks(binary.Left) || HasLineBreaks(binary.Right),
        ParenthesizedExpression grouped => grouped.OpeningLineBreaks > 0 || grouped.ClosingLineBreaks > 0
            || HasLineBreaks(grouped.Expression),
        UnaryExpression unary => HasLineBreaks(unary.Operand),
        MemberAccessExpression member => HasLineBreaks(member.Target),
        InvocationExpression call => call.Arguments.ClosingParenthesisStartsOnNewLine
            || call.Arguments.Arguments.Any(argument => argument.StartsOnNewLine || HasLineBreaks(argument.Value)),
        ClampExpression clamp => clamp.Arguments.ClosingParenthesisStartsOnNewLine
            || clamp.Arguments.Arguments.Any(argument => argument.StartsOnNewLine || HasLineBreaks(argument.Value)),
        _ => false
    };

    public static bool IsBitwise(Expression expression) => expression switch
    {
        BinaryExpression { Operator: "&" or "|" or "^" } => true,
        UnaryExpression { Operator: "~" } => true,
        ParenthesizedExpression grouped => IsBitwise(grouped.Expression),
        _ => false
    };

    public static Expression BitmaskValue(Expression expression) => expression is
        ParenthesizedExpression { OpeningLineBreaks: 0, ClosingLineBreaks: 0 } grouped
        && !HasLineBreaks(grouped.Expression) ? grouped.Expression : expression;
}
