using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace JunctionManager;

public partial class MainWindow : Window
{
    readonly ObservableCollection<JunctionEntry> items = new(IniStore.Load());
    readonly WatcherService watcher = new();
    readonly SemaphoreSlim gate = new(1, 1);
    readonly TrayService tray;
    bool isExiting;
    bool _loaded;

    public MainWindow()
    {
        InitializeComponent();
        App.DarkTitle(this);

        // Apply persisted theme
        var initialTheme = Enum.TryParse<AppTheme>(IniStore.Config.Theme, true, out var t) ? t : AppTheme.System;
        ThemeService.Initialize(initialTheme);
        switch (initialTheme)
        {
            case AppTheme.Light:
                ThemeLightBtn.IsChecked = true;
                break;
            case AppTheme.Dark:
                ThemeDarkBtn.IsChecked = true;
                break;
            default:
                ThemeSystemBtn.IsChecked = true;
                break;
        }

        TrayCloseBox.IsChecked = IniStore.Config.MinimizeToTray;
        _loaded = true;

        tray = new TrayService(this);
        tray.OpenRequested += RestoreWindow;
        tray.ExitRequested += ExitApp;

        List.ItemsSource = items;
        var view = CollectionViewSource.GetDefaultView(items);
        if (view != null)
        {
            view.Filter = FilterJunction;
        }

        foreach (var e in items) e.WatchChanged += OnWatchChanged;
        watcher.RepairRequested += e => Dispatcher.InvokeAsync(async () => await RepairAsync(new[] { e }));
        watcher.Sync(items);
        Closed += (_, _) => { watcher.Dispose(); tray.Dispose(); };
        Refresh();                      // saved junctions load from the ini and are checked on startup
    }

    void OnWatchChanged() { IniStore.Save(items); watcher.Sync(items); Refresh(); }

    bool FilterJunction(object obj)
    {
        if (obj is not JunctionEntry entry) return false;
        var query = SearchBox?.Text?.Trim();
        if (string.IsNullOrEmpty(query)) return true;

        return (entry.Name != null && entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            || (entry.Source != null && entry.Source.Contains(query, StringComparison.OrdinalIgnoreCase))
            || (entry.Target != null && entry.Target.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text;
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(query) ? Visibility.Visible : Visibility.Collapsed;
        SearchClearBtn.Visibility = string.IsNullOrEmpty(query) ? Visibility.Collapsed : Visibility.Visible;

        CollectionViewSource.GetDefaultView(items)?.Refresh();
        UpdateSummary();
    }

    void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (!string.IsNullOrEmpty(SearchBox.Text))
            {
                SearchBox.Text = "";
                e.Handled = true;
            }
        }
    }

