class_name JavaScriptObjects
extends RefCounted

func inspectApi(browser: JavaScriptObject, key: String) -> float:
	var document := JavaScriptBridge.get_interface("document")
	var element := document.querySelector("canvas") as JavaScriptObject
	var viewport := browser.visualViewport as JavaScriptObject
	var width := element.width as float
	var enabled := browser.enabled as bool
	var label := browser.label as String
	var custom := browser[key] as float
	browser.width = width
	browser["data-value"] = label
	browser[key] = enabled
	element.focus()
	browser.configure(width,
		custom, enabled)
	browser.call(key, label)
	browser.get("unused")
	var description := viewport.to_string()
	return width

func invokeApi(browser: JavaScriptObject) -> float:
	return browser.measure(2) as float

func resolveElement() -> JavaScriptObject:
	return JavaScriptBridge.get_interface("document").querySelector("canvas") as JavaScriptObject
