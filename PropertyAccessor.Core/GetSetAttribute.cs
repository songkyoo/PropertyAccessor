using System.Diagnostics;

namespace Macaron.PropertyAccessor;

[Conditional("SOURCE_GENERATOR_ONLY")]
[AttributeUsage(AttributeTargets.Field)]
public sealed class GetSetAttribute(
    PropertyAccessModifier accessModifier = PropertyAccessModifier.Default,
    string name = ""
) : Attribute
{
    public PropertyAccessModifier AccessModifier { get; } = accessModifier;

    public string Name { get; } = name;
}
