using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using EnvyConverter.Conversion;
using EnvyConverter.Ui;
using Microsoft.Win32;

namespace EnvyConverter;

public partial class MainWindow : Window
{
    private readonly ConversionService _service = new();
    private readonly ObservableCollection<FileItem> _files = new();
    private string? _outputFolder;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        Title = ThemeLoader.AppName;
        AppTitle.Text = ThemeLoader.AppName;
        FileList.ItemsSource = _files;
        _files.CollectionChanged += (_, _) => RefreshState();

        FormatList.ItemsSource = new List<ChipOption<OutputFormat>>
        {
            Chip("DOCX", OutputFormat.Docx, "Word editável. Texto, negrito, recuos e imagens reconstruídos."),
            Chip("DOCX fiel", OutputFormat.DocxFiel, "Word com cada página como imagem: idêntico ao PDF, mas o texto não é editável."),
            Chip("DOC", OutputFormat.Doc, "Word 97-2003. Requer Microsoft Word ou LibreOffice."),
            Chip("RTF", OutputFormat.Rtf, "Rich Text, abre em qualquer editor. Requer Word ou LibreOffice."),
            Chip("ODT", OutputFormat.Odt, "OpenDocument (LibreOffice/Google Docs). Requer Word ou LibreOffice."),
            Chip("TXT", OutputFormat.Txt, "Somente o texto, em UTF-8."),
            Chip("HTML", OutputFormat.Html, "Página web. Requer Word ou LibreOffice."),
            Chip("PDF", OutputFormat.Pdf, "Para converter DOCX, DOC, RTF, ODT, TXT ou HTML em PDF."),
            Chip("PNG", OutputFormat.Png, "Uma imagem PNG por página (200 dpi), numa pasta."),
            Chip("JPG", OutputFormat.Jpg, "Uma imagem JPG por página (200 dpi), numa pasta."),
        };
        FormatList.SelectedIndex = 0;

        EngineList.ItemsSource = new List<ChipOption<EngineChoice>>
        {
            Chip("Automático", EngineChoice.Auto, "Usa o melhor motor disponível e tenta o próximo se algum falhar."),
            Chip("Word", EngineChoice.Word, _service.Word.IsAvailable
                ? "Microsoft Word instalado: maior fidelidade." : "Microsoft Word não encontrado neste computador.", _service.Word.IsAvailable),
            Chip("LibreOffice", EngineChoice.LibreOffice, _service.LibreOffice.IsAvailable
                ? "LibreOffice instalado." : "LibreOffice não encontrado neste computador.", _service.LibreOffice.IsAvailable),
            Chip("Interno", EngineChoice.BuiltIn, "Embutido no programa, não precisa de nada instalado. Apenas PDF de entrada."),
        };
        EngineList.SelectedIndex = 0;

        EngineStatus.Text = "Motores: Interno ✓   ·   Word " + (_service.Word.IsAvailable ? "✓" : "—") +
                            "   ·   LibreOffice " + (_service.LibreOffice.IsAvailable ? "✓" : "—");

