using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace gd2cs.Project;

// Applies the Godot project-file changes required when a script changes language.
public sealed class GodotProjectIntegration
{
    private const string CSharpFeature = "C#";
    private readonly string projectPath;

    public GodotProjectIntegration(string projectPath)
    {
        this.projectPath = Path.GetFullPath(projectPath);
    }

    public bool IsCSharpProject()
    {
        var config = File.ReadAllText(Path.Combine(projectPath, "project.godot"));
        return Regex.IsMatch(config, @"config/features\s*=.*""C#""", RegexOptions.Singleline);
    }

    // Adds or removes C# without disturbing the other project feature entries.
    public void ReplaceCSharpFeature(bool enabled)
    {
        var configPath = Path.Combine(projectPath, "project.godot");
        var config = File.ReadAllText(configPath);
        config = Regex.Replace(
            config,
            @"config/features=PackedStringArray\((?<items>[^)]*)\)",
            match => ReplaceFeature(match, enabled));
        File.WriteAllText(configPath, config);
    }

    // Creates the minimal Godot C# project and XML solution when either is absent.
    public void EnsureCSharpProject(string assemblyName)
    {
        EnsureDotnetConfiguration(assemblyName);

        var projectFile = Directory
            .EnumerateFiles(projectPath, "*.csproj", SearchOption.TopDirectoryOnly)
            .FirstOrDefault();
        if (projectFile is null)
        {
            projectFile = Path.Combine(projectPath, assemblyName + ".csproj");
            WriteXml(projectFile, CreateCSharpProject());
        }

        if (!Directory.EnumerateFiles(projectPath, "*.slnx", SearchOption.TopDirectoryOnly).Any())
        {
            var solutionFile = Path.Combine(projectPath, assemblyName + ".slnx");
            WriteXml(solutionFile, CreateSolution(Path.GetFileName(projectFile)));
        }
    }

    // Updates only Script ext_resources that point at the translated source path.
    public List<string> RewriteReferences(string sourceResourcePath, string targetResourcePath)
    {
        var changed = new List<string>();
        foreach (var file in ProjectResourceFiles())
        {
            var content = File.ReadAllText(file);
            var updated = Regex.Replace(
                content,
                @"(?m)^\[ext_resource(?<attributes>[^\]\r\n]*)\](?=\r?$)",
                match => RewriteReference(match, sourceResourcePath, targetResourcePath));
            if (updated == content)
                continue;
            File.WriteAllText(file, updated);
            changed.Add(file);
        }
        return changed;
    }

