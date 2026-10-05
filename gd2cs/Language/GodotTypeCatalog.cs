using System.Reflection;
using System.Text;
using gd2cs.Model;

namespace gd2cs.Language;

/*
Detailed design
---------------
GDScript uses two construction forms: ObjectType.new(...) for reference objects and
ValueType(...) for built-in Variant values. Capitalization cannot distinguish a value
constructor from an ordinary global call, so production runs index the GodotSharp assembly
belonging to the selected Godot executable. Only non-enum value types in the Godot namespace
are recorded, together with the argument ranges accepted by their public constructors.
Public instance properties and fields are indexed by receiver type, canonical identity, and
result type. This lets semantic binding resolve a chain one member at a time: Node3D.position
produces Vector3, which then makes Vector3.x resolvable. Name normalization is confined to a
known receiver type and is never applied as source-text replacement.

Public instance methods are indexed under every concrete receiver, including inherited
methods. The semantic pass can therefore bind Node3D.add_child to Node.AddChild only after
the receiver is known, while unresolved calls on user types retain their original structure.

The same assembly pass indexes public static methods on Godot's globally visible API owners.
A GDScript call such as range(...) and C# call GD.Range(...) resolve to one declaring-type and
method identity in the model. Argument-count bounds reject incompatible overloads, while user
methods are excluded by SemanticNormalizer before catalog lookup. Only exceptional spellings
that reflection cannot derive, such as typed math suffixes, remain explicit mappings.

The executable supplied through --godot has priority. Otherwise discovery mirrors the old
transpiler: find a Mono-enabled executable below C:\Godot, prefer its console variant, and
look for GodotSharp/Api/Debug/GodotSharp.dll beside it. Release and project-local assemblies
are fallbacks. All loading and reflection are synchronous. A small deterministic catalog is
available for context-free Transpiler usage and unit tests; the CLI always loads the actual
Godot API catalog.
*/
public sealed class GodotTypeCatalog
{
    private sealed record Constructor(
        string TypeName,
        int RequiredArgumentCount,
        int MaximumArgumentCount,
        bool HasParameterArray);

    private sealed record Member(
        string ReceiverType,
        string DeclaringType,
        string Name,
        string GdScriptName,
        string CSharpName,
        TypeReference ResultType);

    private sealed record GlobalFunction(
        string DeclaringType,
        string Name,
        string GdScriptName,
        string CSharpName,
        int RequiredArgumentCount,
        int MaximumArgumentCount,
        bool HasParameterArray,
        TypeReference ResultType);

    private sealed record InstanceMethod(
        string ReceiverType,
        string DeclaringType,
        string Name,
        string GdScriptName,
        string CSharpName,
        int RequiredArgumentCount,
        int MaximumArgumentCount,
        bool HasParameterArray,
        TypeReference ResultType);

    private readonly List<Constructor> constructors;
    private GodotEnumCatalog enums = GodotEnumCatalog.CreateKnownBuiltIns();
    // Finds a property or field by concrete receiver type and GDScript name.
    private readonly Dictionary<string, Member> gdScriptMembers;
    // Finds a property or field by concrete receiver type and C# name.
    private readonly Dictionary<string, Member> cSharpMembers;
    // Finds member emission metadata by reflected declaring type and canonical name.
    private readonly Dictionary<string, Member> membersByIdentity;
    // Finds compatible global overloads by their GDScript-visible name.
    private readonly Dictionary<string, List<GlobalFunction>> gdScriptGlobalFunctions;
    // Finds compatible global overloads by their qualified C# name.
    private readonly Dictionary<string, List<GlobalFunction>> cSharpGlobalFunctions;
    // Finds global-function emission metadata by declaring type and method name.
    private readonly Dictionary<string, GlobalFunction> globalFunctionsByIdentity;
    // Finds compatible instance overloads by receiver type and GDScript method name.
    private readonly Dictionary<string, List<InstanceMethod>> gdScriptMethods;
    // Finds compatible instance overloads by receiver type and C# method name.
    private readonly Dictionary<string, List<InstanceMethod>> cSharpMethods;
    // Finds instance-method emission metadata by declaring type and method name.
    private readonly Dictionary<string, InstanceMethod> methodsByIdentity;

