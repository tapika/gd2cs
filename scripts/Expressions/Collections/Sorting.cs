using Godot;
using System;

[GlobalClass]
public partial class Sorting : RefCounted
{
	public Godot.Collections.Array<int> Values = new Godot.Collections.Array<int>();
	public Godot.Collections.Array<Godot.Collections.Dictionary<string, int>> Records = new Godot.Collections.Array<Godot.Collections.Dictionary<string, int>>();

	public void reorder()
	{
		Values.Sort();
		Values.Reverse();
		Values.Shuffle();
		gdSortCustom(Values, before);
		gdSortCustom(Values, (int first, int second) => first > second);
		gdSortCustom(Values,
			before
		);
		gdSortCustom(Values, (int first, int second) =>
		{
			// Keep a multiline predicate body in the model.
			return first < second;
		}
		);
		Godot.Collections.Dictionary<string, int> record = new Godot.Collections.Dictionary<string, int> {{ "priority", 2 }};
		Records.Add(record);
		gdSortCustom(Records, beforeRecord);
		gdSortCustom(Records, (Godot.Collections.Dictionary<string, int> first, Godot.Collections.Dictionary<string, int> second) => first["priority"] > second["priority"]);
	}

	public bool before(int first, int second)
	{
		return first < second;
	}

	public bool beforeRecord(Godot.Collections.Dictionary<string, int> first, Godot.Collections.Dictionary<string, int> second)
	{
		return first["priority"] < second["priority"];
	}

	private static void gdSortCustom<T>(System.Collections.Generic.IList<T> values, Func<T, T, bool> before)
	{
		// Godot's C# Array API is missing GDScript's sort_custom function.
		// Adapt its strict Boolean ordering; equal-key order is not guaranteed.
		ArgumentNullException.ThrowIfNull(before);
		if (values.IsReadOnly) throw new InvalidOperationException("The array is read-only.");
		var sorted = new System.Collections.Generic.List<T>(values);
		sorted.Sort((first, second) => before(first, second) ? -1 : before(second, first) ? 1 : 0);
		// Write back into the same array so aliases still observe the sorted contents.
		for (var index = 0; index < sorted.Count; index++) values[index] = sorted[index];
	}
}
