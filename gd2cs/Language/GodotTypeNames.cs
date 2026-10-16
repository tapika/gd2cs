using System.Reflection;
using System.Text.Json;

namespace gd2cs.Language;

internal sealed class GodotTypeNames
{
    // Exact native/managed spellings resolve to the same reflected CLR identity.
    private readonly Dictionary<string, string> nativeToManaged = new(StringComparer.Ordinal);
    // Native spelling is retained for GDScript emission, including acronyms such as AABB.
    private readonly Dictionary<string, string> managedToNative = new(StringComparer.Ordinal);

    // Mirrors the small, deterministic fallback constructor catalog; production uses reflection/API metadata.
    public static GodotTypeNames CreateKnownBuiltIns()
    {
        var names = new GodotTypeNames();
        names.Add("AABB", "Aabb");
        return names;
    }

    /* Reflection supplies managed identities and native class-name attributes. Value types
       do not consistently carry those attributes, so the engine's classes/builtin_classes
       supply authoritative native spellings. Match only exact attributes or a unique
       case-insensitive reflected name; never invent a type by applying casing to source.
       The caller reuses the API document already exported for enum binding. */
    public static GodotTypeNames FromApi(Assembly assembly, JsonElement api)
    {
        var names = new GodotTypeNames();
        var types = assembly.GetTypes().Where(type => type.Namespace == "Godot" && type.IsVisible
            && !type.IsNested && !type.IsEnum && !type.IsGenericType).ToList();
        var attributed = types.Select(type => new
        {
            Managed = type.Name,
            Native = type.GetCustomAttributesData()
                .FirstOrDefault(attribute => attribute.AttributeType.Name == "GodotClassNameAttribute")
                ?.ConstructorArguments.FirstOrDefault().Value as string
        }).Where(entry => entry.Native is not null).GroupBy(entry => entry.Native!, StringComparer.Ordinal);
        // Singleton wrapper and instance types can share one native name (e.g. ResourceUID).
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in attributed)
        {
            if (group.Count() == 1)
                names.Add(group.Key, group.Single().Managed);
            else
                ambiguous.Add(group.Key);
        }
        foreach (var section in new[] { "classes", "builtin_classes" })
        {
            foreach (var entry in api.GetProperty(section).EnumerateArray())
            {
                var native = entry.GetProperty("name").GetString()!;
                if (names.nativeToManaged.ContainsKey(native) || ambiguous.Contains(native))
                    continue;
                var matches = types.Where(type => string.Equals(type.Name, native, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matches.Count == 1)
                    names.Add(native, matches[0].Name);
            }
        }
        return names;
    }

    public bool TryResolve(string name, ScriptLanguage language, out string managed, out string native)
    {
        if (language == ScriptLanguage.CSharp && name.StartsWith("Godot.", StringComparison.Ordinal))
            name = name["Godot.".Length..];
        managed = language == ScriptLanguage.GdScript && nativeToManaged.TryGetValue(name, out var identity) ? identity : name;
        return managedToNative.TryGetValue(managed, out native!);
    }

    private void Add(string native, string managed)
    {
        nativeToManaged.Add(native, managed);
        managedToNative.Add(managed, native);
    }
}
