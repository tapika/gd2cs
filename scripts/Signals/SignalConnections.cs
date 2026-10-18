using Godot;
using System;

[GlobalClass]
public partial class SignalConnections : RefCounted
{
	public Button ActionButton;
	public OptionButton ChoiceButton;
	public SpinBox ValueControl;
	public int Selected;
	public float Amount;

	public void wire()
	{
		ActionButton.Pressed += onPressed;
		ActionButton.Toggled += onToggled;
		ChoiceButton.ItemSelected += onSelected;
		ChoiceButton.ItemSelected += (long index_) =>
		{
			var index = (int)(index_);
			onSelected(index);
		};
		ActionButton.Pressed += (
			onPressed
		);
		ActionButton.Pressed -= onPressed;
		ChoiceButton.ItemSelected -= onSelected;
		ActionButton.Toggled -= onToggled;
		ValueControl.ValueChanged += onValueChanged;
		ValueControl.ValueChanged += (double value_) =>
		{
			var value = (float)(value_);
			onValueChanged(value);
		};
		ValueControl.ValueChanged -= onValueChanged;
	}

	public void onPressed()
	{
		return;
	}

	public void onToggled(bool enabled)
	{
		return;
	}

	public void onSelected(long index_2)
	{
		var index = (int)(index_2);
		// Generated parameters must not shadow existing locals.
		var index_ = 0;
		Selected = index + index_;
	}

	public void onValueChanged(double value_)
	{
		var value = (float)(value_);
		Amount = value;
	}

	public static void staticWire(Button button)
	{
		button.Pressed += onStaticPressed;
		button.Pressed -= onStaticPressed;
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
		Connector connector = control.item_selected;
		connector.connect(callback);
	}
}
