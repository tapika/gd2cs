using Godot;
using System;

[GlobalClass]
public partial class NestedClasses : RefCounted
{
	// Nested type comment.
	// It has two comment lines.
	public partial class AABB : RefCounted
	{
		public const int Limit = 7;
		public int Count = 2;
		public string Label = "nested";
		public static int SharedCount = 3;
		public Godot.Collections.Array<int> Values = new Godot.Collections.Array<int>();
	}

	public Godot.Collections.Array<NestedClasses.AABB> Values = new Godot.Collections.Array<NestedClasses.AABB>();

	public NestedClasses.AABB identity(NestedClasses.AABB value)
	{
		return value;
	}

	// User types must not be mistaken for the Godot built-in with the same spelling.
	public AABB create()
	{
		return new AABB();
	}
}
