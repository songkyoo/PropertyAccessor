namespace Macaron.PropertyAccessor.Properties;

public sealed class MappedProperty<TValue, TProperty>(TValue value, Func<TValue, TProperty> map)
    : IReadOnlyProperty<TProperty>
{
    #region IReadOnlyProperty<TProperty> Interface
    public TProperty Get<T>(T instance)
    {
        return map.Invoke(value);
    }
    #endregion
}
