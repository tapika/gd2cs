using Godot;
using System;

[GlobalClass]
public partial class CustomSignals : RefCounted
{
	// Registered with this exact name.
	[Signal]
	public delegate void changedEventHandler();
	public int Count;

	public void notify(int count, string label)
	{
		EmitSignal(SignalName.changed);
		EmitSignal(SignalName.payload_ready, count, label);
		EmitSignal(SignalName.payload_ready, count,
			label
		);
	}

	// Declaration order does not limit emission binding.
	[Signal]
	public delegate void payload_readyEventHandler(int count, string label);

	public partial class Receiver : RefCounted
	{
		// Signals also belong to nested classes.
		[Signal]
		public delegate void completedEventHandler();

		public void emit(int value)
		{
			EmitSignal(SignalName.completed);
		}
	}

	public void ordinary(Receiver changed)
	{
		changed.emit(1);
	}
}
