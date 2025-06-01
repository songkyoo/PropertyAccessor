namespace Macaron.PropertyAccessor;

public sealed class ObservableProperty<TProperty>(
    TProperty initialValue,
    Func<TProperty, TProperty, bool>? onBeforeSet = null,
    Action<TProperty, TProperty>? onAfterSet = null
) : IReadWriteProperty<TProperty>
{
    #region Fields
    private TProperty _value = initialValue;
    #endregion

    #region IReadWriteProperty<TProperty> Interface
    public TProperty Get<T>(T instance)
    {
        return _value;
    }

    public void Set<T>(T instance, TProperty value)
    {
        var oldValue = _value;

        if (onBeforeSet?.Invoke(oldValue, value) == false)
        {
            return;
        }

        _value = value;
        onAfterSet?.Invoke(oldValue, value);
    }
    #endregion
}