    private GodotTypeCatalog(
        List<Constructor> constructors,
        List<Member> members,
        List<GlobalFunction> globalFunctions,
        List<InstanceMethod> methods)
    {
        this.constructors = constructors;
        gdScriptMembers = new Dictionary<string, Member>(StringComparer.Ordinal);
        cSharpMembers = new Dictionary<string, Member>(StringComparer.Ordinal);
        membersByIdentity = new Dictionary<string, Member>(StringComparer.Ordinal);
        gdScriptGlobalFunctions = new Dictionary<string, List<GlobalFunction>>(StringComparer.Ordinal);
        cSharpGlobalFunctions = new Dictionary<string, List<GlobalFunction>>(StringComparer.Ordinal);
        globalFunctionsByIdentity = new Dictionary<string, GlobalFunction>(StringComparer.Ordinal);
        gdScriptMethods = new Dictionary<string, List<InstanceMethod>>(StringComparer.Ordinal);
        cSharpMethods = new Dictionary<string, List<InstanceMethod>>(StringComparer.Ordinal);
        methodsByIdentity = new Dictionary<string, InstanceMethod>(StringComparer.Ordinal);
        foreach (var member in members)
        {
            gdScriptMembers.TryAdd(MemberKey(member.ReceiverType, member.GdScriptName), member);
            cSharpMembers.TryAdd(MemberKey(member.ReceiverType, member.CSharpName), member);
            membersByIdentity.TryAdd(MemberKey(member.DeclaringType, member.Name), member);
        }
        foreach (var function in globalFunctions)
        {
            AddFunctionLookup(gdScriptGlobalFunctions, function.GdScriptName, function);
            AddFunctionLookup(cSharpGlobalFunctions, function.DeclaringType + "." + function.CSharpName, function);
            globalFunctionsByIdentity.TryAdd(MemberKey(function.DeclaringType, function.Name), function);
        }
        foreach (var method in methods)
        {
            AddMethodLookup(gdScriptMethods, method.ReceiverType, method.GdScriptName, method);
            AddMethodLookup(cSharpMethods, method.ReceiverType, method.CSharpName, method);
            methodsByIdentity.TryAdd(MemberKey(method.DeclaringType, method.Name), method);
        }
    }

    // Supplies stable support for the representative built-in values used without a project.
    public static GodotTypeCatalog CreateKnownBuiltIns() => new(
        [
            new("Vector2", 0, 0, false),
            new("Vector2", 2, 2, false),
            new("Vector3", 0, 0, false),
            new("Vector3", 3, 3, false),
            new("Vector4", 0, 0, false),
            new("Vector4", 4, 4, false),
            new("Color", 0, 0, false),
            new("Color", 3, 3, false),
            new("Color", 4, 4, false),
            new("Quaternion", 0, 0, false),
            new("Quaternion", 4, 4, false)
        ],
        [
            KnownMember("Node3D", "Position", "Vector3"),
            KnownMember("Node3D", "GlobalPosition", "Vector3"),
            KnownMember("Vector2", "X", "float"),
            KnownMember("Vector2", "Y", "float"),
            KnownMember("Vector3", "X", "float"),
            KnownMember("Vector3", "Y", "float"),
            KnownMember("Vector3", "Z", "float"),
            KnownMember("Vector4", "X", "float"),
            KnownMember("Vector4", "Y", "float"),
            KnownMember("Vector4", "Z", "float"),
            KnownMember("Vector4", "W", "float"),
            KnownMember("Color", "R", "float"),
            KnownMember("Color", "G", "float"),
            KnownMember("Color", "B", "float"),
            KnownMember("Color", "A", "float"),
            KnownMember("Quaternion", "X", "float"),
            KnownMember("Quaternion", "Y", "float"),
            KnownMember("Quaternion", "Z", "float"),
            KnownMember("Quaternion", "W", "float")
        ],
        [
            KnownGlobalFunction("GD", "Range", "range", 1, 1, "Array", "int"),
            KnownGlobalFunction("GD", "Range", "range", 2, 3, "Array", "int"),
            KnownGlobalFunction("GD", "Load", "load", 1, 1, "Resource")
        ],
        [
            KnownMethod("Node3D", "Node", "AddChild", "add_child", 1, 3, "void"),
            KnownMethod("Node3D", "GodotObject", "Tr", "tr", 1, 2, "string"),
            KnownMethod("RefCounted", "GodotObject", "Tr", "tr", 1, 2, "string")
        ]);

