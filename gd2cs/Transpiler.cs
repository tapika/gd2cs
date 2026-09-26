using gd2cs.Emission;
using gd2cs.Language;
using gd2cs.Model;
using gd2cs.Parsing;

namespace gd2cs;

public sealed class Transpiler
{
    private readonly GodotTypeCatalog godotTypes;

    public Transpiler() : this(GodotTypeCatalog.CreateKnownBuiltIns())
    {
    }

    public Transpiler(GodotTypeCatalog godotTypes)
    {
        this.godotTypes = godotTypes;
    }

    public ScriptModel Parse(string source, ScriptLanguage sourceLanguage)
    {
        var parsed = ParserFor(sourceLanguage).Parse(source);
        return new SemanticNormalizer(sourceLanguage, godotTypes).Normalize(parsed);
    }

    public string Transpile(string source, ScriptLanguage sourceLanguage, ScriptLanguage targetLanguage)
    {
        if (sourceLanguage == targetLanguage)
            throw new ArgumentException("Source and target languages must be different.", nameof(targetLanguage));

        var generated = EmitterFor(targetLanguage).Emit(Parse(source, sourceLanguage));
        return PreserveFormatting(source, generated);
    }

    public void TranspileFile(string sourcePath, string targetPath)
    {
        var sourceLanguage = LanguageFromExtension(sourcePath);
        var targetLanguage = LanguageFromExtension(targetPath);
        var source = File.ReadAllText(sourcePath);
        try
        {
            var output = Transpile(source, sourceLanguage, targetLanguage);
            File.WriteAllText(targetPath, output);
        }
        catch (ParseException exception)
        {
            exception.SetSource(sourcePath, source);
            throw;
        }
    }

    public static ScriptLanguage LanguageFromExtension(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".gd" => ScriptLanguage.GdScript,
            ".cs" => ScriptLanguage.CSharp,
            _ => throw new ArgumentException($"Unsupported script extension: '{Path.GetExtension(path)}'.", nameof(path))
        };

    // Emitters use the platform newline; normalize their result to the source file's convention.
    private static string PreserveFormatting(string source, string generated)
    {
        var newline = source.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n"
            : source.Contains('\r') ? "\r" : "\n";
        generated = generated.ReplaceLineEndings(newline);
        var useTabs = DetectIndentation(source);
        return string.Join(newline, generated.Split(newline).Select(line => NormalizeIndent(line, useTabs)));
    }

    private static bool DetectIndentation(string source)
    {
        var tabs = 0;
        var spaces = 0;
        foreach (var line in source.ReplaceLineEndings("\n").Split('\n'))
        {
            if (line.StartsWith('\t')) tabs++;
            else if (line.StartsWith("    ", StringComparison.Ordinal)) spaces++;
        }
        return tabs > spaces;
    }

    private static string NormalizeIndent(string line, bool useTabs)
    {
        if (useTabs)
        {
            var count = 0;
            while (line.StartsWith("    ", StringComparison.Ordinal))
            {
                line = line[4..];
                count++;
            }
            return new string('\t', count) + line;
        }
        var tabs = 0;
        while (line.StartsWith('\t'))
        {
            line = line[1..];
            tabs++;
        }
        return new string(' ', tabs * 4) + line;
    }

    private static IScriptParser ParserFor(ScriptLanguage language) => language switch
    {
        ScriptLanguage.GdScript => new GdScriptParser(),
        ScriptLanguage.CSharp => new CSharpParser(),
        _ => throw new ArgumentOutOfRangeException(nameof(language))
    };

    private IScriptEmitter EmitterFor(ScriptLanguage language) => language switch
    {
        ScriptLanguage.GdScript => new GdScriptEmitter(godotTypes),
        ScriptLanguage.CSharp => new CSharpEmitter(godotTypes),
        _ => throw new ArgumentOutOfRangeException(nameof(language))
    };
}
