namespace Macaron.PropertyAccessor;

public sealed class ReadWriteProperty<T, TProperty>(Func<T, TProperty> getter, Action<T, TProperty> setter)
    : IReadWriteProperty<T, TProperty>
{
    #region IReadWriteProperty<T, TProperty> Interface
    public TProperty Get(T instance)
    {
        return getter.Invoke(instance);
    }

    public void Set(T instance, TProperty value)
    {
        setter.Invoke(instance, value);
    }
    #endregion
}
