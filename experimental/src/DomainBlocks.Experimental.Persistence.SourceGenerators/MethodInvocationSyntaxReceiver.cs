using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DomainBlocks.Experimental.Persistence.SourceGenerators;

public class MethodInvocationSyntaxReceiver : ISyntaxReceiver
{
    public List<InvocationExpressionSyntax> Invocations { get; } = new();

    public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
    {
        if (syntaxNode is InvocationExpressionSyntax invocation)
        {
            Invocations.Add(invocation);
        }
    }
}