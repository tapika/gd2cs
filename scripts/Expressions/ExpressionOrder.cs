using Godot;
using System;

[GlobalClass]
public partial class ExpressionOrder : RefCounted
{
	public int DefaultOrder = 0;
	public int GroupedOrder = 0;
	public int SignedOrder = 0;

	public void evaluate(int value)
	{
		DefaultOrder = first(value) + second(value) * third(value);
		GroupedOrder = (first(value) + second(value)) * third(value);
		SignedOrder = -first(value) + +second(value);
	}

	public int select(bool condition, int left, int right)
	{
		return condition ? left : right;
	}

	public int multiline(int value)
	{
		var total = (
			value * 2
			+ value * 3
		);
		var partial = (value +
			value * 2 + value

			- 1);
		var nested = (

			(value
				+ 1) * 2
			+ value

		);
		return total + partial + nested;
	}
}
