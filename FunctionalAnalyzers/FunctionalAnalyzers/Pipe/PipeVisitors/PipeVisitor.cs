using FunctionalAnalyzers.Pipe.PipeVisitors;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FunctionalAnalyzers.Pipe
{
    public class PipeVisitor : AbstractResultVisitor<PipeResult>
    {
        private readonly List<PipePart> Pipe = new List<PipePart>();

        private IdentifierVisitor IdentifierVisitor = new IdentifierVisitor();
        private InvocationVisitor InvocationVisitor = new InvocationVisitor();

        public override PipeResult VisitResult(SyntaxNode node)
        {
            this.Visit(node);

            if (Pipe.Count == 0)
                return default;

            var distinct = Pipe.Distinct().ToList();

            string template = "Lambda.Pipe";

            foreach (var item in distinct)
            {
                template += $"({item.Method.Name})";
            }

            template += ";";

            InvocationExpressionSyntax pipeInvocation = default;
            CSharpSyntaxTree.ParseText(template, options: new CSharpParseOptions(kind: SourceCodeKind.Script))
                .GetRoot()
                .DescendantNodes(x =>
                {
                    if (x is InvocationExpressionSyntax xInvocation)
                    {
                        pipeInvocation = xInvocation;
                        return false;
                    }

                    return true;
                }).ToArray();

            return new PipeResult
            {
                PipeNode = pipeInvocation,
                RemoveNodes = Pipe.Select(x => x.NodeToRemove).ToArray()
            };
        }

        public override void VisitAssignmentExpression(AssignmentExpressionSyntax node) => ExtractIdentifierAndInvocation(node);
        public override void VisitVariableDeclaration(VariableDeclarationSyntax node) => ExtractIdentifierAndInvocation(node);

        private void ExtractIdentifierAndInvocation(SyntaxNode node)
        {
            var identifier = IdentifierVisitor.VisitResult(node);
            var invocation = InvocationVisitor.VisitResult(node);

            Pipe.Add(new PipePart
            {
                Identifier = identifier,
                Method = invocation,
                NodeToRemove = invocation.Node
            });
        }
    }

    public class PipePart
    {
        public string Identifier { get; set; }

        public MethodInfo Method { get; set; }

        public SyntaxNode NodeToRemove { get; set; }
    }

    public class PipeResult
    {
        public SyntaxNode PipeNode { get; set; }

        public SyntaxNode[] RemoveNodes { get; set; }
    }
}
