namespace Macaron.PropertyAccessor.Properties.Impl;

public sealed class ReadOnlyPropertyWithContext<T, TContext, TProperty>(
    TContext context,
    Func<T, TContext, TProperty> getter
) : IReadOnlyProperty<T, TProperty>
{
    #region IReadOnlyProperty<T, TProperty> Interface
    public TProperty Get(T instance)
    {
        return getter.Invoke(instance, context);
    }
    #endregion
}
