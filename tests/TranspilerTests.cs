using System.Text;

namespace gd2cs.Tests;

public sealed class TranspilerTests
{
    public enum ScriptEncoding
    {
        Utf8,
        Utf8Bom,
        Windows1252
    }

    [Fact]
    public void GdToCs()
    {
        var sourcePath = Fixture("Classes/TestClass.gd");
        var expectedPath = Fixture("Classes/TestClass.cs");
        var outputPath = Path.Combine(Path.GetTempPath(), $"gd2cs-{Guid.NewGuid():N}.cs");

        try
        {
            new Transpiler().TranspileFile(sourcePath, outputPath);

            Assert.Equal(File.ReadAllText(expectedPath), File.ReadAllText(outputPath));
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public void LineEndingStyleIsPreserved()
    {
        var transpiler = new Transpiler();
        var gd = File.ReadAllText(Fixture("Classes/TestClass.gd")).ReplaceLineEndings("\r\n");
        var cs = File.ReadAllText(Fixture("Classes/TestClass.cs")).ReplaceLineEndings("\n");

        var generatedCs = transpiler.Transpile(gd, Language.ScriptLanguage.GdScript, Language.ScriptLanguage.CSharp);
        var generatedGd = transpiler.Transpile(cs, Language.ScriptLanguage.CSharp, Language.ScriptLanguage.GdScript);

        Assert.DoesNotContain("\n", generatedCs.Replace("\r\n", string.Empty));
        Assert.DoesNotContain("\r", generatedGd);
    }

    [Theory]
    [InlineData(".gd", ".cs", ScriptEncoding.Utf8)]
    [InlineData(".gd", ".cs", ScriptEncoding.Utf8Bom)]
    [InlineData(".cs", ".gd", ScriptEncoding.Utf8)]
    [InlineData(".cs", ".gd", ScriptEncoding.Utf8Bom)]
    [InlineData(".cs", ".gd", ScriptEncoding.Windows1252)]
    public void FileEncoding(string sourceExtension, string targetExtension, ScriptEncoding encoding)
    {
        var sourcePath = Path.Combine(Path.GetTempPath(), $"gd2cs-{Guid.NewGuid():N}{sourceExtension}");
        var targetPath = Path.ChangeExtension(sourcePath, targetExtension);
        try
        {
            var source = File.ReadAllText(Fixture("Expressions/InterpolatedStrings" + sourceExtension));
            var inputEncoding = encoding switch
            {
                ScriptEncoding.Utf8 => new UTF8Encoding(false),
                ScriptEncoding.Utf8Bom => new UTF8Encoding(true),
                ScriptEncoding.Windows1252 => CodePagesEncodingProvider.Instance.GetEncoding(1252)!,
                _ => throw new ArgumentOutOfRangeException(nameof(encoding))
            };
            File.WriteAllText(sourcePath, source, inputEncoding);
            if (encoding == ScriptEncoding.Windows1252)
                Assert.Contains((byte)0x80, File.ReadAllBytes(sourcePath));

            new Transpiler().TranspileFile(sourcePath, targetPath);

            var output = File.ReadAllBytes(targetPath);
            Assert.Equal(encoding == ScriptEncoding.Utf8Bom, output.AsSpan().StartsWith(Encoding.UTF8.Preamble));
            Assert.Contains("\u20ac", new UTF8Encoding(false, true).GetString(output));
            Assert.Equal(File.ReadAllText(Fixture("Expressions/InterpolatedStrings" + targetExtension)),
                File.ReadAllText(targetPath, Encoding.UTF8));
        }
        finally
        {
            File.Delete(sourcePath);
            File.Delete(targetPath);
        }
    }

    [Theory]
    [MemberData(nameof(ScriptPairs))]
    public void ParseAndEmit(string baseName)
    {
        var transpiler = new Transpiler();

        var gdModel = transpiler.Parse(
            File.ReadAllText(Fixture(baseName + ".gd")),
            Language.ScriptLanguage.GdScript);
        var csModel = transpiler.Parse(
            File.ReadAllText(Fixture(baseName + ".cs")),
            Language.ScriptLanguage.CSharp);

        Assert.Equivalent(gdModel, csModel, strict: true);

        var csharp = transpiler.Transpile(
            File.ReadAllText(Fixture(baseName + ".gd")),
            Language.ScriptLanguage.GdScript,
            Language.ScriptLanguage.CSharp);

        var gdscript = transpiler.Transpile(
            File.ReadAllText(Fixture(baseName + ".cs")),
            Language.ScriptLanguage.CSharp,
            Language.ScriptLanguage.GdScript);

        Assert.Equal(File.ReadAllText(Fixture(baseName + ".cs")), csharp);
        Assert.Equal(File.ReadAllText(Fixture(baseName + ".gd")), gdscript);
    }

    [Fact]
    public void SignalForms()
    {
        var transpiler = new Transpiler();
        var gd = File.ReadAllText(Fixture("Signals/CustomSignals.gd"));
        var cs = File.ReadAllText(Fixture("Signals/CustomSignals.cs"));
        Assert.Equivalent(transpiler.Parse(gd, Language.ScriptLanguage.GdScript),
            transpiler.Parse(gd.Replace("signal changed", "signal changed()")
                .Replace("changed.emit()", "self.changed.emit()"), Language.ScriptLanguage.GdScript), strict: true);
        Assert.Equivalent(transpiler.Parse(cs, Language.ScriptLanguage.CSharp),
            transpiler.Parse(cs.Replace("EmitSignal(", "this.EmitSignal("), Language.ScriptLanguage.CSharp), strict: true);
    }

    [Theory]
    [InlineData(".gd", "acceptKey(event)", "event = event")]
    [InlineData(".cs", "acceptKey(eventTyped2);", "@event = @event;")]
    public void RejectNarrowedAssignment(string extension, string original, string replacement)
    {
        var source = File.ReadAllText(Fixture("Statements/ControlFlow" + extension)).Replace(original, replacement);
        var language = extension == ".gd" ? Language.ScriptLanguage.GdScript : Language.ScriptLanguage.CSharp;
        var error = Assert.Throws<Parsing.ParseException>(() => new Transpiler().Parse(source, language));
        Assert.Equal("Reassigning a type-narrowed variable is not supported.", error.Message);
        var lines = source.ReplaceLineEndings("\n").Split('\n');
        var line = Array.FindIndex(lines, text => text.Contains(replacement, StringComparison.Ordinal));
        Assert.Equal(line + 1, error.Line);
        Assert.Equal(lines[line].Length - lines[line].TrimStart().Length + 1, error.Column);
    }

    [Theory]
    [InlineData(".gd", "ActionButton.pressed.connect(onPressed)", "ActionButton.pressed.connect(onSelected)",
        "Signal handler must return void and match the signal's argument count.")]
    [InlineData(".gd", "ChoiceButton.item_selected.connect(onSelected)", "ChoiceButton.item_selected.connect(missingHandler)",
        "Signal connection requires an unambiguous named void handler in this class or an inline lambda.")]
    [InlineData(".gd", "ActionButton.pressed.connect(onPressed)", "ActionButton.pressed.connect(onPressed, 1)",
        "Signal connections currently require exactly one callback and no flags.")]
    [InlineData(".cs", "ActionButton.Pressed += onPressed", "ActionButton.Pressed += onSelected",
        "Signal handler must return void and match the signal's argument count.")]
    [InlineData(".cs", "ChoiceButton.ItemSelected += onSelected", "ChoiceButton.ItemSelected += missingHandler",
        "Signal connection requires an unambiguous named void handler in this class or an inline lambda.")]
    [InlineData(".gd", "ChoiceButton.item_selected.connect(func (index: int): onSelected(index))",
        "ChoiceButton.item_selected.connect(func (index: String): onSelected(index))",
        "Signal handler parameter types are incompatible with the native event.")]
    [InlineData(".cs", "ChoiceButton.ItemSelected += (long index_)",
        "ChoiceButton.ItemSelected += (string index_)",
        "Signal handler parameter types are incompatible with the native event.")]
    public void RejectConnections(string extension, string original, string replacement, string message)
    {
        var source = File.ReadAllText(Fixture("Signals/SignalConnections" + extension)).Replace(original, replacement);
        var language = extension == ".gd" ? Language.ScriptLanguage.GdScript : Language.ScriptLanguage.CSharp;
        var error = Assert.Throws<Parsing.ParseException>(() => new Transpiler().Parse(source, language));
        Assert.Equal(message, error.Message);
        var lines = source.ReplaceLineEndings("\n").Split('\n');
        var line = Array.FindIndex(lines, text => text.Contains(replacement, StringComparison.Ordinal));
        Assert.Equal(line + 1, error.Line);
        Assert.Equal(lines[line].Length - lines[line].TrimStart().Length + 1, error.Column);
    }

    [Fact]
    public void EventWidths()
    {
        var transpiler = new Transpiler();
        var cs = File.ReadAllText(Fixture("Signals/SignalConnections.cs")).ReplaceLineEndings("\n");
        var gd = File.ReadAllText(Fixture("Signals/SignalConnections.gd")).ReplaceLineEndings("\n");
        // Both dialects obtain the native signature even if the source uses C# int parameters.
        var narrow = cs.Replace("long index_2", "int index")
            .Replace("\t\tvar index = (int)(index_2);\n", string.Empty);
        Assert.Equivalent(transpiler.Parse(cs, Language.ScriptLanguage.CSharp),
            transpiler.Parse(narrow, Language.ScriptLanguage.CSharp), strict: true);
        Assert.Equal(gd, transpiler.Transpile(narrow, Language.ScriptLanguage.CSharp, Language.ScriptLanguage.GdScript));
        Assert.Equal(cs, transpiler.Transpile(gd, Language.ScriptLanguage.GdScript, Language.ScriptLanguage.CSharp));
        var legacy = narrow.Replace("ChoiceButton.ItemSelected += onSelected;",
            "ChoiceButton.Connect(OptionButton.SignalName.ItemSelected, Callable.From<int>(onSelected));");
        Assert.Equal(gd, transpiler.Transpile(legacy, Language.ScriptLanguage.CSharp, Language.ScriptLanguage.GdScript));
    }

    [Theory]
    [InlineData(".gd", "changed.emit()", "changed.emit(1)", "Signal 'changed' requires 0 argument(s).")]
    [InlineData(".cs", "EmitSignal(SignalName.changed)", "EmitSignal(SignalName.changed, 1)", "Signal 'changed' requires 0 argument(s).")]
    [InlineData(".cs", "changedEventHandler", "changed", "Signal delegate names must end with EventHandler.")]
    public void RejectSignals(string extension, string original, string replacement, string message)
    {
        var source = File.ReadAllText(Fixture("Signals/CustomSignals" + extension)).Replace(original, replacement);
        var language = extension == ".gd" ? Language.ScriptLanguage.GdScript : Language.ScriptLanguage.CSharp;
        var error = Assert.Throws<Parsing.ParseException>(() => new Transpiler().Parse(source, language));
        Assert.Equal(message, error.Message);
        Assert.True(error.Line > 1);
        Assert.True(error.Column > 0);
    }

    [Fact]
    public static void CheckContainerNulls()
    {
        var cs = File.ReadAllText(Fixture("Expressions/Collections/DictionaryExpressions.cs"));
        foreach (var value in new[] { "null", "default" })
        {
            foreach (var constructor in new[]
            {
                "new Godot.Collections.Dictionary<string, Node3D>()",
                "new Godot.Collections.Array<Godot.Collections.Dictionary<string, Node3D>>()",
                "new Godot.Collections.Dictionary<string, int>()"
            })
            {
                var source = cs.Replace(constructor, value);
                var error = Assert.Throws<Parsing.ParseException>(() => new Transpiler().Parse(source, Language.ScriptLanguage.CSharp));
                Assert.Equal("Null containers are not supported; use an empty container constructor or omit the initializer.", error.Message);
                var lines = source.ReplaceLineEndings("\n").Split('\n');
                var line = Array.FindIndex(lines, text => text.Contains(" = " + value + ";", StringComparison.Ordinal));
                Assert.Equal(line + 1, error.Line);
                Assert.Equal(lines[line].IndexOf(" = " + value + ";", StringComparison.Ordinal) + 4, error.Column);
            }
        }
    }

    [Theory]
    [InlineData("PackedVector2Array", "Vector2")]
    [InlineData("PackedVector3Array", "Vector3")]
    [InlineData("PackedColorArray", "Color")]
    public void RejectPackedStorage(string packed, string element)
    {
        var gd = File.ReadAllText(Fixture("Expressions/Collections/ArrayBuffers.gd"));
        foreach (var source in new[]
        {
            gd.Replace("var Points : Array[Vector2]", "var Points : " + packed),
            gd.Replace("PackedVector2Array(Points)", packed + "()"),
            gd.Replace("PackedVector2Array(Points)", packed + "(Points, Points)")
        })
        {
            var error = Assert.Throws<Parsing.ParseException>(() => new Transpiler().Parse(source, Language.ScriptLanguage.GdScript));
            Assert.Contains("not ported to C#", error.Message);
            Assert.Contains("Array[" + element + "]", error.Message);
            var lines = source.ReplaceLineEndings("\n").Split('\n');
            var line = Array.FindIndex(lines, text => text.Contains(packed, StringComparison.Ordinal));
            Assert.Equal(line + 1, error.Line);
            Assert.Equal(lines[line].IndexOf(packed, StringComparison.Ordinal) + 1, error.Column);
        }
    }

    [Fact]
    public void RejectBufferMismatch()
    {
        var source = File.ReadAllText(Fixture("Expressions/Collections/ArrayBuffers.gd"))
            .Replace("PackedVector2Array(Points)", "PackedVector2Array(Positions)");
        var error = Assert.Throws<Parsing.ParseException>(() => new Transpiler().Parse(source, Language.ScriptLanguage.GdScript));
        Assert.Contains("requires Array[Vector2]", error.Message);
        Assert.True(error.Line > 1);
        Assert.True(error.Column > 1);
    }

    [Theory]
    [InlineData(".gd", "return callback.call(1) as bool", "return callback.call(1)", "callback.call(1)")]
    [InlineData(".gd", "if callback.is_valid():", "if callback.call(1):", "callback.call(1)")]
    [InlineData(".gd", "if callback.is_valid():", "if not callback.call(1):", "callback.call(1)")]
    [InlineData(".gd", "callback.call(value) as bool", "callback.call(value)", "(callback.call(value))")]
    [InlineData(".gd", "return callback.call(1) as bool", "var result := callback.call(1)\n\t\treturn result", "return result")]
    [InlineData(".cs", "return callback.Call(1).As<bool>()", "return callback.Call(1)", "callback.Call(1)")]
    [InlineData(".cs", "if (gdCallableIsValid(callback))", "if (callback.Call(1))", "callback.Call(1)")]
    public void RejectVariantConditions(string extension, string original, string replacement, string offending)
    {
        var source = File.ReadAllText(Fixture("Expressions/LocalLambdas" + extension)).Replace(original, replacement);
        var language = extension == ".gd" ? Language.ScriptLanguage.GdScript : Language.ScriptLanguage.CSharp;
        var error = Assert.Throws<Parsing.ParseException>(() => new Transpiler().Parse(source, language));
        Assert.Contains("require an explicit cast", error.Message);
        Assert.Contains("as bool", error.Message);
        var lines = source.ReplaceLineEndings("\n").Split('\n');
        var line = Array.FindIndex(lines, text => text.Contains(offending, StringComparison.Ordinal));
        Assert.Equal(line + 1, error.Line);
        var token = offending == "return result" ? "result" : offending;
        Assert.Equal(lines[line].IndexOf(token, StringComparison.Ordinal) + 1, error.Column);
    }

    [Fact]
    public static void CheckDictionaryReads()
    {
        var transpiler = new Transpiler();
        var gd = File.ReadAllText(Fixture("Expressions/Collections/DictionaryExpressions.gd"));
        var cs = File.ReadAllText(Fixture("Expressions/Collections/DictionaryExpressions.cs"));
        var dotSource = gd.Replace("entry[\"node\"]", "entry.node")
            .Replace("entry[\"count\"]", "entry.count");
        Assert.Equivalent(transpiler.Parse(gd, Language.ScriptLanguage.GdScript),
            transpiler.Parse(dotSource, Language.ScriptLanguage.GdScript), strict: true);

        foreach (var value in new[]
        {
            "entry.node", "entry[\"node\"]", "is_instance_valid(entry.node)",
            "entry.node.get_parent()", "entry.node.get_parent() as Node",
            "(entry[\"node\"])", "is_instance_valid(entry[\"node\"])",
            "entry[\"node\"].get_parent()"
        })
            Reject(gd.Replace("var item := entry[\"node\"] as Node", "var item := " + value),
                Language.ScriptLanguage.GdScript);
        foreach (var value in new[]
        {
            "entry[\"node\"]", "entry[\"node\"].AsGodotObject()",
            "GodotObject.IsInstanceValid(entry[\"node\"])", "entry[\"node\"].GetParent()"
        })
            Reject(cs.Replace("var item = entry[\"node\"].AsGodotObject() as Node", "var item = " + value),
                Language.ScriptLanguage.CSharp);

        void Reject(string source, Language.ScriptLanguage language)
        {
            var error = Assert.Throws<Parsing.ParseException>(() => transpiler.Parse(source, language));
            Assert.Contains("require an explicit cast", error.Message);
            var lines = source.ReplaceLineEndings("\n").Split('\n');
            var line = Array.FindIndex(lines, text => text.TrimStart().StartsWith("var item", StringComparison.Ordinal));
            Assert.Equal(line + 1, error.Line);
            Assert.Equal(lines[line].IndexOf("entry", StringComparison.Ordinal) + 1, error.Column);
            Assert.Contains("as Node", error.Message);
        }
    }

    [Fact]
    public static void RejectUntypedDictionaries()
    {
        var gd = File.ReadAllText(Fixture("Expressions/Collections/DictionaryExpressions.gd"));
        var cs = File.ReadAllText(Fixture("Expressions/Collections/DictionaryExpressions.cs"));
        var gdSources = new[]
        {
            gd.Replace("Dictionary[String, Node3D]", "Dictionary"),
            gd.Replace("Dictionary[String, Node3D]", "Array[Dictionary]"),
            gd.Replace("var Empty : Dictionary[String, int] =", "var Empty :="),
            gd.Replace("var compact : Dictionary[String, Variant] =", "var compact :="),
            gd.Replace("\"entry\": compact", "\"entry\": {}"),
            gd.Replace(" #[String, Node3D]", "")
        };
        var csSources = new[]
        {
            cs.Replace("Godot.Collections.Dictionary<string, Node3D>", "Godot.Collections.Dictionary"),
            cs.Replace("Godot.Collections.Dictionary<string, Node3D>", "Godot.Collections.Array<Godot.Collections.Dictionary>"),
            cs.Replace("new Godot.Collections.Dictionary<string, int>", "new Godot.Collections.Dictionary")
        };
        foreach (var source in gdSources)
            Reject(source, Language.ScriptLanguage.GdScript);
        foreach (var source in csSources)
            Reject(source, Language.ScriptLanguage.CSharp);

        foreach (var hint in new[] { "[String]", "[String,]", "[String, Node3D] trailing", "[String, Dictionary]" })
        {
            var source = gd.Replace("#[String, Node3D]", "#" + hint);
            var error = Assert.Throws<Parsing.ParseException>(() => new Transpiler().Parse(source, Language.ScriptLanguage.GdScript));
            Assert.StartsWith("Invalid dictionary type hint:", error.Message);
            var lines = source.ReplaceLineEndings("\n").Split('\n');
            var line = Array.FindIndex(lines, text => text.StartsWith("var ObjectEntries", StringComparison.Ordinal));
            Assert.Equal(line + 1, error.Line);
            Assert.True(error.Column > lines[line].IndexOf('#'));
        }

        static void Reject(string source, Language.ScriptLanguage language)
        {
            var error = Assert.Throws<Parsing.ParseException>(() => new Transpiler().Parse(source, language));
            Assert.Equal("Untyped dictionaries are not supported; specify Dictionary[KeyType, ValueType] or use a class for mixed fields.", error.Message);
            Assert.True(error.Line > 1);
            Assert.True(error.Column > 0);
        }
    }

    [Fact]
    public static void RejectEmptyArrays()
    {
        var transpiler = new Transpiler();
        var gd = File.ReadAllText(Fixture("Expressions/ArrayExpressions.gd")).ReplaceLineEndings("\n");
        var cs = File.ReadAllText(Fixture("Expressions/ArrayExpressions.cs")).ReplaceLineEndings("\n");
        foreach (var literal in new[] { "[]", "[\n\n\t]" })
        {
            foreach (var declaration in new[] { "var InlineValues := ", "var InlineValues : Array[int] = " })
                Reject(gd.Replace("var InlineValues := [1, 2,\n\t3]", declaration + literal), Language.ScriptLanguage.GdScript);
            Reject(gd.Replace("[4.0, 5.0, 6.0]", literal), Language.ScriptLanguage.GdScript);
            Reject(gd.Replace("ElementValue.new(10, \"first\")", literal), Language.ScriptLanguage.GdScript);
        }
        foreach (var literal in new[] { "gdArray()", "gdArray(\n\n\t)" })
        {
            Reject(cs.Replace("gdArray(1, 2,\n\t\t3)", literal), Language.ScriptLanguage.CSharp);
            Reject(cs.Replace("gdArray(4.0f, 5.0f, 6.0f)", literal), Language.ScriptLanguage.CSharp);
        }

        void Reject(string source, Language.ScriptLanguage language)
        {
            var error = Assert.Throws<Parsing.ParseException>(() => transpiler.Parse(source, language));
            Assert.Equal("Empty array literals are not supported; declare an array type without an initializer instead.", error.Message);
            Assert.True(error.Line > 0);
            Assert.True(error.Column > 0);
        }
    }

    [Theory]
    [InlineData("double")]
    [InlineData("Double")]
    [InlineData("System.Double")]
    [InlineData("Godot.Collections.Array<double>")]
    public void RejectDouble(string type)
    {
        var source = File.ReadAllText(Fixture("Classes/ScalarMembers.cs"))
            .Replace("float", type, StringComparison.Ordinal);

        var error = Assert.Throws<Parsing.ParseException>(() => new Transpiler().Transpile(
            source, Language.ScriptLanguage.CSharp, Language.ScriptLanguage.GdScript));

        Assert.Equal("C# double is not supported; use float instead.", error.Message);
        Assert.True(error.Line > 0);
        Assert.True(error.Column > 0);
    }

    [Fact]
    public void LocalErrorLocation()
    {
        var sourcePath = Path.Combine(Path.GetTempPath(), $"gd2cs-{Guid.NewGuid():N}.gd");
        var targetPath = Path.ChangeExtension(sourcePath, ".cs");
        var source = File.ReadAllText(Fixture("Statements/LocalVariables.gd"))
            .Replace("var pending : int", "var pending", StringComparison.Ordinal);
        var lines = source.ReplaceLineEndings("\n").Split('\n');
        var lineIndex = Array.FindIndex(lines, line => line.Trim() == "var pending");
        try
        {
            File.WriteAllText(sourcePath, source);
            var error = Assert.Throws<Parsing.ParseException>(() =>
                new Transpiler().TranspileFile(sourcePath, targetPath));

            Assert.Equal("Explicit local requires a type.", error.Message);
            Assert.Equal(sourcePath, error.SourcePath);
            Assert.Equal(lineIndex + 1, error.Line);
            Assert.Equal(lines[lineIndex].IndexOf("var pending", StringComparison.Ordinal) + 1, error.Column);
            Assert.Equal(lines[lineIndex], error.SourceLine);
        }
        finally
        {
            File.Delete(sourcePath);
            File.Delete(targetPath);
        }
    }

    public static IEnumerable<object[]> ScriptPairs()
    {
        foreach (var pair in DiscoverScriptPairs())
            yield return new object[] { pair };
    }

    private static List<string> DiscoverScriptPairs()
    {
        var pairs = new List<string>();
        foreach (var gdPath in Directory.EnumerateFiles(ScriptsDirectory(), "*.gd", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(ScriptsDirectory(), gdPath);
            var baseName = Path.ChangeExtension(relativePath, null);
            if (!File.Exists(Fixture(baseName + ".cs")))
                throw new InvalidOperationException($"Missing C# fixture for {baseName}.");
            pairs.Add(baseName);
        }
        var r = pairs.Order(StringComparer.Ordinal).ToList();
        // Just make sure that we won't accidentally remove a script.
        Assert.Equal(25, r.Count);
        return r;
    }

    private static string Fixture(string name) =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "scripts", name));

    private static string ScriptsDirectory() => Fixture(string.Empty);
}
