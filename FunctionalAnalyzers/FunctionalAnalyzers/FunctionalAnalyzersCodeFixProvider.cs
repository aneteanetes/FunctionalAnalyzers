using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;

namespace FunctionalAnalyzers
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(FunctionalAnalyzersCodeFixProvider)), Shared]
    public class FunctionalAnalyzersCodeFixProvider : CodeFixProvider
    {
        private const string GeneratePipe_s = "Generate pipe function";
        private const string MakeFuncPipe_s = "Make function piped";

        public sealed override ImmutableArray<string> FixableDiagnosticIds
        {
            get { return ImmutableArray.Create(PipeAnalyzer.DiagnosticId); }
        }

        public sealed override FixAllProvider GetFixAllProvider()
        {
            // See https://github.com/dotnet/roslyn/blob/master/docs/analyzers/FixAllProvider.md for more information on Fix All Providers
            return WellKnownFixAllProviders.BatchFixer;
        }

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

            // TODO: Replace the following code with your own analysis, generating a CodeAction for each fix to suggest
            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;

            // Find the type declaration identified by the diagnostic.
            var declaration = root.FindToken(diagnosticSpan.Start).Parent.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().First();

            // Register a code action that will invoke the fix.
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: GeneratePipe_s,
                    createChangedDocument: c => GeneratePipe(context.Document),
                    equivalenceKey: GeneratePipe_s),
                diagnostic);

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: MakeFuncPipe_s,
                    createChangedDocument: c => MakeFunctionPipe(context.Document, declaration, c),
                    equivalenceKey: MakeFuncPipe_s),
                diagnostic);
        }

        private async Task<Document> GeneratePipe(Document document)
        {
            var pipe = CSharpSyntaxTree.ParseText(
                @"namespace "+ document.Project.Name + @"
                {
                    using System;

                    internal static class Lambda
                    {
                        public static Pipe<T> Pipe<T>(Func<T> f)
                        {            
                            T data = default;
                            Pipe<T> start(Func<T> arg)
                            {
                                data = arg();
                                Pipe<T> pipe(Func<T, T> arg1)
                                {
                                    data = arg1(data);
                                    return pipe;
                                }
                                return pipe;
                            }
                            return start(f);
                        }
                    }

                    delegate Pipe<T> Pipe<T>(Func<T, T> a);
                }");



            return document.Project.AddDocument("Lambda.cs", pipe.GetRoot(), new string[] { "Functions" });
        }

        private async Task<Document> MakeFunctionPipe(Document document, TypeDeclarationSyntax typeDecl, CancellationToken cancellationToken)
        {
             var tree = await document.GetSyntaxTreeAsync();

            // Compute new uppercase name.
            var identifierToken = typeDecl.Identifier;
            var newName = identifierToken.Text.ToUpperInvariant();

            // Get the symbol representing the type to be renamed.
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl, cancellationToken);

            // Produce a new solution that has all references to that type renamed, including the declaration.
            var originalSolution = document.Project.Solution;
            var optionSet = originalSolution.Workspace.Options;
            var newSolution = await Renamer.RenameSymbolAsync(document.Project.Solution, typeSymbol, newName, optionSet, cancellationToken).ConfigureAwait(false);

            // Return the new solution with the now-uppercase type name.
            return document;
        }
    }
}
