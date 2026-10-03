Emitter rule: use one `StringBuilder.Append*` call per line; never chain calls.
Example rule: borrow syntax, not names; use neutral language-feature names.
Fixture rule: omit optional syntax by default; include it only when that syntax is under test.
Coverage rule: every language syntax or semantics change requires a new or updated ParseAndEmit script pair covering that change.
Test rule: prefer ParseAndEmit script pairs over separate tests whenever they can cover the behavior.
Comment rule: briefly explain non-obvious functions and code blocks.
Fixture folder rule: keep each script subfolder at eight file pairs or fewer.
Rename rule: use Git-aware renames so history records a rename, not delete plus add.
