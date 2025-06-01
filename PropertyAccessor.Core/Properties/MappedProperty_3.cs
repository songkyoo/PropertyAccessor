namespace Macaron.PropertyAccessor.Properties;

public sealed class MappedProperty<T, TValue, TProperty>(TValue value, Func<T, TValue, TProperty> map)
    : IReadOnlyProperty<T, TProperty>
{
    #region IReadOnlyProperty<T, TProperty> Interface
    public TProperty Get(T instance)
    {
        return map.Invoke(instance, value);
    }
    #endregion
}
