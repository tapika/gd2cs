using Godot;
using System;

[GlobalClass]
public partial class InheritedMethods : Node3D
{
	public void inheritedCalls(Node3D child)
	{
		Tr("message");
		this.Tr("message", "context");
		AddChild(child);
	}

	// Unresolved calls intentionally test shadowing, static scope, and invalid argument counts.
	public void parameterShadowed(Callable tr, Callable Tr)
	{
		tr("message");
		Tr("message");
	}

	public void localShadowed()
	{
		var tr = opaque();
		var Tr = tr;
		tr("message");
		Tr("message");
	}

	public RefCounted opaque()
	{
		return new RefCounted();
	}

	public static void staticCalls()
	{
		tr("message");
		Tr("message");
	}

	public void incompatibleArgumentCount()
	{
		tr();
		tr("message", "context", "extra");
	}

	public partial class UserMethods : RefCounted
	{
		public string tr(string message)
		{
			return message;
		}

		public string Tr(string message)
		{
			return message;
		}

		public void userCalls()
		{
			tr("message");
			this.tr("message");
			Tr("message");
			this.Tr("message");
		}
	}
}
