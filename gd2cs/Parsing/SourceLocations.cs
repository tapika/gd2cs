using System.Runtime.CompilerServices;

namespace gd2cs.Parsing;

// Keeps diagnostic coordinates outside the neutral model's equality and public properties.
// Weak keys release metadata with its nodes; normalization copies it to replacement nodes.
internal static class SourceLocations
{
    private sealed record Position(int Line, int Column);
    private static readonly ConditionalWeakTable<object, Position> positions = new();

    public static T Record<T>(T element, Token token) where T : class
    {
        positions.Add(element, new Position(token.Line, token.Column));
        return element;
    }

    public static T Copy<T>(object source, T target) where T : class
    {
        if (positions.TryGetValue(source, out var position))
            positions.Add(target, position);
        return target;
    }

    public static ParseException Error(object element, string message)
    {
        var position = positions.TryGetValue(element, out var recorded) ? recorded : new Position(1, 1);
        return new ParseException(message, position.Line, position.Column);
    }
}
