using System.Diagnostics;
using gd2cs.Language;
using gd2cs.Parsing;

namespace gd2cs.Tests;

public sealed class SortingTests
{
    [Theory]
    [InlineData(".gd", "Values.sort()", "Values.sort(1)", "Unsupported collection ordering")]
    [InlineData(".gd", "Values.sort()", "Records[0].sort()", "Unsupported collection ordering")]
    [InlineData(".gd", "Values.sort()", "Values.bsearch_custom(1, before)", "Unsupported collection ordering")]
    [InlineData(".gd", "Values.sort_custom(before)", "Values.sort_custom()", "one Boolean comparator")]
    [InlineData(".gd", "Values.sort_custom(before)", "Values.sort_custom(unknown)", "opaque Callable")]
    [InlineData(".gd", "Values.sort_custom(before)", "var callback : Callable\n\tValues.sort_custom(callback)", "opaque Callable")]
    [InlineData(".cs", "Values.Sort()", "Values.Sort(before)", "Unsupported collection ordering")]
    [InlineData(".cs", "Values.Sort()", "Values.OrderBy(before)", "Unsupported collection ordering")]
    [InlineData(".cs", "Values.Sort()", "Records[0].Sort()", "Unsupported collection ordering")]
    [InlineData(".cs", "gdSortCustom(Values, before)", "gdSortCustom(Values)", "array and a Boolean comparator")]
    [InlineData(".cs", "gdSortCustom(Values, before)", "gdSortCustom(Values, unknown)", "opaque Callable")]
    public void RejectSort(string extension, string original, string replacement, string message)
    {
        var source = File.ReadAllText(Fixture(extension)).ReplaceLineEndings("\n").Replace(original, replacement);
        var path = Path.Combine(Path.GetTempPath(), $"gd2cs-sort-{Guid.NewGuid():N}" + extension);
        var output = Path.ChangeExtension(path, extension == ".gd" ? ".cs" : ".gd");
        try
        {
            File.WriteAllText(path, source);
            var error = Assert.Throws<ParseException>(() => new Transpiler().TranspileFile(path, output));
            Assert.Contains(message, error.Message);
            Assert.Equal(path, error.SourcePath);
            var offending = replacement.Split('\n')[^1].TrimStart();
            var lines = source.Split('\n');
            var line = Array.FindIndex(lines, text => text.Contains(offending, StringComparison.Ordinal));
            Assert.Equal(line + 1, error.Line);
            Assert.Equal(lines[line].IndexOf(offending, StringComparison.Ordinal) + 1, error.Column);
            Assert.Equal(lines[line], error.SourceLine);
            Assert.False(File.Exists(output));
        }
        finally
        {
            File.Delete(path);
            File.Delete(output);
        }
    }

    [Theory]
    [InlineData(".gd", "func before(first: int, second: int) -> bool:", "func before(first: float, second: int) -> bool:")]
    [InlineData(".gd", "func before(first: int, second: int) -> bool:", "func before(first: int, second: int) -> int:")]
    [InlineData(".gd", "func (first: int, second: int) -> bool: return first > second", "func (first: int, second: int) -> int: return first")]
    [InlineData(".cs", "public bool before(int first, int second)", "public bool before(float first, int second)")]
    [InlineData(".cs", "public bool before(int first, int second)", "public int before(int first, int second)")]
    public void RejectPredicate(string extension, string original, string replacement)
    {
        var source = File.ReadAllText(Fixture(extension)).Replace(original, replacement);
        var language = extension == ".gd" ? ScriptLanguage.GdScript : ScriptLanguage.CSharp;
        var error = Assert.Throws<ParseException>(() => new Transpiler().Parse(source, language));
        Assert.Contains("two array-element parameters and return bool", error.Message);
        Assert.True(error.Line > 1);
        Assert.True(error.Column > 1);
    }

    [Fact]
    public void SortHelper()
    {
        var standard = File.ReadAllText(Path.Combine(Path.GetDirectoryName(Fixture(".gd"))!, "..", "CollectionOperations.gd"));
        Assert.DoesNotContain("gdSortCustom", new Transpiler().Transpile(standard,
            ScriptLanguage.GdScript, ScriptLanguage.CSharp));
        var generated = new Transpiler().Transpile(File.ReadAllText(Fixture(".gd")),
            ScriptLanguage.GdScript, ScriptLanguage.CSharp);
        const string signature = "private static void gdSortCustom<T>";
        var start = generated.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start > 0);
        Assert.Equal(start, generated.LastIndexOf(signature, StringComparison.Ordinal));
        // Compile the actual generated helper against IList/List: no native Godot runtime is needed in CI.
        var helper = generated[start..generated.LastIndexOf('}')];
        var directory = Path.Combine(Path.GetTempPath(), "gd2cs-sort-runtime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "Check.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(directory, "NuGet.Config"),
                "<configuration><packageSources><clear /></packageSources></configuration>");
            File.WriteAllText(Path.Combine(directory, "Program.cs"), RuntimeChecks + helper + "\n}");
            var info = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            info.ArgumentList.Add("run");
            info.ArgumentList.Add("--project");
            info.ArgumentList.Add("Check.csproj");
            info.ArgumentList.Add("--verbosity");
            info.ArgumentList.Add("quiet");
            info.Environment["DOTNET_NOLOGO"] = "1";
            using var process = Process.Start(info)!;
            if (!process.WaitForExit(60000))
            {
                process.Kill(entireProcessTree: true);
                Assert.Fail("Generated sort helper check timed out.");
            }
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            Assert.True(process.ExitCode == 0, output);
            Assert.Contains("Sort helper OK", output);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // Both the ordering contract and mutation of the original collection are observable.
    private const string RuntimeChecks = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        public static class Program
        {
            public static void Main()
            {
                foreach (var input in new[] { Array.Empty<int>(), new[] { 7 }, new[] { 3, 2, 1 }, new[] { 1, 2, 3 }, new[] { 2, 1, 2, 1 } })
                {
                    var values = new List<int>(input);
                    var alias = values;
                    gdSortCustom(values, (a, b) => a < b);
                    if (!alias.SequenceEqual(input.OrderBy(value => value))) throw new Exception("Ascending/alias failure");
                    gdSortCustom(values, (a, b) => a > b);
                    if (!values.SequenceEqual(input.OrderByDescending(value => value))) throw new Exception("Descending failure");
                }
                var records = new List<Dictionary<string, int>> { new() { ["rank"] = 2 }, new() { ["rank"] = 1 } };
                var first = records[1];
                gdSortCustom(records, (a, b) => a["rank"] < b["rank"]);
                if (!ReferenceEquals(first, records[0])) throw new Exception("Element identity failure");
                try { gdSortCustom(Array.AsReadOnly(new[] { 1 }), (a, b) => a < b); throw new Exception("Read-only sort accepted"); }
                catch (InvalidOperationException) { }
                Console.WriteLine("Sort helper OK");
            }

        """;

    private static string Fixture(string extension) => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "scripts", "Expressions", "Collections", "Sorting" + extension));
}
