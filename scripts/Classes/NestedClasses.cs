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
	}
}
