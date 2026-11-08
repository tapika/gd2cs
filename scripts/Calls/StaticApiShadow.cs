using Godot;
using System;

[GlobalClass]
public partial class StaticApiShadow : RefCounted
{
	public partial class TranslationServer : RefCounted
	{
		public static void set_locale(string locale)
		{
			return;
		}
	}

	public partial class JavaScriptObject : RefCounted
	{
		public float width = 1.0f;

		public float measure()
		{
			return width;
		}
	}

	public void invoke()
	{
		TranslationServer.set_locale("en");
	}

	public float readScript(JavaScriptObject api)
	{
		return api.width + api.measure();
	}
}
