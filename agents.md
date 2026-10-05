Emitter rule: use one `StringBuilder.Append*` call per line; never chain calls.
Example rule: borrow syntax, not names; use neutral language-feature names.
Fixture rule: omit optional syntax by default; include it only when that syntax is under test.
Coverage rule: every language syntax or semantics change requires a new or updated ParseAndEmit script pair covering that change.
Test rule: prefer ParseAndEmit script pairs over separate tests whenever they can cover the behavior.
Comment rule: add a brief comment explaining what non-self-obvious code does, including functions, code blocks, and model definitions. 
This is especially important in `ScriptModel.cs`: explain each non-obvious model node's semantic purpose and any translation-specific distinctions; 
avoid restating self-explanatory code.
Fixture folder rule: keep each script subfolder at eight file pairs or fewer.
Rename rule: use Git-aware renames so history records a rename, not delete plus add.
