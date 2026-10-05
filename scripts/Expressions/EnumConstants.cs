using Godot;
using System;

[GlobalClass]
public partial class EnumConstants : RefCounted
{
	public void constants()
	{
		var ignored = Control.MouseFilterEnum.Ignore;
		var stopped = Control.MouseFilterEnum.Stop;
		var flags = Control.SizeFlags.ExpandFill;
		var alignment = HorizontalAlignment.Center;
		var error = Error.Ok;
	}

	public void shadowed(RefCounted Control)
	{
		var ignored = Control.MOUSE_FILTER_IGNORE;
	}

	public void localShadowed()
	{
		var Control = opaque();
		var ignored = Control.MOUSE_FILTER_IGNORE;
	}

	public void globalShadowed(int HORIZONTAL_ALIGNMENT_CENTER)
	{
		var alignment = HORIZONTAL_ALIGNMENT_CENTER;
	}

	public RefCounted opaque()
	{
		return new RefCounted();
	}
}
