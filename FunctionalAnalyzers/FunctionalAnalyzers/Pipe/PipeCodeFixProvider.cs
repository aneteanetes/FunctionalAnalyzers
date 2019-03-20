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
using Microsoft.CodeAnalysis.Editing;

namespace FunctionalAnalyzers
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(PipeCodeFixProvider)), Shared]
    public class PipeCodeFixProvider : CodeFixProvider
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

            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;
            var methodCall = root.FindToken(diagnosticSpan.Start).Parent.Parent;

            var pipeExists = context.Document.Project.Documents.FirstOrDefault(x => x.Name == "Lambda.cs") != null;

            if (!pipeExists)
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: GeneratePipe_s,
                        createChangedDocument: c => GeneratePipe(context.Document),
                        equivalenceKey: GeneratePipe_s),
                    diagnostic);
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: MakeFuncPipe_s,
                    createChangedDocument: c => MakeFunctionPipe(context.Document, methodCall),
                    equivalenceKey: MakeFuncPipe_s),
                diagnostic);
        }

        private async Task<Document> GeneratePipe(Document document)
        {
            var pipe = CSharpSyntaxTree.ParseText(
                @"namespace " + document.Project.Name + @"
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

        private async Task<Document> MakeFunctionPipe(Document document, SyntaxNode method)
        {
            var invocations = method.DescendantNodesAndSelf()
                .Where(x => x is InvocationExpressionSyntax)
                .Cast<InvocationExpressionSyntax>()
                .Reverse()
                .ToArray();

            InvocationExpressionSyntax pipeInvocation = null;

            var piped = "Lambda.Pipe";

            foreach (var invocation in invocations)
            {
                piped += $"({invocation.Expression.GetText().ToString()})";
            }

            piped += ";";

            CSharpSyntaxTree.ParseText(piped, options: new CSharpParseOptions(kind: SourceCodeKind.Script))
                .GetRoot()
                .DescendantNodes(x =>
                {
                    if (x is InvocationExpressionSyntax xInvocation)
                    {
                        pipeInvocation = xInvocation;
                        return false;
                    }

                    return true;
                }).ToArray();

            var editor = await DocumentEditor.CreateAsync(document);
            editor.ReplaceNode(method, pipeInvocation);

            return editor.GetChangedDocument();
        }
    }
}