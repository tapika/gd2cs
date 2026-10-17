using System.Reflection;
using System.Text.Json;
using gd2cs.Model;

namespace gd2cs.Language;

internal sealed class GodotStaticMembers
{
    private sealed record Entry(string NativeName, GodotStaticMemberReference Reference);
    private sealed record Candidate(string Owner, string Name, Type Type, GodotStaticMemberKind Kind,
        string Receiver);

    // GDScript intrinsics are absent from the extension API's class constants. Only these
    // names are globally visible; never expose unrelated Mathf fields such as Epsilon.
    // Managed spellings here belong to the deterministic fallback, not production binding.
    private static readonly List<(string Native, string KnownManaged)> globals =
        [("PI", "Pi"), ("TAU", "Tau"), ("INF", "Inf"), ("NAN", "NaN")];

    // Native and managed qualified spellings point to the same reflected member identity.
    private readonly Dictionary<string, Entry> nativeMembers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Entry> managedMembers = new(StringComparer.Ordinal);
    // Known API members without a unique managed counterpart must not be guessed.
    private readonly HashSet<string> unresolved = new(StringComparer.Ordinal);

    /* Static binding has two authorities: the engine API supplies native owners/spellings,
       and reflection supplies managed identities, result types and constant semantics.
       Match normalized names only within the resolved owner. Some bindings move constants
       to a companion type; in that case require a unique name AND matching result type
       (for example a color-valued constant declared on a separate managed class).
       Neither arbitrary source casing nor a list of class/member replacements is used.
       Keep both exact qualified spellings because even the owner can differ by dialect.
       Enum constants remain the responsibility of GodotEnumCatalog. */
    public static GodotStaticMembers FromApi(Assembly assembly, JsonElement api, GodotTypeNames names)
    {
        var catalog = new GodotStaticMembers();
        var candidates = new List<Candidate>();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        foreach (var type in assembly.GetTypes().Where(type => type.Namespace == "Godot"
            && type.IsVisible && !type.IsNested && !type.IsEnum && !type.IsGenericType))
        {
            foreach (var field in type.GetFields(flags))
                candidates.Add(new Candidate(field.DeclaringType!.Name, field.Name, field.FieldType,
                    field.IsLiteral ? GodotStaticMemberKind.Constant : field.IsInitOnly
                        ? GodotStaticMemberKind.ReadonlyField : GodotStaticMemberKind.Field, type.Name));
            foreach (var property in type.GetProperties(flags).Where(property =>
                property.GetIndexParameters().Length == 0 && property.GetMethod?.IsPublic == true))
                candidates.Add(new Candidate(property.DeclaringType!.Name, property.Name, property.PropertyType,
                    GodotStaticMemberKind.Property, type.Name));
        }

        // Build the name index once; the API can contain thousands of class constants.
        var byName = candidates.GroupBy(candidate => Normalize(candidate.Name))
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        foreach (var section in new[] { "classes", "builtin_classes" })
        {
            foreach (var owner in api.GetProperty(section).EnumerateArray())
            {
                var nativeOwner = owner.GetProperty("name").GetString()!;
                if (!names.TryResolve(nativeOwner, ScriptLanguage.GdScript, out var managedOwner, out _))
                    continue;
                foreach (var group in new[] { "constants", "properties" })
                {
                    if (!owner.TryGetProperty(group, out var members))
                        continue;
                    foreach (var member in members.EnumerateArray())
                    {
                        var nativeName = member.GetProperty("name").GetString()!;
                        var matchingName = byName.GetValueOrDefault(Normalize(nativeName)) ?? new List<Candidate>();
                        var matches = matchingName.Where(candidate => candidate.Receiver == managedOwner).ToList();
                        // A companion-class search is safe only for typed constants, never instance properties.
                        if (matches.Count == 0 && group == "constants" && member.TryGetProperty("type", out var nativeType)
                            && names.TryResolve(nativeType.GetString()!, ScriptLanguage.GdScript, out var resultType, out _))
                            matches = matchingName.Where(candidate => candidate.Type.Name == resultType).ToList();
                        matches = matches.DistinctBy(candidate => (candidate.Owner, candidate.Name)).ToList();
                        if (matches.Count == 1)
                        {
                            var match = matches[0];
                            var reference = new GodotStaticMemberReference(match.Owner, match.Name,
                                GodotTypeCatalog.ToTypeReference(match.Type), match.Kind);
                            catalog.Add(nativeOwner + "." + nativeName, reference,
                                match.Receiver == managedOwner ? managedOwner : null);
                        }
                        else if (group == "constants")
                            catalog.unresolved.Add(nativeOwner + "." + nativeName);
                    }
                }
            }
        }
        // The API lists declared constants only, while both languages allow inherited statics.
        // Walk nearest ancestors first so a derived declaration wins over a base declaration.
        var classes = api.GetProperty("classes").EnumerateArray()
            .ToDictionary(owner => owner.GetProperty("name").GetString()!, StringComparer.Ordinal);
        var declared = catalog.nativeMembers.Values.GroupBy(entry => entry.NativeName[..entry.NativeName.LastIndexOf('.')])
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        foreach (var (receiver, owner) in classes)
        {
            if (!names.TryResolve(receiver, ScriptLanguage.GdScript, out var managedReceiver, out _))
                continue;
            var ancestor = owner;
            while (ancestor.TryGetProperty("inherits", out var parent) && classes.TryGetValue(parent.GetString()!, out ancestor))
            {
                if (!declared.TryGetValue(parent.GetString()!, out var inherited))
                    continue;
                foreach (var entry in inherited)
                {
                    catalog.nativeMembers.TryAdd(receiver + entry.NativeName[entry.NativeName.LastIndexOf('.')..], entry);
                    catalog.managedMembers.TryAdd(managedReceiver + "." + entry.Reference.Name, entry);
                }
            }
        }
        // Reuse reflected identities/types/constant semantics, rather than substituting text
        // or embedding numeric values. Add globals after inheritance indexing: they have no owner.
        foreach (var (native, _) in globals)
        {
            var matches = (byName.GetValueOrDefault(Normalize(native)) ?? new List<Candidate>())
                .Where(candidate => candidate.Owner == "Mathf"
                    && candidate.Kind is GodotStaticMemberKind.Constant or GodotStaticMemberKind.ReadonlyField)
                .DistinctBy(candidate => (candidate.Owner, candidate.Name)).ToList();
            if (matches.Count == 1)
            {
                var match = matches[0];
                catalog.Add(native, new GodotStaticMemberReference(match.Owner, match.Name,
                    GodotTypeCatalog.ToTypeReference(match.Type), match.Kind));
            }
            else
                catalog.unresolved.Add(native);
        }
        return catalog;
    }

