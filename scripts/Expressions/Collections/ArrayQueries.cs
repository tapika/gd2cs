using Godot;
using System;
using System.Linq;

[GlobalClass]
public partial class ArrayQueries : RefCounted
{
	public partial class Stock : RefCounted
	{
		public int Length;
	}

	public partial class Item : RefCounted
	{
		public Stock Board;
	}

	public Godot.Collections.Array<int> Values = new Godot.Collections.Array<int>();
	public Godot.Collections.Array<float> Weights = new Godot.Collections.Array<float>();
	public Godot.Collections.Array<Item> Items = new Godot.Collections.Array<Item>();

	public bool evaluate()
	{
		var gd2csSum = 5;
		var selected = new Godot.Collections.Array<int>(Values.Where((int value) => value > 1));
		var mapped = new Godot.Collections.Array<int>(selected.Select((int value) => value * 2));
		var hasLarge = mapped.Any((int value) => value > 4);
		var allPositive = mapped.All((int value) => value > 0);
		var hasAny = Values.Count != 0;
		var count = Values.Count((int value) => value > 1);
		int rawTotal = Values.Sum((int item) => item);
		int total = Values.Sum((int value) => value * 2);
		int product = Values.Aggregate(1, (int accumulator, int value) => accumulator * value);
		int aggregate = Values.Aggregate(0, (int accumulator, int value) => accumulator + value);
		return hasLarge && allPositive && hasAny && count > 0 && rawTotal > 0 && total > product && gd2csSum == 5;
	}

	public int sumLengths()
	{
		int total = Items.Sum(
			(Item item) => item.Board.Length
		);
		return total;
	}

	public float sumWeights()
	{
		float total = Weights.Sum((float value) => value);
		return total;
	}
}
