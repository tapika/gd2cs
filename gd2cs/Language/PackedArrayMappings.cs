using gd2cs.Model;

namespace gd2cs.Language;

internal static class PackedArrayMappings
{
    // These packed-array classes were not ported to C#: Godot's managed API uses T[] instead.
    // Keep mutable Godot Array<T> storage separate from the copied buffer passed to that API.
    private static readonly Dictionary<string, string> elements = new(StringComparer.Ordinal)
    {
        ["PackedVector2Array"] = "Vector2",
        ["PackedVector3Array"] = "Vector3",
        ["PackedColorArray"] = "Color"
    };

    public static bool TryElement(string name, out string element) => elements.TryGetValue(name, out element!);

    public static string NativeName(TypeReference element) =>
        elements.Single(mapping => mapping.Value == element.Name).Key;

    public static bool Supports(TypeReference element) => elements.ContainsValue(element.Name);

    public static string StorageError(string name) =>
        $"{name} is not supported as collection storage: Godot's packed-array classes were not ported to C#. "
        + $"Use Array[{elements[name]}] and append there; use {name}(values) only to convert an existing typed array for a Godot API.";
}
