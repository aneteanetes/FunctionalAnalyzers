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
        private SyntaxNode forRemove = null;

        private IdentifierVisitor IdentifierVisitor = new IdentifierVisitor();
        private InvocationVisitor InvocationVisitor = new InvocationVisitor();

        public override PipeResult VisitResult(SyntaxNode node)
        {
            this.Visit(node);

            if (Pipe.Count == 0)
                return default;

            var endNode = Pipe.Where(x => x.End).FirstOrDefault();

            var distinct = Pipe.Where(n=>!n.End)
                .GroupBy(x => x.Identifier + x.Method.Name + x.Method.Argument)                
                .Select(g => g.First());

            string template = "Lambda.Pipe";

            foreach (var item in distinct)
            {
                template += $"({item.Method.Name})";
            }

            if (endNode != default)
            {
                template = "=> " + template + ".Value()";
            }

            template += ";";

            SyntaxNode pipeNode = default;
            CSharpSyntaxTree.ParseText(template, options: new CSharpParseOptions(kind: SourceCodeKind.Script))
                .GetRoot()
                .DescendantNodes(x =>
                {
                    pipeNode = x;
                    return x is CompilationUnitSyntax || x is GlobalStatementSyntax;
                }).ToArray();

            return new PipeResult
            {
                PipeNode = pipeNode,
                RemoveNodes = Pipe.Select(x => x.NodeToRemove).ToArray(),
                NodeToReplace = (endNode ?? distinct.Last()).NodeToReplace,
                BlockToExpressionNode = endNode?.BlockToExpressionNode
            };
        }

        public override void VisitAssignmentExpression(AssignmentExpressionSyntax node) => ExtractIdentifierAndInvocation(node);
        public override void VisitVariableDeclaration(VariableDeclarationSyntax node) => ExtractIdentifierAndInvocation(node);

        public override void VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
        {
            forRemove = node;
            base.VisitLocalDeclarationStatement(node);
        }

        public override void VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            forRemove = node;
            base.VisitExpressionStatement(node);
        }

        public override void VisitReturnStatement(ReturnStatementSyntax node)
        {
            Pipe.Add(new PipePart()
            {
                Identifier = "return",
                End = true,
                NodeToReplace = node,
                BlockToExpressionNode=node.Parent
            });
            base.VisitReturnStatement(node);
        }

        private void ExtractIdentifierAndInvocation(SyntaxNode node)
        {
            var identifier = new IdentifierVisitor().VisitResult(node);
            var invocation = new InvocationVisitor().VisitResult(node);

            Pipe.Add(new PipePart
            {
                Identifier = identifier,
                Method = invocation,
                NodeToRemove = forRemove,
                NodeToReplace=invocation.Node
            });

            forRemove = null;
        }
    }

    public class PipePart
    {
        public string Identifier { get; set; }

        public MethodInfo Method { get; set; }

        public SyntaxNode NodeToRemove { get; set; }

        public SyntaxNode NodeToReplace { get; set; }

        public bool End { get; set; }

        public SyntaxNode BlockToExpressionNode { get; set; }
    }

    public class PipeResult
    {
        public SyntaxNode PipeNode { get; set; }

        public SyntaxNode[] RemoveNodes { get; set; }

        public SyntaxNode NodeToReplace { get; set; }

        public SyntaxNode BlockToExpressionNode { get; set; }
    }
}
