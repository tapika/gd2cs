using Godot;
using System;

[GlobalClass]
public partial class Accessors : RefCounted
{
	public int Stored;
	public Node3D Selected;
	public static int Shared = 0;

	// Supplies reusable receiver type information.
	public Node3D TargetNode
	{
		get
		{
			return Selected;
		}
	}

	public int Count
	{
		// Read the separately named backing field.
		// Accessor comments retain their position.
		get
		{
			return Stored;
		}

		set
		{
			if (value < 0)
			{
				return;
			}
			Stored = value;
		}
	}

	public int AssignedCount
	{
		set
		{
			var incoming = value;
			// The setter parameter name survives backtranslation.
			Stored = incoming;
		}
	}

	public static int SharedCount
	{
		get
		{
			return Shared;
		}
		set
		{
			Shared = value;
		}
	}

	public void change(int value)
	{
		Count = value;
		AssignedCount = Count;
		TargetNode.Position = Vector3.Zero;
		var position = TargetNode.Position;
	}

	public Vector3 targetPosition(Holder holder)
	{
		return holder.Target.Position;
	}

	public partial class Holder : RefCounted
	{
		public int Stored;
		public Node3D Selected;

		public Node3D Target
		{
			get
			{
				return Selected;
			}
		}

		public int Number
		{
			set
			{
				Stored = value;
			}
			get
			{
				return Stored;
			}
		}
	}
}
