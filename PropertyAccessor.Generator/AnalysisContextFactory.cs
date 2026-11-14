using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using static Macaron.PropertyAccessor.AttributeMetadataNames;

namespace Macaron.PropertyAccessor;

internal static class AnalysisContextFactory
{
    #region Static
    public static AttributeData? GetAssemblyDefaultsAttribute(Compilation compilation)
    {
        AttributeData? defaultsAttribute = null;

        foreach (var attribute in compilation.Assembly.GetAttributes())
        {
            if (attribute.ApplicationSyntaxReference == null
                || attribute.AttributeClass?.ToDisplayString() != PropertyGenerationDefaultsAttributeMetadataName
            )
            {
                continue;
            }

            // 중복 선언은 C# 컴파일러가 진단하므로 어느 쪽의 옵션도 사용하지 않는다.
            if (defaultsAttribute != null)
            {
                return null;
            }

            defaultsAttribute = attribute;
        }

        return defaultsAttribute;
    }

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
