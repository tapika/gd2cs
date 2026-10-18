using Godot;
using System;

[GlobalClass]
public partial class CollectionOperations : RefCounted
{
	public partial class CustomValue : RefCounted
	{
		public CustomValue getValues()
		{
			return this;
		}

		public CustomValue values()
		{
			return this;
		}

		public CustomValue keys()
		{
			return this;
		}

		public int size()
		{
			return 4;
		}

		public int remove_at(int index)
		{
			return index;
		}

		public int find(int value)
		{
			return value;
		}

		public int IndexOf(int value)
		{
			return value;
		}

		public static Godot.Collections.Array<int> readArray()
		{
			return gdArray(1);
		}
	}

	public Godot.Collections.Array<int> Values = gdArray(1, 2, 3);
	public Godot.Collections.Array<CustomValue> CustomValues = new Godot.Collections.Array<CustomValue>();
	public Godot.Collections.Dictionary<string, int> Entries = new Godot.Collections.Dictionary<string, int>();
	public CustomValue Custom;

	public void inspect()
	{
		int valueCount = Values.Count;
		int entryCount = Entries.Count;
		bool valuesEmpty = Values.Count == 0;
		bool entriesEmpty = Entries.Count == 0;
		bool valuesPresent = Values.Count != 0;
		int valueIndex = Values.IndexOf(2);

		var localValues = Values;
		var selectedValue = CustomValues[0];
		var createdValue = new CustomValue();
		localValues.Add(4);
		localValues.RemoveAt(0);
		Values.Clear();
		Entries.Clear();
		Values.Remove(2);
		Entries.Remove("second");

		int customSize = Custom.size();
		Custom.remove_at(0);
		Custom.find(1);
		Custom.IndexOf(1);
	}

	public int locate(Godot.Collections.Array<string> values, string value)
	{
		var first = values.IndexOf(value);
		return values.IndexOf(value, first + 1);
	}

	public int itemIndex(CustomValue value)
	{
		return CustomValues.IndexOf(value);
	}

	public void removeItem(Godot.Collections.Array<CustomValue> values, int index)
	{
		values.RemoveAt(index);
	}

	public bool contains(Godot.Collections.Array<int> values, Godot.Collections.Dictionary<string, int> entries)
	{
		return values.Contains(1) && entries.ContainsKey("first");
	}

	public bool literalContains(int value)
	{
		return gdArray(1, 2, 3).Contains(value);
	}

	public bool results()
	{
		var values = getValues();
		values.Add(4);
		var entries = this.getEntries();
		var count = getValues().Count;
		var empty = getEntries(1).Count == 0;
		var explicitEmpty = this.getEntries().Count == 0;
		var shared = CollectionOperations.staticValues();
		shared.Add(5);
		var value = getVariantEntries()["count"].As<int>();
		return entries.Count == 0 && values.Contains(4);
	}

	public int customResult(CustomValue source)
	{
		return (source.getValues() as CustomValue).size();
	}

	public void externalCollections()
	{
		Godot.Collections.Array<int> externalValues = CustomValue.readArray();
		var count = externalValues.Count;
		var castValues = CustomValue.readArray() as Godot.Collections.Array<int>;
		castValues.Add(count);
	}

	public void dictionaryArrays(Godot.Collections.Dictionary<int, Vector2> points)
	{
		var values = gdDictValues(points);
		var keys = gdDictKeys(points);
		var first = values[0];
		var component = first.X;
		var key = keys[0];
		var count = gdDictKeys(points).Count;
		var distance = gdDictValues(points)[0].DistanceTo(gdDictValues(points)[1]);
		foreach (var point in gdDictValues(points))
		{
			component = point.Y;
		}
		var names = gdDictKeys(Entries);
		var valueKey = Entries["values"];
		var keyKey = Entries["keys"];
		var returned = gdDictValues(getEntries());
		var custom = (Custom.values() as CustomValue).keys();
	}

	public Godot.Collections.Array<string> entryKeys()
	{
		return gdDictKeys(Entries);
	}

	public Godot.Collections.Array<int> entryValues()
	{
		return gdDictValues(Entries);
	}

	public static int staticCount()
	{
		return staticValues().Count;
	}

	// Providers follow their callers so result binding cannot depend on declaration order.
	public Godot.Collections.Array<int> getValues()
	{
		return Values;
	}

	public Godot.Collections.Dictionary<string, int> getEntries(int limit = 0)
	{
		return Entries;
	}

	public Godot.Collections.Dictionary<string, Variant> getVariantEntries()
	{
		return new Godot.Collections.Dictionary<string, Variant> {{ "count", 1 }};
	}

	public static Godot.Collections.Array<int> staticValues()
	{
		return gdArray(6);
	}

	private static Godot.Collections.Array<T> gdArray<[MustBeVariant] T>(params T[] values)
	{
		var result = new Godot.Collections.Array<T>();
		foreach (var value in values) result.Add(value);
		return result;
	}

	private static Godot.Collections.Array<TKey> gdDictKeys<[MustBeVariant] TKey, [MustBeVariant] TValue>(Godot.Collections.Dictionary<TKey, TValue> dictionary)
	{
		// Godot internally returns Array<T> from Keys/Values, exposed as ICollection<T>.
		// https://github.com/godotengine/godot/blob/master/modules/mono/glue/GodotSharp/GodotSharp/Core/Dictionary.cs
		return (Godot.Collections.Array<TKey>)dictionary.Keys;
	}

	private static Godot.Collections.Array<TValue> gdDictValues<[MustBeVariant] TKey, [MustBeVariant] TValue>(Godot.Collections.Dictionary<TKey, TValue> dictionary)
	{
		// Godot internally returns Array<T> from Keys/Values, exposed as ICollection<T>.
		// https://github.com/godotengine/godot/blob/master/modules/mono/glue/GodotSharp/GodotSharp/Core/Dictionary.cs
		return (Godot.Collections.Array<TValue>)dictionary.Values;
	}
}
