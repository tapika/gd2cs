using Godot;
using System;

[GlobalClass]
public partial class LocalLambdas : RefCounted
{
	public int Value = 0;

	public void replace()
	{
		Callable callback = default;
		callback = default;
		callback = Callable.From<int>((int value) =>
		{
			this.Value = value;
		});
		if (callback.Delegate is not null)
		{
			callback.Call(3);
		}
		callback = default;
		if (callback.Delegate is null)
		{
			this.Value = 0;
		}
		if (!gdCallableIsValid(callback))
		{
			this.Value = 1;
		}
	}

	public bool predicate(Callable callback, int value)
	{
		return gdCallableIsValid(callback) && callback.Call(value).AsBool();
	}

	public bool testValidity()
	{
		var callback = Callable.From<int, bool>((int value) => value > 0);
		if (gdCallableIsValid(callback))
		{
			return callback.Call(1).AsBool();
		}
		return false;
	}

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

	private static bool gdCallableIsValid(Callable callback)
	{
		// Callable.IsValid() is not exposed by Godot's C# API yet.
		// Mirrors the supported cases of Callable::is_valid() and CallableCustom::is_valid():
		// https://github.com/godotengine/godot/blob/master/core/variant/callable.cpp
		// Managed delegates may target an ordinary object or no object at all.
		if (callback.Delegate is not null)
			return callback.Target is null || GodotObject.IsInstanceValid(callback.Target);
		return GodotObject.IsInstanceValid(callback.Target) && callback.Target.HasMethod(callback.Method);
	}
}
