using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace EnvyConverter.Ui;

/// <summary>
/// Lê o tema.json que fica ao lado do executável e troca as cores da interface.
/// Assim dá para aplicar a paleta da equipe sem recompilar o programa.
/// </summary>
public static class ThemeLoader
{
    public static string AppName { get; private set; } = "Envy Converter";

    private static readonly Dictionary<string, string> KeyMap = new()
    {
        ["Fundo"] = "BgBrush",
        ["Superficie"] = "SurfaceBrush",
        ["SuperficieAlta"] = "SurfaceHighBrush",
        ["Borda"] = "LineBrush",
        ["Texto"] = "TextBrush",
        ["TextoSuave"] = "MutedBrush",
        ["Destaque"] = "AccentBrush",
        ["Destaque2"] = "Accent2Brush",
        ["Sucesso"] = "SuccessBrush",
        ["Erro"] = "ErrorBrush",
        ["Aviso"] = "WarnBrush",
    };

    public static void Apply(ResourceDictionary resources)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "tema.json");
        if (!File.Exists(path)) return;

        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var prop in json.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.String) continue;
                var value = prop.Value.GetString()!;

                if (prop.Name == "NomeApp")
                {
                    if (!string.IsNullOrWhiteSpace(value)) AppName = value;
                    continue;
                }
                if (KeyMap.TryGetValue(prop.Name, out var key) && TryParse(value, out var color))
                    resources[key] = Frozen(new SolidColorBrush(color));
            }

            if (resources["AccentBrush"] is SolidColorBrush a && resources["Accent2Brush"] is SolidColorBrush b)
            {
                var gradient = new LinearGradientBrush(a.Color, b.Color, new Point(0, 0), new Point(1, 1));
                resources["AccentGradient"] = Frozen(gradient);
            }
        }
        catch
        {
            // Tema inválido: mantém as cores padrão em vez de impedir o programa de abrir.
        }
    }

    private static bool TryParse(string hex, out Color color)
    {
        try
        {
            color = (Color)ColorConverter.ConvertFromString(hex);
            return true;
        }
        catch
        {
            color = default;
            return false;
        }
    }

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
