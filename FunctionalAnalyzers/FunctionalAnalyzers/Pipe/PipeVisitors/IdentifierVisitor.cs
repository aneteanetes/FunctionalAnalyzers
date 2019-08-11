using FunctionalAnalyzers.Pipe.PipeVisitors;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Text;

namespace FunctionalAnalyzers.Pipe
{
    public class IdentifierVisitor : AbstractResultVisitor<string>
    {
        string value = string.Empty;

        public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
        {
            ProcessedNode = node.Left;
            value = node.Left.ToString();
        }

        public override void VisitVariableDeclarator(VariableDeclaratorSyntax node)
        {
            ProcessedNode = node;
            value = node.Identifier.ToString();
        }

        public override string VisitResult(SyntaxNode node)
        {
            this.Visit(node);

            return value;
        }
    }
}
