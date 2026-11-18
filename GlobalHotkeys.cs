namespace AutoTyper;

internal sealed record HotkeyGesture(uint Modifiers, uint Key, string Label)
{
    internal void Validate()
    {
        if ((Modifiers & ~0x400fu) != 0 || (Modifiers & 0xf) == 0 || Key < 1 || Key > 254
            || Key is 0x10 or 0x11 or 0x12 or 0x5b or 0x5c or 0x7b or 0xa0 or 0xa1 or 0xa2 or 0xa3 or 0xa4 or 0xa5
            || string.IsNullOrWhiteSpace(Label) || Label.Length > 80 || Label.Any(char.IsControl))
            throw new InvalidOperationException("Choose a hotkey with Ctrl, Alt, Shift or Windows and a non-modifier key.");
    }
}

internal sealed record HotkeyRegistration(int Id, string Action, HotkeyGesture Gesture);

// Keeps registration changes atomic; a failed combination restores the previous set.
internal sealed class GlobalHotkeySet(Func<int, uint, uint, bool> register, Func<int, bool> unregister)
{
    private readonly List<HotkeyRegistration> _registered = [];
    internal string FailedAction { get; private set; } = "";
    internal bool IsRegistered(int id) => _registered.Any(binding => binding.Id == id);
    internal List<string> RegisterAvailable(IReadOnlyList<HotkeyRegistration> bindings)
    {
        Release();
        List<string> failed = [];
        foreach (var binding in bindings)
        {
            if (_registered.Any(b => b.Id == binding.Id)) { failed.Add(binding.Action); continue; }
            if (register(binding.Id, binding.Gesture.Modifiers, binding.Gesture.Key)) _registered.Add(binding);
            else failed.Add(binding.Action);
        }
        return failed;
    }
    internal bool TryApply(IReadOnlyList<HotkeyRegistration> bindings)
    {
        var previous = _registered.ToArray();
        if (!Release())
        {
            foreach (var old in previous)
                if (!_registered.Any(b => b.Id == old.Id) && register(old.Id, old.Gesture.Modifiers, old.Gesture.Key)) _registered.Add(old);
            FailedAction = "release existing shortcuts";
            return false;
        }
        foreach (var binding in bindings)
        {
            if (register(binding.Id, binding.Gesture.Modifiers, binding.Gesture.Key)) { _registered.Add(binding); continue; }
            FailedAction = binding.Action;
            Release();
            foreach (var old in previous)
                if (!_registered.Any(b => b.Id == old.Id) && register(old.Id, old.Gesture.Modifiers, old.Gesture.Key)) _registered.Add(old);
            return false;
        }
        FailedAction = "";
        return true;
    }
    internal bool Release()
    {
        bool success = true;
        foreach (var binding in _registered.ToArray())
        {
            if (unregister(binding.Id)) _registered.Remove(binding);
            else success = false;
        }
        return success;
    }
}
