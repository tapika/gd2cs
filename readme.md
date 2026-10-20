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

## Undo project integration

To return to the existing GDScript versions without transpiling:

```text
gd2cs --project <project> --scripts myscript,otherscript --undo
```

`--undo` switches project/scene references back to `.gd`, removes the C# project feature, clears generated C# caches, and activates the GDScript folders through `.gdignore`. Only the script attached to the main scene's root may have its loader references updated; all other `.gd` and `.cs` files remain unchanged, even if the C# version does not compile. C# project/solution files are retained.

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

For a singleton-style class, expose its shared instance through a static function too. In GDScript:

```gdscript
class_name SharedState
extends RefCounted

static var Shared := SharedState.new()

static func getInstance() -> SharedState:
    return Shared
```

The corresponding C# class contains:

```csharp
public static SharedState Shared = new SharedState();

public static SharedState getInstance()
{
    return Shared;
}
```

Access it from GDScript through the loaded script resource:

```gdscript
var SharedStateScript := load("res://scripts/SharedState-cs/SharedState.cs")
var state = SharedStateScript.getInstance()
```

Use `=` to retain dynamic access to the returned C# instance's members. Call the getter instead of `.new()` to reuse the shared instance; do not access `SharedStateScript.Shared` directly. The same pattern works with the GDScript resource path and does not require a Godot Autoload.

## C# and GDScript interoperability

### Typed properties

Prefer a typed property when the same receiver type is needed outside one function. A getter can expose a singleton without repeating its lookup or caching an instance:

```gdscript
var planner : SharedState:
    get:
        return SharedState.getInstance()
```

gd2cs maps this to a C# `SharedState planner` property with a `get` block. All methods can then use `planner`; its declared type participates in member resolution. Members of unavailable external classes still require explicit type information where needed.

Read/write properties use explicit accessor blocks and a separately named backing field:

```gdscript
var storedCount := 0
var count : int:
    get:
        return storedCount
    set(value):
        storedCount = value
```

Read-only, write-only, static and nested properties are supported. Custom setter parameter names are preserved using a reversible C# local alias of `value`. Use explicit blocks: auto-properties, property initializers and GDScript's implicit self-referencing backing storage are not supported.


### Return types

Functions returning a value must declare a return type (`-> Type` in GDScript). gd2cs treats an omitted return annotation as `void` and reports an error at any value-returning `return`. Bare `return` remains supported in void functions.

### Membership operator

GDScript membership expressions such as `value in values` are not supported. Use `values.has(value)` instead: arrays translate to C# `Contains(value)`, dictionaries to `ContainsKey(value)`. For example, `[1, 2, 3].has(value)` replaces `value in [1, 2, 3]`. Negate `.has()` with `not` when needed. This restriction does not affect `for value in values` loops.

### Reassigning type-narrowed variables

`is` / `is not` checks support local/parameter type narrowing in conditional branches, loops, conditional expressions and short-circuit boolean expressions. C# uses collision-free pattern variables; GDScript retains the original variable names. Reassigning a narrowed variable is currently rejected, since assigning a C# pattern variable would not update the original variable. Use a separate local instead.

### Multiline conditional expressions

GDScript permits multiline `value_if_true if condition else value_if_false` expressions inside parentheses, but gd2cs currently supports only their single-line form. Use ordinary `if`/`else` statements instead:

```gdscript
if enabled:
    applyValue(first)
else:
    applyValue(second)
```

### Untyped dictionaries

`gd2cs` rejects untyped `Dictionary` declarations (including `Array[Dictionary]`), anonymous dictionary literals, and non-generic C# `Godot.Collections.Dictionary`. Specify both key and value types instead:

```gdscript
var counts : Dictionary[String, int] = {"first": 1}
```

Use `Dictionary[String, Variant]` for mixed values, or prefer a class with explicitly typed members when each field has a different type. GDScript supports untyped dictionaries; this restriction belongs to gd2cs. Empty typed dictionaries remain supported.

### Collection type hints

Bracketed trailing comments can supply generic type arguments to gd2cs without exposing them to Godot's type checker. On a plain `Array`, the hint supplies its single element type:

```gdscript
var items : Array #[Catalog.Entry]
```

gd2cs reads this as `Array[Catalog.Entry]` and emits C# `Godot.Collections.Array<Catalog.Entry>`. Qualified array element types are reconstructed as hints on declarations without initializers and standalone local casts. For a local initialized by another function, use a separate assignment:

```gdscript
var items : Array #[Catalog.Entry]
items = Catalog.getEntries()
```

For an array of dictionaries, Godot forbids nested typed collections. Here the hint supplies the **dictionary's two arguments**, not the array's element type:

```gdscript
var entries : Array[Dictionary] #[String, Node3D]
```

gd2cs reads this as `Array[Dictionary[String, Node3D]]` and emits C# `Godot.Collections.Array<Godot.Collections.Dictionary<string, Node3D>>`. Backtranslation reconstructs the hint. Both forms work on fields and locals. Godot treats hints as comments and does not enforce the hinted element/key/value types; hints do not convert or retype existing collections. Ordinary comments are not hints: the brackets are required. Without its hint, `Array[Dictionary]` remains rejected. Mixed node and vector values require a `Variant` value type, not `Node3D`.

