using FunctionalAnalyzers.Pipe.PipeVisitors;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Text;

namespace FunctionalAnalyzers.Pipe
{
    public class InvocationVisitor : AbstractResultVisitor<VisitMethodInfo>
    {
        VisitMethodInfo value = new VisitMethodInfo();
        
        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            ProcessedNode = node.Expression;

            value.Name = node.Expression.ToString();
            value.Node = node;
            base.VisitInvocationExpression(node);
        }

        public override void VisitArgument(ArgumentSyntax node)
        {
            value.Arguments.Add(node.ToString());
            base.VisitArgument(node);
        }

        public override VisitMethodInfo VisitResult(SyntaxNode node)
        {
            this.Visit(node);

            return value;
        }
    }

    public class VisitMethodInfo
    {
        public string Name { get; set; }

        public List<string> Arguments { get; set; } = new List<string>();

        public SyntaxNode Node { get; set; }
    }
}
