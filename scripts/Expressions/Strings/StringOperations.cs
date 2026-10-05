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
}
