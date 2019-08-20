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
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(CurryingArgsCodeFixProvider)), Shared]
    public class CurryingArgsCodeFixProvider : CodeFixProvider
    {
        private const string GeneratePipe_s = "Generate curried function";
        private const string MakeFuncPipe_s = "Make function piped";

        public sealed override ImmutableArray<string> FixableDiagnosticIds
        {
            get { return ImmutableArray.Create("CS1503", "CurringArgs"); }
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


            var guid = diagnostic.Descriptor.CustomTags.FirstOrDefault();

            if (CurryingAnalyzer.SelectedNodes.TryGetValue(guid, out var invocationInfos))
            {
                var sameArgs = string.Join(",",
                    invocationInfos
                        .SelectMany(x => x.Arguments)
                        .GroupBy(x => x)
                        .Where(x => x.Count() > 1)
                        .Select(x => x.Key));

                var curryFunc = $"Curry `{sameArgs}` arguments in `{methodCall.ToString()}`";

                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: curryFunc,
                        createChangedDocument: c => FullCurrying(context.Document, methodCall, guid, invocationInfos),                        
                        equivalenceKey: curryFunc),
                    diagnostic);
            }
        }

        /// <summary>
        /// Полное каррирование функции
        /// </summary>
        /// <param name="document"></param>
        /// <param name="invocationNode"></param>
        /// <param name="guid"></param>
        /// <param name="invocationInfos"></param>
        /// <returns></returns>
        private async Task<Document> FullCurrying(Document document, SyntaxNode invocationNode, string guid, List<InvocationInfo> invocationInfos)
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

            var last = new KeyValuePair<string, string>("", method.ReturnType.ToString());

            var first = args.First();
            var prevs = args.ToList();

            var second = args.Skip(1)
                .FirstOrDefault();

            var template = $"Func<{second.Value},{NextTypeChain(prevs, second, last, true)} {identifier.Text}({first.Value} {first.Key})";


            for (int i = 1; i < argsList.Length; i++)
            {
                template += $"{Environment.NewLine}=> {argsList[i].Key}";
            }

            template += $"{Environment.NewLine}";

            if (method.Body != null)
            {
                template += $"=>{Environment.NewLine}{method.Body.ToString()};";
            }

            if (method.ExpressionBody != null)
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
            methodAdd = Formatter.Format(methodAdd, workspace).NormalizeWhitespace();

            #endregion

            #region готовим переменную-вызов каррированной функции

            var invocationsArgs = invocationInfos
                        .SelectMany(x => x.Arguments)
                        .GroupBy(x => x)
                        .Where(x => x.Count() > 1)
                        .Select(x => x.Key);

            var invocationVars = string.Join("", invocationsArgs);

            var callName = invocationInfos.First().Name + invocationVars;

            template = "void x(){ ";
            template += $"var {callName} = {invocationInfos.First().Name}";
            foreach (var item in invocationsArgs)
            {
                template += $"({item})";
            };
            template += ";";
            template += "}";


            LocalDeclarationStatementSyntax curriedVar = null;
            CSharpSyntaxTree.ParseText(template, options: new CSharpParseOptions(kind: SourceCodeKind.Script))
               .GetRoot()
               .DescendantNodes(x =>
               {
                   if (x is            LocalDeclarationStatementSyntax declarationSyntax)
                   {
                       curriedVar = declarationSyntax;
                       return false;
                   }
                   return true;
               }).ToArray();

            #endregion
            
            #region готовим замену всем вызовам

            //ищем ноды

            root.DescendantNodes(x =>
            {
                foreach (var info in invocationInfos)
                {
                    if (SyntaxFactory.AreEquivalent(x, info.InvocationNode))
                    {
                        info.Node = x;
                    }
                }

                return true;

            }).ToArray();

            #endregion


            VariableDeclarationSyntax declaration = null;
            root.DescendantNodes(d =>
            {
                if(d is VariableDeclarationSyntax variableDeclarationSyntax)
                {
                    declaration = variableDeclarationSyntax;
                    return false;
                }

                return true;
            }).ToArray();


            var editor = await DocumentEditor.CreateAsync(document);
            editor.InsertAfter(method, methodAdd);

            editor.InsertAfter(declaration, curriedVar);

            foreach (var item in invocationInfos)
            {
                #region заменяем вызов на вызов каррированной


                template = $"{callName}";
                foreach (var arg in item.Arguments.Except(invocationsArgs))
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

                editor.ReplaceNode(item.Node, invocationReplace);

                #endregion
            }

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
}
