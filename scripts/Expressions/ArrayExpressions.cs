using Godot;
using System;

[GlobalClass]
public partial class ArrayExpressions : RefCounted
{
	public partial class ElementValue : RefCounted
	{
		public int Number;
		public string Text;

		public ElementValue(int number, string text)
		{
			Number = number;
			Text = text;
		}
	}

	public Godot.Collections.Array<int> InlineValues = gdArray(1, 2,
		3);

	public Godot.Collections.Array<ElementValue> MultilineValues = gdArray(
		new ElementValue(10, "first"),
		new ElementValue(20, "second"),
		new ElementValue(30, "third")
	);

	public int inlineValue(int index)
	{
		return InlineValues[index];
	}

	public float inferredValue(int index)
	{
		var values = gdArray(4.0f, 5.0f, 6.0f);
		return values[index];
	}

	public ElementValue multilineValue(int index)
	{
		return MultilineValues[index];
	}

	private static Godot.Collections.Array<T> gdArray<[MustBeVariant] T>(params T[] values)
	{
		var result = new Godot.Collections.Array<T>();
		foreach (var value in values) result.Add(value);
		return result;
	}
}
