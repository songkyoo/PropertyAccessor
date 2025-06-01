namespace Macaron.PropertyAccessor.Properties;

public sealed class ReadOnlyProperty<TProperty>(Func<TProperty> getter) : IReadOnlyProperty<TProperty>
{
    #region IReadOnlyProperty<TProperty> Interface
    public TProperty Get<T>(T instance)
    {
        return getter.Invoke();
    }
    #endregion
}
