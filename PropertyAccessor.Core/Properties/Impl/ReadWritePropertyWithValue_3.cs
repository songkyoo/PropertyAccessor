namespace Macaron.PropertyAccessor.Properties.Impl;

public sealed class ReadWritePropertyWithValue<T, TValue, TProperty>(
    TValue initialValue,
    Func<T, TValue, TProperty> getter,
    Func<T, TValue, TProperty, TValue> setter
) : IReadWriteProperty<T, TProperty>
{
    #region Fields
    private TValue _value = initialValue;
    #endregion

    #region IReadWriteProperty<T, TProperty> Interface
    public TProperty Get(T instance)
    {
        return getter.Invoke(instance, _value);
    }

    public void Set(T instance, TProperty value)
    {
        _value = setter.Invoke(instance, _value, value);
    }
    #endregion
}
