namespace Macaron.PropertyAccessor.Properties;

public sealed class ReadWriteProperty<TProperty>(Func<TProperty> getter, Action<TProperty> setter)
    : IReadWriteProperty<TProperty>
{
    #region IReadWriteProperty<TProperty> Interface
    public TProperty Get<T>(T instance)
    {
        return getter.Invoke();
    }

    public void Set<T>(T instance, TProperty value)
    {
        setter.Invoke(value);
    }
    #endregion
}
