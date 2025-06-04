namespace Macaron.PropertyAccessor.Properties.Impl;

public sealed class ReadOnlyProperty<T, TProperty>(Func<T, TProperty> getter) : IReadOnlyProperty<T, TProperty>
{
    #region IReadOnlyProperty<T, TProperty> Interface
    public TProperty Get(T instance)
    {
        return getter.Invoke(instance);
    }
    #endregion
}
