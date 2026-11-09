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

	public string combine(Godot.Collections.Array<string> values, string separator)
	{
		var literal = string.Join(" / ", values);
		var compact = string.Join("", values);
		var multiline = string.Join(separator,
			values
		);
		Godot.Collections.Array<string> empty = new Godot.Collections.Array<string>();
		var emptyResult = string.Join(separator, empty);
		return string.Join(separator.ToLowerInvariant(), values).ToUpperInvariant();
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

	public string convert(Variant value, Godot.Collections.Dictionary<string, Variant> entries, Callable callback)
	{
		var empty = gdStr();
		var nil = gdStr(new Variant());
		var text = gdStr(value);
		var entry = gdStr(entries["value"]);
		var called = gdStr(callback.Call());
		return gdStr(empty, nil, text,
			entry, called, "text", 42, true);
	}

	public string custom(LocalStrings source, Variant value)
	{
		return source.str(value) + source.ToString(1);
	}

	public string customJoin(LocalStrings source, Godot.Collections.Array<string> values)
	{
		return source.join(values);
	}

	public partial class LocalStrings : RefCounted
	{
		public string join(Godot.Collections.Array<string> values)
		{
			return "custom";
		}

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

	private static string gdStr(params Variant[] values)
	{
		var result = new System.Text.StringBuilder();
		foreach (var value in values)
			result.Append(value.ToString());
		return result.ToString();
	}
}
