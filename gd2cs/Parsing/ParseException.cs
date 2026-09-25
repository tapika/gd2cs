namespace gd2cs.Parsing;

public sealed class ParseException : Exception
{
    public ParseException(string message, int line, int column)
        : base($"{message} (line {line}, column {column})")
    {
        Line = line;
        Column = column;
    }

    public int Line { get; }

    public int Column { get; }
}
