using Godot;
using System;

[GlobalClass]
public partial class OptionalParameters : RefCounted
{
	public int Value = 0;

	public OptionalParameters(int value = 0)
	{
		Value = value;
	}

	public Callable choose(Callable callback = default)
	{
		return callback;
	}

	public void configure(
		int count = 2, float ratio = 1.5f,
		bool enabled = true, string label = "sample",
		OptionalParameters target = null, StringName key = default, NodePath path = default
	)
	{
		Value = count;
	}

	public void inspect()
	{
		var copy = new OptionalParameters();
		choose();
		configure();
		configure(4, 2.0f, false, "other", copy);
	}
}
