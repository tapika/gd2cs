using Godot;
using System;

[GlobalClass]
public partial class CollectionOperations : RefCounted
{
	public partial class CustomValue : RefCounted
	{
		public int size()
		{
			return 4;
		}
	}

	public Godot.Collections.Array<int> Values = gdArray(1, 2, 3);
	public Godot.Collections.Dictionary<string, int> Entries;
	public CustomValue Custom;

	public void inspect()
	{
		int valueCount = Values.Count;
		int entryCount = Entries.Count;
		bool valuesEmpty = Values.Count == 0;
		bool entriesEmpty = Entries.Count == 0;
		bool valuesPresent = Values.Count != 0;

		var localValues = Values;
		localValues.Add(4);
		Values.Clear();
		Entries.Clear();
		Values.Remove(2);
		Entries.Remove("second");

		int customSize = Custom.size();
	}

	public bool contains(Godot.Collections.Array<int> values, Godot.Collections.Dictionary<string, int> entries)
	{
		return values.Contains(1) && entries.ContainsKey("first");
	}

	private static Godot.Collections.Array<T> gdArray<[MustBeVariant] T>(params T[] values)
	{
		var result = new Godot.Collections.Array<T>();
		foreach (var value in values) result.Add(value);
		return result;
	}
}