    // Updates GDScript preload/load declarations that embed the translated script path.
    public List<string> RewriteScriptReferences(string sourceResourcePath, string targetResourcePath)
    {
        var changed = new List<string>();
        var pattern = $@"(?<loader>preload|load)\(""{Regex.Escape(sourceResourcePath)}""\)";
        foreach (var file in Directory.EnumerateFiles(projectPath, "*.*", SearchOption.AllDirectories)
                     .Where(path => path.EndsWith(".gd", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            var content = File.ReadAllText(file);
            var replacement = targetResourcePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                ? "load(\"" + targetResourcePath + "\")"
                : "preload(\"" + targetResourcePath + "\")";
            var updated = Regex.Replace(content, pattern, replacement, RegexOptions.IgnoreCase);
            var targetKeyword = targetResourcePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ? "var" : "const";
            updated = Regex.Replace(
                updated,
                @"(?m)^(?<indent>\s*)(?<keyword>const|var)(?<rest>\s+\w+\s*:=\s*(?:load|preload)\()",
                $"${{indent}}{targetKeyword}${{rest}}");
            if (updated == content)
                continue;
            File.WriteAllText(file, updated);
            changed.Add(file);
        }
        return changed;
    }

    // Removes generated C# metadata that can otherwise keep stale script registrations alive.
    public void ClearGodotCSharpCaches()
    {
        var monoTempPath = Path.Combine(projectPath, ".godot", "mono", "temp");
        if (Directory.Exists(monoTempPath))
            Directory.Delete(monoTempPath, recursive: true);

        var globalClassCachePath = Path.Combine(projectPath, ".godot", "global_script_class_cache.cfg");
        if (File.Exists(globalClassCachePath))
            File.Delete(globalClassCachePath);
    }

    // Converts a physical project path to the stable resource form stored by Godot files.
    public string ToResourcePath(string physicalPath)
    {
        var relative = Path.GetRelativePath(projectPath, Path.GetFullPath(physicalPath));
        if (relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || relative == "..")
            throw new ArgumentException("Script path must be inside the Godot project.", nameof(physicalPath));
        return "res://" + relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    // Produces a filesystem-safe assembly name from Godot's display name.
    public string ReadAssemblyName()
    {
        var config = File.ReadAllText(Path.Combine(projectPath, "project.godot"));
        var match = Regex.Match(config, "config/name=\"(?<name>[^\"]+)\"");
        var name = match.Success ? match.Groups["name"].Value : new DirectoryInfo(projectPath).Name;
        return Regex.Replace(name, "[^A-Za-z0-9_.-]", "-");
    }

    private static string ReplaceFeature(Match match, bool enabled)
    {
        var features = Regex.Matches(match.Groups["items"].Value, "\"(?<value>[^\"]*)\"")
            .Select(item => item.Groups["value"].Value)
            .Where(item => !string.Equals(item, CSharpFeature, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (enabled)
            features.Add(CSharpFeature);
        return "config/features=PackedStringArray(" +
               string.Join(", ", features.Select(item => $"\"{item}\"")) +
               ")";
    }

    // Adds the section only when Godot has not configured a C# assembly yet.
    private void EnsureDotnetConfiguration(string assemblyName)
    {
        var configPath = Path.Combine(projectPath, "project.godot");
        var config = File.ReadAllText(configPath);
        if (config.Contains("[dotnet]", StringComparison.Ordinal))
            return;
        config = config.TrimEnd('\r', '\n') +
                 $"\n\n[dotnet]\n\nproject/assembly_name=\"{assemblyName}\"\n";
        File.WriteAllText(configPath, config);
    }

    private static XDocument CreateCSharpProject() => new(
        new XElement(
            "Project",
            new XAttribute("Sdk", "Godot.NET.Sdk/4.7.2"),
            new XElement(
                "PropertyGroup",
                new XElement("TargetFramework", "net8.0"),
                new XElement("EnableDynamicLoading", "true"))));

    // Mirrors Godot's compact .slnx structure and points it at the selected C# project.
    private static XDocument CreateSolution(string projectFile) => new(
        new XElement(
            "Solution",
            new XElement(
                "Configurations",
                new XElement("BuildType", new XAttribute("Name", "Debug")),
                new XElement("BuildType", new XAttribute("Name", "ExportDebug")),
                new XElement("BuildType", new XAttribute("Name", "ExportRelease"))),
            new XElement("Project", new XAttribute("Path", projectFile))));

    // XDocument supplies escaping and indentation while the no-declaration form matches .slnx files.
    private static void WriteXml(string path, XDocument document) =>
        File.WriteAllText(path, document.ToString() + Environment.NewLine);

    private List<string> ProjectResourceFiles() =>
        Directory.EnumerateFiles(projectPath, "*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".tscn", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".tres", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static string RewriteReference(
        Match match,
        string sourceResourcePath,
        string targetResourcePath)
    {
        var attributes = match.Groups["attributes"].Value;
        if (!Regex.IsMatch(attributes, "(?:^|\\s)type=\"Script\"(?:\\s|$)"))
            return match.Value;

        var pathMatch = Regex.Match(attributes, "(?:^|\\s)path=\"(?<path>[^\"]+)\"");
        if (!pathMatch.Success ||
            !string.Equals(pathMatch.Groups["path"].Value, sourceResourcePath, StringComparison.OrdinalIgnoreCase))
        {
            return match.Value;
        }

        // A script UID belongs to the old language resource and must not follow the new path.
        attributes = Regex.Replace(attributes, "\\s+uid=\"[^\"]*\"", "");
        attributes = Regex.Replace(
            attributes,
            "(?<=\\spath=\")[^\"]+(?=\")",
            targetResourcePath);
        return "[ext_resource" + attributes + "]";
    }
}
