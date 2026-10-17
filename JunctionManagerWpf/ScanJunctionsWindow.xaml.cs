using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace JunctionManager;

public partial class ScanJunctionsWindow : Window
{
    public List<JunctionEntry> Discovered { get; } = new();
    public int AlreadyRegisteredCount { get; private set; }

    readonly HashSet<string> _existingSources;
    bool _isScanning;

    public ScanJunctionsWindow(IEnumerable<JunctionEntry> currentItems)
    {
        InitializeComponent();
        App.DarkTitle(this);

        _existingSources = new HashSet<string>(
            currentItems.Select(x => Path.GetFullPath(x.Source).TrimEnd('\\')),
            StringComparer.OrdinalIgnoreCase);

        FolderPathBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        StatusText.Text = "Ready to scan. Click 'Scan' to begin.";
    }

    void Browse_Click(object sender, RoutedEventArgs e)
    {
        var d = new OpenFolderDialog
        {
            Title = "Select Directory to Scan",
            InitialDirectory = Directory.Exists(FolderPathBox.Text)
                ? FolderPathBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };
        if (d.ShowDialog(this) == true)
        {
            FolderPathBox.Text = d.FolderName;
        }
    }

    void PresetLocalAppData_Click(object sender, RoutedEventArgs e) =>
        FolderPathBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    void PresetAppData_Click(object sender, RoutedEventArgs e) =>
        FolderPathBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    void PresetUserProfile_Click(object sender, RoutedEventArgs e) =>
        FolderPathBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    static readonly HashSet<string> KnownSystemJunctionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Application Data",
        "History",
        "Temporary Internet Files",
        "Content.IE5",
        "Cookies",
        "NetHood",
        "PrintHood",
        "Recent",
        "SendTo",
        "Start Menu",
        "Templates",
        "Local Settings",
        "My Documents",
        "Documents and Settings"
    };

    public static bool IsSystemJunction(DirectoryInfo di, string target)
    {
        // 1. Windows system attribute on directory reparse point
        if (di.Attributes.HasFlag(FileAttributes.System))
            return true;

        // 2. Known legacy Windows compatibility junction name
        if (KnownSystemJunctionNames.Contains(di.Name))
            return true;

        // 3. Path analysis (circular ancestor/self link or system cache redirection)
        try
        {
            var normSrc = Path.GetFullPath(di.FullName).TrimEnd('\\').ToLowerInvariant();
            var normTgt = Path.GetFullPath(target).TrimEnd('\\').ToLowerInvariant();

            if (normSrc == normTgt || normSrc.StartsWith(normTgt + "\\"))
                return true;

            if (normTgt.Contains(@"\microsoft\windows\inetcache") ||
                normTgt.Contains(@"\microsoft\windows\history"))
                return true;
        }
        catch { }

        return false;
    }

    async void StartScan_Click(object sender, RoutedEventArgs e)
    {
        if (_isScanning) return;

        var root = FolderPathBox.Text.Trim().TrimEnd('\\');
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            StatusText.Text = "Please select a valid, existing directory.";
            return;
        }

        _isScanning = true;
        ScanButton.IsEnabled = false;
        ScanProgressBar.Visibility = Visibility.Visible;
        StatusText.Text = $"Scanning \"{root}\"...";

        bool recursive = RecursiveBox.IsChecked == true;
        bool skipSystem = SkipSystemBox.IsChecked == true;
        Discovered.Clear();
        AlreadyRegisteredCount = 0;

        int inspected = 0;
        int skippedSystemCount = 0;

        await Task.Run(() =>
        {
            var queue = new Queue<string>();
            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                string[] subs;
                try
                {
                    subs = Directory.GetDirectories(current);
                }
                catch
                {
                    continue;
                }

                foreach (var sub in subs)
                {
                    inspected++;
                    try
                    {
                        var di = new DirectoryInfo(sub);
                        if (di.Attributes.HasFlag(FileAttributes.ReparsePoint))
                        {
                            string? target = di.LinkTarget;
                            if (!string.IsNullOrEmpty(target))
                            {
                                if (target.StartsWith(@"\??\")) target = target[4..];

                                if (skipSystem && IsSystemJunction(di, target))
                                {
                                    skippedSystemCount++;
                                    continue;
                                }

                                var normSource = Path.GetFullPath(di.FullName).TrimEnd('\\');

                                if (_existingSources.Contains(normSource))
                                {
                                    AlreadyRegisteredCount++;
                                }
                                else
                                {
                                    bool hidden = false;
                                    try
                                    {
                                        if (Directory.Exists(target))
                                            hidden = File.GetAttributes(target).HasFlag(FileAttributes.Hidden);
                                    }
                                    catch { }

                                    var entry = new JunctionEntry
                                    {
                                        Name = di.Name,
                                        Source = di.FullName,
                                        Target = target,
                                        Watch = false,
                                        Hidden = hidden,
                                        Protected = false
                                    };
                                    _existingSources.Add(normSource);
                                    Discovered.Add(entry);
                                }
                            }
                            // Never recurse into an existing junction to avoid loops
                            continue;
                        }

                        if (recursive)
                        {
                            queue.Enqueue(sub);
                        }
                    }
                    catch { }
                }
            }
        });

        _isScanning = false;
        ScanProgressBar.Visibility = Visibility.Collapsed;
        ScanButton.IsEnabled = true;

        if (Discovered.Count > 0)
        {
            DialogResult = true;
        }
        else if (AlreadyRegisteredCount > 0)
        {
            string sysMsg = skippedSystemCount > 0 ? $" (skipped {skippedSystemCount} system junction(s))" : "";
            StatusText.Text = $"Scan finished ({inspected} folders checked). Found {AlreadyRegisteredCount} junction(s), but all are already registered in your list{sysMsg}.";
        }
        else if (skippedSystemCount > 0)
        {
            StatusText.Text = $"Scan finished ({inspected} folders checked). Skipped {skippedSystemCount} Windows system compatibility junction(s). No user-created junctions found.";
        }
        else
        {
            StatusText.Text = $"Scan finished ({inspected} folders checked). No NTFS directory junctions were found in this directory.";
        }
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
