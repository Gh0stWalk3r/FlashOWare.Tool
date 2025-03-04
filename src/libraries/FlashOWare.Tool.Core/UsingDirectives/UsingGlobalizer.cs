using FlashOWare.Tool.Core.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Diagnostics;

namespace FlashOWare.Tool.Core.UsingDirectives;

public static class UsingGlobalizer
{
    private const string DefaultTargetDocument = "GlobalUsings.cs";

    public static Task<UsingGlobalizationResult> GlobalizeAsync(Project project, CancellationToken cancellationToken = default)
    {
        return GlobalizeAsync(project, ImmutableArray<string>.Empty, cancellationToken);
    }

    public static Task<UsingGlobalizationResult> GlobalizeAsync(Project project, string localUsing, CancellationToken cancellationToken = default)
    {
        return GlobalizeAsync(project, ImmutableArray.Create(localUsing), cancellationToken);
    }

    public static async Task<UsingGlobalizationResult> GlobalizeAsync(Project project, ImmutableArray<string> usings, CancellationToken cancellationToken = default)
    {
        RoslynUtilities.ThrowIfNotCSharp(project, LanguageVersion.CSharp10, LanguageFeatures.GlobalUsingDirective);

        Compilation? compilation = await project.GetCompilationAsync(cancellationToken);
        if (compilation is null)
        {
            throw new InvalidOperationException($"{nameof(Project)}.{nameof(Project.SupportsCompilation)} = {project.SupportsCompilation} ({project.Name})");
        }

        var result = new UsingGlobalizationResult(project, DefaultTargetDocument);
        result.Initialize(usings);

        if (RoslynUtilities.IsGeneratedCode(compilation))
        {
            return result;
        }

        var solution = project.Solution;

        foreach (Document document in project.Documents)
        {
            if (RoslynUtilities.IsGeneratedCode(document))
            {
                continue;
            }

            SyntaxNode? syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken);
            if (syntaxRoot is null)
            {
                throw new InvalidOperationException($"{nameof(Document)}.{nameof(Document.SupportsSyntaxTree)} = {document.SupportsSyntaxTree} ({document.Name})");
            }

            var compilationUnit = (CompilationUnitSyntax)syntaxRoot;

            RoslynUtilities.ThrowIfContainsError(compilationUnit);

            if (RoslynUtilities.IsGeneratedCode(compilationUnit))
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            solution = await GlobalizeAsync(solution, project.Id, document, compilationUnit, usings, result, cancellationToken);
        }

        var newProject = solution.GetProject(project.Id);
        Debug.Assert(newProject is not null, $"{nameof(ProjectId)} is not a {nameof(ProjectId)} of a {nameof(Project)} that is part of this {nameof(Solution)}.");
        result.Update(newProject);

        result.Verify();
        return result;
    }

    private static async Task<Solution> GlobalizeAsync(Solution solution, ProjectId projectId, Document document, CompilationUnitSyntax compilationUnit, ImmutableArray<string> usings, UsingGlobalizationResult result, CancellationToken cancellationToken)
    {
        var usingNodes = compilationUnit.Usings.Where(IsLocalUsing);
        if (usings.Length != 0)
        {
            usingNodes = usingNodes.Where(usingNode => usings.Contains(usingNode.Name.ToString()));
        }

        UsingDirectiveSyntax[] globalizedNodes = usingNodes.ToArray();
        if (globalizedNodes.Length == 0)
        {
            return solution;
        }
        string[] globalizedIdentifiers = globalizedNodes.Select(static usingNode => usingNode.Name.ToString()).ToArray();

        var newRoot = compilationUnit.RemoveNodes(globalizedNodes, SyntaxRemoveOptions.KeepLeadingTrivia);
        Debug.Assert(newRoot is not null, "The root node itself is removed.");

        result.Update(globalizedIdentifiers);
        solution = solution.WithDocumentSyntaxRoot(document.Id, newRoot);

        Project? project = solution.GetProject(projectId);
        Debug.Assert(project is not null, $"{nameof(ProjectId)} is not a {nameof(ProjectId)} of a {nameof(Project)} that is part of this {nameof(Solution)}.");

        if (project.Documents.SingleOrDefault(static document => document.Name == DefaultTargetDocument && document.Folders.Count == 0) is { } globalUsings)
        {
            SyntaxNode? globalUsingsSyntaxRoot = await globalUsings.GetSyntaxRootAsync(cancellationToken);
            if (globalUsingsSyntaxRoot is null)
            {
                throw new InvalidOperationException($"{nameof(Document)}.{nameof(Document.SupportsSyntaxTree)} = {globalUsings.SupportsSyntaxTree} ({globalUsings.Name})");
            }

            var globalUsingsCompilationUnit = (CompilationUnitSyntax)globalUsingsSyntaxRoot;
            var existingUsings = globalUsingsCompilationUnit.Usings.Select(static usingDirective => usingDirective.Name.ToString());
            string[] addedUsings = globalizedIdentifiers.Except(existingUsings, StringComparer.Ordinal).ToArray();
            if (addedUsings.Length != 0)
            {
                var options = await document.GetOptionsAsync(cancellationToken);
                var nodes = CSharpSyntaxFactory.GlobalUsingDirectives(addedUsings, options);
                var newUsings = globalUsingsCompilationUnit.Usings.AddRange(nodes);
                var newGlobalUsingsRoot = globalUsingsCompilationUnit.WithUsings(newUsings);
                solution = solution.WithDocumentSyntaxRoot(globalUsings.Id, newGlobalUsingsRoot);
            }
        }
        else
        {
            var options = await document.GetOptionsAsync(cancellationToken);
            IEnumerable<string> addedUsings = globalizedIdentifiers.Distinct(StringComparer.Ordinal);
            var syntaxRoot = CSharpSyntaxFactory.GlobalUsingDirectivesRoot(addedUsings, options);
            solution = solution.AddDocument(DocumentId.CreateNewId(projectId), DefaultTargetDocument, syntaxRoot);
        }

        return solution;
    }

    private static bool IsLocalUsing(UsingDirectiveSyntax usingNode)
    {
        return usingNode.Alias is null
            && usingNode.StaticKeyword.IsKind(SyntaxKind.None)
            && usingNode.GlobalKeyword.IsKind(SyntaxKind.None);
    }
}
