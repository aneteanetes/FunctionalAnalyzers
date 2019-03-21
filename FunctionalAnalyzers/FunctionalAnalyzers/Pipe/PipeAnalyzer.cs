using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using FunctionalAnalyzers.Pipe;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FunctionalAnalyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class PipeAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "Pipe";

        private static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.AnalyzerTitle), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.AnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString Description = new LocalizableResourceString(nameof(Resources.AnalyzerDescription), Resources.ResourceManager, typeof(Resources));

        private const string Category = "Functions";

        private static DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: Description);
        private static DiagnosticDescriptor RuleMethod = new DiagnosticDescriptor(DiagnosticId, ".", "Function: '{0}' method can contains pipe instead of sequential calls", Category, DiagnosticSeverity.Error, isEnabledByDefault: true, description: Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(Rule,RuleMethod); } }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(AnalyzeNode, SyntaxKind.InvocationExpression);
            //context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
        }

        private static readonly List<InvocationExpressionSyntax> selectedNodes = new List<InvocationExpressionSyntax>();

        private static void AnalyzeNode(SyntaxNodeAnalysisContext context)
        {
            var node = (InvocationExpressionSyntax)context.Node;

            bool alreadyLambda = false;
            node.DescendantTokens(token =>
             {
                 if (token is IdentifierNameSyntax nameTokenSyntax)
                 {
                     if(nameTokenSyntax.Identifier.Text == "Lambda")
                     {
                         alreadyLambda = true;
                     }
                     return false;
                 }

                 return true;
             }).ToArray();

            if (alreadyLambda)
                return;

            List<InvocationExpressionSyntax> pipeAvailableFor = new List<InvocationExpressionSyntax>();

            node.ArgumentList.DescendantNodes(descNode =>
            {
                if (descNode is InvocationExpressionSyntax invokeNode)
                {
                    pipeAvailableFor.Add(invokeNode);
                    return false;
                }

                return true;
            }).ToArray();

            //can add node
            if (pipeAvailableFor.Count == 0)
                return;

            //node already added
            var nested = selectedNodes.SelectMany(selectedNode => selectedNode.DescendantNodes().Where(d => d == selectedNode));
            if (nested.Count() != 0)
                return;

            selectedNodes.Add(node);

            var nestedMethods = string.Join(", ", pipeAvailableFor.Select(m => m.ToString()));

            var diagnostic = Diagnostic.Create(Rule, node.GetLocation(), nestedMethods, node.ToString());
            context.ReportDiagnostic(diagnostic);
        }

        private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
        {
            if (!(context.Node is MethodDeclarationSyntax methodDeclarationNode))
                return;

            var result = new PipeVisitor().VisitResult(methodDeclarationNode);

            if (result == default)
                return;

            var diagnostic = Diagnostic.Create(RuleMethod, methodDeclarationNode.GetLocation(), methodDeclarationNode.Identifier.ToString());
            context.ReportDiagnostic(diagnostic);
        }

        static int Method(int a) => a;

        static int Example()
        {
            var a = 15;

            var data = Method(a);

            data = Method(a);

            var x = Method(data);

            return x;
        }

    }
}