    void SearchClear_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
        SearchBox.Focus();
    }

    void Refresh()
    {
        foreach (var e in items) e.Status = JunctionService.GetStatus(e);
        CollectionViewSource.GetDefaultView(items)?.Refresh();
        UpdateSummary();
        int watchCount = items.Count(x => x.Watch);
        tray?.EnsureTrayIcon($"Junction Manager ({watchCount} watched)");
    }

    void UpdateSummary()
    {
        var view = CollectionViewSource.GetDefaultView(items);
        int total = items.Count;
        int ok = items.Count(x => x.Status == "OK");

        var query = SearchBox?.Text?.Trim();
        if (!string.IsNullOrEmpty(query) && view != null)
        {
            int visibleCount = view.Cast<object>().Count();
            Summary.Text = $"{visibleCount} of {total} shown  •  {ok} OK";
            if (EmptyStateText != null)
                EmptyStateText.Text = $"No junctions match \"{query}\"";
        }
        else
        {
            Summary.Text = $"{total} junction(s)  •  {ok} OK  •  {total - ok} need attention";
            if (EmptyStateText != null)
                EmptyStateText.Text = "No junctions yet. Click New junction to add one.";
        }
    }

    public enum ToastType { Success, Info, Warning, Error }

    DispatcherTimer? _toastTimer;

    public void ShowToast(string message, ToastType type = ToastType.Success)
    {
        _toastTimer?.Stop();

        ToastMessage.Text = message;

        switch (type)
        {
            case ToastType.Success:
                ToastIconBg.Background = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                ToastIconPath.Data = Geometry.Parse("M4,11 L8,15 L16,7");
                break;
            case ToastType.Info:
                ToastIconBg.Background = (Brush)FindResource("Accent");
                ToastIconPath.Data = Geometry.Parse("M10,4 A6,6 0 1,0 10,16 A6,6 0 1,0 10,4 M10,8 V12 M10,6 H10.01");
                break;
            case ToastType.Warning:
                ToastIconBg.Background = new SolidColorBrush(Color.FromRgb(245, 158, 11));
                ToastIconPath.Data = Geometry.Parse("M10,3 L18,17 H2 L10,3 M10,8 V12 M10,14 H10.01");
                break;
            case ToastType.Error:
                ToastIconBg.Background = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                ToastIconPath.Data = Geometry.Parse("M5,5 L15,15 M15,5 L5,15");
                break;
        }

        ToastContainer.Visibility = Visibility.Visible;

        var animFade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220));
        var animSlide = new DoubleAnimation(-16, 0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        ToastContainer.BeginAnimation(UIElement.OpacityProperty, animFade);
        ToastTransform.BeginAnimation(TranslateTransform.YProperty, animSlide);

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.8) };
        _toastTimer.Tick += (s, e) =>
        {
            _toastTimer.Stop();
            HideToast();
        };
        _toastTimer.Start();
    }

    void HideToast()
    {
        var animFade = new DoubleAnimation(ToastContainer.Opacity, 0, TimeSpan.FromMilliseconds(200));
        var animSlide = new DoubleAnimation(ToastTransform.Y, -16, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        animFade.Completed += (s, e) =>
        {
            if (ToastContainer.Opacity <= 0.05)
                ToastContainer.Visibility = Visibility.Collapsed;
        };
        ToastContainer.BeginAnimation(UIElement.OpacityProperty, animFade);
        ToastTransform.BeginAnimation(TranslateTransform.YProperty, animSlide);
    }

    void ToastDismiss_Click(object sender, RoutedEventArgs e)
    {
        _toastTimer?.Stop();
        HideToast();
    }

    void Info(string title, string text) => MessageBox.Show(this, text, title, MessageBoxButton.OK, MessageBoxImage.Information);

    void ShowCard(bool show)
    {
        if (show)
        {
            ProgressCard.Visibility = Visibility.Visible;
            ProgressCard.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(250)));
        }
        else
        {
            var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(300));
            a.Completed += (_, _) => { if (ProgressCard.Opacity < 0.01) ProgressCard.Visibility = Visibility.Collapsed; };
            ProgressCard.BeginAnimation(OpacityProperty, a);
        }
    }

    void SetBar(double v)
    {
        double val = Math.Clamp(v, 0, 100);
        PBar.BeginAnimation(RangeBase.ValueProperty, new DoubleAnimation(val, TimeSpan.FromMilliseconds(150)));
        PPct.Text = $"{val:0}%";
    }

    async Task<string?> LinkWithProgressAsync(JunctionEntry e, string verb)
    {
        await gate.WaitAsync();
        ActionButtons.IsEnabled = false;
        try
        {
            PTitle.Text = $"{verb} {e.Name}";
            PBar.BeginAnimation(RangeBase.ValueProperty, null); PBar.Value = 0; PPct.Text = "0%";
            PText.Text = "Preparing...";
            ShowCard(true);
            var prog = new Progress<CopyProgress>(p => { SetBar(p.Percent); PText.Text = p.Text; });
            var err = await Task.Run(() => JunctionService.Link(e, prog));
            if (err == null) { SetBar(100); PText.Text = "Done. Junction is live."; } else PText.Text = err;
            await Task.Delay(1500);
            ShowCard(false);
            return err;
        }
        finally
        {
            ActionButtons.IsEnabled = true;
            gate.Release();
        }
    }

    async void New_Click(object s, RoutedEventArgs a)
    {
        var dlg = new NewJunctionWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Result is not { } e) return;
        if (items.Any(x => x.Source.Equals(e.Source, StringComparison.OrdinalIgnoreCase)))
        { ShowToast("A junction with that source is already in the list.", ToastType.Warning); return; }

        var err = await LinkWithProgressAsync(e, "Creating");
        if (err != null) { Info("Could not create junction", err); return; }
        e.WatchChanged += OnWatchChanged;
        items.Add(e); IniStore.Save(items); watcher.Sync(items); Refresh();
        ShowToast($"Created junction \"{e.Name}\"", ToastType.Success);
    }

    void Refresh_Click(object s, RoutedEventArgs a)
    {
        Refresh();
        int ok = items.Count(x => x.Status == "OK");
        int attn = items.Count - ok;
        if (items.Count == 0)
        {
            ShowToast("No junctions in list.", ToastType.Info);
        }
        else if (attn == 0)
        {
            ShowToast($"All {items.Count} junction(s) verified healthy (Status: OK)", ToastType.Success);
        }
        else
        {
            ShowToast($"{attn} of {items.Count} junction(s) need attention. Click 'Repair all' to fix.", ToastType.Warning);
        }
    }

    async Task RepairAsync(IEnumerable<JunctionEntry> es)
    {
        var errs = new List<string>();
        foreach (var e in es.ToList())
        {
            var r = await LinkWithProgressAsync(e, "Repairing");
            if (r != null) errs.Add($"{e.Name}: {r}");
        }
        Refresh();
        if (errs.Count > 0) ShowToast($"Some repairs failed: {string.Join(", ", errs)}", ToastType.Error);
    }

    async void Repair_Click(object s, RoutedEventArgs a)
    {
        if (List.SelectedItem is JunctionEntry e) await RepairAsync(new[] { e });
        else ShowToast("Pick a junction to repair, or use Repair all.", ToastType.Info);
    }

    async void RepairAll_Click(object s, RoutedEventArgs a)
    {
        if (items.Count == 0)
        {
            ShowToast("No junctions in list to repair.", ToastType.Info);
            return;
        }
        var broken = items.Where(x => x.Status != "OK").ToList();
        if (broken.Count == 0)
        {
            ShowToast("All junctions are already in OK status! No repairs needed.", ToastType.Success);
            return;
        }
        await RepairAsync(broken);
        Refresh();
        int remaining = items.Count(x => x.Status != "OK");
        if (remaining == 0)
        {
            ShowToast($"Successfully repaired {broken.Count} junction(s).", ToastType.Success);
        }
        else
        {
            ShowToast($"Repaired {broken.Count - remaining} junction(s). {remaining} still need attention.", ToastType.Warning);
        }
    }

    void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        if (items.Count == 0)
        {
            ShowToast("List is already empty.", ToastType.Info);
            return;
        }

        if (MessageBox.Show(this,
            $"Are you sure you want to clear all {items.Count} junction(s) from the list?\n\n" +
            "This only removes the entries from Junction Manager. " +
            "Existing folder links and actual files on disk will NOT be deleted.",
            "Clear Junction List", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        int count = items.Count;
        items.Clear();
        IniStore.Save(items);
        watcher.Sync(items);
        Refresh();
        ShowToast($"Cleared {count} junction(s) from list.", ToastType.Info);
    }

    void Open_Click(object s, RoutedEventArgs a)
    {
        if (List.SelectedItem is not JunctionEntry e)
        {
            ShowToast("Pick a junction to open its target folder.", ToastType.Info);
            return;
        }
        if (!Directory.Exists(e.Target))
        {
            ShowToast($"Target folder does not exist:\n{e.Target}", ToastType.Error);
            return;
        }
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{e.Target}\"") { UseShellExecute = true });
    }

    void Remove_Click(object s, RoutedEventArgs a)
    {
        if (List.SelectedItem is not JunctionEntry e)
        {
            ShowToast("Pick a junction to remove.", ToastType.Info);
            return;
        }
        if (MessageBox.Show(this, $"Remove \"{e.Name}\"?\n\nThis deletes the junction link and the list entry. Files in the target folder are not touched.",
                "Remove junction", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var err = JunctionService.Unlink(e);
        if (err != null) { ShowToast($"Could not remove link: {err}", ToastType.Error); return; }
        items.Remove(e); IniStore.Save(items); watcher.Sync(items); Refresh();
        ShowToast($"Removed junction \"{e.Name}\"", ToastType.Info);
    }

    void Scan_Click(object sender, RoutedEventArgs e)
    {
        var scanDlg = new ScanJunctionsWindow(items) { Owner = this };
        if (scanDlg.ShowDialog() == true && scanDlg.Discovered.Count > 0)
        {
            var resultsDlg = new ScanResultsWindow(scanDlg.Discovered) { Owner = this };
            if (resultsDlg.ShowDialog() == true && resultsDlg.SelectedEntries.Count > 0)
            {
                foreach (var entry in resultsDlg.SelectedEntries)
                {
                    entry.Status = JunctionService.GetStatus(entry);
                    entry.WatchChanged += OnWatchChanged;
                    items.Add(entry);
                }
                IniStore.Save(items);
                watcher.Sync(items);
                Refresh();

                ShowToast($"Successfully imported {resultsDlg.SelectedEntries.Count} junction(s)", ToastType.Success);
            }
        }
    }

    void About_Click(object s, RoutedEventArgs a)
    {
        var dlg = new AboutWindow { Owner = this };
        dlg.ShowDialog();
    }

    void ThemeRadio_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag } && Enum.TryParse<AppTheme>(tag, true, out var theme))
        {
            ThemeService.Apply(theme);
            IniStore.Config.Theme = theme.ToString();
            IniStore.Save(items);
        }
    }

    void ToggleHidden_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: JunctionEntry entry })
        {
            entry.Hidden = !entry.Hidden;
            JunctionService.ApplyFlags(entry);
            IniStore.Save(items);
            if (entry.Hidden)
                ShowToast($"Target folder for \"{entry.Name}\" is now Hidden in Explorer", ToastType.Info);
            else
                ShowToast($"Target folder for \"{entry.Name}\" is now Visible in Explorer", ToastType.Info);
        }
    }

    void ToggleProtected_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: JunctionEntry entry })
        {
            entry.Protected = !entry.Protected;
            JunctionService.ApplyFlags(entry);
            IniStore.Save(items);
            if (entry.Protected)
                ShowToast($"Target folder for \"{entry.Name}\" is now Protected from deletion", ToastType.Success);
            else
                ShowToast($"Deletion protection removed for \"{entry.Name}\"", ToastType.Warning);
        }
    }

    async void RowRepair_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: JunctionEntry entry })
        {
            var err = await LinkWithProgressAsync(entry, "Repairing");
            Refresh();
            if (err != null)
            {
                ShowToast($"Could not repair \"{entry.Name}\": {err}", ToastType.Error);
            }
            else
            {
                ShowToast($"Junction \"{entry.Name}\" is healthy (Status: OK).", ToastType.Success);
            }
        }
    }

    void RowOpen_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: JunctionEntry entry })
        {
            if (!Directory.Exists(entry.Target))
            {
                ShowToast($"Target folder does not exist:\n{entry.Target}", ToastType.Error);
                return;
            }
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{entry.Target}\"") { UseShellExecute = true });
        }
    }

    void RowDismiss_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: JunctionEntry entry })
        {
            if (MessageBox.Show(this,
                $"Remove \"{entry.Name}\" from the list?\n\n" +
                "This only removes the junction from Junction Manager's view and settings file. " +
                "The junction link on disk and all files in the target folder will remain completely untouched.",
                "Remove from List", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            items.Remove(entry);
            IniStore.Save(items);
            watcher.Sync(items);
            Refresh();
            ShowToast($"Removed \"{entry.Name}\" from list (link & files preserved)", ToastType.Info);
        }
    }

    void RowRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: JunctionEntry entry })
        {
            if (MessageBox.Show(this,
                $"Delete junction link for \"{entry.Name}\"?\n\n" +
                "This deletes the junction folder link from Windows and removes it from the list. " +
                "Files in the target storage folder will NOT be touched.",
                "Delete Junction Link", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            var err = JunctionService.Unlink(entry);
            if (err != null) { ShowToast($"Could not remove link: {err}", ToastType.Error); return; }
            items.Remove(entry);
            IniStore.Save(items);
            watcher.Sync(items);
            Refresh();
            ShowToast($"Deleted junction link \"{entry.Name}\"", ToastType.Info);
        }
    }

    void TrayCloseBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded || TrayCloseBox == null) return;
        IniStore.Config.MinimizeToTray = TrayCloseBox.IsChecked == true;
        IniStore.Save(items);
    }

    void RestoreWindow()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Focus();
    }

    void ExitApp()
    {
        isExiting = true;
        tray.Dispose();
        Application.Current.Shutdown();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!isExiting && TrayCloseBox.IsChecked == true)
        {
            e.Cancel = true;
            Hide();
            int watching = items.Count(x => x.Watch);
            tray.ShowBalloon("Junction Manager", watching > 0
                ? $"Running in background. {watching} junction(s) are actively monitored."
                : "Running in background.");
            return;
        }
        tray.Dispose();
        base.OnClosing(e);
    }
}
