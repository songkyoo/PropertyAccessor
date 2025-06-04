namespace Macaron.PropertyAccessor.Properties.Impl;

public sealed class ReadOnlyPropertyWithContext<TContext, TProperty>(
    TContext context,
    Func<TContext, TProperty> getter
) : IReadOnlyProperty<TProperty>
{
    #region IReadOnlyProperty<TProperty> Interface
    public TProperty Get<T>(T instance)
    {
        return getter.Invoke(context);
    }
    #endregion
}
