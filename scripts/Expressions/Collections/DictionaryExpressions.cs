using Godot;
using System;

[GlobalClass]
public partial class DictionaryExpressions : RefCounted
{
	public partial class Item : RefCounted
	{
		public int Count;
	}

	public static readonly Godot.Collections.Dictionary<string, bool> Defaults = new Godot.Collections.Dictionary<string, bool> {{ "enabled", true }};

	public Godot.Collections.Dictionary<string, int> Empty = new Godot.Collections.Dictionary<string, int> {};
	public Godot.Collections.Dictionary<string, Variant> Inline = new Godot.Collections.Dictionary<string, Variant> {{ "count", 2 }, { "label", "value" }};
	public Godot.Collections.Dictionary<string, Node3D> Nodes = new Godot.Collections.Dictionary<string, Node3D>();
	public Godot.Collections.Array<Godot.Collections.Dictionary<string, Node3D>> ObjectEntries = new Godot.Collections.Array<Godot.Collections.Dictionary<string, Node3D>>();
	public Godot.Collections.Array<Godot.Collections.Dictionary<string, Variant>> MixedEntries = new Godot.Collections.Array<Godot.Collections.Dictionary<string, Variant>>();
	public Godot.Collections.Array<Godot.Collections.Dictionary<string, DictionaryExpressions.Item>> CustomEntries = new Godot.Collections.Array<Godot.Collections.Dictionary<string, DictionaryExpressions.Item>>();
	public Godot.Collections.Array<DictionaryExpressions.Item> Items = new Godot.Collections.Array<DictionaryExpressions.Item>();

	public void record(Node3D node, Vector3 size)
	{
		Godot.Collections.Dictionary<string, Variant> entry = new Godot.Collections.Dictionary<string, Variant> {
			{ "node", node },
			{ "size", size },
		};
	}

	public void inspect()
	{
		Godot.Collections.Array<DictionaryExpressions.Item> pendingItems = new Godot.Collections.Array<DictionaryExpressions.Item>();
		Godot.Collections.Array<Godot.Collections.Dictionary<string, Node3D>> pending = new Godot.Collections.Array<Godot.Collections.Dictionary<string, Node3D>>();
		Godot.Collections.Dictionary<string, int> pendingCounts = new Godot.Collections.Dictionary<string, int>();
		Godot.Collections.Dictionary<string, Variant> compact = new Godot.Collections.Dictionary<string, Variant> {{ "enabled", true }, { "offset", new Vector3(1.0f, 2.0f, 3.0f) }};
		Godot.Collections.Dictionary<string, Variant> nested = new Godot.Collections.Dictionary<string, Variant> {{ "entry", compact }};
		Godot.Collections.Dictionary<string, int> mixed = new Godot.Collections.Dictionary<string, int> {{ "first", 1 },
			{ "second", 2 }, { "third", 3 },

			{ "last", 4 }
		};
		Godot.Collections.Dictionary<string, int> empty = new Godot.Collections.Dictionary<string, int> {

		};
	}

	public Node parentOf(Godot.Collections.Dictionary<string, Variant> entry)
	{
		var item = entry["node"].AsGodotObject() as Node;
		if (GodotObject.IsInstanceValid(item))
		{
			return item.GetParent();
		}
		return null;
	}

	public Vector3 offsetOf(Godot.Collections.Dictionary<string, Variant> entry)
	{
		return entry["offset"].As<Vector3>();
	}

	public Node inlineParent(Godot.Collections.Dictionary<string, Variant> entry)
	{
		return (entry["node"].AsGodotObject() as Node).GetParent();
	}

	public bool valid(Godot.Collections.Dictionary<string, Variant> entry)
	{
		return GodotObject.IsInstanceValid(entry["node"].AsGodotObject() as Node);
	}

	public Node typedNode(Godot.Collections.Dictionary<string, Node> entry)
	{
		return entry["node"];
	}

	public int countOf(Godot.Collections.Dictionary<string, int> entry)
	{
		return entry["count"];
	}

	public void store(Godot.Collections.Dictionary<string, Variant> entry, Node node)
	{
		entry["node"] = node;
	}

	public int hintedItems(Callable callback)
	{
		var items = callback.Call().As<Godot.Collections.Array<DictionaryExpressions.Item>>();
		return items.Count;
	}
}
