namespace Godot;

// Metadata-only stand-ins exercise production reflection without requiring Godot in CI.
[AttributeUsage(AttributeTargets.Class)]
public sealed class GodotClassNameAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

[GodotClassName("NativeApi")]
public sealed class NativeApiInstance
{
    public bool Enabled { get; set; }
}

// The wrapper intentionally has a different name: its Singleton return type is the evidence.
public static class ManagedApi
{
    public static NativeApiInstance Singleton => throw new InvalidOperationException("Metadata only.");
    public static bool Enabled
    {
        get => throw new InvalidOperationException("Metadata only.");
        set => throw new InvalidOperationException("Metadata only.");
    }
}

public static class StaticApiDecoy
{
    public static bool Enabled
    {
        get => throw new InvalidOperationException("Metadata only.");
        set => throw new InvalidOperationException("Metadata only.");
    }
}
