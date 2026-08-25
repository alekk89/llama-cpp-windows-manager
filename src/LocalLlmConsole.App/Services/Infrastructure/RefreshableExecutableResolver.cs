namespace LocalLlmConsole.Services;

internal sealed class RefreshableExecutableResolver
{
    private static readonly TimeSpan NegativeResultLifetime = TimeSpan.FromMinutes(5);
    private readonly Func<string> _resolve;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly object _gate = new();
    private bool _resolved;
    private string _value = "";
    private Exception? _error;
    private DateTimeOffset _resolvedAt;

    public RefreshableExecutableResolver(Func<string> resolve, Func<DateTimeOffset>? utcNow = null)
    {
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public string Resolve()
    {
        lock (_gate)
        {
            var now = _utcNow();
            if (_resolved
                && (_error is not null || string.IsNullOrWhiteSpace(_value))
                && now - _resolvedAt >= NegativeResultLifetime)
                Clear();

            if (!_resolved)
            {
                try
                {
                    _value = _resolve() ?? "";
                }
                catch (Exception ex)
                {
                    _error = ex;
                }
                _resolved = true;
                _resolvedAt = now;
            }

            if (_error is not null) throw _error;
            return _value;
        }
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            Clear();
        }
    }

    private void Clear()
    {
        _resolved = false;
        _value = "";
        _error = null;
        _resolvedAt = default;
    }
}
