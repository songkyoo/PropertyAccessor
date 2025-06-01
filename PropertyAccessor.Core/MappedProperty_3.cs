namespace Macaron.PropertyAccessor;

public sealed class MappedProperty<T, TRaw, TProperty>(TRaw value, Func<T, TRaw, TProperty> map)
    : IReadOnlyProperty<T, TProperty>
{
    #region IReadOnlyProperty<T, TProperty> Interface
    public TProperty Get(T instance)
    {
        return map.Invoke(instance, value);
    }
    #endregion
}
