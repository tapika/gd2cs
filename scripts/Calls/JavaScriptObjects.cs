using Godot;
using System;

[GlobalClass]
public partial class JavaScriptObjects : RefCounted
{
	public float inspectApi(JavaScriptObject browser, string key)
	{
		var document = JavaScriptBridge.GetInterface("document");
		var element = document.Call("querySelector", "canvas").AsGodotObject() as JavaScriptObject;
		var viewport = browser.Get("visualViewport").AsGodotObject() as JavaScriptObject;
		var width = element.Get("width").As<float>();
		var enabled = browser.Get("enabled").As<bool>();
		var label = browser.Get("label").As<string>();
		var custom = browser.Get(key).As<float>();
		browser.Set("width", width);
		browser.Set("data-value", label);
		browser.Set(key, enabled);
		element.Call("focus");
		browser.Call("configure", width,
			custom, enabled);
		browser.Call(key, label);
		browser.Get("unused");
		var description = viewport.ToString();
		return width;
	}

	public float invokeApi(JavaScriptObject browser)
	{
		return browser.Call("measure", 2).As<float>();
	}

	public JavaScriptObject resolveElement()
	{
		return JavaScriptBridge.GetInterface("document").Call("querySelector", "canvas").AsGodotObject() as JavaScriptObject;
	}
}
