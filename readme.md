# Overview

`gd2cs` is GDScript/C# transpiler. Both source languages are parsed
into the same semantic model before target code is emitted.

Normally it's assumed that .cs code was generated first from gd script
and was not dramatically changed. So .cs to gd script transpiling is supported, but
not all C# language features are supported.

## Paired script folders

Scripts can be kept in paired language folders:

```text
scripts/myscript-gd/            scripts/myscript-cs/
```

Run the script by name:

```text
gd2cs --project <project> --scripts myscript
```

The active project language selects the source folder. Output is written to the opposite folder without deleting the source. `gd2cs` adds `.gdignore` to the source folder, removes it from the target folder, and updates script references.

Pass multiple names or paths separated by commas: `--scripts myscript,otherscript`. `--script` remains an alias.

This is done in case if you need to transpile `.cs` back to `.gd`, for example for web export.

## Build backtranslation

Add `--postbuild` when translating GDScript to C#:

```text
gd2cs --project <project> --scripts myscript,otherscript --postbuild
```

This adds an idempotent target to the generated or existing `.csproj`. After each successful C# build, the target runs gd2cs with `--scripts`, `--to gd`, and `--scriptonly` to regenerate all selected GDScript files.

`--scriptonly` translates only the selected scripts. It does not change project settings, references, caches, `.gdignore` files, or the source scripts.

This way it's possible to observe that you will not break `.gd` script due to `gd2cs` non-supported C# languages features.

## Gd / C# interoperability gotchas

### Instantiating a loaded C# script

Use `=` rather than `:=` when instantiating a dynamically loaded C# script:

```gdscript
var ScriptFactory := load("res://scripts/ScriptFactory-cs/ScriptFactory.cs")
var factory = ScriptFactory.new()
```

`:=` requires static type inference, which may fail or resolve only a base Godot type rather than the C# script's concrete type. `=` keeps the variable dynamically typed, allowing access to the instance's script members. The same declaration works when the resource path is switched to the GDScript version.

### Accessing C# static data from GDScript

Loading a C# script gives a `CSharpScript` resource. Direct access to its static fields or properties is unavailable, but static functions can expose the data:

```gdscript
var SharedValuesScript := load("res://scripts/SharedValues-cs/SharedValues.cs")

# SharedValuesScript.Count is unavailable; use a static function.
var count = SharedValuesScript.getCount()
```

In the C# class:

```csharp
public static int Count = 3;

public static int getCount()
{
    return Count;
}
```

The corresponding GDScript class uses `static var Count` and `static func getCount()`, so the function-based access works in either language.

## Unsupported language features

### Untyped dictionaries

`gd2cs` rejects untyped `Dictionary` declarations (including `Array[Dictionary]`), anonymous dictionary literals, and non-generic C# `Godot.Collections.Dictionary`. Specify both key and value types instead:

```gdscript
var counts : Dictionary[String, int] = {"first": 1}
```

Use `Dictionary[String, Variant]` for mixed values, or prefer a class with explicitly typed members when each field has a different type. GDScript supports untyped dictionaries; this restriction belongs to gd2cs. Empty typed dictionaries remain supported.

For an array of dictionaries, Godot forbids nested typed collections. On declarations without an initializer, put the dictionary types in a trailing gd2cs hint:

```gdscript
var entries : Array[Dictionary] #[String, Node3D]
```

gd2cs reads this as `Array[Dictionary[String, Node3D]]` and emits C# `Godot.Collections.Array<Godot.Collections.Dictionary<string, Node3D>>`. Backtranslation reconstructs the hint. It works on fields and locals; Godot treats it as a comment and does not enforce the dictionary key/value types. Without the hint, `Array[Dictionary]` remains rejected. Mixed node and vector values require a `Variant` value type, not `Node3D`.

### Empty array literals

`gd2cs` requires array literals to contain at least one element. Empty literals such as `var values := []` and `var values : Array[int] = []` are rejected. For an initially empty array, declare its type without an initializer:

```gdscript
var values : Array[int]
```

Nonempty literals can still use type inference, for example `var values := [1, 2]`. 

### C# double

`gd2cs` maps C# `float` to GDScript `float` in both directions. C# `double` (`System.Double`) is unsupported and produces a translation error. This is a tooling choice: GDScript `Vector2/3` uses 32-bit `float`, and even thus some properties like `SpinBox.MinValue` use `double`, but mixing two different type can produce precision errors.
 Easier to support all types as `float`'s.

Godot is also possible to compile with the `precision=double` option, let me know if you need support for such custom build, maybe `gd2cs` can be made reconfigurable then.

### C# object initializers

C# can assign properties as part of object construction:

```csharp
var value = new Example
{
    Name = "sample"
};
```

GDScript has no equivalent object-initializer syntax, so `gd2cs` does not support this C# feature. Support can be added if a corresponding feature is added to GDScript.

### Nullable types

C# can normally assign `null` to `string`. For gd script this is not possible.
If you pass in function - recommended to use empty string instead.

```csharp
void func(string s = "")
```

This will get translated correctly to gd script.

Generally C# also supports nullable types, e.g. 

```csharp
void func(string? s = null)
```

Gd script does not supports that one, so `gd2cs` does not support this C# feature. Support can be added if a corresponding feature is added to GDScript.
