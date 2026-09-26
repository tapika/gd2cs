namespace gd2cs.Parsing;

public sealed class ParseException : Exception
{
    public ParseException(string message, int line, int column)
        : base(message)
    {
        Line = line;
        Column = column;
    }

    public int Line { get; }

    public int Column { get; }

    public string? SourcePath { get; private set; }

    public string? SourceLine { get; private set; }

    public void SetSource(string sourcePath, string source)
    {
        SourcePath = Path.GetFullPath(sourcePath);
        SourceLine = source.ReplaceLineEndings("\n").Split('\n').ElementAtOrDefault(Line - 1);
    }
}
