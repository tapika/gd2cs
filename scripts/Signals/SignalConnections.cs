using Godot;
using System;

[GlobalClass]
public partial class SignalConnections : RefCounted
{
	public Button ActionButton;
	public OptionButton ChoiceButton;
	public int Selected;

	public void wire()
	{
		ActionButton.Connect(BaseButton.SignalName.Pressed, Callable.From(onPressed));
		ChoiceButton.Connect(OptionButton.SignalName.ItemSelected, Callable.From<int>(onSelected));
		ChoiceButton.Connect(OptionButton.SignalName.ItemSelected, Callable.From<int>((int index) => onSelected(index)));
		ActionButton.Connect(BaseButton.SignalName.Pressed,
			Callable.From(onPressed)
		);
	}

	public void onPressed()
	{
		return;
	}

	public void onSelected(int index)
	{
		Selected = index;
	}

	public void supplied(Callable callback)
	{
		ActionButton.Connect(BaseButton.SignalName.Pressed, callback);
	}

	public static void staticWire(Button button)
	{
		button.Connect(BaseButton.SignalName.Pressed, Callable.From(onStaticPressed));
	}

	public static void onStaticPressed()
	{
		return;
	}

	public partial class Connector : RefCounted
	{
		public void connect(Callable callback)
		{
			return;
		}
	}

	public partial class CustomControl : RefCounted
	{
		public Connector item_selected;
	}

	public void unrelated(CustomControl control, Callable callback)
	{
		control.item_selected.connect(callback);
	}
}
