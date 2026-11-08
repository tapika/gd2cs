using System.Text.Json;
using gd2cs.Language;
using gd2cs.Model;

namespace gd2cs.Tests;

public sealed class GodotStaticMemberTests
{
    // ScriptPairs exercise syntax and emission; this synthetic API tests production owner
    // discovery itself, so a fallback entry cannot hide a reflection regression in CI.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SingletonOwner(bool registered)
    {
        var registrations = registered ? """[{"name": "NativeApi", "type": "NativeApi"}]""" : "[]";
        using var document = JsonDocument.Parse($$"""
            {
                "classes": [{"name": "NativeApi", "properties": [{"name": "enabled"}]}],
                "builtin_classes": [],
                "singletons": {{registrations}}
            }
            """);
        var assembly = typeof(Godot.ManagedApi).Assembly;
        var names = GodotTypeNames.FromApi(assembly, document.RootElement);
        Assert.True(names.TryResolve("NativeApi", ScriptLanguage.GdScript, out var instance, out _));
        Assert.Equal("NativeApiInstance", instance);

        var catalog = GodotStaticMembers.FromApi(assembly, document.RootElement, names);
        Assert.Equal(registered, catalog.TryResolve("NativeApi.enabled", ScriptLanguage.GdScript, out var native));
        Assert.Equal(registered, catalog.TryResolve("ManagedApi.Enabled", ScriptLanguage.CSharp, out var managed));
        Assert.False(catalog.TryResolve("NativeApiInstance.Enabled", ScriptLanguage.CSharp, out _));
        Assert.False(catalog.TryResolve("StaticApiDecoy.Enabled", ScriptLanguage.CSharp, out _));
        if (registered)
        {
            Assert.Equal(native, managed);
            Assert.Equal("ManagedApi", native.DeclaringType);
            Assert.Equal(GodotStaticMemberKind.Property, native.Kind);
            Assert.Equal(TypeKind.Bool, native.ResultType.Kind);
            Assert.Equal("NativeApi.enabled", catalog.Name(native, ScriptLanguage.GdScript));
            Assert.Equal("ManagedApi.Enabled", catalog.Name(native, ScriptLanguage.CSharp));
        }
    }
}
