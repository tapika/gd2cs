using Godot;
using System;

[GlobalClass]
public partial class StaticGodotCalls : RefCounted
{
	public string changeLocale(string locale)
	{
		TranslationServer.SetLocale(locale);
		return TranslationServer.GetLocale();
	}

	public string platformName()
	{
		return OS.GetName();
	}

	public bool configureInput(bool enabled)
	{
		Input.EmulateMouseFromTouch = enabled;
		Input.EmulateTouchFromMouse = false;
		return Input.EmulateMouseFromTouch;
	}

	public bool configurePower(bool enabled)
	{
		OS.LowProcessorUsageMode = enabled;
		return OS.LowProcessorUsageMode;
	}

	public partial class CustomApi : RefCounted
	{
		public bool emulate_mouse_from_touch = false;

		public void set_locale(string locale)
		{
			return;
		}
	}

	public void parameterShadowed(CustomApi TranslationServer)
	{
		TranslationServer.set_locale("en");
	}

	public void localShadowed()
	{
		var TranslationServer = new CustomApi();
		TranslationServer.set_locale("en");
	}

	public void inputShadowed(CustomApi Input)
	{
		Input.emulate_mouse_from_touch = true;
	}
}
