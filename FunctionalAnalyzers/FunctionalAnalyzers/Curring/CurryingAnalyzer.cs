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
    public class CurryingAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "Curring";

        private const string Category = "Functions";

        private static DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, "TOTTTTEL", "Function: '{0}' can be curried", Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: "@$R#%#%KLH%L");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(Rule); } }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(AnalyzeNode, SyntaxKind.InvocationExpression);
        }

        private static readonly List<InvocationExpressionSyntax> selectedNodes = new List<InvocationExpressionSyntax>();

        private static void AnalyzeNode(SyntaxNodeAnalysisContext context)
        {
            var node = (InvocationExpressionSyntax)context.Node;

            var argumentsCount = node.ArgumentList.ChildNodes().Count();

            if (argumentsCount > 1)
            {
                var diagnostic = Diagnostic.Create(Rule, node.GetLocation(), node.ToString());
                context.ReportDiagnostic(diagnostic);
            }
        }

    }
}