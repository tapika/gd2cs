using gd2cs.Model;

namespace gd2cs.Language;

// Shares bridge argument layout and safe dot-name selection between the two emitters.
internal static class JavaScriptOperations
{
    public static ArgumentList WithName(Expression name, ArgumentList arguments)
    {
        var values = new List<Argument> { new(name, false) };
        values.AddRange(arguments.Arguments);
        return arguments with { Arguments = values };
    }

    // Only literal identifiers can use dot syntax; computed/escaped names retain lookup syntax.
    public static string? DotName(Expression name)
    {
        if (name is not ValueExpression value || value.Text.Length < 3
            || value.Text[0] != '"' || value.Text[^1] != '"')
            return null;
        var text = value.Text[1..^1];
        return (char.IsLetter(text[0]) || text[0] == '_')
            && text.All(character => char.IsLetterOrDigit(character) || character == '_') ? text : null;
    }
}
