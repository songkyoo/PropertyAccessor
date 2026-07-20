using System.Diagnostics;

namespace Macaron.PropertyAccessor;

[Conditional("SOURCE_GENERATOR_ONLY")]
[AttributeUsage(AttributeTargets.Field)]
public sealed class GetAttribute(
    Type? propertyType = null,
    PropertyAccessModifier accessModifier = PropertyAccessModifier.Default,
    string name = ""
) : Attribute
{
    public Type? PropertyType { get; } = propertyType;

    public PropertyAccessModifier AccessModifier { get; } = accessModifier;

    public string Name { get; } = name;
}
