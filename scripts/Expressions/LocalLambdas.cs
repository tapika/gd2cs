using Godot;
using System;

[GlobalClass]
public partial class LocalLambdas : RefCounted
{
	public int Value = 0;

	public Variant evaluate(int offset)
	{
		Callable sum = Callable.From<int, int, int>((int left, int right) =>
		{
			// Read an enclosing parameter without changing it.
			var result = left + right + offset;
			return result;
		});
		Callable store = Callable.From<int>((int value) =>
		{
			this.Value = value;
		});
		Callable reset = Callable.From(() =>
		{
			this.Value = 0;
		});
		var doubleValue = Callable.From<int, int>((int value) => value * 2);
		reset.Call();
		store.Call(3);
		doubleValue.Call(4);
		return sum.Call(1, 2);
	}
}
