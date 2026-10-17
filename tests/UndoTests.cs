using System.Text;
using gd2cs.Cli;

namespace gd2cs.Tests;

public sealed class UndoTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Undo(bool sharedFolders, bool uidScene)
    {
        using var project = new UndoProject(sharedFolders, uidScene);
        var protectedFiles = project.Snapshot().Where(file => file.Key.EndsWith(".gd", StringComparison.Ordinal)
            || file.Key.EndsWith(".cs", StringComparison.Ordinal) || file.Key.EndsWith(".csproj", StringComparison.Ordinal)
            || file.Key.EndsWith(".slnx", StringComparison.Ordinal)).Where(file => file.Key != project.MainScript).ToDictionary();

        // No supplied catalog: undo must work without loading Godot or parsing either script version.
        Assert.Equal(0, project.Run());
        foreach (var file in protectedFiles)
            Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
        var main = File.ReadAllText(project.MainScript);
        foreach (var name in new[] { "First", "Second" })
        {
            Assert.Contains($"const {name}Script := preload(\"{project.Resource(name, "gd")}\")", main);
            Assert.Contains($"path=\"{project.Resource(name, "gd")}\"", File.ReadAllText(project.Scene));
            Assert.DoesNotContain($"path=\"{project.Resource(name, "cs")}\"", File.ReadAllText(project.Scene));
            Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(project.Script(name, "gd"))!, ".gdignore")));
            Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(project.Script(name, "cs"))!, ".gdignore")));
        }
        Assert.Contains("var Unchanged := load(\"res://scripts/Other.cs\")", main);
        Assert.DoesNotContain("\"C#\"", File.ReadAllText(Path.Combine(project.Directory, "project.godot")));
        Assert.False(System.IO.Directory.Exists(Path.Combine(project.Directory, ".godot", "mono", "temp")));
        Assert.False(File.Exists(Path.Combine(project.Directory, ".godot", "global_script_class_cache.cfg")));

        var snapshot = project.Snapshot();
        Assert.Equal(1, project.Run());
        project.AssertUnchanged(snapshot);
    }

    [Theory]
    [InlineData("--reset-before")]
    [InlineData("--scriptonly")]
    [InlineData("--postbuild")]
    [InlineData("--to")]
    public void UndoConflicts(string option)
    {
        using var project = new UndoProject();
        var snapshot = project.Snapshot();
        Assert.Equal(1, project.Run(option == "--to" ? [option, "gd"] : [option]));
        project.AssertUnchanged(snapshot);
    }

    [Fact]
    public void UndoMissingGd()
    {
        using var project = new UndoProject();
        var gd = project.Script("Second", "gd");
        File.Move(gd, gd + ".bkp");
        var snapshot = project.Snapshot();
        Assert.Equal(1, project.Run());
        project.AssertUnchanged(snapshot);
    }

    [Fact]
    public void UndoMissingCs()
    {
        using var project = new UndoProject();
        File.Delete(project.Script("Second", "cs"));
        var original = File.ReadAllBytes(project.Script("Second", "gd"));
        Assert.Equal(0, project.Run());
        Assert.Equal(original, File.ReadAllBytes(project.Script("Second", "gd")));
        Assert.False(File.Exists(project.Script("Second", "cs")));
    }

    // A deliberately non-compilable project exercises integration independently of language support.
    private sealed class UndoProject : IDisposable
    {
        private readonly bool sharedFolders;
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "gd2cs-undo-" + Guid.NewGuid().ToString("N"));
        public string Scene => Path.Combine(Directory, "scenes", "entry.tscn");
        public string MainScript => Path.Combine(Directory, "scripts", "entry.gd");

        public UndoProject(bool sharedFolders = false, bool uidScene = false)
        {
            this.sharedFolders = sharedFolders;
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(Scene)!);
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(MainScript)!);
            File.WriteAllText(Path.Combine(Directory, "project.godot"),
                "[application]\nrun/main_scene=\"" + (uidScene ? "uid://entry" : "res://scenes/entry.tscn")
                + "\"\nconfig/features=PackedStringArray(\"4.7\", \"C#\")\n");
            var scene = "[gd_scene format=3 uid=\"uid://entry\"]\n\n[ext_resource type=\"Script\" path=\"res://scripts/entry.gd\" id=\"entry\"]\n";
            var main = "extends Node\n";
            foreach (var name in new[] { "First", "Second" })
            {
                foreach (var language in new[] { "gd", "cs" })
                {
                    var path = Script(name, language);
                    System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    var content = $"unsupported syntax {name} {language} €\nload(\"{Resource("First", "cs")}\")\n";
                    File.WriteAllBytes(path, Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(content)).ToArray());
                }
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(Script(name, "gd"))!, ".gdignore"), string.Empty);
                scene += $"[ext_resource type=\"Script\" uid=\"uid://{name}\" path=\"{Resource(name, "cs")}\" id=\"{name}\"]\n";
                main += $"var {name}Script := load(\"{Resource(name, "cs")}\")\n";
            }
            File.WriteAllText(Scene, scene + "\n[node name=\"Root\" type=\"Node\"]\nscript = ExtResource(\"entry\")\n");
            File.WriteAllText(MainScript, main + "var Unchanged := load(\"res://scripts/Other.cs\")\n");
            File.WriteAllText(Path.Combine(Directory, "scripts", "Other.gd"), main);
            File.WriteAllText(Path.Combine(Directory, "scripts", "Other.cs"), main);
            File.WriteAllText(Path.Combine(Directory, "Example.csproj"), "<Project />");
            File.WriteAllText(Path.Combine(Directory, "Example.slnx"), "<Solution />");
            var cache = Path.Combine(Directory, ".godot", "mono", "temp");
            System.IO.Directory.CreateDirectory(cache);
            File.WriteAllText(Path.Combine(cache, "stale.txt"), "stale");
            File.WriteAllText(Path.Combine(Directory, ".godot", "global_script_class_cache.cfg"), "stale");
        }

        public string Script(string name, string language) => Path.Combine(Directory, "scripts",
            sharedFolders ? language : name + "-" + language, name + "." + language);

        public string Resource(string name, string language) => "res://" + Path.GetRelativePath(Directory, Script(name, language)).Replace('\\', '/');

        public int Run(params string[] extra) => CliApplication.CreateCommand().Parse(
            new[] { "--project", Directory, "--scripts", "First,Second", "--undo" }.Concat(extra).ToArray()).Invoke();

        public Dictionary<string, byte[]> Snapshot() => System.IO.Directory.EnumerateFiles(Directory, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes);

        public void AssertUnchanged(Dictionary<string, byte[]> snapshot)
        {
            var current = Snapshot();
            Assert.Equal(snapshot.Keys.Order(), current.Keys.Order());
            foreach (var file in snapshot)
                Assert.Equal(file.Value, current[file.Key]);
        }

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
