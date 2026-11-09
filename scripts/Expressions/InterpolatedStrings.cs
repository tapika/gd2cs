using Godot;
using System;

[GlobalClass]
public partial class InterpolatedStrings : RefCounted
{
	public string format(string text, int count, float ratio)
	{
		var single = $"Value: {text}";
		var multiple = $"{text}_{count + 1}";
		var precise = $"{text}: {ratio:F2}";
		var fraction = $"{ratio:F1} units";
		var adjusted = $"{(ratio + 1.0f):F1}";
		var remainder = count % 3;
		var percent = $"{text}: 100%";
		var braces = $"{{{text}}}";
		var currency = $"€{text}";
		return precise;
	}

	public string formatNumber(float value, int decimals)
	{
		var whole = gdNum(1200, 0);
		var fraction = gdNum(12.5f, 2);
		var rounded = gdNum(12.345f, 2);
		var negative = gdNum(-12.345f, 2);
		return gdNum(value, decimals);
	}

	private static string gdNum(float value, int decimals) =>
		value.ToString(
			"0." + new string('#', decimals),
			System.Globalization.CultureInfo.InvariantCulture);
}
