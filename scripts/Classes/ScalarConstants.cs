using Godot;
using System;

[GlobalClass]
public partial class ScalarConstants : RefCounted
{
    // First scalar value.
    public const float ValueA = 0.001f;
    // Second scalar value.
    // This comment spans two lines.
    public const float ValueB = 3.0f;
    public const int ValueC = 42;
    public const bool ValueD = true;
    public const string ValueE = "text";
    public static readonly Godot.Collections.Array<int> Values = gdArray(1, 2, 3);

    private static Godot.Collections.Array<T> gdArray<[MustBeVariant] T>(params T[] values)
    {
        var result = new Godot.Collections.Array<T>();
        foreach (var value in values) result.Add(value);
        return result;
    }
}
