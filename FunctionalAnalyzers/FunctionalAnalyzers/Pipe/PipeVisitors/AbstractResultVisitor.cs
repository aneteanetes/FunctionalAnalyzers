using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Text;

namespace FunctionalAnalyzers.Pipe.PipeVisitors
{
    public abstract class AbstractResultVisitor<T> : CSharpSyntaxWalker
    {
        public abstract T VisitResult(SyntaxNode node);
    }
}
