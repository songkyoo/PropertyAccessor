using System.Diagnostics;

namespace Macaron.PropertyAccessor;

[Conditional("SOURCE_GENERATOR_ONLY")]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class PropertyGenerationDefaultsAttribute : Attribute
{
    public PropertyAccessModifier AccessModifier { get; set; } = PropertyAccessModifier.Default;

    public string PrefixPattern { get; set; } = "";

    public PropertyNamingRule NamingRule { get; set; } = PropertyNamingRule.Default;
}
