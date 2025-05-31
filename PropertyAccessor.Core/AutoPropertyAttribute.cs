namespace Macaron.PropertyAccessor;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field, Inherited = false)]
public class AutoPropertyAttribute(
    PropertyAccessModifier accessModifier = PropertyAccessModifier.Default,
    string prefix = "",
    PropertyNamingRule namingRule = PropertyNamingRule.Default
) : Attribute
{
    public PropertyAccessModifier AccessModifier { get; } = accessModifier;

    public string Prefix { get; } = prefix;

    public PropertyNamingRule NamingRule { get; } = namingRule;
}