        StateChanged += (_, _) => Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);

        // Permite "Abrir com" e arrastar arquivos sobre o .exe.
        AddFiles(Environment.GetCommandLineArgs().Skip(1));
        RefreshState();
    }

    private static ChipOption<T> Chip<T>(string label, T value, string description, bool available = true) =>
        new() { Label = label, Value = value, Description = description, IsAvailable = available };

    private OutputFormat SelectedFormat => ((ChipOption<OutputFormat>)FormatList.SelectedItem).Value;
    private EngineChoice SelectedEngine => ((ChipOption<EngineChoice>)EngineList.SelectedItem).Value;

    private void AddFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                AddFiles(Directory.EnumerateFiles(path));
                continue;
            }
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (!File.Exists(path) || !OutputFormats.SupportedInputs.Contains(ext)) continue;
            if (_files.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase))) continue;
            _files.Add(new FileItem(path));
        }
    }

    private void RefreshState()
    {
        if (FormatList.SelectedItem is not ChipOption<OutputFormat> format ||
            EngineList.SelectedItem is not ChipOption<EngineChoice> engine) return;

        FormatHint.Text = format.Description;
        EngineHint.Text = engine.Description;
        EmptyHint.Visibility = _files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        FilesHeader.Text = _files.Count == 0 ? "ARQUIVOS" : $"ARQUIVOS ({_files.Count})";
        ClearButton.IsEnabled = !_busy && _files.Count > 0;
        ConvertButton.IsEnabled = !_busy && _files.Count > 0;
        FormatList.IsEnabled = EngineList.IsEnabled = !_busy;
        ConvertButton.Content = _busy ? "Convertendo…" : _files.Count > 1 ? $"Converter {_files.Count} arquivos" : "Converter";
    }

    private async void OnConvert(object sender, RoutedEventArgs e)
    {
        var format = SelectedFormat;
        var engine = SelectedEngine;
        var queue = _files.ToList();
        int ok = 0, failed = 0;

        _busy = true;
        RefreshState();
        Progress.Value = 0;

        for (int i = 0; i < queue.Count; i++)
        {
            var item = queue[i];
            ProgressText.Text = $"Convertendo {i + 1} de {queue.Count}: {item.Name}";
            item.Status = FileStatus.Working;
            try
            {
                if (!_service.CanConvert(item.Path, format, engine))
                    throw new NotSupportedException(UnsupportedMessage(item.Path, format));

                var result = await _service.ConvertAsync(item.Path, format, engine, _outputFolder, CancellationToken.None);
                item.OutputPath = result.OutputPath;
                item.Detail = $"Salvo em {result.OutputPath}  ·  motor: {result.EngineName}  ·  verificado ✓";
                item.Status = FileStatus.Done;
                ok++;
            }
            catch (Exception ex)
            {
                item.Detail = ex.Message.Replace(Environment.NewLine, "  |  ");
                item.Status = FileStatus.Failed;
                failed++;
            }
            Progress.Value = (i + 1.0) / queue.Count;
        }

        ProgressText.Text = failed == 0
            ? $"Pronto! {ok} arquivo(s) convertido(s) e verificado(s)."
            : $"{ok} convertido(s), {failed} com erro. Passe o mouse no erro para ver o motivo.";
        _busy = false;
        RefreshState();
    }

    private static string UnsupportedMessage(string path, OutputFormat format)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".pdf" && format == OutputFormat.Pdf) return "O arquivo já é PDF.";
        if (ext != ".pdf" && format is OutputFormat.DocxFiel or OutputFormat.Png or OutputFormat.Jpg)
            return "Este formato só está disponível para arquivos PDF.";
        return $"Para {ConversionService.Label(format)} a partir de {ext.TrimStart('.').ToUpperInvariant()}, instale o Microsoft Word ou o LibreOffice (ou escolha o motor Automático).";
    }

    private void OnPickFiles(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Title = "Escolha os arquivos para converter",
            Filter = "Documentos|*.pdf;*.docx;*.doc;*.rtf;*.odt;*.txt;*.html;*.htm|PDF|*.pdf|Todos os arquivos|*.*",
        };
        if (dialog.ShowDialog(this) == true) AddFiles(dialog.FileNames);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = !_busy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        DropRect.Stroke = (System.Windows.Media.Brush)FindResource("AccentBrush");
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e) =>
        DropRect.Stroke = (System.Windows.Media.Brush)FindResource("LineBrush");

    private void OnDrop(object sender, DragEventArgs e)
    {
        DropRect.Stroke = (System.Windows.Media.Brush)FindResource("LineBrush");
        if (!_busy && e.Data.GetData(DataFormats.FileDrop) is string[] paths) AddFiles(paths);
    }

    private void OnPickFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Onde salvar os arquivos convertidos?" };
        if (dialog.ShowDialog(this) == true)
        {
            _outputFolder = dialog.FolderName;
            OutputFolderText.Text = _outputFolder;
        }
    }

    private void OnResetFolder(object sender, RoutedEventArgs e)
    {
        _outputFolder = null;
        OutputFolderText.Text = "Mesma pasta do arquivo original";
    }

    private void OnOpenOutput(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not FileItem { OutputPath: { } path }) return;
        var args = Directory.Exists(path) ? $"\"{path}\"" : $"/select,\"{path}\"";
        Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if (!_busy && (sender as FrameworkElement)?.Tag is FileItem item) _files.Remove(item);
    }

    private void OnClear(object sender, RoutedEventArgs e) => _files.Clear();

    private void OnOptionChanged(object sender, SelectionChangedEventArgs e) => RefreshState();

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