    // Loads constructor metadata from the Godot installation selected for this project.
    public static GodotTypeCatalog Load(string projectPath, string? godotPath)
    {
        var executablePath = godotPath is null ? FindGodot() : Path.GetFullPath(godotPath);
        if (!File.Exists(executablePath))
            throw new FileNotFoundException("The Godot executable was not found.", executablePath);

        var assemblyPath = FindGodotSharp(projectPath, executablePath);
        if (assemblyPath is null)
            throw new FileNotFoundException("GodotSharp.dll was not found beside Godot or inside the project.");
        var assembly = Assembly.LoadFrom(assemblyPath);
        var catalog = FromAssembly(assembly);
        catalog.enums = GodotEnumCatalog.Load(assembly, executablePath);
        return catalog;
    }

    public bool TryResolveEnum(string name, ScriptLanguage language, out GodotEnumReference reference) =>
        enums.TryResolve(name, language, out reference);

    public string EnumName(GodotEnumReference reference, ScriptLanguage language) =>
        enums.Name(reference, language);

    public bool IsBuiltInValueConstructor(string typeName, int argumentCount)
    {
        foreach (var constructor in constructors)
        {
            if (constructor.TypeName == typeName &&
                argumentCount >= constructor.RequiredArgumentCount &&
                (constructor.HasParameterArray || argumentCount <= constructor.MaximumArgumentCount))
            {
                return true;
            }
        }
        return false;
    }

    // Resolves only exact members of a known receiver, preventing global casing guesses.
    public bool TryResolveMember(
        string receiverType,
        string sourceName,
        ScriptLanguage language,
        out GodotMemberReference reference)
    {
        var source = language == ScriptLanguage.GdScript
            ? gdScriptMembers
            : cSharpMembers;
        if (source.TryGetValue(MemberKey(receiverType, sourceName), out var member))
        {
            reference = new GodotMemberReference(
                member.DeclaringType,
                member.Name,
                member.ResultType);
            return true;
        }
        reference = null!;
        return false;
    }

    // Converts a bound semantic identity back to the requested language spelling.
    public string MemberName(GodotMemberReference reference, ScriptLanguage language)
    {
        if (!membersByIdentity.TryGetValue(
                MemberKey(reference.DeclaringType, reference.Name),
                out var member))
        {
            throw new InvalidOperationException(
                $"Godot member '{reference.DeclaringType}.{reference.Name}' is not cataloged.");
        }
        return language == ScriptLanguage.GdScript
            ? member.GdScriptName
            : member.CSharpName;
    }

    // Resolves a global call only when its reflected overload accepts the supplied argument count.
    public bool TryResolveGlobalFunction(
        string sourceName,
        ScriptLanguage language,
        int argumentCount,
        out GodotGlobalFunctionReference reference)
    {
        var source = language == ScriptLanguage.GdScript
            ? gdScriptGlobalFunctions
            : cSharpGlobalFunctions;
        if (source.TryGetValue(sourceName, out var candidates))
        {
            var function = candidates.FirstOrDefault(candidate =>
                argumentCount >= candidate.RequiredArgumentCount &&
                (candidate.HasParameterArray || argumentCount <= candidate.MaximumArgumentCount));
            if (function is not null)
            {
                reference = new GodotGlobalFunctionReference(
                    function.DeclaringType,
                    function.Name,
                    function.ResultType);
                return true;
            }
        }
        reference = null!;
        return false;
    }

