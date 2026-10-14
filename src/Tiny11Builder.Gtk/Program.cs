using Gtk;

namespace Tiny11Builder.Gtk;

internal static class Program
{
    private static void Main()
    {
        Application.Init();
        var window = new MainWindow();
        window.ShowAll();
        window.RefreshVisibility();
        Application.Run();
    }
}
