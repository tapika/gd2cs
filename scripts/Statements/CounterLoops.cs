using Godot;
using System;

[GlobalClass]
public partial class CounterLoops : RefCounted
{
	public int Step = 1;

	public int changing(Godot.Collections.Array<int> values)
	{
		var total = 0;
		for (int index = 0; index < values.Count; index++)
		{
			// Recheck the changed count, including after continue.
			if (index == 0)
			{
				values.Add(4);
				continue;
			}
			if (index == 2)
			{
				break;
			}
			total += values[index];
		}
		return total;
	}

	public int stepping(int limit, int step)
	{
		var total = 0;
		for (var index = limit; index >= 0; index -= 2)
		{
			total += index;
		}
		for (int index = 0; index <= limit; index += step)
		{
			total += index;
		}
		var cursor = 0;
		for (cursor = limit; cursor > 0; cursor--)
		{
			total += cursor;
		}
		return total + cursor;
	}

	public int nested(int limit)
	{
		var total = 0;
		for (int outer = 0; outer < limit; outer++)
		{
			for (int inner = 0; inner < 2; inner++)
			{
				if (inner == 0)
				{
					continue;
				}
				while (total < 1)
				{
					total += 1;
					continue;
				}
				total += outer;
			}
			if (outer == 1)
			{
				continue;
			}
			total += 1;
		}
		return total;
	}

	public int stop(int limit)
	{
		for (int index = 0; index < limit; index++)
		{
			if (index == 1)
			{
				return index;
			}
		}
		return -1;
	}

	public int shadowed()
	{
		var total = 0;
		for (int index = 0; index < 3; index += this.Step)
		{
			var Step = 2;
			total += Step;
			if (index == 0)
			{
				continue;
			}
		}
		return total;
	}

	public int ordinary(int limit)
	{
		var total = 0;
		if (true)
		{
			int index = 0;
			while (index < limit)
			{
				// A different continue step must remain an ordinary while loop.
				if (index == 0)
				{
					index += 2;
					continue;
				}
				total += index;
				index += 1;
			}
		}
		return total;
	}
}
