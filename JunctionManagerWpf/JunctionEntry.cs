using System.ComponentModel;
using System.Windows.Media;

namespace JunctionManager;

public class JunctionEntry : INotifyPropertyChanged
{
    static Brush B(byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromRgb(r, g, b)); x.Freeze(); return x; }
    static readonly Brush Green = B(63, 185, 80), Orange = B(227, 160, 8), Red = B(248, 81, 73);

    public string Name { get; set; } = "";
    public string Source { get; set; } = "";   // the junction (old location)
    public string Target { get; set; } = "";   // real folder
    bool _hidden;
    public bool Hidden
    {
        get => _hidden;
        set
        {
            if (_hidden == value) return;
            _hidden = value;
            Raise(nameof(Hidden));
            Raise(nameof(HiddenToolTip));
            Raise(nameof(Flags));
        }
    }

    bool _protected;
    public bool Protected
    {
        get => _protected;
        set
        {
            if (_protected == value) return;
            _protected = value;
            Raise(nameof(Protected));
            Raise(nameof(ProtectedToolTip));
            Raise(nameof(Flags));
        }
    }

    public string HiddenToolTip => Hidden
        ? "Target folder is hidden in File Explorer (click to make visible)"
        : "Target folder is visible in File Explorer (click to hide)";

    public string ProtectedToolTip => Protected
        ? "Target folder is protected from deletion (click to unprotect)"
        : "Target folder is unprotected (click to protect from deletion)";

    bool _watch;
    public event Action? WatchChanged;
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool Watch
    {
        get => _watch;
        set { if (_watch == value) return; _watch = value; WatchChanged?.Invoke(); Raise(nameof(Watch)); }
    }

    string _status = "";
    public string Status
    {
        get => _status;
        set { _status = value; Raise(nameof(Status)); Raise(nameof(StatusBrush)); }
    }
    public Brush StatusBrush => _status == "OK" ? Green : _status is "Unlinked" or "Wrong target" ? Orange : Red;

    public string Flags => string.Join(", ", new[] { Hidden ? "Hidden" : "", Protected ? "Protected" : "" }.Where(s => s != ""));

    void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