    // Emits a resolved global identity using the requested language's callable spelling.
    public string GlobalFunctionName(
        GodotGlobalFunctionReference reference,
        ScriptLanguage language)
    {
        if (!globalFunctionsByIdentity.TryGetValue(
                MemberKey(reference.DeclaringType, reference.Name),
                out var function))
        {
            throw new InvalidOperationException(
                $"Godot global function '{reference.DeclaringType}.{reference.Name}' is not cataloged.");
        }
        return language == ScriptLanguage.GdScript
            ? function.GdScriptName
            : function.DeclaringType + "." + function.CSharpName;
    }

    // Resolves an instance method only after the semantic pass has bound its receiver type.
    public bool TryResolveMethod(
        string receiverType,
        string sourceName,
        ScriptLanguage language,
        int argumentCount,
        out GodotMethodReference reference)
    {
        var source = language == ScriptLanguage.GdScript
            ? gdScriptMethods
            : cSharpMethods;
        if (source.TryGetValue(MemberKey(receiverType, sourceName), out var candidates))
        {
            var method = candidates.FirstOrDefault(candidate =>
                argumentCount >= candidate.RequiredArgumentCount &&
                (candidate.HasParameterArray || argumentCount <= candidate.MaximumArgumentCount));
            if (method is not null)
            {
                reference = new GodotMethodReference(
                    method.DeclaringType,
                    method.Name,
                    method.ResultType);
                return true;
            }
        }
        reference = null!;
        return false;
    }

    // Converts a bound method identity back to the requested language spelling.
    public string MethodName(GodotMethodReference reference, ScriptLanguage language)
    {
        if (!methodsByIdentity.TryGetValue(
                MemberKey(reference.DeclaringType, reference.Name),
                out var method))
        {
            throw new InvalidOperationException(
                $"Godot method '{reference.DeclaringType}.{reference.Name}' is not cataloged.");
        }
        return language == ScriptLanguage.GdScript
            ? method.GdScriptName
            : method.CSharpName;
    }

