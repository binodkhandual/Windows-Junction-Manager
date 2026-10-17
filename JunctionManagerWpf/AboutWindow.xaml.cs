using System.Reflection;
using System.Windows;

namespace JunctionManager;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        App.DarkTitle(this);
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        if (v != null) VersionText.Text = $"v{v.Major}.{v.Minor}.{v.Build}";
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