    // Context-free tests use a small deterministic subset; the CLI uses the full reflected API.
    public static GodotStaticMembers CreateKnownBuiltIns()
    {
        var catalog = new GodotStaticMembers();
        foreach (var (owner, name) in new[]
        {
            ("Vector3", "Zero"), ("Vector2", "Zero"), ("Vector2", "One"), ("Vector2", "Right"),
            ("Quaternion", "Identity"), ("Transform3D", "Identity")
        })
            catalog.Add(owner + "." + name.ToUpperInvariant(),
                new GodotStaticMemberReference(owner, name, new TypeReference(owner), GodotStaticMemberKind.Property));
        catalog.Add("Color.RED", new GodotStaticMemberReference("Colors", "Red", new TypeReference("Color"),
            GodotStaticMemberKind.Property));
        var notification = new GodotStaticMemberReference("Node", "NotificationReady", new TypeReference("int64"),
            GodotStaticMemberKind.Constant);
        catalog.Add("Node.NOTIFICATION_READY", notification);
        catalog.nativeMembers.Add("Node3D.NOTIFICATION_READY", catalog.nativeMembers["Node.NOTIFICATION_READY"]);
        catalog.managedMembers.Add("Node3D.NotificationReady", catalog.managedMembers["Node.NotificationReady"]);
        foreach (var (native, managed) in globals)
            catalog.Add(native, new GodotStaticMemberReference("Mathf", managed, new TypeReference("float"),
                GodotStaticMemberKind.Constant));
        return catalog;
    }

    // Register the concrete owner as an alias for inherited static members without losing declaration identity.
    private void Add(string nativeName, GodotStaticMemberReference reference, string? receiver = null)
    {
        var entry = new Entry(nativeName, reference);
        nativeMembers.TryAdd(nativeName, entry);
        managedMembers.TryAdd(reference.DeclaringType + "." + reference.Name, entry);
        if (receiver is not null)
            managedMembers.TryAdd(receiver + "." + reference.Name, entry);
    }

    public bool TryResolve(string name, ScriptLanguage language, out GodotStaticMemberReference reference)
    {
        if (language == ScriptLanguage.CSharp && name.StartsWith("Godot.", StringComparison.Ordinal))
            name = name["Godot.".Length..];
        var members = language == ScriptLanguage.GdScript ? nativeMembers : managedMembers;
        if (members.TryGetValue(name, out var entry))
        {
            reference = entry.Reference;
            return true;
        }
        reference = null!;
        return false;
    }

    public bool IsUnresolved(string name) => unresolved.Contains(name);

    public string Name(GodotStaticMemberReference reference, ScriptLanguage language) =>
        language == ScriptLanguage.CSharp ? reference.DeclaringType + "." + reference.Name
            : managedMembers[reference.DeclaringType + "." + reference.Name].NativeName;

    private static string Normalize(string name) =>
        new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
