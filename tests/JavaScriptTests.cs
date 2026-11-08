using gd2cs.Language;
using gd2cs.Parsing;

namespace gd2cs.Tests;

public sealed class JavaScriptTests
{
    [Theory]
    [InlineData(".gd", "element.width as float", "element.width", "Variant")]
    [InlineData(".gd", "var width := element.width as float", "var width : float = element.width", "Variant")]
    [InlineData(".gd", "document.querySelector(\"canvas\") as JavaScriptObject", "document.querySelector(\"canvas\")", "Variant")]
    [InlineData(".gd", "browser.measure(2) as float", "browser.measure(2) + 1", "Variant")]
    [InlineData(".gd", "element.focus()", "element.focus(browser.width)", "Variant")]
    [InlineData(".gd", "element.focus()", "if browser.enabled:\n\t\telement.focus()", "Variant")]
    [InlineData(".gd", "element.width as float", "element.style.width as float", "Variant")]
    [InlineData(".gd", "browser[key] as float", "browser[2] as float", "String")]
    [InlineData(".gd", "browser.width = width", "browser.width += width", "Variant")]
    [InlineData(".gd", "element.width as float", "element.width as Variant", "concrete type")]
    [InlineData(".gd", "browser.measure(2) as float", "(browser.measure(2) + 1) as float", "Variant")]
    [InlineData(".cs", "element.Get(\"width\").As<float>()", "element.Get(\"width\")", "Variant")]
    [InlineData(".cs", "var width = element.Get(\"width\").As<float>()", "float width = element.Get(\"width\")", "Variant")]
    [InlineData(".cs", "browser.Call(\"measure\", 2).As<float>()", "browser.Call(\"measure\", 2)", "Variant")]
    [InlineData(".cs", "element.Get(\"width\")", "element.Get()", "requires")]
    [InlineData(".cs", "element.Call(\"focus\")", "element.Call()", "requires")]
    [InlineData(".cs", "browser.Set(\"width\", width)", "browser.Set(\"width\")", "requires")]
    [InlineData(".cs", "element.Get(\"width\")", "element.Get(2)", "String")]
    [InlineData(".cs", "element.Get(\"width\").As<float>()", "browser.Set(\"width\", 1)", "standalone")]
    public void RejectUnsafeUse(string extension, string original, string replacement, string diagnostic)
    {
        var source = Fixture(extension);
        Assert.Contains(original, source);
        source = source.Replace(original, replacement);
        var language = extension == ".gd" ? ScriptLanguage.GdScript : ScriptLanguage.CSharp;
        var error = Assert.Throws<ParseException>(() => new Transpiler().Parse(source, language));
        Assert.Contains(diagnostic, error.Message);
        Assert.True(error.Line > 1);
        Assert.True(error.Column > 1);
    }

    [Fact]
    public void ErrorLocation()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gd2cs-js-{Guid.NewGuid():N}.gd");
        var source = Fixture(".gd").Replace("element.width as float", "element.width");
        try
        {
            File.WriteAllText(path, source);
            var error = Assert.Throws<ParseException>(() => new Transpiler().TranspileFile(path, Path.ChangeExtension(path, ".cs")));
            Assert.Equal(path, error.SourcePath);
            var lines = source.ReplaceLineEndings("\n").Split('\n');
            var line = Array.FindIndex(lines, text => text.Contains("var width := element.width", StringComparison.Ordinal));
            Assert.Equal(line + 1, error.Line);
            Assert.Equal(lines[line].IndexOf("element", StringComparison.Ordinal) + 1, error.Column);
            Assert.Equal(lines[line], error.SourceLine);
        }
        finally
        {
            File.Delete(path);
            File.Delete(Path.ChangeExtension(path, ".cs"));
        }
    }

    private static string Fixture(string extension) => File.ReadAllText(Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "scripts", "Calls", "JavaScriptObjects" + extension)));
}