    // Records every callable public constructor without loading Godot types into the model.
    private static GodotTypeCatalog FromAssembly(Assembly assembly)
    {
        var constructors = new List<Constructor>();
        var members = new List<Member>();
        var globalFunctions = new List<GlobalFunction>();
        var methods = new List<InstanceMethod>();
        var memberKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in SafeGetTypes(assembly))
        {
            if (type?.Namespace != "Godot" || type.IsEnum)
                continue;

            IndexMembers(type, members, memberKeys);
            IndexMethods(type, methods);
            if (type.Name is "GD" or "Mathf" or "GodotObject")
                IndexGlobalFunctions(type, globalFunctions);
            if (!type.IsValueType)
                continue;

            // Value types always have a valid default construction even if reflection omits it.
            constructors.Add(new Constructor(type.Name, 0, 0, false));
            foreach (var constructor in type.GetConstructors(BindingFlags.Instance | BindingFlags.Public))
            {
                var parameters = constructor.GetParameters();
                var requiredCount = parameters.Count(parameter =>
                    !parameter.IsOptional && parameter.GetCustomAttribute<ParamArrayAttribute>() is null);
                var hasParameterArray = parameters.LastOrDefault()?.GetCustomAttribute<ParamArrayAttribute>() is not null;
                constructors.Add(new Constructor(
                    type.Name,
                    requiredCount,
                    parameters.Length,
                    hasParameterArray));
            }
        }
        return new GodotTypeCatalog(constructors, members, globalFunctions, methods);
    }

    // Records inherited public methods under each concrete Godot receiver type.
    private static void IndexMethods(Type receiverType, List<InstanceMethod> methods)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
        foreach (var method in receiverType.GetMethods(flags).Where(method => !method.IsSpecialName))
        {
            var parameters = method.GetParameters();
            var requiredCount = parameters.Count(parameter =>
                !parameter.IsOptional && parameter.GetCustomAttribute<ParamArrayAttribute>() is null);
            var hasParameterArray = parameters.LastOrDefault()?.GetCustomAttribute<ParamArrayAttribute>() is not null;
            methods.Add(new InstanceMethod(
                receiverType.Name,
                method.DeclaringType?.Name ?? receiverType.Name,
                method.Name,
                ToSnakeCase(method.Name),
                method.Name,
                requiredCount,
                parameters.Length,
                hasParameterArray,
                ToTypeReference(method.ReturnType)));
        }
    }

    // Indexes only Godot API owners whose static methods are globally visible in GDScript.
    private static void IndexGlobalFunctions(Type type, List<GlobalFunction> functions)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public;
        foreach (var method in type.GetMethods(flags).Where(method => !method.IsSpecialName))
        {
            var parameters = method.GetParameters();
            var requiredCount = parameters.Count(parameter =>
                !parameter.IsOptional && parameter.GetCustomAttribute<ParamArrayAttribute>() is null);
            var hasParameterArray = parameters.LastOrDefault()?.GetCustomAttribute<ParamArrayAttribute>() is not null;
            functions.Add(new GlobalFunction(
                type.Name,
                method.Name,
                ToSnakeCase(method.Name),
                method.Name,
                requiredCount,
                parameters.Length,
                hasParameterArray,
                ToTypeReference(method.ReturnType)));
        }
    }

    // Indexes inherited members under the concrete receiver while retaining their declaration identity.
    private static void IndexMembers(
        Type receiverType,
        List<Member> members,
        HashSet<string> memberKeys)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
        foreach (var property in receiverType.GetProperties(flags))
        {
            if (property.GetIndexParameters().Length == 0)
                AddMember(
                    receiverType,
                    property.DeclaringType,
                    property.Name,
                    property.PropertyType,
                    members,
                    memberKeys);
        }
        foreach (var field in receiverType.GetFields(flags))
        {
            AddMember(
                receiverType,
                field.DeclaringType,
                field.Name,
                field.FieldType,
                members,
                memberKeys);
        }
    }

    private static void AddMember(
        Type receiverType,
        Type? declaringType,
        string cSharpName,
        Type resultType,
        List<Member> members,
        HashSet<string> memberKeys)
    {
        var name = NormalizeName(cSharpName);
        if (!memberKeys.Add(MemberKey(receiverType.Name, name)))
            return;
        members.Add(new Member(
            receiverType.Name,
            declaringType?.Name ?? receiverType.Name,
            name,
            ToSnakeCase(cSharpName),
            cSharpName,
            ToTypeReference(resultType)));
    }

    private static Member KnownMember(string receiverType, string cSharpName, string resultType) => new(
        receiverType,
        receiverType,
        NormalizeName(cSharpName),
        ToSnakeCase(cSharpName),
        cSharpName,
        new TypeReference(resultType));

    private static GlobalFunction KnownGlobalFunction(
        string declaringType,
        string cSharpName,
        string gdScriptName,
        int requiredArgumentCount,
        int maximumArgumentCount,
        string resultType,
        string? elementType = null) => new(
            declaringType,
            cSharpName,
            gdScriptName,
            cSharpName,
            requiredArgumentCount,
            maximumArgumentCount,
            false,
            new TypeReference(resultType, elementType is null
                ? new List<TypeReference>()
                : new List<TypeReference> { new(elementType) }));

    private static InstanceMethod KnownMethod(
        string receiverType,
        string declaringType,
        string cSharpName,
        string gdScriptName,
        int requiredArgumentCount,
        int maximumArgumentCount,
        string resultType) => new(
            receiverType,
            declaringType,
            cSharpName,
            gdScriptName,
            cSharpName,
            requiredArgumentCount,
            maximumArgumentCount,
            false,
            new TypeReference(resultType));

    private static void AddFunctionLookup(
        Dictionary<string, List<GlobalFunction>> lookup,
        string name,
        GlobalFunction function)
    {
        if (!lookup.TryGetValue(name, out var functions))
        {
            functions = new List<GlobalFunction>();
            lookup.Add(name, functions);
        }
        functions.Add(function);
    }

    private static void AddMethodLookup(
        Dictionary<string, List<InstanceMethod>> lookup,
        string receiverType,
        string name,
        InstanceMethod method)
    {
        var key = MemberKey(receiverType, name);
        if (!lookup.TryGetValue(key, out var methods))
        {
            methods = new List<InstanceMethod>();
            lookup.Add(key, methods);
        }
        methods.Add(method);
    }

    // Converts reflected CLR type names into the same canonical spellings used by both parsers.
    private static TypeReference ToTypeReference(Type type)
    {
        if (type.IsArray)
        {
            return new TypeReference(
                "Array",
                new List<TypeReference> { ToTypeReference(type.GetElementType()!) });
        }
        var name = type.Name switch
        {
            "Boolean" => "bool",
            "Int32" => "int",
            "Int64" => "int64",
            "Single" => "float",
            "Double" => "float64",
            "String" => "string",
            _ => type.Name.Split('`')[0]
        };
        if (name is "Array" or "Dictionary" && type.Namespace == "Godot.Collections")
            name = type.Name.Split('`')[0];
        var arguments = type.IsGenericType
            ? type.GetGenericArguments().Select(ToTypeReference).ToList()
            : new List<TypeReference>();
        return new TypeReference(name, arguments);
    }

    private static string NormalizeName(string name) =>
        new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string MemberKey(string typeName, string memberName) =>
        typeName + "\0" + memberName;

    // Handles word and acronym boundaries while producing Godot's lower snake-case convention.
    private static string ToSnakeCase(string name)
    {
        var result = new StringBuilder();
        for (var index = 0; index < name.Length; index++)
        {
            var current = name[index];
            var startsWord = index > 0 && char.IsUpper(current) &&
                (char.IsLower(name[index - 1]) ||
                 char.IsDigit(name[index - 1]) ||
                 index + 1 < name.Length && char.IsLower(name[index + 1]));
            if (startsWord)
                result.Append('_');
            result.Append(char.ToLowerInvariant(current));
        }
        return result.ToString();
    }

    // Reflection may return a partial type list when an unrelated optional dependency is absent.
    private static List<Type?> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes().Cast<Type?>().ToList();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.ToList();
        }
    }

    // Uses the old transpiler's convention for locating a Mono-enabled Godot installation.
    private static string FindGodot()
    {
        const string godotDirectory = @"C:\Godot";
        if (Directory.Exists(godotDirectory))
        {
            var candidate = Directory.EnumerateFiles(godotDirectory, "*.exe", SearchOption.AllDirectories)
                .Where(path => Path.GetFileNameWithoutExtension(path)
                    .Contains("mono", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => Path.GetFileName(path)
                    .Contains("console", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (candidate is not null)
                return candidate;
        }
        throw new FileNotFoundException(
            "A Mono-enabled Godot executable was not found under C:\\Godot. Pass --godot <path>.");
    }

    // GodotSharp ships next to the executable; project search supports copied build artifacts.
    private static string? FindGodotSharp(string projectPath, string godotPath)
    {
        var godotDirectory = Path.GetDirectoryName(godotPath) ?? "";
        var debug = Path.Combine(godotDirectory, "GodotSharp", "Api", "Debug", "GodotSharp.dll");
        if (File.Exists(debug))
            return debug;
        var release = Path.Combine(godotDirectory, "GodotSharp", "Api", "Release", "GodotSharp.dll");
        if (File.Exists(release))
            return release;
        return Directory.Exists(projectPath)
            ? Directory.EnumerateFiles(projectPath, "GodotSharp.dll", SearchOption.AllDirectories).FirstOrDefault()
            : null;
    }
}
