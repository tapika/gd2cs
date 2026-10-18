using gd2cs.Model;
using gd2cs.Parsing;

namespace gd2cs.Language;

// Script symbols supplement reflection; they never rename user-defined members.
internal sealed class ScriptTypeCatalog
{
    private sealed record ClassSymbol(string Path, TypeReference BaseType,
        Dictionary<string, TypeReference> Members, List<MethodDeclaration> Methods);

    // Fully qualified identities prevent equally named classes in different nested scopes colliding.
    private readonly Dictionary<string, ClassSymbol> classes = new(StringComparer.Ordinal);
    private readonly GodotTypeCatalog godotTypes;
    private readonly ScriptLanguage language;

    /* Register the entire declaration tree before inspecting bodies, including forward references.
       Resolve a short type name in its declaring class first, then walk enclosing classes. A type
       returned from another scope is qualified only when its original spelling would bind to a
       different class at the call site. Expressions retain their original user-member spellings.
       No cross-file or initializer-based guessing is performed for unavailable declarations. */
    public ScriptTypeCatalog(ScriptModel model, GodotTypeCatalog godotTypes, ScriptLanguage language)
    {
        this.godotTypes = godotTypes;
        this.language = language;
        foreach (var declaration in model.Elements.OfType<ClassDeclaration>())
            AddClass(declaration.Name, declaration.BaseType, declaration.Elements);
    }

    private void AddClass(string path, TypeReference baseType, List<SyntaxElement> elements)
    {
        var members = new Dictionary<string, TypeReference>(StringComparer.Ordinal);
        foreach (var element in elements)
        {
            if (element is FieldDeclaration field)
                members.Add(field.Name, field.Type);
            else if (element is PropertyDeclaration property)
                members.Add(property.Name, property.Type);
            else if (element is ConstantDeclaration constant)
                members.Add(constant.Name, constant.Type);
        }
        classes.Add(path, new ClassSymbol(path, baseType, members, elements.OfType<MethodDeclaration>().ToList()));
        foreach (var nested in elements.OfType<NestedClassDeclaration>())
            AddClass(path + "." + nested.Name, nested.BaseType, nested.Elements);
    }

    // Keep inferred fields synchronized with the semantic pass's existing fixed-point binding.
    public void UpdateMember(string path, string name, TypeReference type) => classes[path].Members[name] = type;

    // Prefer the nearest lexical declaration; never choose an unrelated class by its short name.
    private ClassSymbol? FindClass(string name, string context)
    {
        for (var scope = context; scope.Length > 0;)
        {
            if (classes.TryGetValue(scope + "." + name, out var nested))
                return nested;
            var separator = scope.LastIndexOf('.');
            scope = separator < 0 ? "" : scope[..separator];
        }
        return classes.GetValueOrDefault(name);
    }

    // Field and method result types belong to their declaration's scope, not the caller's scope.
    private TypeReference RebindType(TypeReference type, string owner, string caller)
    {
        var name = type.Name;
        var native = type.GodotName;
        var symbol = FindClass(name, owner);
        if (symbol is not null)
        {
            if (FindClass(name, caller)?.Path != symbol.Path)
                name = symbol.Path;
        }
        else if (godotTypes.TryResolveTypeName(name, language, out var managed, out var gdName))
        {
            name = managed;
            native = gdName == managed ? null : gdName;
        }
        return type with
        {
            Name = name,
            GodotName = native,
            TypeArguments = type.TypeArguments.Select(argument => RebindType(argument, owner, caller)).ToList()
        };
    }

    // Walk user inheritance without allowing cycles to recurse indefinitely.
    private List<ClassSymbol> Hierarchy(TypeReference receiver, string context)
    {
        var result = new List<ClassSymbol>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var symbol = FindClass(receiver.Name, context);
        while (symbol is not null && visited.Add(symbol.Path))
        {
            result.Add(symbol);
            symbol = FindClass(symbol.BaseType.Name, symbol.Path);
        }
        return result;
    }

    public TypeReference? ResolveMember(TypeReference? receiver, string member, string context)
    {
        if (receiver is null)
            return null;
        foreach (var symbol in Hierarchy(receiver, context))
            if (symbol.Members.TryGetValue(member, out var type))
                return RebindType(type, symbol.Path, context);
        return null;
    }

    // Return the first non-script base so inherited engine APIs still use reflected binding.
    public TypeReference? NativeBase(TypeReference receiver, string context)
    {
        var hierarchy = Hierarchy(receiver, context);
        return hierarchy.Count == 0 ? null : RebindType(hierarchy[^1].BaseType, hierarchy[^1].Path, context);
    }

    public bool IsClass(string name, string context) => FindClass(name, context) is not null;

    public bool HasMethod(TypeReference receiver, string name, string context) =>
        Hierarchy(receiver, context).Any(symbol => symbol.Methods.Any(method => method.Name == name));

    // Use declared signatures and optional-argument bounds, requiring an exact match for overloads.
    public TypeReference? ResolveCall(TypeReference receiver, string name, ArgumentList arguments,
        bool isStatic, string context, Func<Expression, TypeReference?> resolveType, Expression location)
    {
        foreach (var symbol in Hierarchy(receiver, context))
        {
            var candidates = symbol.Methods.Where(method => method.Name == name && method.IsStatic == isStatic
                && arguments.Arguments.Count <= method.Parameters.Parameters.Count
                && arguments.Arguments.Count >= method.Parameters.Parameters.Count(parameter => parameter.DefaultValue is null)).ToList();
            if (candidates.Count == 0)
                continue;
            if (candidates.Count > 1)
            {
                var types = arguments.Arguments.Select(argument => resolveType(argument.Value)).ToList();
                candidates = candidates.Where(method => types.Select((type, index) => type is not null
                    && SameType(type, RebindType(method.Parameters.Parameters[index].Type, symbol.Path, context), context)).All(match => match)).ToList();
            }
            if (candidates.Count != 1)
                throw SourceLocations.Error(location, $"Cannot resolve return type of overloaded script method '{name}'; use arguments with unambiguous types.");
            return RebindType(candidates[0].ReturnType, symbol.Path, context);
        }
        return null;
    }

    // Qualified and short spellings may designate the same class; generic arguments stay structural.
    private bool SameType(TypeReference left, TypeReference right, string context) =>
        (FindClass(left.Name, context)?.Path ?? left.Name) == (FindClass(right.Name, context)?.Path ?? right.Name)
        && left.TypeArguments.Count == right.TypeArguments.Count
        && left.TypeArguments.Zip(right.TypeArguments).All(pair => SameType(pair.First, pair.Second, context));
}
