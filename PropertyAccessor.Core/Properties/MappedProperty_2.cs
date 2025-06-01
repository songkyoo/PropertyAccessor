namespace Macaron.PropertyAccessor.Properties;

public sealed class MappedProperty<TRaw, TProperty>(TRaw value, Func<TRaw, TProperty> map)
    : IReadOnlyProperty<TProperty>
{
    #region IReadOnlyProperty<TProperty> Interface
    public TProperty Get<T>(T instance)
    {
        return map.Invoke(value);
    }
    #endregion
}
