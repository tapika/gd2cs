using Godot;
using System;

[GlobalClass]
public partial class DictionaryArrayValues : RefCounted
{
	// The hint supplies the element type of each array stored as a dictionary value.
	public Godot.Collections.Dictionary<Vector2, Godot.Collections.Array<float>> Groups = new Godot.Collections.Dictionary<Vector2, Godot.Collections.Array<float>>();
	public Godot.Collections.Dictionary<string, Godot.Collections.Array<int>> Counts = new Godot.Collections.Dictionary<string, Godot.Collections.Array<int>>();

	public void add(Vector2 key, float value)
	{
		if (!Groups.ContainsKey(key))
		{
			Godot.Collections.Array<float> values = new Godot.Collections.Array<float>();
			Groups[key] = values;
		}
		Groups[key].Add(value);
	}

	public void copy()
	{
		Godot.Collections.Dictionary<string, Godot.Collections.Array<int>> pending = new Godot.Collections.Dictionary<string, Godot.Collections.Array<int>>();
		pending = Counts;
	}

	public float first(Callable callback, Vector2 key)
	{
		var groups = callback.Call().As<Godot.Collections.Dictionary<Vector2, Godot.Collections.Array<float>>>();
		return groups[key][0];
	}

	public Godot.Collections.Dictionary<Vector2, Godot.Collections.Array<float>> groups()
	{
		return Groups;
	}
}
