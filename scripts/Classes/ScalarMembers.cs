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
    public Godot.Collections.Array<int> IntegerValues;
    public string TextValue;

    // Ratio has a multiline note.
    // It is intentionally generic.
    public float Ratio = 1.5f;

    public string Label = "sample";
}
