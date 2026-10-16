using Godot;
using System;

[GlobalClass]
public partial class ExpressionOrder : RefCounted
{
	public int DefaultOrder = 0;
	public int GroupedOrder = 0;
	public int SignedOrder = 0;

	public void evaluate(int value)
	{
		DefaultOrder = first(value) + second(value) * third(value);
		GroupedOrder = (first(value) + second(value)) * third(value);
		SignedOrder = -first(value) + +second(value);
	}

	public int select(bool condition, int left, int right)
	{
		return condition ? left : right;
	}

	public void compound(float value, Godot.Collections.Array<int> values, Node3D node, Vector3 offset)
	{
		var total = 12.0f;
		total += value * 2.0f;
		total -= value;
		total *= 2.0f;
		total /= 2.0f;
		DefaultOrder += 1;
		values[nextIndex()] += 2;
		node.Position += offset;
	}

	public int nextIndex()
	{
		return 0;
	}

	public void bitwise(int flags, bool enabled)
	{
		var combined = flags | 2 ^ 4 & 1 + 2 * 3;
		var inverted = ~flags;
		var selected = combined & 7;
		var grouped = flags & (1 | 2);
		var ordered = (flags & 1) + 2;
		var compared = flags == (1 | 2);
		var present = (flags & 64) != 0;
		if ((flags & 1) != 0)
		{
			DefaultOrder = selected;
		}
		if (enabled && (flags & 2) != 0 || (flags & 4) != 0)
		{
			DefaultOrder = combined;
		}
		if ((flags & 8) == 0)
		{
			DefaultOrder = inverted;
		}
		while ((flags & 16) != 0)
		{
			break;
		}
		if ((~flags) != 0)
		{
			DefaultOrder = ordered;
		}
		var chosen = (flags & 32) != 0 ? 1 : 0;
	}

	public int maskValue(int flags)
	{
		return flags & 3;
	}

	public bool maskPresent(int flags)
	{
		return (flags & 1) != 0;
	}

	public bool compareMask(int flags, int expected)
	{
		return (flags & 3) == expected;
	}

	public bool enumMask(Control.SizeFlags flags)
	{
		return (flags & Control.SizeFlags.ExpandFill) != 0;
	}

	public int multiline(int value)
	{
		var total = (
			value * 2
			+ value * 3
		);
		var partial = (value +
			value * 2 + value

			- 1);
		var nested = (

			(value
				+ 1) * 2
			+ value

		);
		return total + partial + nested;
	}
}
