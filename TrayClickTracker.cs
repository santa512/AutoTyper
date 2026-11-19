namespace AutoTyper;

// A double click cancels the deferred single click and consumes its trailing
// activation. A new physical button-down begins the next independent gesture.
internal sealed class TrayClickTracker
{
    private bool _pending, _suppressActivation;
    internal void ButtonDown() => _suppressActivation = false;
    internal bool Select()
    {
        if (_suppressActivation) return false;
        _pending = true; return true;
    }
    internal void DoubleClick() { _pending = false; _suppressActivation = true; }
    internal bool TakeSingle() { bool result = _pending; _pending = false; return result; }
    internal void Cancel() { _pending = _suppressActivation = false; }
}
