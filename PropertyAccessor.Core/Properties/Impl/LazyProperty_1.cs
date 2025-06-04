namespace Macaron.PropertyAccessor.Properties.Impl;

public sealed class LazyProperty<TProperty> : IReadOnlyProperty<TProperty>
{
    #region Fields
    private readonly Lazy<TProperty> _lazy;
    #endregion

    #region Constructors
    public LazyProperty()
    {
        _lazy = new Lazy<TProperty>();
    }

    public LazyProperty(bool isThreadSafe)
    {
        _lazy = new Lazy<TProperty>(isThreadSafe);
    }

    public LazyProperty(Func<TProperty> valueFactory)
    {
        _lazy = new Lazy<TProperty>(valueFactory);
    }

    public LazyProperty(LazyThreadSafetyMode mode)
    {
        _lazy = new Lazy<TProperty>(mode);
    }

    public LazyProperty(Func<TProperty> valueFactory, bool isThreadSafe)
    {
        _lazy = new Lazy<TProperty>(valueFactory, isThreadSafe);
    }

    public LazyProperty(Func<TProperty> valueFactory, LazyThreadSafetyMode mode)
    {
        _lazy = new Lazy<TProperty>(valueFactory, mode);
    }
    #endregion

    #region IReadOnlyProperty<TProperty> Interface
    public TProperty Get<T>(T instance)
    {
        return _lazy.Value;
    }
    #endregion
}
