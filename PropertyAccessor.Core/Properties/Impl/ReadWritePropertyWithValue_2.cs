namespace Macaron.PropertyAccessor.Properties.Impl;

public sealed class ReadWritePropertyWithValue<TValue, TProperty>(
    TValue initialValue,
    Func<TValue, TProperty> getter,
    Func<TValue, TProperty, TValue> setter
) : IReadWriteProperty<TProperty>
{
    #region Fields
    private TValue _value = initialValue;
    #endregion

    #region IReadWriteProperty<TProperty> Interface
    public TProperty Get<T>(T instance)
    {
        return getter.Invoke(_value);
    }

    public void Set<T>(T instance, TProperty value)
    {
        _value = setter.Invoke(_value, value);
    }
    #endregion
}
