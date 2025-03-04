using FlashOWare.Tool.Cli.CodeAnalysis;
using FlashOWare.Tool.Cli.IO;
using FlashOWare.Tool.Core.UsingDirectives;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using System.CommandLine.IO;
using System.Diagnostics;

namespace FlashOWare.Tool.Cli;

public static partial class CliApplication
{
    private static void AddUsingCommand(RootCommand rootCommand, MSBuildWorkspace workspace, IFileSystemAccessor fileSystem)
    {
        var usingCommand = new Command("using", " Analyze or refactor C# using directives.");
        var countCommand = new Command("count", "Count and list the top-level using directives of a C# project.");
        var globalizeCommand = new Command("globalize", "Move top-level using directives to global using directives in a C# project.");

        var projectOption = new Option<FileInfo>(new[] { "--project", "--proj" }, "The path to the project file to operate on (defaults to the current directory if there is only one project).")
            .ExistingOnly();

        var countArgument = new Argument<string[]>("USINGS", "The names of the top-level using directives to count. If usings are not specified, the command will list all top-level directives.");
        countCommand.Add(countArgument);
        countCommand.Add(projectOption);
        countCommand.SetHandler(async (InvocationContext context) =>
        {
            string[] usings = context.ParseResult.GetValueForArgument(countArgument);
            FileInfo? project = context.ParseResult.GetValueForOption(projectOption);
            if (project is null)
            {
                var currentDirectory = fileSystem.GetCurrentDirectory();
                var files = currentDirectory.GetFiles("*.*proj");

                project = files switch
                {
                    [] => throw new InvalidOperationException("Specify a project file. The current working directory does not contain a project file."),
                    [var file] => file,
                    [..] => throw new InvalidOperationException("Specify which project file to use because this folder contains more than one project file."),
                };
            }

            await CountUsingsAsync(workspace, project.FullName, usings.ToImmutableArray(), context.Console, context.GetCancellationToken());
        });

        var globalizeArgument = new Argument<string[]>("USINGS", "The names of the top-level using directives to convert to global using directives. If usings are not specified, the command will globalize all top-level directives.");
        var forceOption = new Option<bool>("--force", "Forces all top-level using directives to be globalized when no usings are specified.");
        globalizeCommand.Add(globalizeArgument);
        globalizeCommand.Add(projectOption);
        globalizeCommand.Add(forceOption);
        globalizeCommand.SetHandler(async (InvocationContext context) =>
        {
            string[] usings = context.ParseResult.GetValueForArgument(globalizeArgument);
            FileInfo? project = context.ParseResult.GetValueForOption(projectOption);
            if (project is null)
            {
                var currentDirectory = fileSystem.GetCurrentDirectory();
                var files = currentDirectory.GetFiles("*.*proj");

                project = files switch
                {
                    [] => throw new InvalidOperationException("Specify a project file. The current working directory does not contain a project file."),
                    [var file] => file,
                    [..] => throw new InvalidOperationException("Specify which project file to use because this folder contains more than one project file."),
                };
            }

            bool isForced = context.ParseResult.GetValueForOption(forceOption);
            if (usings.Length == 0 && !isForced)
            {
                throw new InvalidOperationException("No usings specified. To globalize all top-level using directives, run the command with '--force' option.");
            }

            await GlobalizeUsingsAsync(workspace, project.FullName, usings.ToImmutableArray(), context.Console, context.GetCancellationToken());
        });

        usingCommand.Add(countCommand);
        usingCommand.Add(globalizeCommand);
        rootCommand.Add(usingCommand);
    }

    private static async Task CountUsingsAsync(MSBuildWorkspace workspace, string projectFilePath, ImmutableArray<string> usings, IConsole console, CancellationToken cancellationToken)
    {
        try
        {
            await s_msBuildMutex.WaitAsync(cancellationToken);
            Project project = await workspace.OpenProjectAsync(projectFilePath, null, cancellationToken);

            var result = await UsingCounter.CountAsync(project, usings, cancellationToken);
            console.WriteLine($"{nameof(Project)}: {result.ProjectName}");
            foreach (var usingDirective in result.Usings)
            {
                console.WriteLine($"  {usingDirective.Name}: {usingDirective.Occurrences}");
            }
        }
        catch (OperationCanceledException)
        {
            console.WriteLine("Operation canceled.");
        }
        finally
        {
            s_msBuildMutex.Release();
        }
    }

    private static async Task GlobalizeUsingsAsync(MSBuildWorkspace workspace, string projectFilePath, ImmutableArray<string> usings, IConsole console, CancellationToken cancellationToken)
    {
        try
        {
            await s_msBuildMutex.WaitAsync(cancellationToken);
            Project project = await workspace.OpenProjectAsync(projectFilePath, null, cancellationToken);

            workspace.ThrowIfCannotApplyChanges(ApplyChangesKind.AddDocument, ApplyChangesKind.ChangeDocument);
            var result = await UsingGlobalizer.GlobalizeAsync(project, usings, cancellationToken);

            string? oldProject = null;
            if (project.DocumentIds.Count < result.Project.DocumentIds.Count)
            {
                Debug.Assert(project.FilePath is not null, $"{nameof(Project)} '{project.Name}' has no project file.");
                oldProject = await File.ReadAllTextAsync(project.FilePath, cancellationToken);
            }

            workspace.ApplyChanges(result.Project.Solution);

            if (oldProject is not null && !IsNetFramework(oldProject))
            {
                Debug.Assert(project.FilePath is not null, $"{nameof(Project)} '{project.Name}' has no project file.");
                await File.WriteAllTextAsync(project.FilePath, oldProject, cancellationToken);
            }

            console.WriteLine($"{nameof(Project)}: {result.Project.Name}");

            if (result.Occurrences == 0)
            {
                string message = result.Usings.Count == 1
                    ? $"""No occurrences of Using Directive "{result.Usings.Single().Name}" were globalized."""
                    : $"""No occurrences of {result.Usings.Count} Using Directives were globalized.""";
                console.WriteLine(message);
            }
            else if (result.Occurrences == 1)
            {
                string message = result.Usings.Count == 1
                    ? $"""1 occurrence of Using Directive "{result.Usings.Single().Name}" was globalized to "{result.TargetDocument}"."""
                    : $"""1 occurrence of {result.Usings.Count} Using Directives was globalized to "{result.TargetDocument}".""";
                console.WriteLine(message);
            }
            else
            {
                string message = result.Usings.Count == 1
                    ? $"""{result.Usings.Single().Occurrences} occurrences of Using Directive "{result.Usings.Single().Name}" were globalized to "{result.TargetDocument}"."""
                    : $"""{result.Occurrences} occurrences of {result.Usings.Count} Using Directives were globalized to "{result.TargetDocument}".""";
                console.WriteLine(message);
            }
        }
        catch (OperationCanceledException)
        {
            console.WriteLine("Operation canceled.");
        }
        finally
        {
            s_msBuildMutex.Release();
        }
    }

    private static bool IsNetFramework(string project)
    {
        var index = project.IndexOf("<Project ");
        if (index == -1)
        {
            return true;
        }

        index = project.IndexOf("Sdk=\"", index + "<Project ".Length);
        if (index == -1)
        {
            return true;
        }

        return false;
    }
}
