using System.Windows;

namespace JunctionManager;

public partial class App : Application
{
    public static void DarkTitle(Window w) =>
        w.SourceInitialized += (_, _) => ThemeService.UpdateTitleBar(w, ThemeService.IsEffectiveDark);
}
