using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Macaron.PropertyAccessor;

internal static class AnalysisContextFactory
{
    #region Static
    public static AccessorAttributeContext? GetAccessorAttributeContext(
        GeneratorAttributeSyntaxContext generatorAttributeSyntaxContext,
        PropertyAccessorKind kind
    )
    {
        if (generatorAttributeSyntaxContext.Attributes.Length != 1)
        {
            return null;
        }

        return generatorAttributeSyntaxContext is
            {
                TargetSymbol: IFieldSymbol fieldSymbol,
                SemanticModel.Compilation: CSharpCompilation compilation,
            }
            ? new AccessorAttributeContext(fieldSymbol, kind, compilation)
            : null;
    }

    public static PropertyGenerationDefaultsAttributeContext? GetPropertyGenerationDefaultsAttributeContext(
        GeneratorAttributeSyntaxContext generatorAttributeSyntaxContext
    )
    {
        if (generatorAttributeSyntaxContext.Attributes.Length != 1)
        {
            return null;
        }

        return generatorAttributeSyntaxContext is
            {
                TargetSymbol: INamedTypeSymbol typeSymbol,
                SemanticModel.Compilation: CSharpCompilation compilation,
            }
            ? new PropertyGenerationDefaultsAttributeContext(
                typeSymbol,
                generatorAttributeSyntaxContext.Attributes,
                compilation
            )
            : null;
    }
    #endregion
}
