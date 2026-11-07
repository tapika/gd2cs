using System.Globalization;
using gd2cs.Model;
using gd2cs.Parsing;

namespace gd2cs.Language;

internal static class EnumSemantics
{
    // Both languages accept aliases and automatic increments. Bound the shared dialect to
    // C#'s default int backing type; GDScript's wider integers must not silently truncate.
    public static EnumDeclaration Validate(EnumDeclaration declaration)
    {
        var values = new Dictionary<string, long>(StringComparer.Ordinal);
        long next = 0;
        foreach (var member in declaration.Elements.OfType<EnumMemberDeclaration>())
        {
            if (values.ContainsKey(member.Name))
                throw SourceLocations.Error(member, $"Duplicate enum member '{member.Name}'.");
            long value;
            try
            {
                value = member.Value is null ? next : Evaluate(member.Value, values);
            }
            catch (Exception error) when (error is OverflowException or DivideByZeroException or InvalidOperationException)
            {
                throw SourceLocations.Error(member,
                    "Enum values require integer constant expressions using previously declared members.");
            }
            if (value < int.MinValue || value > int.MaxValue)
                throw SourceLocations.Error(member, "Enum values must fit the C# int range (-2147483648 to 2147483647).");
            values.Add(member.Name, value);
            next = value + 1;
        }
        return declaration;
    }

    // Evaluate for validation only; preserve the original expression and implicit numbering.
    private static long Evaluate(Expression expression, Dictionary<string, long> values) => expression switch
    {
        ValueExpression value when long.TryParse(value.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var integer) => integer,
        ValueExpression value when values.TryGetValue(value.Text, out var integer) => integer,
        ParenthesizedExpression grouped => Evaluate(grouped.Expression, values),
        UnaryExpression { Operator: "-" } unary => checked(-Evaluate(unary.Operand, values)),
        UnaryExpression { Operator: "+" } unary => Evaluate(unary.Operand, values),
        UnaryExpression { Operator: "~" } unary => ~Evaluate(unary.Operand, values),
        BinaryExpression binary => Calculate(binary.Operator, Evaluate(binary.Left, values), Evaluate(binary.Right, values)),
        _ => throw new InvalidOperationException()
    };

    private static long Calculate(string operation, long left, long right) => operation switch
    {
        "+" => checked(left + right),
        "-" => checked(left - right),
        "*" => checked(left * right),
        "/" => left / right,
        "%" => left % right,
        "&" => left & right,
        "|" => left | right,
        "^" => left ^ right,
        _ => throw new InvalidOperationException()
    };
}
