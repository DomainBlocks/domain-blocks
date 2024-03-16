using System.Text;
using Microsoft.CodeAnalysis;

namespace DomainBlocks.Experimental.Persistence.SourceGenerators;

[Generator]
public class EntityAdapterSourceGenerator : ISourceGenerator
{
    public void Initialize(GeneratorInitializationContext context)
    {
        context.RegisterForSyntaxNotifications(() => new MethodInvocationSyntaxReceiver());
    }

    public void Execute(GeneratorExecutionContext context)
    {
        if (context.SyntaxReceiver is not MethodInvocationSyntaxReceiver receiver)
        {
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("namespace DomainBlocks.Experimental.Persistence.Generated;");
        sb.AppendLine();
        sb.AppendLine("public static class EntityTypes");
        sb.AppendLine("{");
        sb.AppendLine("    public static IEnumerable<Type> GetEntityTypes()");
        sb.AppendLine("    {");

        foreach (var invocation in receiver.Invocations)
        {
            var model = context.Compilation.GetSemanticModel(invocation.SyntaxTree);

            if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol symbol)
            {
                continue;
            }

            if (symbol.ToString().Contains("LoadAsync"))
            {
                sb.AppendLine($"        yield return typeof({symbol.TypeArguments[0]});");
            }
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        context.AddSource("foo.g.cs", sb.ToString());
    }
}