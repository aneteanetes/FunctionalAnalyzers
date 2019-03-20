using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
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

        private static DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Error, isEnabledByDefault: true, description: Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(Rule); } }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(AnalyzeNode, SyntaxKind.InvocationExpression);
        }

        private static readonly List<InvocationExpressionSyntax> selectedNodes = new List<InvocationExpressionSyntax>();

        private static void AnalyzeNode(SyntaxNodeAnalysisContext context)
        {
            var node = (InvocationExpressionSyntax)context.Node;

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

            Console.WriteLine();
        }

    }
}