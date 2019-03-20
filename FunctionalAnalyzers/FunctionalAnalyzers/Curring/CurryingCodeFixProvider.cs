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
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(CurryingCodeFixProvider)), Shared]
    public class CurryingCodeFixProvider : CodeFixProvider
    {
        private const string GeneratePipe_s = "Generate curried function";
        private const string MakeFuncPipe_s = "Make function piped";

        public sealed override ImmutableArray<string> FixableDiagnosticIds
        {
            get { return ImmutableArray.Create("CS1503", CurryingAnalyzer.DiagnosticId); }
        }

        public sealed override FixAllProvider GetFixAllProvider()
        {
            return WellKnownFixAllProviders.BatchFixer;
        }

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;
            var methodCall = root.FindToken(diagnosticSpan.Start).Parent.Parent;

            var curryFunc = $"Curry `{methodCall.ToString()}`";

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: curryFunc,
                    createChangedDocument: c => GenerateCarry(context.Document, methodCall),
                    equivalenceKey: curryFunc),
                diagnostic);
        }

        private async Task<Document> GenerateCarry(Document document, SyntaxNode invocationNode)
        {
            var root = await document.GetSyntaxRootAsync();

            #region готовим замену функции на каррированную

            var identifier = ((invocationNode as InvocationExpressionSyntax).Expression as IdentifierNameSyntax).Identifier;
            MethodDeclarationSyntax method = null;
            root.DescendantNodes(x =>
            {
                if (x is MethodDeclarationSyntax methodDeclaration)
                {
                    if (methodDeclaration.Identifier.Text == identifier.Text)
                    {
                        method = methodDeclaration;
                        return false;
                    }
                }
                return true;
            }).ToArray();

            Dictionary<string, string> args = method.ParameterList.ChildNodes().ToDictionary(
                key => (key as ParameterSyntax).Identifier.Text,
                type => (type as ParameterSyntax).ChildNodes().Select(chNode => chNode.GetText()).First().ToString());

            var argsList = args.Select(x => x).ToArray();

            var funcType = args.First().Value;
            var secondArg = args.Last();

            var template = $@"Func<{funcType},{funcType}> {identifier.Text}({secondArg.Value}{secondArg.Key})";

            for (int i = 1; i < argsList.Length; i++)
            {
                template += $"{Environment.NewLine}=> {argsList[i - 1].Key}";
            }

            template += $"{Environment.NewLine}=>{Environment.NewLine}{method.Body.ToString()};";

            MethodDeclarationSyntax methodReplace = null;

            CSharpSyntaxTree.ParseText(template, options: new CSharpParseOptions(kind: SourceCodeKind.Script))
                .GetRoot()
                .DescendantNodes(x =>
                {
                    if (x is MethodDeclarationSyntax xInvocation)
                    {
                        methodReplace = xInvocation;
                        return false;
                    }

                    return true;
                }).ToArray();

            #endregion

            #region заменяем вызов на вызов каррированной

            var node = invocationNode as InvocationExpressionSyntax;
            var arguments = node.ArgumentList.ChildNodes()
                .Select(arg => (arg as ArgumentSyntax).GetText().ToString())
                .Reverse()
                .ToArray();

            template = $"{identifier.Text}";
            foreach (var arg in arguments)
            {
                template += $"({arg})";
            }
            template += ";";

            InvocationExpressionSyntax invocationReplace = null;
            CSharpSyntaxTree.ParseText(template, options: new CSharpParseOptions(kind: SourceCodeKind.Script))
                .GetRoot()
                .DescendantNodes(x =>
                {
                    if (x is InvocationExpressionSyntax xInvocation)
                    {
                        invocationReplace = xInvocation;
                        return false;
                    }

                    return true;
                }).ToArray();

            #endregion

            var editor = await DocumentEditor.CreateAsync(document);
            editor.ReplaceNode(method, methodReplace);
            editor.ReplaceNode(invocationNode, invocationReplace);

            return editor.GetChangedDocument();
        }

        private async Task<Document> MakeFunctionPipe(Document document, SyntaxNode method)
        {
            // Return the new solution with the now-uppercase type name.
            return document;
        }
    }
}
