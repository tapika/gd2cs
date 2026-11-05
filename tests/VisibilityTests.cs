using gd2cs.Language;
using gd2cs.Parsing;

namespace gd2cs.Tests;

public sealed class VisibilityTests
{
    [Theory]
    [InlineData("Properties/Accessors.cs", "public int Stored;", "private int Stored;")]
    [InlineData("Properties/Accessors.cs", "public int Stored;", "protected int Stored;")]
    [InlineData("Properties/Accessors.cs", "public int Stored;", "internal int Stored;")]
    [InlineData("Properties/Accessors.cs", "public int Stored;", "protected internal int Stored;")]
    [InlineData("Properties/Accessors.cs", "public int Stored;", "int Stored;")]
    [InlineData("Properties/Accessors.cs", "public static int Shared", "private static int Shared")]
    [InlineData("Properties/Accessors.cs", "public int Stored;", "private static int gdStr;")]
    [InlineData("Properties/Accessors.cs", "public void change", "private void change")]
    [InlineData("Properties/Accessors.cs", "public void change", "private void gdStr")]
    [InlineData("Properties/Accessors.cs", "public void change", "private static void gdSortCustomOther")]
    [InlineData("Properties/Accessors.cs", "public Node3D TargetNode", "internal Node3D TargetNode")]
    [InlineData("Properties/Accessors.cs", "\t\tset\n", "\t\tprivate set\n")]
    [InlineData("Properties/Accessors.cs", "\t\tget\n", "\t\tprotected get\n")]
    [InlineData("Properties/Accessors.cs", "public partial class Accessors", "internal partial class Accessors")]
    [InlineData("Classes/NestedClasses.cs", "public partial class AABB", "private partial class AABB")]
    [InlineData("Signals/CustomSignals.cs", "public delegate void changedEventHandler", "private delegate void changedEventHandler")]
    public void RejectNonPublic(string fixture, string original, string replacement)
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../scripts", fixture));
        var source = File.ReadAllText(path).ReplaceLineEndings("\n");
        Assert.Contains(original, source);
        source = source.Replace(original, replacement);

        var error = Assert.Throws<ParseException>(() => new Transpiler().Parse(source, ScriptLanguage.CSharp));
        Assert.Contains("Non-public C#", error.Message);
        Assert.Contains("public", error.Message);
        var lines = source.Split('\n');
        var expectedLine = Array.FindIndex(lines, line => line.Contains(replacement.Trim(), StringComparison.Ordinal));
        Assert.Equal(expectedLine + 1, error.Line);
        Assert.Equal(lines[expectedLine].Length - lines[expectedLine].TrimStart().Length + 1, error.Column);
    }

    [Fact]
    public void HelperNameIgnoresSignature()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../scripts/Expressions/Strings/StringOperations.cs"));
        var source = File.ReadAllText(path);
        var helperStart = source.IndexOf("private static string gdStr", StringComparison.Ordinal);
        Assert.True(helperStart >= 0);
        var changed = source[..helperStart] + source[helperStart..]
            .Replace("gdStr(params Variant[] values)", "gdStr(params Variant[] arguments)")
            .Replace("foreach (var value in values)", "foreach (var value in arguments)");
        Assert.NotEqual(source, changed);

        var transpiler = new Transpiler();
        Assert.Equivalent(transpiler.Parse(source, ScriptLanguage.CSharp),
            transpiler.Parse(changed, ScriptLanguage.CSharp), strict: true);
    }
}
