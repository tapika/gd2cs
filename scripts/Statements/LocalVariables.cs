using Godot;
using System;

[GlobalClass]
public partial class LocalVariables : RefCounted
{
	public partial class Element : RefCounted
	{
		public int Value;
	}

	public int evaluate(int value)
	{
		int pending = 0;
		Godot.Collections.Array<int> pendingValues = new Godot.Collections.Array<int>();
		pendingValues.Add(value);
		int first = value;
		var result = first;
		return result;
	}

	public void inspect(LocalVariables.Element value, Godot.Collections.Array<LocalVariables.Element> values, Godot.Collections.Dictionary<string, Godot.Collections.Array<LocalVariables.Element>> groups)
	{
		LocalVariables.Element selected = null;
		selected = value;
		Godot.Collections.Array<LocalVariables.Element> localValues = values;
		Godot.Collections.Dictionary<string, Godot.Collections.Array<LocalVariables.Element>> localGroups = groups;
		LocalVariables.Element item = values[0];
		LocalVariables.Element supplied = LocalVariables.getElements(values)[0];
		var inferred = firstValue();
		localValues.Clear();
		localGroups.Clear();
	}

	public int firstValue()
	{
		return 1;
	}

	public static Godot.Collections.Array<LocalVariables.Element> getElements(Godot.Collections.Array<LocalVariables.Element> values)
	{
		return values;
	}
}
