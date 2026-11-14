namespace Macaron.PropertyAccessor;

internal static class PropertyAccessModifierResolver
{
    #region Static
    public static PropertyAccessModifier Resolve(
        object? value,
        PropertyAccessModifier defaultValue
    )
    {
        if (value == null)
        {
            return defaultValue;
        }

        var accessModifier = (PropertyAccessModifier)value;
        var isDefined = Enum.IsDefined(typeof(PropertyAccessModifier), accessModifier);

        return isDefined && accessModifier != PropertyAccessModifier.Default ? accessModifier : defaultValue;
    }
    #endregion
}
