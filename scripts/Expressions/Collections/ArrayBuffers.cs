using Godot;
using System;
using System.Linq;

[GlobalClass]
public partial class ArrayBuffers : RefCounted
{
	public Godot.Collections.Array<Vector2> Points = new Godot.Collections.Array<Vector2>();
	public Godot.Collections.Array<Vector3> Positions = new Godot.Collections.Array<Vector3>();
	public Godot.Collections.Array<Color> Palette = new Godot.Collections.Array<Color>();

	public void apply(Polygon2D shape, Line2D line)
	{
		Points.Add(new Vector2(1.0f, 2.0f));
		Positions.Add(new Vector3(1.0f, 2.0f, 3.0f));
		Palette.Add(new Color(1.0f, 0.5f, 0.25f));
		shape.Polygon = Points.ToArray();
		line.Points = gdArray(new Vector2(0.0f, 0.0f),
				new Vector2(3.0f, 4.0f)).ToArray();
	}

	public Variant positions()
	{
		return Positions.ToArray();
	}

	public Variant colors()
	{
		return Palette.ToArray();
	}

	private static Godot.Collections.Array<T> gdArray<[MustBeVariant] T>(params T[] values)
	{
		var result = new Godot.Collections.Array<T>();
		foreach (var value in values) result.Add(value);
		return result;
	}
}
