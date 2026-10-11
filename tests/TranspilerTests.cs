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
        Assert.Equal(19, r.Count);
        return r;
    }

    private static string Fixture(string name) =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "scripts", name));

    private static string ScriptsDirectory() => Fixture(string.Empty);
}
