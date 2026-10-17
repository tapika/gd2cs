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
    public const int ValueF = -2 + 3;
    public static readonly Godot.Collections.Array<int> Values = gdArray(1, 2, 3);

    // Built-in value constructors are inferred through the Godot type catalog.
    public static readonly Color Tint = new Color(1.0f, 0.5f, 0.25f);
    public static readonly Vector2 Offset2 = new Vector2(1.0f, 2.0f);
    public static readonly Vector3 Offset3 = new Vector3(1.0f, 2.0f, 3.0f);
    public static readonly Vector3 Copy3 = Offset3;
    public static readonly Quaternion Rotation = new Quaternion(0.0f, 0.0f, 0.0f, 1.0f);
    public static readonly Aabb Bounds = new Aabb(new Vector3(0.0f, 0.0f, 0.0f), new Vector3(1.0f, 2.0f, 3.0f));

    public float component()
    {
        return Tint.R;
    }

    private static Godot.Collections.Array<T> gdArray<[MustBeVariant] T>(params T[] values)
    {
        var result = new Godot.Collections.Array<T>();
        foreach (var value in values) result.Add(value);
        return result;
    }
}
