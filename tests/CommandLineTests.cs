using gd2cs.Cli;
using gd2cs.Language;

namespace gd2cs.Tests;

public sealed class CommandLineTests
{
    [Fact]
    public void CliToCs()
    {
        var projectPath = Path.Combine(Path.GetTempPath(), $"gd2cs-cli-{Guid.NewGuid():N}");
        var scriptsPath = Path.Combine(projectPath, "scripts");
        Directory.CreateDirectory(scriptsPath);

        try
        {
            File.WriteAllText(
                Path.Combine(projectPath, "project.godot"),
                "[application]\n\nconfig/name=\"cli-test\"\nconfig/features=PackedStringArray(\"4.7\")\n");
            File.Copy(Fixture("Classes/TestClass.gd"), Path.Combine(scriptsPath, "TestClass.gd"));
            File.WriteAllText(Path.Combine(scriptsPath, "Reference.gd"),
                "const TestScript := preload(\"res://scripts/TestClass.gd\")\n");
            File.WriteAllText(
                Path.Combine(projectPath, "main.tscn"),
                "[ext_resource type=\"Script\" uid=\"uid://old\" path=\"res://scripts/TestClass.gd\" id=\"1\"]\n");

            var result = CliApplication.CreateCommand(GodotTypeCatalog.CreateKnownBuiltIns()).Parse([
                "--project", projectPath,
                "--script", "TestClass"
            ]);

            Assert.Equal(0, result.Invoke());
            Assert.Equal(
                File.ReadAllText(Fixture("Classes/TestClass.cs")),
                File.ReadAllText(Path.Combine(scriptsPath, "TestClass.cs")));
            Assert.False(File.Exists(Path.Combine(scriptsPath, "TestClass.gd")));
            Assert.True(File.Exists(Path.Combine(scriptsPath, "TestClass.gd.bkp")));
            Assert.Contains("load(\"res://scripts/TestClass.cs\")", File.ReadAllText(Path.Combine(scriptsPath, "Reference.gd")));
            Assert.Contains("config/features=PackedStringArray(\"4.7\", \"C#\")", File.ReadAllText(Path.Combine(projectPath, "project.godot")));
            Assert.Contains("project/assembly_name=\"cli-test\"", File.ReadAllText(Path.Combine(projectPath, "project.godot")));
            Assert.True(File.Exists(Path.Combine(projectPath, "cli-test.csproj")));
            Assert.Contains("<TargetFramework>net8.0</TargetFramework>", File.ReadAllText(Path.Combine(projectPath, "cli-test.csproj")));
            Assert.Contains("<Project Path=\"cli-test.csproj\" />", File.ReadAllText(Path.Combine(projectPath, "cli-test.slnx")));
            var scene = File.ReadAllText(Path.Combine(projectPath, "main.tscn"));
            Assert.Contains("path=\"res://scripts/TestClass.cs\"", scene);
            Assert.DoesNotContain("uid=", scene);
        }
        finally
        {
            Directory.Delete(projectPath, recursive: true);
        }
    }

    [Fact]
    public void CliToGd()
    {
        var projectPath = Path.Combine(Path.GetTempPath(), $"gd2cs-cli-{Guid.NewGuid():N}");
        var scriptsPath = Path.Combine(projectPath, "scripts");
        var monoCachePath = Path.Combine(projectPath, ".godot", "mono", "temp");
        Directory.CreateDirectory(scriptsPath);
        Directory.CreateDirectory(monoCachePath);

        try
        {
            File.WriteAllText(
                Path.Combine(projectPath, "project.godot"),
                "[application]\n\nconfig/features=PackedStringArray(\"4.7\", \"C#\")\n");
            File.Copy(Fixture("Classes/TestClass.cs"), Path.Combine(scriptsPath, "TestClass.cs"));
            File.WriteAllText(Path.Combine(scriptsPath, "Reference.gd"),
                "var TestScript := load(\"res://scripts/TestClass.cs\")\n");
            File.WriteAllText(Path.Combine(monoCachePath, "stale.txt"), "stale");
            File.WriteAllText(Path.Combine(projectPath, ".godot", "global_script_class_cache.cfg"), "stale");
            File.WriteAllText(
                Path.Combine(projectPath, "main.tscn"),
                "[ext_resource type=\"Script\" uid=\"uid://old\" path=\"res://scripts/TestClass.cs\" id=\"1\"]\n");

            var result = CliApplication.CreateCommand(GodotTypeCatalog.CreateKnownBuiltIns()).Parse([
                "--project", projectPath,
                "--script", "TestClass"
            ]);

            Assert.Equal(0, result.Invoke());
            Assert.Equal(
                File.ReadAllText(Fixture("Classes/TestClass.gd")),
                File.ReadAllText(Path.Combine(scriptsPath, "TestClass.gd")));
            Assert.Contains("const TestScript := preload(\"res://scripts/TestClass.gd\")", File.ReadAllText(Path.Combine(scriptsPath, "Reference.gd")));
            Assert.DoesNotContain("\"C#\"", File.ReadAllText(Path.Combine(projectPath, "project.godot")));
            Assert.False(Directory.Exists(monoCachePath));
            Assert.False(File.Exists(Path.Combine(projectPath, ".godot", "global_script_class_cache.cfg")));
            var scene = File.ReadAllText(Path.Combine(projectPath, "main.tscn"));
            Assert.Contains("path=\"res://scripts/TestClass.gd\"", scene);
            Assert.DoesNotContain("uid=", scene);
        }
        finally
        {
            Directory.Delete(projectPath, recursive: true);
        }
    }

    [Fact]
    public void CliOptions()
    {
        var command = CliApplication.CreateCommand();

        var result = command.Parse([
            "--project", ".",
            "--script", "TestClass",
            "--to", "cs,gd",
            "--godot", "Godot.exe",
            "--verbose",
            "--reset-before",
            "--scriptonly",
            "--postbuild"
        ]);

        Assert.Empty(result.Errors);
    }

    private static string Fixture(string name) =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "scripts", name));
}
