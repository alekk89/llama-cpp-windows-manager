using System.Collections;

namespace LocalLlmConsole.Services;

/// <summary>A detached catalog with the same lookup precedence as the legacy resolver.</summary>
public sealed class ModelGatewayRouteSnapshot : IReadOnlyList<ModelGatewayModelRoute>
{
    private readonly ModelGatewayModelRoute[] _routes;
    private readonly Dictionary<string, ModelGatewayModelRoute> _lookup = new(StringComparer.OrdinalIgnoreCase);

    public ModelGatewayRouteSnapshot(IReadOnlyList<ModelGatewayModelRoute> routes)
    {
        _routes = routes.ToArray();
        // Priority applies across the entire list, not within each individual route.
        AddKeys(route => route.Id);
        AddKeys(route => route.LegacyId);
        AddKeys(route => route.Name);
        AddKeys(route => route.Profile.Id);
        AddKeys(route => route.Profile.IsDefault ? route.Model.Name : null);
        AddKeys(route => route.Profile.IsDefault ? Path.GetFileName(route.Model.ModelPath) : null);
        AddKeys(route => route.Profile.IsDefault ? Path.GetFileNameWithoutExtension(route.Model.ModelPath) : null);
    }

    public ModelGatewayModelRoute? Resolve(string requested)
        => _lookup.GetValueOrDefault(requested.Trim());

    public int Count => _routes.Length;
    public ModelGatewayModelRoute this[int index] => _routes[index];
    public IEnumerator<ModelGatewayModelRoute> GetEnumerator()
        => ((IEnumerable<ModelGatewayModelRoute>)_routes).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void AddKeys(Func<ModelGatewayModelRoute, string?> keySelector)
    {
        foreach (var route in _routes)
        {
            var key = keySelector(route);
            if (!string.IsNullOrWhiteSpace(key)) _lookup.TryAdd(key, route);
        }
    }
}
