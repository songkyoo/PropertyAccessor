using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Macaron.PropertyAccessor;

internal abstract record AnalysisResult<T>
{
    public sealed record Success(T Model) : AnalysisResult<T>;

    public sealed record Failure(ImmutableArray<Diagnostic> Diagnostics) : AnalysisResult<T>
    {
        public Failure(Diagnostic diagnostic) : this(ImmutableArray.Create(diagnostic))
        {
        }
    }
}
