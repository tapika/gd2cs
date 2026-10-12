using Godot;
using System;

[GlobalClass]
public partial class NestedClasses : RefCounted
{
	// Nested type comment.
	// It has two comment lines.
	public partial class InnerType : RefCounted
	{
		public const int Limit = 7;
		public int Count = 2;
		public string Label = "nested";
		public static int SharedCount = 3;
		public Godot.Collections.Array<int> Values = new Godot.Collections.Array<int>();
	}

	public Godot.Collections.Array<NestedClasses.InnerType> Values = new Godot.Collections.Array<NestedClasses.InnerType>();

	public NestedClasses.InnerType identity(NestedClasses.InnerType value)
	{
		return value;
	}
}
