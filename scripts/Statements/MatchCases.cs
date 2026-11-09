using Godot;
using System;

[GlobalClass]
public partial class MatchCases : RefCounted
{
	public enum TokenState { Ready, Active, Complete }

	public string describe(TokenState state)
	{
		switch (state)
		{
			case TokenState.Ready:
				// Return from the function, not just the matching branch.
				return "ready";
			case TokenState.Active:
				return "active";
			default:
				return "complete";
		}
	}

	public int adjust(int value)
	{
		var result = 0;
		switch (value)
		{
			case -1:
			case 0:
				result = 1;
				break;

			case 1:
				switch (result)
				{
					case 0:
						result = 2;
						break;
				}
				break;
			// Unmatched values use the fallback.
			default:
				result = 3;
				break;
		}
		return result;
	}

	public int external(Enums.TokenState state)
	{
		switch (state)
		{
			case Enums.TokenState.Empty:
				return 0;
			default:
				return 1;
		}
	}

	public int scoped(int value, bool enabled)
	{
		switch (value)
		{
			case 0:
			{
				var result = 1;
				return result;
			}
			default:
			{
				var result = 2;
				if (enabled)
				{
					return result;
				}
				else
				{
					return 0;
				}
			}
		}
	}

	public void skip(string label)
	{
		switch (label)
		{
			case "empty":
				// This branch intentionally does nothing.
				break;
			default:
				return;
		}
	}

	public int count(int limit)
	{
		var total = 0;
		for (int index = 0; index < limit; index++)
		{
			switch (index)
			{
				case 0:
					continue;
				default:
					total += index;
					break;
			}
		}
		return total;
	}
}
