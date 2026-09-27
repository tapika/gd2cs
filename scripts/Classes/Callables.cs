using Godot;
using System;

// A class comment for a non-trivial script.
// It also verifies multiline comments before the class declaration.
[GlobalClass]
public partial class Callables : RefCounted
{
	public int Value = 0;

	// Initializes the instance.
	public Callables(int value, string label)
	{
		this.Value = value;

		// Store the descriptive text too.
		this.Label = label;
	}

	// Text declared between callables.
	// Its position must be preserved.
	public string Label = "";

	// Changes the numeric value.
	public void update(Callables target, int value)
	{
		target.Value = value;
		evaluate(value);
	}

	// State declared after a method.
	public bool Enabled = true;

	public void forward(Callables target, int value)
	{
		var copy = new Callables(
			value,
			target.Label
		);
		copy.update(
			this,
			value
		);
		apply(value,
			value,
			value, value);
	}

	public void apply(
		int first,
		int second,
		int third, int fourth
	)
	{
		Value = first + second + third + fourth;
	}

	public static int evaluate(int value)
	{
		return value;
	}

	// Handles cleanup.
	~Callables()
	{
		Enabled = Enabled;
	}
}
