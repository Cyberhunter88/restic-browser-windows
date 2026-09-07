namespace ResticBrowser.ViewModels;

// Owns only the session's directory history; no repository or UI dependency.
internal sealed class NavigationHistory
{
    private readonly Stack<string> _back = new();
    private readonly Stack<string> _forward = new();
    public bool CanGoBack => _back.Count > 0;
    public bool CanGoForward => _forward.Count > 0;
    public void PushBack(string path) => _back.Push(path);
    public void PushForward(string path) => _forward.Push(path);
    public string PopBack() => _back.Pop();
    public string PopForward() => _forward.Pop();
    public void ClearForward() => _forward.Clear();
    public void Clear() { _back.Clear(); _forward.Clear(); }
}
