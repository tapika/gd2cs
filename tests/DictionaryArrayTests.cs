using gd2cs.Emission;
using gd2cs.Language;
using gd2cs.Model;
using gd2cs.Parsing;

namespace gd2cs.Tests;

public sealed class DictionaryArrayTests
{
    [Theory]
    [InlineData("inspect", false, false)]
    [InlineData("entryKeys", true, false)]
    [InlineData("entryValues", false, true)]
    public void Helpers(string method, bool keys, bool values)
    {
        var model = new Transpiler().Parse(File.ReadAllText(Fixture(".gd")), ScriptLanguage.GdScript);
        // Keep fixture declarations but select one method to exercise independent helper emission.
        var declaration = model.Class with
        {
            Elements = model.Class.Elements.Where(element => element is not MethodDeclaration callable || callable.Name == method).ToList()
        };
        var selected = new ScriptModel(new List<SyntaxElement> { declaration });
        var emitter = new CSharpEmitter(GodotTypeCatalog.CreateKnownBuiltIns());
        var generated = emitter.Emit(selected);
        Assert.Equal(keys, generated.Contains("gdDictKeys<", StringComparison.Ordinal));
        Assert.Equal(values, generated.Contains("gdDictValues<", StringComparison.Ordinal));
        if (keys || values)
        {
            Assert.Contains("Godot internally returns Array<T>", generated);
            Assert.Contains("GodotSharp/Core/Dictionary.cs", generated);
            Assert.DoesNotContain("new Godot.Collections.Array<TKey>", generated);
            Assert.DoesNotContain("new Godot.Collections.Array<TValue>", generated);
            Assert.Equal(1, generated.Split("// Godot internally returns Array<T>").Length - 1);
        }
        var parsed = new Transpiler().Parse(generated, ScriptLanguage.CSharp);
        Assert.DoesNotContain(parsed.Class.Elements.OfType<MethodDeclaration>(), callable => callable.Name is "gdDictKeys" or "gdDictValues");
        // Reusing an emitter must not leak helpers into later files.
        var noMethods = selected with { Elements = new List<SyntaxElement> { declaration with
        {
            Elements = declaration.Elements.Where(element => element is not MethodDeclaration).ToList()
        } } };
        var second = emitter.Emit(noMethods);
        Assert.DoesNotContain("gdDictKeys", second);
        Assert.DoesNotContain("gdDictValues", second);
    }

    [Fact]
    public void DotKeys()
    {
        var source = File.ReadAllText(Fixture(".gd"));
        var dotted = source.Replace("Entries[\"values\"]", "Entries.values").Replace("Entries[\"keys\"]", "Entries.keys");
        Assert.NotEqual(source, dotted);
        var transpiler = new Transpiler();
        Assert.Equivalent(transpiler.Parse(source, ScriptLanguage.GdScript), transpiler.Parse(dotted, ScriptLanguage.GdScript), strict: true);
        Assert.Equal(File.ReadAllText(Fixture(".cs")), transpiler.Transpile(dotted, ScriptLanguage.GdScript, ScriptLanguage.CSharp));
    }

    [Theory]
    [InlineData(".gd", "points.values()", "points.values(1)", "takes no arguments")]
    [InlineData(".gd", "points.keys()", "points.keys(1)", "takes no arguments")]
    [InlineData(".cs", "gdDictKeys(points)", "gdDictKeys()", "one typed dictionary argument")]
    [InlineData(".cs", "gdDictValues(points)", "gdDictValues(points, points)", "one typed dictionary argument")]
    [InlineData(".cs", "gdDictValues(points)", "gdDictValues(Values)", "explicit key and value types")]
    public void RejectHelper(string extension, string original, string replacement, string message)
    {
        var source = File.ReadAllText(Fixture(extension)).Replace(original, replacement);
        var language = extension == ".gd" ? ScriptLanguage.GdScript : ScriptLanguage.CSharp;
        var error = Assert.Throws<ParseException>(() => new Transpiler().Parse(source, language));
        Assert.Contains(message, error.Message);
        var lines = source.ReplaceLineEndings("\n").Split('\n');
        var line = Array.FindIndex(lines, text => text.Contains(replacement, StringComparison.Ordinal));
        Assert.Equal(line + 1, error.Line);
        Assert.Equal(lines[line].IndexOf(replacement, StringComparison.Ordinal) + 1, error.Column);
    }

    private static string Fixture(string extension) => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "scripts", "Expressions", "CollectionOperations" + extension));
}
