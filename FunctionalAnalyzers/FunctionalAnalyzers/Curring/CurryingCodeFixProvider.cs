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
using Microsoft.CodeAnalysis.Formatting;

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
            var methodCall = root.FindNode(diagnosticSpan);


            var curryFunc = $"Create curry version of function inside pipe";

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: curryFunc,
                    createChangedDocument: c => CurryingInside(context.Document, methodCall),
                    equivalenceKey: curryFunc),
                diagnostic);
        }

        /// <summary>
        /// Полное каррирование функции
        /// </summary>
        /// <param name="document"></param>
        /// <param name="invocationNode"></param>
        /// <param name="guid"></param>
        /// <param name="invocationInfos"></param>
        /// <returns></returns>
        private async Task<Document> CurryingInside(Document document, SyntaxNode syntaxNode)
        {
            var root = await document.GetSyntaxRootAsync();

            #region готовим замену функции на каррированную

            if (!(syntaxNode is InvocationExpressionSyntax invocationNode))
                return document;

            var argNode = invocationNode.ArgumentList.ChildNodes().FirstOrDefault();
            
            if (!(argNode is ArgumentSyntax arg))
                return document;

            if (!(arg.Expression is InvocationExpressionSyntax innerInvocation))
                return document;

           var searchMethod = innerInvocation.Expression.ToString();

            MethodDeclarationSyntax method = null;
            root.DescendantNodes(x =>
            {
                if (x is MethodDeclarationSyntax methodDeclaration)
                {
                    if (methodDeclaration.Identifier.Text == searchMethod)
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

            var last = args.Last();

            var first = args.First();
            var prevs = args.ToList();

            var second = args.Skip(1)
                .FirstOrDefault();

            var modifiers = string.Join(" ", method.Modifiers.Select(p => p.ToString()));

            var template =  $"{modifiers} Func<{second.Value},{NextTypeChain(prevs, second, last,true)} {searchMethod}({first.Value} {first.Key})";
            

            for (int i = 1; i < argsList.Length; i++)
            {
                template += $"{Environment.NewLine}=> {argsList[i].Key}";
            }

            template += $"{Environment.NewLine}";

            if (method.Body != null)
            {
                template += $"=>{Environment.NewLine}{method.Body.ToString()};";
            }

            if(method.ExpressionBody!=null)
            {
                template += method.ExpressionBody.ToString() + ";";
            }

            SyntaxNode methodAdd = default;

            CSharpSyntaxTree.ParseText(template, options: new CSharpParseOptions(kind: SourceCodeKind.Script))
                .GetRoot()
                .DescendantNodes(x =>
                {
                    if (x is MethodDeclarationSyntax member)
                    {
                        methodAdd = member;
                        return false;
                    }

                    return true;
                }).ToArray();

            var workspace = new AdhocWorkspace();
            methodAdd = Formatter.Format(methodAdd, workspace);

            #endregion

            #region заменяем вызов на вызов каррированной

            var node = invocationNode as InvocationExpressionSyntax;
            var arguments = node.ArgumentList.ChildNodes()
                .Select(a => (a as ArgumentSyntax).GetText().ToString())
                .FirstOrDefault()
                .Replace(searchMethod, string.Empty)
                .Replace("(",string.Empty)
                .Replace(")", string.Empty)
                .Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries);

            template = $"{searchMethod}";
            foreach (var argg in arguments)
            {
                template += $"({argg})";
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
            editor.InsertAfter(method, methodAdd);
            editor.ReplaceNode(innerInvocation, invocationReplace);

            editor.ReplaceNode(editor.OriginalRoot, Formatter.Format(editor.GetChangedRoot(), workspace).NormalizeWhitespace());

            return editor.GetChangedDocument();
        }

        private static string NextTypeChain(List<KeyValuePair<string, string>> prevs, KeyValuePair<string, string> prev, KeyValuePair<string, string> last, bool first = false)
        {
            var idx = prevs.IndexOf(prev);
            var next = prevs.GetRange(idx + 1, prevs.Count - idx - 1);

            string type = string.Empty;

            foreach (var nxt in next)
            {
                type = NextTypeChain(prevs, nxt, last);
            }

            if (next.Count == 0)
            {
                type = last.Value;
            }

            var result = string.Empty;

            if (!first)
            {
                result += $"Func<{prev.Value},";
            }

            return $"{result}{type}>";
        }
    }

    public static class StringExtensions
    {
        public static string Capitalize(this string input)
        {
            switch (input)
            {
                case null: throw new ArgumentNullException(nameof(input));
                case "": throw new ArgumentException($"{nameof(input)} cannot be empty", nameof(input));
                default: return input.First().ToString().ToUpper() + input.Substring(1);
            }
        }
    }
}
