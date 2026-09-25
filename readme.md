# Overview

`gd2cs` is GDScript/C# transpiler. Both source languages are parsed
into the same semantic model before target code is emitted.

Normally it's assumed that .cs code was generated first from gd script
and was not dramatically changed. So .cs to gd script transpiling is supported, but
not all C# language features are supported.

