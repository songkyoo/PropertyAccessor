using System.Diagnostics;

namespace Macaron.PropertyAccessor;

[Conditional("SOURCE_GENERATOR_ONLY")]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class PropertyGenerationDefaultsAttribute(
    PropertyAccessModifier accessModifier = PropertyAccessModifier.Default,
    string prefixPattern = "",
    PropertyNamingRule namingRule = PropertyNamingRule.Default
) : Attribute
{
    public PropertyAccessModifier AccessModifier { get; } = accessModifier;

    public string PrefixPattern { get; } = prefixPattern;

    public PropertyNamingRule NamingRule { get; } = namingRule;
}
