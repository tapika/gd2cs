using Godot;
using System;

[GlobalClass]
public partial class BuiltInValues : RefCounted
{
	public Vector3 create()
	{
		var point2 = new Vector2(1.0f, 2.0f);
		var point3 = new Vector3(1.0f, 2.0f, 3.0f);
		var point4 = new Vector4(1.0f, 2.0f, 3.0f, 4.0f);
		var tint = new Color(0.1f, 0.2f, 0.3f, 1.0f);
		var rotation = new Quaternion(0.0f, 0.0f, 0.0f, 1.0f);
		return point3;
	}

	public Vector3 withHeight(Node3D node, float height)
	{
		return new Vector3(node.Position.X, height, node.Position.Z);
	}

	public void attach(Node3D parent, Node3D child)
	{
		parent.AddChild(child);
	}

	public Texture2D loadTexture()
	{
		var texture = GD.Load("res://icons/sample.svg") as Texture2D;
		return texture;
	}

	public Vector3 castPosition(Node node)
	{
		return (node as Node3D).Position;
	}

	public bool incompatibleCast()
	{
		return (new RefCounted() as Node3D) == null;
	}
}
