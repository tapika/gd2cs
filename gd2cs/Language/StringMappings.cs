using gd2cs.Model;

namespace gd2cs.Language;

// Keeps string operations culture-independent and separate from unrelated user methods.
internal static class StringMappings
{
    private sealed record Mapping(
        StringOperation Operation,
        string GdScriptMember,
        string CSharpMember,
        int ArgumentCount,
        string ResultType,
        bool OrdinalComparison = false);

    private static readonly Mapping[] mappings =
    [
        new(StringOperation.Uppercase, "to_upper", "ToUpperInvariant", 0, "string"),
        new(StringOperation.Lowercase, "to_lower", "ToLowerInvariant", 0, "string"),
        new(StringOperation.Contains, "contains", "Contains", 1, "bool"),
        new(StringOperation.StartsWith, "begins_with", "StartsWith", 1, "bool", true),
        new(StringOperation.EndsWith, "ends_with", "EndsWith", 1, "bool", true)
    ];

    public static bool TryInvocation(
        ScriptLanguage language,
        string member,
        ArgumentList arguments,
        out StringOperation operation,
        out ArgumentList normalizedArguments)
    {
        operation = default;
        normalizedArguments = arguments;
        var isCSharp = language == ScriptLanguage.CSharp;
        var mapping = mappings.FirstOrDefault(mapping =>
            member == (isCSharp ? mapping.CSharpMember : mapping.GdScriptMember));
        if (mapping is null)
            return false;

        var ordinal = isCSharp && mapping.OrdinalComparison;
        var expectedCount = mapping.ArgumentCount + (ordinal ? 1 : 0);
        if (arguments.Arguments.Count != expectedCount)
            throw new NotSupportedException(
                $"String method '{member}' expects {expectedCount} argument(s), but received {arguments.Arguments.Count}.");
        if (ordinal && arguments.Arguments[^1].Value is not MemberAccessExpression
            { Target: ValueExpression { Text: "StringComparison" }, Member: "Ordinal" })
            return false;

        operation = mapping.Operation;
        normalizedArguments = ordinal
            ? arguments with { Arguments = arguments.Arguments.Take(mapping.ArgumentCount).ToList() }
            : arguments;
        return true;
    }

    public static string Member(StringOperation operation, ScriptLanguage language)
    {
        var mapping = mappings.First(mapping => mapping.Operation == operation);
        return language == ScriptLanguage.GdScript ? mapping.GdScriptMember : mapping.CSharpMember;
    }

    public static TypeReference ResultType(StringOperation operation) =>
        new(mappings.First(mapping => mapping.Operation == operation).ResultType);

    // Ordinal comparison is explicit in C# but implicit in GDScript's prefix/suffix checks.
    public static ArgumentList CSharpArguments(StringOperation operation, ArgumentList arguments)
    {
        if (!mappings.First(mapping => mapping.Operation == operation).OrdinalComparison)
            return arguments;
        return arguments with
        {
            Arguments = [.. arguments.Arguments, new Argument(
                new MemberAccessExpression(new ValueExpression("StringComparison"), "Ordinal"), false)]
        };
    }
}
