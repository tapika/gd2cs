using Godot;
using System;

[GlobalClass]
public partial class ControlFlow : RefCounted
{
	public void process(Godot.Collections.Array<int> values, bool enabled)
	{
		foreach (var index in GD.Range(values.Count))
		{
			accept(index);
		}
		foreach (var index in GD.Range(1, values.Count))
		{
			accept(index);
		}

		// Iterate while preserving nested branches.
		foreach (var value in values)
		{
			if (value > 0 && enabled)
			{
				accept(value);
			}
			else if (value == 0 || !enabled)
			{
				continue;
			}
			else
			{
				break;
			}
		}

		if (enabled)
		{
			accept(1);
		}
		if (!enabled)
		{
			accept(0);
		}

		while (enabled)
		{
			if (values[0] < 10)
			{
				enabled = false;
			}
			else if (values[0] <= 20)
			{
				continue;
			}
			else if (values[0] >= 30)
			{
				break;
			}
			else if (values[0] != 25)
			{
				accept(values[0]);
			}
		}
	}

	public void accept(int value)
	{
		value = value;
	}

	public void iterate(Callable callback)
	{
		foreach (Node node in callback.Call().As<Godot.Collections.Array<Node>>())
		{
			node.GetParent();
		}

		var entries = callback.Call().As<Godot.Collections.Array<Godot.Collections.Dictionary<string, Variant>>>();
		foreach (Godot.Collections.Dictionary<string, Variant> entry in entries)
		{
			accept(entry["count"].As<int>());
		}
		foreach (var entry in entries)
		{
			accept(entry["count"].As<int>());
		}
	}

	public void iterateKeys(Godot.Collections.Dictionary<int, int> entries)
	{
		foreach (var key in entries.Keys)
		{
			accept(entries[key]);
		}
		foreach (int key in entries.Keys)
		{
			accept(key);
		}
	}

	public void iterateEngineKeys(PhysicsDirectSpaceState3D state, PhysicsRayQueryParameters3D query)
	{
		var entries = state.IntersectRay(query);
		foreach (var key in entries.Keys)
		{
			entries[key] = key;
		}
	}

	public void inspectInput(InputEvent @event, bool enabled)
	{
		// Reserve the first generated pattern name to test collision handling.
		var eventTyped = 0;
		accept(eventTyped);
		if (@event is InputEventKey eventTyped2 && eventTyped2.Pressed && !eventTyped2.Echo)
		{
			acceptKey(eventTyped2);
		}
		else if (@event is InputEventMouseButton eventTyped3 && eventTyped3.Pressed)
		{
			acceptPoint(eventTyped3.Position);
		}
		else
		{
			acceptEvent(@event);
		}
		acceptEvent(@event);

		if (@event is not InputEventKey eventTyped4)
		{
			acceptEvent(@event);
		}
		else
		{
			acceptKey(eventTyped4);
		}
		if (@event is not InputEventKey eventTyped5 || !eventTyped5.Pressed)
		{
			acceptEvent(@event);
		}
		if (@event is InputEventKey eventTyped6 || enabled)
		{
			acceptEvent(@event);
		}

		if (@event is InputEventKey eventTyped7)
		{
			if (eventTyped7.Pressed)
			{
				acceptKey(eventTyped7);
			}
		}
		while (@event is InputEventKey eventTyped8 && eventTyped8.Pressed)
		{
			acceptKey(eventTyped8);
			break;
		}
		var matched = @event is InputEventKey eventTyped9 ? eventTyped9.Pressed : false;
		acceptEvent(@event);
	}

	public void acceptKey(InputEventKey value)
	{
		return;
	}

	public void acceptPoint(Vector2 value)
	{
		return;
	}

	public void acceptEvent(InputEvent value)
	{
		return;
	}
}
