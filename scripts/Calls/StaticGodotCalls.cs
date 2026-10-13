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

	public partial class CustomApi : RefCounted
	{
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
}
