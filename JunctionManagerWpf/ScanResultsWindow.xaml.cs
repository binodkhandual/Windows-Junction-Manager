using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace JunctionManager;

public class ScannedJunctionModel : INotifyPropertyChanged
{
    private bool _isSelected = true;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }

    public JunctionEntry Entry { get; set; } = null!;
    public string Name => Entry.Name;
    public string Source => Entry.Source;
    public string Target => Entry.Target;
    public string Status => Entry.Status;
    public Brush StatusBrush => Entry.StatusBrush;

    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class ScanResultsWindow : Window
{
    public ObservableCollection<ScannedJunctionModel> Items { get; } = new();
    public List<JunctionEntry> SelectedEntries { get; } = new();

    public ScanResultsWindow(IEnumerable<JunctionEntry> discovered)
    {
        InitializeComponent();
        App.DarkTitle(this);

        foreach (var entry in discovered)
        {
            entry.Status = JunctionService.GetStatus(entry);
            Items.Add(new ScannedJunctionModel { Entry = entry, IsSelected = true });
        }

        DiscoveredList.ItemsSource = Items;
        HeaderSubtitle.Text = $"Found {Items.Count} NTFS directory junction(s). Select the ones you want to import.";
        UpdateSelectionSummary();
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        bool isChecked = SelectAllHeaderBox.IsChecked == true;
        foreach (var item in Items)
        {
            item.IsSelected = isChecked;
        }
        UpdateSelectionSummary();
    }

    private void ItemCheck_Click(object sender, RoutedEventArgs e)
    {
        UpdateSelectionSummary();
    }

    private void UpdateSelectionSummary()
    {
        int total = Items.Count;
        int selected = Items.Count(x => x.IsSelected);

        SelectedCountText.Text = $"{selected} of {total} selected";
        ImportButtonText.Text = selected > 0 ? $"Import Selected ({selected})" : "Import Selected";
        ImportButton.IsEnabled = selected > 0;

        if (selected == total)
            SelectAllHeaderBox.IsChecked = true;
        else if (selected == 0)
            SelectAllHeaderBox.IsChecked = false;
        else
            SelectAllHeaderBox.IsChecked = null; // Indeterminate
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        SelectedEntries.Clear();
        SelectedEntries.AddRange(Items.Where(x => x.IsSelected).Select(x => x.Entry));
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
