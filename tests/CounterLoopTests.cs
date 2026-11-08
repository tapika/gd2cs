using gd2cs.Language;
using gd2cs.Parsing;

namespace gd2cs.Tests;

public sealed class CounterLoopTests
{
    // ParseAndEmit covers the canonical script pair. These alternative C# spellings
    // normalize to that same model and cannot all occupy its single expected C# file.
    [Theory]
    [InlineData("index++", "++index")]
    [InlineData("index++", "index += 1")]
    [InlineData("index++", "index = index + 1")]
    [InlineData("cursor--", "--cursor")]
    [InlineData("cursor--", "cursor = cursor - 1")]
    public void UpdateForms(string original, string replacement)
    {
        var source = File.ReadAllText(Fixture(".cs"));
        Assert.Contains(original, source);
        Assert.Equal(File.ReadAllText(Fixture(".gd")),
            new Transpiler().Transpile(source.Replace(original, replacement), ScriptLanguage.CSharp, ScriptLanguage.GdScript));
    }

    [Theory]
    [InlineData("int index = 0", "float index = 0.0f", "int local or parameter counter")]
    [InlineData("index < values.Count", "values.Count", "bool expression")]
    [InlineData("index++", "index *= 2", "additive counter update")]
    [InlineData("index += step", "index += 0.5f", "int expression")]
    [InlineData("index += this.Step", "index += Step", "qualify class members")]
    public void RejectHeader(string original, string replacement, string message)
    {
        var source = File.ReadAllText(Fixture(".cs"));
        Assert.Contains(original, source);
        var error = Assert.Throws<ParseException>(() => new Transpiler().Parse(source.Replace(original, replacement), ScriptLanguage.CSharp));
        Assert.Contains(message, error.Message);
        Assert.True(error.Line > 1);
        Assert.True(error.Column > 0);
    }

    private static string Fixture(string extension) => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "scripts", "Statements", "CounterLoops" + extension));
}
