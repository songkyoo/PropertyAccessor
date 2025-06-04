namespace Macaron.PropertyAccessor.Properties.Impl;

public sealed class ObservableProperty<T, TProperty>(
    TProperty initialValue,
    Func<T, TProperty, TProperty, bool>? onBeforeSet = null,
    Action<T, TProperty, TProperty>? onAfterSet = null
) : IReadWriteProperty<T, TProperty>
{
    #region Fields
    private TProperty _value = initialValue;
    #endregion

    #region IReadWriteProperty<T, TProperty> Interface
    public TProperty Get(T instance)
    {
        return _value;
    }

    public void Set(T instance, TProperty value)
    {
        var oldValue = _value;

        if (onBeforeSet?.Invoke(instance, oldValue, value) == false)
        {
            return;
        }

        _value = value;
        onAfterSet?.Invoke(instance, oldValue, value);
    }
    #endregion
}
