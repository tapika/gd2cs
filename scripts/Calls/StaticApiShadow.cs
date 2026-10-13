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

	public void invoke()
	{
		TranslationServer.set_locale("en");
	}
}
