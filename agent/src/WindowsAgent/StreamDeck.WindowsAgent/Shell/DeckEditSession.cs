using StreamDeck.Configuration;
using StreamDeck.Shortcuts;

namespace StreamDeck.WindowsAgent.Shell;

/// <summary>In-memory editor only. Persistence and profile-switch policy belong to the shell.</summary>
internal sealed class DeckEditSession
{
    internal List<ShortcutDefinition> Shortcuts { get; }
    internal string?[] Slots { get; }
    internal int SelectedSlot { get; private set; }
    internal string? SelectedLibraryActionId { get; private set; }
    internal string? SelectedActionId { get; private set; }
    internal string ProfileName { get; }
    private readonly Stack<string?[]> assignmentHistory = new();
    internal bool CanUndo => assignmentHistory.Count > 0;

    internal DeckEditSession(AgentConfiguration configuration)
    {
        ProfileName = configuration.ActiveProfile;
        Shortcuts = (configuration.Shortcuts ?? []).ToList();
        var assigned = configuration.Profiles.FirstOrDefault(p => p.Name == ProfileName)?.AssignedShortcutIds;
        // Null means historical format; an explicit empty array means no assignments.
        Slots = Enumerable.Range(0, 6).Select(i => assigned is null
            ? Shortcuts.ElementAtOrDefault(i)?.Id : assigned.ElementAtOrDefault(i)).ToArray();
        SelectSlot(0);
    }

    internal void SelectLibrary(string id)
    {
        if (!Shortcuts.Any(s => s.Id == id)) throw new ArgumentException("Unknown shortcut.", nameof(id));
        SelectedLibraryActionId = SelectedActionId = id;
    }

    internal void SelectSlot(int slot)
    {
        CheckSlot(slot);
        SelectedSlot = slot;
        SelectedActionId = Slots[slot];
    }

    internal void Assign(string id, int slot)
    {
        CheckSlot(slot);
        if (!Shortcuts.Any(s => s.Id == id)) throw new ArgumentException("Unknown shortcut.", nameof(id));
        Remember();
        for (var i = 0; i < Slots.Length; i++) if (Slots[i] == id) Slots[i] = null;
        Slots[slot] = id;
        SelectSlot(slot);
    }

    internal void Detach(int slot)
    {
        CheckSlot(slot);
        if (Slots[slot] is not null) Remember();
        Slots[slot] = null;
        SelectSlot(slot);
    }

    internal void Swap(int source, int destination)
    {
        CheckSlot(source); CheckSlot(destination);
        if (source == destination) return;
        Remember();
        (Slots[source], Slots[destination]) = (Slots[destination], Slots[source]);
        SelectSlot(destination);
    }

    internal void DeleteSelected()
    {
        var id = SelectedActionId;
        Shortcuts.RemoveAll(s => s.Id == id);
        for (var i = 0; i < Slots.Length; i++) if (Slots[i] == id) Slots[i] = null;
        // Undo never resurrects an assignment to a deleted definition.
        assignmentHistory.Clear();
        if (SelectedLibraryActionId == id) SelectedLibraryActionId = null;
        SelectSlot(SelectedSlot);
    }

    internal void UndoAssignment()
    {
        if (!assignmentHistory.TryPop(out var previous)) return;
        Array.Copy(previous, Slots, Slots.Length);
        SelectSlot(SelectedSlot);
    }

    internal AgentConfiguration ApplyTo(AgentConfiguration current)
    {
        if (current.ActiveProfile != ProfileName) throw new InvalidOperationException("Deck editor belongs to a different profile.");
        var profiles = current.Profiles.ToList();
        var profile = new DeckProfileConfiguration(ProfileName, Shortcuts.ToArray(), Slots.ToArray());
        var index = profiles.FindIndex(p => p.Name == ProfileName);
        if (index < 0) profiles.Add(profile); else profiles[index] = profile;
        return current with { Shortcuts = Shortcuts.ToArray(), Profiles = profiles };
    }

    private void Remember() => assignmentHistory.Push(Slots.ToArray());
    private static void CheckSlot(int slot)
    {
        if (slot is < 0 or >= 6) throw new ArgumentOutOfRangeException(nameof(slot));
    }
}
