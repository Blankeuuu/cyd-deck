using StreamDeck.Protocol;

namespace StreamDeck.WindowsAgent.Runtime;

public sealed record PendingShortcutImport(string Id, string Label, string ActionType);

public sealed class PendingShortcutImportStore
{
    private readonly object gate = new();
    private readonly Dictionary<string, PendingShortcutImport> pending = new(StringComparer.OrdinalIgnoreCase);

    public void UpdateFromDeviceReport(ShortcutsReportMessage report, IReadOnlySet<string> savedShortcutIds)
    {
        lock (gate)
        {
            pending.Clear();
            foreach (var shortcut in report.Shortcuts.Take(6).Where(s => !s.Id.StartsWith("__empty_slot_", StringComparison.Ordinal)))
            {
                if (savedShortcutIds.Contains(shortcut.Id) || pending.ContainsKey(shortcut.Id))
                {
                    continue;
                }

                pending[shortcut.Id] = new PendingShortcutImport(shortcut.Id, shortcut.Label, shortcut.ActionType ?? string.Empty);
            }
        }
    }

    public IReadOnlyList<PendingShortcutImport> Snapshot(IReadOnlySet<string> savedShortcutIds)
    {
        lock (gate)
        {
            foreach (var savedId in savedShortcutIds)
            {
                pending.Remove(savedId);
            }

            return pending.Values.OrderBy(shortcut => shortcut.Label, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }
}
