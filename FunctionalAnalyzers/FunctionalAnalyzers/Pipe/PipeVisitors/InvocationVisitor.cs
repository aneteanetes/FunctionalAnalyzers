using FunctionalAnalyzers.Pipe.PipeVisitors;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Text;

namespace FunctionalAnalyzers.Pipe
{
    public class InvocationVisitor : AbstractResultVisitor<MethodInfo>
    {
        MethodInfo value = new MethodInfo();

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            ProcessedNode = node.Expression;

            value.Name = node.Expression.ToString();
            value.Node = node;
            base.VisitInvocationExpression(node);
        }

        public override void VisitArgument(ArgumentSyntax node)
        {
            value.Argument = node.ToString();
            base.VisitArgument(node);
        }

        public override MethodInfo VisitResult(SyntaxNode node)
        {
            this.Visit(node);

            return value;
        }
    }

    public class MethodInfo
    {
        public string Name { get; set; }

        public string Argument { get; set; }

        public SyntaxNode Node { get; set; }
    }
}