Hints also work on a local's collection cast, for example `var entries := callback.call() as Array[Dictionary] #[String, Variant]`. Store the cast in a local before iterating it. A raw `Variant` (including `Callable.call()` results) cannot be iterated: cast it to `Array[ElementType]` first. Iterator annotations such as `for entry: Dictionary[String, Variant] in entries:` map to explicitly typed C# `foreach`; omitted annotations map to `foreach (var entry in entries)`. An iterator annotation alone does not cast a Variant collection.

### Reading Variant dictionary values

Reads from `Dictionary[String, Variant]` require an explicit concrete cast, including arguments to `is_instance_valid` and method receivers. Both dictionary-dot and bracket access are recognized:

```gdscript
var item := entry.node as Node
var offset := entry["offset"] as Vector3
```

C# uses `entry["node"].AsGodotObject() as Node` for object casts and `entry["offset"].As<Vector3>()` for value extraction. gd2cs reports the uncast read's location and suggests `as Type`; it does not guess types from dictionary keys. Dictionary writes and reads whose declared value type is already concrete do not require casts.

### Callable results in Boolean expressions

`Callable.call()` returns `Variant`, even if its target function declares a `bool` return type. gd2cs requires an explicit `as bool` cast before using that result as a condition, logical operand, or Boolean return/assignment:

```gdscript
return callback.is_valid() and (callback.call(value) as bool)
```

This emits `gdCallableIsValid(callback) && callback.Call(value).As<bool>()` in C#. The cast is preserved on backtranslation; gd2cs does not insert implicit Boolean extraction or generate `AsBool()`. Uncast Variant conditions produce a source-located error suggesting the conversion, including when the Variant is stored in a local. Calls used as statements or returned as `Variant` do not require a cast. Validity checks remain separate from result conversion.

### Packed arrays at API boundaries

Godot's `PackedVector2Array`, `PackedVector3Array`, and `PackedColorArray` classes were not ported to C#; the managed API uses `Vector2[]`, `Vector3[]`, and `Color[]`. gd2cs rejects packed-array type declarations and empty construction such as `PackedVector2Array()`. Build values using a typed Godot array instead:

```gdscript
var points : Array[Vector2]
points.append(Vector2(1.0, 2.0))
shape.polygon = PackedVector2Array(points)
```

Only conversion from an existing, matching typed array is supported: `PackedVector2Array(points)` becomes `points.ToArray()` in C#, and backtranslation restores the packed conversion. The same applies to `Array[Vector3]` and `Array[Color]`. Nonempty array literals can also be converted; initializer line breaks are preserved. C# adds `using System.Linq;` when required. Conversion copies the values into an API buffer; it does not turn the mutable Godot array into packed storage or add packed-array mutation support. Calls on unrelated types named `ToArray` are not rewritten.

### Empty array literals

`gd2cs` requires array literals to contain at least one element. Empty literals such as `var values := []` and `var values : Array[int] = []` are rejected. For an initially empty array, declare its type without an initializer:

```gdscript
var values : Array[int]
```

Arrays and dictionaries declared without initializers start empty in GDScript. gd2cs emits `new Godot.Collections.Array<T>()` or `new Godot.Collections.Dictionary<TKey, TValue>()` for C# fields (including static fields) and locals. Backtranslation folds an empty constructor of the exact declared type back to an omitted initializer. Explicit C# container `null` or `default` initializers are rejected rather than silently converted to empty containers; ordinary object references are not automatically constructed.

Nonempty literals can still use type inference, for example `var values := [1, 2]`.

### Native signal connections

GDScript `button.pressed.connect(onPressed)` / `.disconnect(onPressed)` translate to C# `button.Pressed += onPressed` / `-= onPressed`. Named handlers and inline lambdas are supported; no adapter delegates are generated. For differing integer widths, C# keeps the native signature and adds a conversion local, e.g. `long index_` followed by `var index = (int)(index_);`. Matching parameters stay unchanged. Generated names avoid collisions (`index_2`, etc.). Backtranslation removes these generated locals and restores the original GDScript parameter names/types; ordinary locals and casts remain intact. Unrelated parameter types and conflicting event signatures are rejected. Narrowing casts follow the C# project's overflow-checking policy.

C# `Connect(..., Callable.From(handler))` without flags is emitted as event syntax. Connections with an additional flags argument instead use C# `Connect(..., Callable.From(handler), (uint)(flags))`, preserving options such as `CONNECT_ONE_SHOT` in both directions. Runtime `Callable` callback values are not supported for native event connections. Inline lambdas have normal C# delegate-identity limitations; use a named handler when later disconnecting.

`Callable.bind()` is currently unsupported. For a signal callback, use an inline lambda instead of `refresh.bind(target)`:

```gdscript
target.get_tree().process_frame.connect(
    func(): refresh(target), Object.CONNECT_ONE_SHOT
)
```

The lambda captures `target` and passes it to `refresh` when the signal fires. Unlike `bind()`, which stores argument values when binding, a lambda evaluates its argument expressions when invoked; avoid side-effecting expressions if equivalent behavior is required.

### C# double

Native event signatures are the exception: gd2cs generates a `double` parameter and a leading float conversion (`var value = (float)(value_);`) when the event requires double but the script handler uses float. Backtranslation removes this C#-only conversion. Named handlers and inline lambdas use the same mechanism as int/long event parameters, preserving disconnect support without delegate adapters.

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
