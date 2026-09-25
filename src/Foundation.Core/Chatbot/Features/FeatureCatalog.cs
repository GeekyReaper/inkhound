namespace Foundation.Core.Chatbot.Features;

public sealed class FeatureCatalog(IEnumerable<IChatFeature> features)
{
    private readonly Dictionary<string, IChatFeature> _byName =
        features.ToDictionary(f => f.Name.ToLowerInvariant());

    public IReadOnlyCollection<IChatFeature> All => _byName.Values;

    public bool TryResolve(string name, out IChatFeature feature) =>
        _byName.TryGetValue(name.ToLowerInvariant(), out feature!);
}
