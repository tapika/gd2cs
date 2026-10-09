using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics;
using gd2cs.Language;
using gd2cs.Parsing;
using gd2cs.Project;

namespace gd2cs.Cli;

public static class CliApplication
{
    public static RootCommand CreateCommand(GodotTypeCatalog? godotTypes = null)
    {
        var projectOption = new Option<DirectoryInfo>("--project")
        {
            Description = "Path to the Godot project.",
            Required = true
        };
        var scriptOption = new Option<string>("--scripts", "--script")
        {
            Description = "Comma-separated script paths or names.",
            Required = true
        };
        var targetOption = new Option<string?>("--to")
        {
            Description = "Target language: cs or gd. Multiple targets may be comma-separated."
        };
        var godotOption = new Option<FileInfo?>("--godot")
        {
            Description = "Path to the Godot .NET executable."
        };
        var verboseOption = new Option<bool>("--verbose")
        {
            Description = "Write detailed progress information."
        };
        var resetOption = new Option<bool>("--reset-before")
        {
            Description = "Reset the project before transpiling."
        };
        var scriptOnlyOption = new Option<bool>("--scriptonly")
        {
            Description = "Transpile the scripts without modifying the Godot project."
        };
        var postBuildOption = new Option<bool>("--postbuild")
        {
            Description = "Backtranslate the C# scripts to GDScript after each C# build."
        };

        var command = new RootCommand("Translate Godot scripts between GDScript and C#.");
        command.Options.Add(projectOption);
        command.Options.Add(scriptOption);
        command.Options.Add(targetOption);
        command.Options.Add(godotOption);
        command.Options.Add(verboseOption);
        command.Options.Add(resetOption);
        command.Options.Add(scriptOnlyOption);
        command.Options.Add(postBuildOption);
        command.SetAction(parseResult => Execute(() => Run(
                parseResult,
                projectOption,
                scriptOption,
                targetOption,
                godotOption,
                verboseOption,
                resetOption,
                scriptOnlyOption,
                postBuildOption,
                godotTypes)));
        return command;
    }

