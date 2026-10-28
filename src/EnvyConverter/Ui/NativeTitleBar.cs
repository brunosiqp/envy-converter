using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace EnvyConverter.Ui;

/// <summary>
/// Deixa a barra de título nativa do Windows escura e nas cores do tema.
/// Os botões minimizar/maximizar/fechar continuam sendo os do próprio Windows.
/// </summary>
internal static class NativeTitleBar
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19; // Windows 10 antes do 20H1
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_CAPTION_COLOR = 35;               // Windows 11
    private const int DWMWA_TEXT_COLOR = 36;                  // Windows 11

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void Apply(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            int on = 1;
            if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref on, sizeof(int));

            if (Application.Current.Resources["SurfaceBrush"] is SolidColorBrush caption)
            {
                int color = ColorRef(caption.Color);
                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref color, sizeof(int));
            }
            if (Application.Current.Resources["TextBrush"] is SolidColorBrush text)
            {
                int color = ColorRef(text.Color);
                DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref color, sizeof(int));
            }
        }
        catch
        {
            // Versões antigas do Windows: fica a barra padrão, sem problema.
        }
    }

    private static int ColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);
}
