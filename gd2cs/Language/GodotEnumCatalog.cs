using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using gd2cs.Model;

namespace gd2cs.Language;

internal sealed class GodotEnumCatalog
{
    private sealed record Constant(GodotEnumReference Reference, string GdScriptName, string CSharpName);

    private readonly Dictionary<string, Constant> gdScriptConstants = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Constant> cSharpConstants = new(StringComparer.Ordinal);

    public static GodotEnumCatalog CreateKnownBuiltIns()
    {
        var catalog = new GodotEnumCatalog();
        catalog.Add("Control.MouseFilterEnum", "Ignore", "Control.MOUSE_FILTER_IGNORE");
        catalog.Add("Control.MouseFilterEnum", "Stop", "Control.MOUSE_FILTER_STOP");
        catalog.Add("Control.SizeFlags", "ExpandFill", "Control.SIZE_EXPAND_FILL");
        catalog.Add("HorizontalAlignment", "Center", "HORIZONTAL_ALIGNMENT_CENTER");
        catalog.Add("Error", "Ok", "OK");
        return catalog;
    }

    public static GodotEnumCatalog Load(Assembly assembly, string executablePath)
    {
        var directory = Path.Combine(Path.GetTempPath(), "gd2cs-api-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var start = new ProcessStartInfo(executablePath)
            {
                WorkingDirectory = directory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("--headless");
            start.ArgumentList.Add("--dump-extension-api");
            using var process = Process.Start(start)
                ?? throw new InvalidOperationException("Godot API export could not be started.");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(60000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                throw new TimeoutException("Godot API export exceeded 60 seconds.");
            }
            Task.WaitAll(output, error);
            if (process.ExitCode != 0)
                throw new InvalidOperationException("Godot API export failed: " + error.Result + output.Result);
            using var api = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "extension_api.json")));
            return FromApi(assembly, api.RootElement);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    internal static GodotEnumCatalog FromApi(Assembly assembly, JsonElement api)
    {
        var catalog = new GodotEnumCatalog();
        var enums = assembly.GetTypes()
            .Where(type => type.Namespace == "Godot" && type.IsEnum && type.IsVisible)
            .ToDictionary(type => NormalizeEnumName(NativeEnumName(type)), StringComparer.Ordinal);
        catalog.IndexGroups(api.GetProperty("global_enums"), null, enums);
        foreach (var section in new[] { "classes", "builtin_classes" })
        {
            foreach (var owner in api.GetProperty(section).EnumerateArray())
            {
                if (owner.TryGetProperty("enums", out var groups))
                    catalog.IndexGroups(groups, owner.GetProperty("name").GetString()!, enums);
            }
        }
        var classes = api.GetProperty("classes").EnumerateArray()
            .ToDictionary(owner => owner.GetProperty("name").GetString()!, StringComparer.Ordinal);
        var declaredConstants = catalog.cSharpConstants.Values
            .Where(constant => constant.GdScriptName.Contains('.'))
            .GroupBy(constant => constant.GdScriptName[..constant.GdScriptName.LastIndexOf('.')])
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        foreach (var owner in classes.Values)
        {
            var receiver = owner.GetProperty("name").GetString()!;
            var ancestor = owner;
            while (ancestor.TryGetProperty("inherits", out var parent) &&
                   classes.TryGetValue(parent.GetString()!, out ancestor))
            {
                var ancestorName = ancestor.GetProperty("name").GetString()!;
                if (!declaredConstants.TryGetValue(ancestorName, out var constants))
                    continue;
                var prefix = ancestorName + ".";
                foreach (var constant in constants)
                {
                    catalog.gdScriptConstants.TryAdd(receiver + "." + constant.GdScriptName[prefix.Length..], constant);
                }
            }
        }
        return catalog;
    }

    private void IndexGroups(JsonElement groups, string? owner, Dictionary<string, Type> enums)
    {
        foreach (var group in groups.EnumerateArray())
        {
            var nativeName = group.GetProperty("name").GetString()!;
            var qualifiedName = owner is null ? nativeName : owner + "." + nativeName;
            if (!enums.TryGetValue(NormalizeEnumName(qualifiedName), out var enumType))
                continue;
            var fields = enumType.GetFields(BindingFlags.Public | BindingFlags.Static);
            foreach (var value in group.GetProperty("values").EnumerateArray())
            {
                var nativeMember = value.GetProperty("name").GetString()!;
                var number = value.GetProperty("value").GetInt64();
                var candidates = fields.Where(field =>
                    Convert.ToDecimal(field.GetRawConstantValue(), CultureInfo.InvariantCulture) == number &&
                    Normalize(nativeMember).EndsWith(Normalize(field.Name), StringComparison.Ordinal))
                    .OrderByDescending(field => Normalize(field.Name).Length).ToArray();
                if (candidates.Length == 0 || candidates.Length > 1 &&
                    Normalize(candidates[0].Name).Length == Normalize(candidates[1].Name).Length)
                    continue;
                Add(EnumName(enumType), candidates[0].Name, owner is null ? nativeMember : owner + "." + nativeMember);
            }
        }
    }

    private void Add(string enumType, string member, string gdScriptName)
    {
        var reference = new GodotEnumReference(enumType, member);
        var constant = new Constant(reference, gdScriptName, enumType + "." + member);
        gdScriptConstants.TryAdd(gdScriptName, constant);
        cSharpConstants.TryAdd(constant.CSharpName, constant);
    }

    public bool TryResolve(string name, ScriptLanguage language, out GodotEnumReference reference)
    {
        if (language == ScriptLanguage.CSharp && name.StartsWith("Godot.", StringComparison.Ordinal))
            name = name["Godot.".Length..];
        var constants = language == ScriptLanguage.GdScript ? gdScriptConstants : cSharpConstants;
        if (constants.TryGetValue(name, out var constant))
        {
            reference = constant.Reference;
            return true;
        }
        reference = null!;
        return false;
    }

    public string Name(GodotEnumReference reference, ScriptLanguage language)
    {
        var constant = cSharpConstants[reference.EnumType + "." + reference.Member];
        return language == ScriptLanguage.GdScript ? constant.GdScriptName : constant.CSharpName;
    }

    private static string EnumName(Type type) => type.FullName!["Godot.".Length..].Replace('+', '.');

    private static string NativeEnumName(Type type)
    {
        if (type.DeclaringType is not { } owner)
            return type.Name;
        var nativeName = owner.GetCustomAttributesData()
            .FirstOrDefault(attribute => attribute.AttributeType.Name == "GodotClassNameAttribute")
            ?.ConstructorArguments.FirstOrDefault().Value as string;
        return (nativeName ?? owner.Name) + "." + type.Name;
    }

    private static string NormalizeEnumName(string name) => string.Concat(name.Split('.').Select(part =>
        Normalize(part.EndsWith("Enum", StringComparison.Ordinal) ? part[..^4] : part)));

    private static string Normalize(string name) =>
        new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}