    private static int Execute(Func<int> action)
    {
        try
        {
            return action();
        }
        catch (ParseException exception)
        {
            var path = exception.SourcePath ?? "gd2cs";
            Console.Error.WriteLine($"{path}({exception.Line},{exception.Column}): error: {exception.Message}");
            if (exception.SourceLine is not null)
            {
                Console.Error.WriteLine(exception.SourceLine);
                var prefix = exception.SourceLine[..Math.Min(exception.Column - 1, exception.SourceLine.Length)];
                Console.Error.WriteLine(string.Concat(prefix.Select(character => character == '\t' ? '\t' : ' ')) + "^");
            }
            return 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"gd2cs: error: {exception.Message}");
            return 1;
        }
    }

    private static int Run(
        ParseResult parseResult,
        Option<DirectoryInfo> projectOption,
        Option<string> scriptOption,
        Option<string?> targetOption,
        Option<FileInfo?> godotOption,
        Option<bool> verboseOption,
        Option<bool> resetOption,
        Option<bool> scriptOnlyOption,
        Option<bool> postBuildOption,
        GodotTypeCatalog? godotTypes)
    {
        var project = parseResult.GetValue(projectOption)!;
        var scripts = parseResult.GetValue(scriptOption)!
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (scripts.Count == 0)
            throw new ArgumentException("--scripts must contain at least one script path or name.");
        var target = parseResult.GetValue(targetOption);
        var godot = parseResult.GetValue(godotOption);
        var scriptOnly = parseResult.GetValue(scriptOnlyOption);
        var postBuild = parseResult.GetValue(postBuildOption);

        if (!project.Exists)
            throw new DirectoryNotFoundException(project.FullName);
        if (!File.Exists(Path.Combine(project.FullName, "project.godot")))
            throw new InvalidOperationException("--project does not contain project.godot.");

        if (parseResult.GetValue(resetOption) && !scriptOnly)
            ResetProject(project.FullName);

        var projectIntegration = new GodotProjectIntegration(project.FullName);
        // Detect the direction once, before integration changes the project's active language.
        var targets = ParseTargets(target, projectIntegration.IsCSharpProject());
        var transpiler = new Transpiler(
            godotTypes ?? GodotTypeCatalog.Load(project.FullName, godot?.FullName));

        foreach (var outputLanguage in targets)
        {
            var sourceExtension = outputLanguage == "cs" ? ".gd" : ".cs";
            var targetExtension = outputLanguage == "cs" ? ".cs" : ".gd";
            foreach (var script in scripts)
            {
                var resourceRelativeScript = script.StartsWith("res://", StringComparison.OrdinalIgnoreCase)
                    ? script[6..]
                    : script;
                var relativeScript = resourceRelativeScript.Replace('/', Path.DirectorySeparatorChar);
                var scriptWithoutExtension = Path.ChangeExtension(relativeScript, null);
                var scriptDirectory = relativeScript.Contains(Path.DirectorySeparatorChar)
                    ? project.FullName
                    : Path.Combine(project.FullName, "scripts");
                var basePath = Path.Combine(scriptDirectory, scriptWithoutExtension);
                var paths = ResolvePaths(project.FullName, basePath, scriptWithoutExtension, sourceExtension, targetExtension);
                var sourcePath = paths.Source;
                var targetPath = paths.Target;
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                transpiler.TranspileFile(sourcePath, targetPath);

                if (scriptOnly)
                {
                    Console.WriteLine($"Converted from {Path.GetFileName(sourcePath)} to {Path.GetFileName(targetPath)}");
                    continue;
                }

                var sourceResourcePath = projectIntegration.ToResourcePath(sourcePath);
                var targetResourcePath = projectIntegration.ToResourcePath(targetPath);
                var changedReferences = projectIntegration.RewriteReferences(
                    sourceResourcePath,
                    targetResourcePath);
                projectIntegration.RewriteScriptReferences(sourceResourcePath, targetResourcePath);
                if (paths.IsPaired)
                    ActivateFolder(Path.GetDirectoryName(sourcePath)!, Path.GetDirectoryName(targetPath)!);
                else if (outputLanguage == "cs")
                    BackupSource(sourcePath);
                Console.WriteLine($"Converted from {Path.GetFileName(sourcePath)} to {Path.GetFileName(targetPath)}");
                if (parseResult.GetValue(verboseOption))
                    Console.WriteLine($"Updated {changedReferences.Count} project resource file(s).");
            }

            if (scriptOnly)
                continue;
            projectIntegration.ReplaceCSharpFeature(enabled: outputLanguage == "cs");
            if (outputLanguage == "cs")
                projectIntegration.EnsureCSharpProject(
                    projectIntegration.ReadAssemblyName(),
                    postBuild ? string.Join(',', scripts) : null,
                    postBuild ? CurrentCommand() : null);
            else
                projectIntegration.ClearGodotCSharpCaches();
        }

        return 0;
    }

    private static (string Source, string Target, bool IsPaired) ResolvePaths(
        string projectPath,
        string basePath,
        string scriptWithoutExtension,
        string sourceExtension,
        string targetExtension)
    {
        var sourcePath = basePath + sourceExtension;
        var sourceSuffix = sourceExtension[1..];
        var targetSuffix = targetExtension[1..];
        var sourceDirectory = Path.GetDirectoryName(basePath)!;

        if (Path.GetFileName(sourceDirectory).EndsWith(sourceSuffix, StringComparison.OrdinalIgnoreCase))
        {
            var targetDirectory = sourceDirectory[..^(sourceSuffix.Length)] + targetSuffix;
            return (sourcePath, Path.Combine(targetDirectory, Path.GetFileName(basePath) + targetExtension), true);
        }

        if (!File.Exists(sourcePath))
        {
            var scriptName = Path.GetFileName(scriptWithoutExtension);
            var scriptsDirectory = Path.Combine(projectPath, "scripts");
            var namedDirectory = Path.Combine(scriptsDirectory, scriptName + "-" + sourceSuffix);
            var namedSource = Path.Combine(namedDirectory, scriptName + sourceExtension);
            var useNamedDirectory = File.Exists(namedSource);
            sourceDirectory = useNamedDirectory ? namedDirectory : Path.Combine(scriptsDirectory, sourceSuffix);
            sourcePath = useNamedDirectory ? namedSource : Path.Combine(sourceDirectory, scriptName + sourceExtension);
            var targetFolder = useNamedDirectory ? scriptName + "-" + targetSuffix : targetSuffix;
            var targetDirectory = Path.Combine(scriptsDirectory, targetFolder);
            return (sourcePath, Path.Combine(targetDirectory, scriptName + targetExtension), true);
        }

        return (sourcePath, basePath + targetExtension, false);
    }

    private static void ActivateFolder(string sourceDirectory, string targetDirectory)
    {
        File.WriteAllText(Path.Combine(sourceDirectory, ".gdignore"), string.Empty);
        var targetIgnore = Path.Combine(targetDirectory, ".gdignore");
        if (File.Exists(targetIgnore))
            File.Delete(targetIgnore);
    }

    private static List<string> ParseTargets(string? target, bool isCSharpProject)
    {
        if (target is null)
            return [isCSharpProject ? "gd" : "cs"];

        var result = target.ToLowerInvariant()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (result.Length == 0 || result.Any(value => value is not ("cs" or "gd")))
            throw new ArgumentException("--to values must be cs or gd.");
        return new List<string>(result);
    }

    private static void ResetProject(string projectPath)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = projectPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add($"safe.directory={projectPath}");
        process.StartInfo.ArgumentList.Add("--no-optional-locks");
        process.StartInfo.ArgumentList.Add("reset");
        process.StartInfo.ArgumentList.Add("--hard");
        process.StartInfo.ArgumentList.Add("HEAD");
        process.Start();
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException("Git reset failed; no files were transpiled.\n" + standardOutput + standardError);
    }

    private static void BackupSource(string sourcePath)
    {
        var backupPath = sourcePath + ".bkp";
        if (File.Exists(backupPath))
            File.Delete(backupPath);
        File.Move(sourcePath, backupPath);
    }

    private static string CurrentCommand()
    {
        var command = Path.GetFullPath(Environment.GetCommandLineArgs()[0]);
        return command.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? "dotnet \"" + command + "\""
            : "\"" + command + "\"";
    }
}
