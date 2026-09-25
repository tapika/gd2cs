using gd2cs.Model;

namespace gd2cs.Language;

// Defines dialect spellings at the language boundary; the model stores only semantic operations.
internal static class CollectionMappings
{
    private sealed record Mapping(
        TypeKind ReceiverKind,
        CollectionOperation Operation,
        string GdScriptMember,
        string CSharpMember,
        int ArgumentCount);

    private static readonly List<Mapping> mappings =
    [
        new(TypeKind.Array, CollectionOperation.Count, "size", "Count", 0),
        new(TypeKind.Dictionary, CollectionOperation.Count, "size", "Count", 0),
        new(TypeKind.Array, CollectionOperation.IsEmpty, "is_empty", "", 0),
        new(TypeKind.Dictionary, CollectionOperation.IsEmpty, "is_empty", "", 0),
        new(TypeKind.Array, CollectionOperation.Append, "append", "Add", 1),
        new(TypeKind.Array, CollectionOperation.Clear, "clear", "Clear", 0),
        new(TypeKind.Dictionary, CollectionOperation.Clear, "clear", "Clear", 0),
        new(TypeKind.Array, CollectionOperation.RemoveValue, "erase", "Remove", 1),
        new(TypeKind.Dictionary, CollectionOperation.RemoveKey, "erase", "Remove", 1),
        new(TypeKind.Array, CollectionOperation.ContainsValue, "has", "Contains", 1),
        new(TypeKind.Dictionary, CollectionOperation.ContainsKey, "has", "ContainsKey", 1)
    ];

    public static bool TryInvocation(
        ScriptLanguage language,
        TypeKind receiverKind,
        string member,
        int argumentCount,
        out CollectionOperation operation)
    {
        foreach (var mapping in mappings)
        {
            var sourceMember = language == ScriptLanguage.GdScript
                ? mapping.GdScriptMember
                : mapping.CSharpMember;
            if (mapping.ReceiverKind == receiverKind &&
                mapping.ArgumentCount == argumentCount &&
                sourceMember.Length > 0 &&
                sourceMember == member)
            {
                operation = mapping.Operation;
                return true;
            }
        }
        operation = default;
        return false;
    }

    public static bool TryCSharpProperty(
        TypeKind receiverKind,
        string member,
        out CollectionOperation operation)
    {
        foreach (var mapping in mappings)
        {
            if (mapping.ReceiverKind == receiverKind &&
                mapping.ArgumentCount == 0 &&
                mapping.CSharpMember == member &&
                mapping.Operation == CollectionOperation.Count)
            {
                operation = mapping.Operation;
                return true;
            }
        }
        operation = default;
        return false;
    }

    public static string GdScriptMember(CollectionOperation operation) =>
        mappings.First(mapping => mapping.Operation == operation).GdScriptMember;

    public static string CSharpMember(CollectionOperation operation) =>
        mappings.First(mapping => mapping.Operation == operation).CSharpMember;
}
