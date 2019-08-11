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
                BlockToExpressionNode = endNode?.BlockToExpressionNode,
                CanReplaceMethod = interestedNodes.Count==0
            };
        }

        bool collect = false;


        private List<SyntaxNode> interestedNodes = new List<SyntaxNode>();

        public override void Visit(SyntaxNode node)
        {
            if (collect)
            {
                interestedNodes.Add(node);
            }

            base.Visit(node);
        }

        public override void VisitBlock(BlockSyntax node)
        {
            if (node.Parent is MethodDeclarationSyntax)
            {
                collect = true;
            }
            base.VisitBlock(node);
        }

        public override void VisitIdentifierName(IdentifierNameSyntax node)
        {
            interestedNodes.Remove(node);
            base.VisitIdentifierName(node);
        }

        public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
        {
            interestedNodes.Remove(node);
            ExtractIdentifierAndInvocation(node);
        }

        public override void VisitVariableDeclaration(VariableDeclarationSyntax node)
        {
            interestedNodes.Remove(node);
            ExtractIdentifierAndInvocation(node);
        }

        public override void VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
        {
            forRemove = node;
            interestedNodes.Remove(node);
            base.VisitLocalDeclarationStatement(node);
        }

        public override void VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            forRemove = node;
            interestedNodes.Remove(node);
            base.VisitExpressionStatement(node);
        }

        public override void VisitReturnStatement(ReturnStatementSyntax node)
        {
            interestedNodes.Remove(node);
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
            var idVisitor = new IdentifierVisitor();
            var invVisitor = new InvocationVisitor();
            

            var identifier = idVisitor.VisitResult(node);
            var invocation = invVisitor.VisitResult(node);

            interestedNodes.Remove(idVisitor.ProcessedNode);
            interestedNodes.Remove(invVisitor.ProcessedNode);

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

        public bool CanReplaceMethod { get; set; }
    }
}
