using System.Diagnostics;

namespace Macaron.PropertyAccessor;

[Conditional("SOURCE_GENERATOR_ONLY")]
[AttributeUsage(AttributeTargets.Field)]
public sealed class GetAttribute(string name = "") : Attribute
{
    public Type? Type { get; set; }

    public PropertyAccessModifier AccessModifier { get; set; } = PropertyAccessModifier.Default;

    public string Name { get; } = name;
}
