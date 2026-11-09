using gd2cs.Language;
using gd2cs.Parsing;

namespace gd2cs.Tests;

public sealed class MatchTests
{
    [Theory]
    [InlineData(".cs", "result = 1;\n\t\t\t\tbreak;", "result = 1;", "must end")]
    [InlineData(".cs", "case -1:", "case int number:", "constant switch labels")]
    [InlineData(".cs", "case -1:", "default:", "cannot share")]
    [InlineData(".gd", "-1, 0:", "[1, 2]:", "constant match patterns")]
    [InlineData(".gd", "-1, 0:", "var capture:", "constant match patterns")]
    [InlineData(".gd", "-1, 0:", "0 when value > 0:", "pattern guards")]
    [InlineData(".gd", "-1, 0:", "_:", "must be last")]
    [InlineData(".gd", "result = 1", "break", "final top-level")]
    [InlineData(".cs", "\t\t\t\tresult = 1;", "\t\t\t\tif (value == 0)\n\t\t\t\t{\n\t\t\t\t\tbreak;\n\t\t\t\t}", "final top-level")]
    [InlineData(".gd", "func describe(state: TokenState) -> String:", "func describe(state: TokenState):", "void")]
    public void RejectUnsupported(string extension, string original, string replacement, string diagnostic)
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "scripts", "Statements", "MatchCases" + extension));
        var source = File.ReadAllText(path).ReplaceLineEndings("\n");
        Assert.Contains(original, source);
        source = source.Replace(original, replacement);
        var language = extension == ".gd" ? ScriptLanguage.GdScript : ScriptLanguage.CSharp;
        var error = Assert.Throws<ParseException>(() => new Transpiler().Parse(source, language));
        Assert.Contains(diagnostic, error.Message);
        Assert.True(error.Line > 1);
        Assert.True(error.Column > 1);
    }
}
