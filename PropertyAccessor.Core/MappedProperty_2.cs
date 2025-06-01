namespace Macaron.PropertyAccessor;

public sealed class MappedProperty<TRaw, TProperty>(TRaw value, Func<TRaw, TProperty> map)
    : IReadOnlyProperty<TProperty>
{
    #region IReadonlyProperty<TProperty> Interface
    public TProperty Get<T>(T instance)
    {
        return map.Invoke(value);
    }
    #endregion
}
