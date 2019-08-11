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

        private static DiagnosticDescriptor CurringRule = new DiagnosticDescriptor(DiagnosticId, "Functions", "Function: '{0}' can be curried", Category, DiagnosticSeverity.Hidden, isEnabledByDefault: true);
        private static DiagnosticDescriptor CurringArgsRule = new DiagnosticDescriptor("CurringArgs", "Arguments", $"Function: Arguments in '{{0}}' can be curried", Category, DiagnosticSeverity.Hidden, isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(CurringRule, CurringArgsRule); } }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxTreeAction(AnalyzeTree);

            context.RegisterSyntaxNodeAction(AnalyzeNode, SyntaxKind.InvocationExpression);
        }

        public static Dictionary<string, List<InvocationInfo>> SelectedNodes = new Dictionary<string, List<InvocationInfo>>();

        private static void AnalyzeTree(SyntaxTreeAnalysisContext context)
        {
            var invocations = new Dictionary<string, List<InvocationInfo>>();

            context.Tree.GetRoot().DescendantNodes(node =>
            {
                if (node is InvocationExpressionSyntax invocation)
                {
                    if (invocation.Expression is IdentifierNameSyntax identifier)
                    {
                        var id = identifier.ToString();
                        if (!invocations.ContainsKey(id))
                        {
                            invocations.Add(id, new List<InvocationInfo>());
                        }

                        invocations[id].Add(new InvocationInfo()
                        {
                            InvocationNode = invocation,
                            Name = id,
                            ArgsCount = invocation.ArgumentList.ChildNodes().Count(),
                            Arguments = invocation.ArgumentList.ChildNodes().Select(x => (x as ArgumentSyntax).GetText().ToString()).ToList()
                        });
                    }
                }

                return true;
            }).ToArray();

            var curryPossible = new Dictionary<string, List<InvocationInfo>>();

            foreach (var sameName in invocations)
            {
                foreach (var countGroup in sameName.Value.GroupBy(x => x.ArgsCount))
                {
                    foreach (var val in countGroup)
                    {
                        var sames = countGroup.Where(x => x != val && x.Arguments.Intersect(val.Arguments).Count() > 0 && !x.MarkAndSweep).ToList();
                        foreach (var same in sames)
                        {
                            same.MarkAndSweep = true;
                        }

                        if (!curryPossible.ContainsKey(val.Name))
                        {
                            curryPossible.Add(val.Name, new List<InvocationInfo>());
                        }

                        curryPossible[val.Name].AddRange(sames);
                    }
                }
            }

            SelectedNodes.Clear();

            if (curryPossible.Count>0)
            {
                foreach (var possible in curryPossible)
                {
                    if (possible.Value.Count == 0)
                        continue;

                    var node = possible.Value.Last().InvocationNode;

                    var sameArgs = possible.Value
                        .SelectMany(x => x.Arguments)
                        .GroupBy(x => x)
                        .Where(x => x.Count() > 1)
                        .Select(x => x.Key)
                        .ToArray();

                    var guid = Guid.NewGuid().ToString();

                    var rule = new DiagnosticDescriptor(
                        "CurringArgs",
                        "Arguments", 
                        $"Function: Arguments ({string.Join(",",sameArgs)}) in '{{0}}' can be curried", 
                        Category, 
                        DiagnosticSeverity.Hidden, 
                        isEnabledByDefault: true, 
                        customTags: guid);
                    
                    SelectedNodes.Add(guid, possible.Value);

                    var diagnostic = Diagnostic.Create(rule, node.GetLocation(), node.ToString());
                    context.ReportDiagnostic(diagnostic);
                }
            }
        }

        private static void AnalyzeNode(SyntaxNodeAnalysisContext context)
        {
            // IF IN PIPE

            //var node = (InvocationExpressionSyntax)context.Node;

            //var argumentsCount = node.ArgumentList.ChildNodes().Count();

            //if (argumentsCount > 1)
            //{
            //    var diagnostic = Diagnostic.Create(Rule, node.GetLocation(), node.ToString());
            //    context.ReportDiagnostic(diagnostic);
            //}
        }
    }

    public class InvocationInfo
    {
        public string Name { get; set; }

        public int ArgsCount { get; set; }

        public List<string> Arguments { get; set; }

        public InvocationExpressionSyntax InvocationNode { get; set; }

        public SyntaxNode Node { get; set; }

        public bool MarkAndSweep { get; set; }
    }
}