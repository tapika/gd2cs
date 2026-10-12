using Godot;
using System;

[GlobalClass]
public partial class ScalarMembers : RefCounted
{
    // Count starts at three.
    public int Count = 3;

    // Value assigned later.
    public float PendingA;
    // Typed collection assigned later.
    public Godot.Collections.Array<int> IntegerValues = new Godot.Collections.Array<int>();
    public string TextValue;

    // Ratio has a multiline note.
    // It is intentionally generic.
    public float Ratio = 1.5f;

    public string Label = "sample";

    public float scale(float value)
    {
        return value * Ratio;
    }

    // Shared by all instances.
    public static int SharedCount = 4;
    public static string SharedLabel;
    public static Godot.Collections.Array<int> SharedValues = new Godot.Collections.Array<int>();

    public static void resetShared()
    {
        SharedCount = 0;
    }
}
