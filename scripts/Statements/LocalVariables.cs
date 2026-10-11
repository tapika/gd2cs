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
		localValues.Clear();
		localGroups.Clear();
	}
}
