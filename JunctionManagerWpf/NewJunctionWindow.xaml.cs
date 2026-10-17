using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace JunctionManager;

public partial class NewJunctionWindow : Window
{
    public JunctionEntry? Result { get; private set; }

    public NewJunctionWindow() { InitializeComponent(); App.DarkTitle(this); }

    void Browse(System.Windows.Controls.TextBox box)
    {
        var d = new OpenFolderDialog();
        if (d.ShowDialog(this) == true) box.Text = d.FolderName;
    }
    void BrowseSrc_Click(object s, RoutedEventArgs e) => Browse(SrcBox);
    void BrowseTgt_Click(object s, RoutedEventArgs e) => Browse(TgtBox);

    void Create_Click(object s, RoutedEventArgs a)
    {
        var src = SrcBox.Text.Trim().TrimEnd('\\');
        var tgt = TgtBox.Text.Trim().TrimEnd('\\');
        if (src == "" || tgt == "") { Err.Text = "Source and target are required."; return; }
        if (src.Equals(tgt, StringComparison.OrdinalIgnoreCase)) { Err.Text = "Source and target can't be the same."; return; }
        var name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileName(src);
        if (string.IsNullOrWhiteSpace(name)) name = src;
        Result = new JunctionEntry
        {
            Name = name,
            Source = src, Target = tgt,
            Hidden = HideBox.IsChecked == true, Protected = ProtBox.IsChecked == true,
            Watch = WatchBox.IsChecked == true
        };
        DialogResult = true;
    }
}
