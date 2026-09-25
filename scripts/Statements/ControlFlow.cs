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
}
