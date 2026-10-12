using Godot;
using System;

[GlobalClass]
public partial class DictionaryExpressions : RefCounted
{
	public static readonly Godot.Collections.Dictionary Defaults = new Godot.Collections.Dictionary {{ "enabled", true }};

	public Godot.Collections.Dictionary Empty = new Godot.Collections.Dictionary {};
	public Godot.Collections.Dictionary Inline = new Godot.Collections.Dictionary {{ "count", 2 }, { "label", "value" }};
	public Godot.Collections.Array<Godot.Collections.Dictionary> Values;

	public void record(Node3D node, Vector3 size)
	{
		Values.Add(new Godot.Collections.Dictionary {
			{ "node", node },
			{ "size", size },
		});
	}

	public void inspect()
	{
		var compact = new Godot.Collections.Dictionary {{ "nested", new Godot.Collections.Dictionary {{ "enabled", true }} }, { "offset", new Vector3(1.0f, 2.0f, 3.0f) }};
		var mixed = new Godot.Collections.Dictionary {{ "first", 1 },
			{ "second", 2 }, { "third", 3 },

			{ "last", 4 }
		};
		var empty = new Godot.Collections.Dictionary {

		};
	}
}
