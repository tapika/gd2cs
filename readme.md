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
gd2cs --project <project> --script myscript
```

The active project language selects the source folder. Output is written to the opposite folder without deleting the source. `gd2cs` adds `.gdignore` to the source folder, removes it from the target folder, and updates script references.

This is done in case if you need to transpile `.cs` back to `.gd`, for example for web export.

## Build backtranslation

Add `--postbuild` when translating GDScript to C#:

```text
gd2cs --project <project> --script myscript --postbuild
```

This adds an idempotent target to the generated or existing `.csproj`. After each successful C# build, the target runs gd2cs with `--scriptonly` to regenerate the GDScript file again.

`--scriptonly` translates only the selected script. It does not change project settings, references, caches, `.gdignore` files, or the source script.

This way it's possible to observe that you will not break `.gd` script due to `gd2cs` non-supported C# languages features.

