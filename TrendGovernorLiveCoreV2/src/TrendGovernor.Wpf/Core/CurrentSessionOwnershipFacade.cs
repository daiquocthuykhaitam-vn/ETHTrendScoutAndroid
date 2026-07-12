namespace TrendGovernor.Wpf;

public sealed partial class MainWindow
{
    private readonly CurrentSessionOwnershipFacade _botOwnedSymbols = new();
}

internal sealed class CurrentSessionOwnershipFacade
{
    public bool Contains(string symbol)
        => SessionOwnershipRegistry.IsCurrentSessionBotOwned(symbol);

    public bool Add(string symbol)
        => SessionOwnershipRegistry.IsCurrentSessionBotOwned(symbol);

    public bool Remove(string symbol)
    {
        if (!SessionOwnershipRegistry.IsCurrentSessionBotOwned(symbol)) return false;
        SessionOwnershipRegistry.Release(symbol);
        return true;
    }
}
