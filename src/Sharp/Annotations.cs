namespace Sharp;

// Marks a method's purity / side-effect contract.
//   [Pure]          - deterministic, no I/O, no shared-state mutation.
//   [Query]         - reads external state (files, disk, caches) but does not mutate it.
//   [HasSideEffects]- mutates shared state or external state (writes files, updates caches).
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class PureAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class QueryAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class HasSideEffectsAttribute : Attribute
{
}
