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
using FunctionalAnalyzers.Pipe;
using Microsoft.CodeAnalysis.Formatting;

namespace FunctionalAnalyzers
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(PipeCodeFixProvider)), Shared]
    public class PipeCodeFixProvider : CodeFixProvider
    {
        private const string GeneratePipe_s = "Generate pipe function";
        private const string MakeFuncPipe_s = "Make function piped";
        private const string RemoveSeq = "Replace sequential calls with pipe";

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
            var methodCall = root.FindToken(diagnosticSpan.Start).Parent;
            //.Parent;

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

            //context.RegisterCodeFix(
            //    CodeAction.Create(
            //        title: MakeFuncPipe_s,
            //        createChangedDocument: c => MakeFunctionPipe(context.Document, methodCall),
            //        equivalenceKey: MakeFuncPipe_s),
            //    diagnostic);

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: RemoveSeq,
                    createChangedDocument: c => MakeMethodPipeContained(context.Document, methodCall),
                    equivalenceKey: RemoveSeq),
                diagnostic);
        }

        private async Task<Document> GeneratePipe(Document document)
        {
            var pipeNode = CSharpSyntaxTree.ParseText(
                @"namespace " + document.Project.Name + @"
                {
                    using System;
                    using System.Collections.Generic;

                    internal static class Lambda
                    {
                        private static Dictionary<object, object> execFuncs = new Dictionary<object, object>();

                        public static Pipe<T> Pipe<T>(Func<T> f)
                        {
                            T data = default;
                            Func<T> startValue = () => data;

                            Pipe<T> start(Func<T> arg)
                            {
                                data = arg();
                                Pipe<T> pipe(Func<T, T> arg1)
                                {
                                    data = arg1(data);
                                    return pipe;
                                }

                                Pipe<T> pipeObj = pipe;

                                execFuncs[pipeObj] = startValue;

                                return pipe;
                            }
                            return start(f);
                        }

                        public static T Value<T>(Pipe<T> pipe)
                        {
                            if (execFuncs.TryGetValue(pipe, out var val))
                            {
                                return (T)val;
                            }

                            return default;
                        }
                    }

                    delegate Pipe<T> Pipe<T>(Func<T, T> a);
                }");

            var workspace = new AdhocWorkspace();
            var pipe = Formatter.Format(pipeNode.GetRoot(), workspace).NormalizeWhitespace();

            return document.Project.AddDocument("Lambda.cs", pipe, new string[] { "Functions" });
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

        private async Task<Document> MakeMethodPipeContained(Document document, SyntaxNode node)
        {
            if (!(node is MethodDeclarationSyntax methodDeclarationNode))
                return document;

            var result = new PipeVisitor().VisitResult(methodDeclarationNode);

            var doc = document;

            if (result.NodeToReplace != null)
            {
                var editor = await DocumentEditor.CreateAsync(document);


                if (result.BlockToExpressionNode == default)
                {
                    var removeNodes = result.RemoveNodes;
                    for (int i = 0; i < removeNodes.Length; i++)
                    {
                        var removingNode = removeNodes[i];

                        if (i == removeNodes.Length - 1)
                        {
                            editor.ReplaceNode(result.NodeToReplace, result.PipeNode);
                        }
                        else
                        {
                            editor.RemoveNode(removingNode);
                        }

                    }
                }
                else
                {
                    if (methodDeclarationNode.ExpressionBody != null)
                    {
                        editor.ReplaceNode(methodDeclarationNode.ExpressionBody, result.PipeNode);
                    }
                    else
                    {
                        var template = string.Join(" ", methodDeclarationNode.Modifiers.Select(p => p.ToString()))
                            + " " + methodDeclarationNode.ReturnType.ToString()
                            + " " + methodDeclarationNode.Identifier.ToString()
                            + "(" + string.Join(",", methodDeclarationNode.ParameterList.Parameters.Select(p => p.ToString())) + ")"
                            + "=> " + result.PipeNode.ToString();

                        SyntaxNode newNode = default;
                        CSharpSyntaxTree.ParseText(template, options: new CSharpParseOptions(kind: SourceCodeKind.Script))
                            .GetRoot()
                            .DescendantNodes(x =>
                            {
                                newNode = x;
                                return x is CompilationUnitSyntax || x is GlobalStatementSyntax;
                            }).ToArray();
                        editor.ReplaceNode(node, newNode);
                    }
                }

                var workspace = new AdhocWorkspace();
                editor.ReplaceNode(editor.OriginalRoot, Formatter.Format(editor.GetChangedRoot(), workspace).NormalizeWhitespace());

                doc = editor.GetChangedDocument();
            }

            return doc;
        }
    }
}