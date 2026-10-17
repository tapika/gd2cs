using Godot;
using System;

[GlobalClass]
public partial class StringOperations : RefCounted
{
	public void operations(string text)
	{
		var upper = text.ToUpperInvariant();
		var lower = upper.ToLowerInvariant();
		var contains = text.Contains("part");
		var prefix = text.StartsWith("pre", StringComparison.Ordinal);
		var suffix = text.EndsWith("post", StringComparison.Ordinal);
	}

	public void loop(Godot.Collections.Array<string> values)
	{
		foreach (var value in values)
		{
			value.ToUpperInvariant();
		}
	}

	public string literal()
	{
		return "message".ToLowerInvariant();
	}

	public string chained(string text)
	{
		return text.ToUpperInvariant().ToLowerInvariant();
	}

	public string stringify(RefCounted value)
	{
		return value.ToString();
	}

	public string stringifyOptional(RefCounted value)
	{
		if (value == null)
		{
			return "<null>";
		}
		return value.ToString();
	}

	public string stringifyChain(RefCounted value)
	{
		var text = value.ToString();
		return text.ToUpperInvariant();
	}

	public string custom(LocalStrings source, Variant value)
	{
		return source.str(value) + source.ToString(1);
	}

	public partial class LocalStrings : RefCounted
	{
		public string str(Variant value)
		{
			return "custom";
		}

		public string format(Variant value)
		{
			return str(value);
		}

		public string ToString(int value)
		{
			return "custom";
		}
	}
}
