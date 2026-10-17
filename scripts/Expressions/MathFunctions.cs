using Godot;
using System;

[GlobalClass]
public partial class MathFunctions : RefCounted
{
	public const float FullTurn = Mathf.Tau;
	public const float HalfTurn = Mathf.Pi;
	public const float Infinity = Mathf.Inf;
	public const float Invalid = Mathf.NaN;

	public float PositiveLimit = Mathf.Inf;
	public float NegativeLimit = -Mathf.Inf;
	public float Missing = Mathf.NaN;

	public float Limited = 0.0f;
	public int Ceiling = 0;
	public int Rounded = 0;
	public float Minimum = 0.0f;
	public float Maximum = 0.0f;
	public float Absolute = 0.0f;
	public int Floor = 0;
	public float Root = 0.0f;
	public float Power = 0.0f;
	public float Sine = 0.0f;
	public float Interpolated = 0.0f;
	public int IntegerMinimum = 0;
	public int IntegerMaximum = 0;

	public void evaluate(float value)
	{
		Limited = Mathf.Clamp((float)(value - 1.0f), 0.0f, 1.0f);
		Ceiling = Mathf.CeilToInt(value);
		Rounded = Mathf.RoundToInt(value);
		Minimum = Mathf.Min(value, 1.0f);
		Maximum = Mathf.Max(value, 0.0f);
		Absolute = Mathf.Abs(value);
		Floor = Mathf.FloorToInt(value);
		Root = Mathf.Sqrt(value);
		Power = Mathf.Pow(value, 2.0f);
		Sine = Mathf.Sin(value);
		Interpolated = Mathf.Lerp(value, 1.0f, 0.5f);
		IntegerMinimum = Mathf.Min(2, Mathf.CeilToInt(value));
		IntegerMaximum = Mathf.Max(2, Mathf.CeilToInt(value));
	}

	public void clampValues(float value, int index)
	{
		var lower = 0;
		var upper = 10;
		var boundedIndex = Mathf.Clamp(index, lower, upper);
		var boundedValue = Mathf.Clamp(value, 0, 1);
		var widened = Mathf.Clamp((float)(index), lower, upper);
		var narrowed = Mathf.Clamp((int)(value), lower, upper);
		IntegerMinimum = Mathf.Clamp(boundedIndex, lower, upper);
		Limited = Mathf.Clamp(
			Mathf.Clamp(boundedValue, 0, 1),
			0.0f, 1.0f
		);
	}

	public float constants()
	{
		var turn = Mathf.Tau;
		var edge = -Mathf.Inf;
		return turn + Mathf.Pi + edge;
	}

	public float shadow(float TAU)
	{
		var PI = TAU;
		return PI;
	}

	public partial class LocalConstants : RefCounted
	{
		public const float INF = 2.0f;

		public float read()
		{
			return INF;
		}
	}
}
