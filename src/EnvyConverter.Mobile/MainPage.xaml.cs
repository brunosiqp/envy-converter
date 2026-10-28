using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;
using System.Collections.ObjectModel;
using EnvyConverter.Conversion;
using EnvyConverter.Mobile.Services;

namespace EnvyConverter.Mobile;

public partial class MainPage : ContentPage
{
    private readonly Dictionary<OutputFormat, Border> _chips = new();
    private OutputFormat _format = OutputFormat.Docx;
    private bool _busy;

    public ObservableCollection<FileItem> Files { get; } = new();

    public MainPage()
    {
        InitializeComponent();
        BindingContext = this;
        Files.CollectionChanged += (_, _) => RefreshState();

        foreach (var format in MobileConverter.Formats)
            FormatChips.Children.Add(CreateChip(format));
        SelectFormat(OutputFormat.Docx);

        IncomingFiles.Received += files => MainThread.BeginInvokeOnMainThread(() => AddFiles(files));
        AddFiles(IncomingFiles.TakePending());
        RefreshState();
    }

    private Border CreateChip(OutputFormat format)
    {
        var chip = new Border
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            StrokeThickness = 1,
            Padding = new Thickness(16, 9),
            Margin = new Thickness(0, 0, 8, 8),
            Content = new Label { Text = MobileConverter.Label(format), FontSize = 14 },
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => { if (!_busy) SelectFormat(format); };
        chip.GestureRecognizers.Add(tap);
        _chips[format] = chip;
        return chip;
    }

    private void SelectFormat(OutputFormat format)
    {
        _format = format;
        foreach (var (key, chip) in _chips)
        {
            bool selected = key == format;
            var label = (Label)chip.Content!;
            chip.Background = selected ? (Brush)Application.Current!.Resources["AccentGradient"] : new SolidColorBrush(Color.FromArgb("#212A34"));
            chip.Stroke = selected ? Colors.Transparent : Color.FromArgb("#2C3740");
            label.TextColor = selected ? Colors.White : Color.FromArgb("#E6EDF3");
            label.FontAttributes = selected ? FontAttributes.Bold : FontAttributes.None;
        }
        FormatHint.Text = MobileConverter.Description(format);
    }

    private void AddFiles(IEnumerable<(string Name, string Path, long Size)> files)
    {
        foreach (var (name, path, size) in files)
            Files.Add(new FileItem(name, path, size));
    }

    private bool? _wide;

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        // Tablet deitado (ou tela larga): duas colunas. Em pé ou celular: uma coluna.
        if (width > 0) ApplyLayout(width > height && width >= 840);
    }

    private void ApplyLayout(bool wide)
    {
        if (_wide == wide) return;
        _wide = wide;

        Body.ColumnDefinitions.Clear();
        Body.RowDefinitions.Clear();
        Body.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        Body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        MainStack.Remove(StepFormat);
        SideStack.Remove(StepFormat);

        if (wide)
        {
            Body.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            Body.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(440)));
            SideStack.Add(StepFormat);

            Grid.SetColumn(MainScroll, 0); Grid.SetRow(MainScroll, 0); Grid.SetRowSpan(MainScroll, 2);
            Grid.SetColumn(SideScroll, 1); Grid.SetRow(SideScroll, 0);
            Grid.SetColumn(ActionBar, 1); Grid.SetRow(ActionBar, 1);
            SideScroll.IsVisible = true;
            ActionBar.Margin = new Thickness(0, 0, 20, 20);
            ActionBar.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 };
        }
        else
        {
            Body.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            MainStack.Add(StepFormat);

            Grid.SetColumn(MainScroll, 0); Grid.SetRow(MainScroll, 0); Grid.SetRowSpan(MainScroll, 1);
            Grid.SetColumn(ActionBar, 0); Grid.SetRow(ActionBar, 1);
            SideScroll.IsVisible = false;
            ActionBar.Margin = new Thickness(0);
            ActionBar.StrokeShape = new Microsoft.Maui.Controls.Shapes.Rectangle();
        }
    }

    private void RefreshState()
    {
        FilesHeader.Text = Files.Count == 0 ? "Escolha os PDFs" : $"PDFs escolhidos ({Files.Count})";
        PickTitle.Text = Files.Count == 0 ? "Toque aqui para escolher" : "Adicionar mais PDFs";
        if (!_busy && Files.All(f => f.Status == FileStatus.Waiting))
            ProgressText.Text = Files.Count == 0 ? "Escolha os PDFs para começar." : $"Tudo pronto. Toque em Converter.";
        ClearButton.IsEnabled = !_busy && Files.Count > 0;
        ConvertButton.IsEnabled = !_busy && Files.Count > 0;
        ConvertButton.Opacity = ConvertButton.IsEnabled ? 1 : 0.4;
        ConvertButton.Text = _busy ? "Convertendo…" : Files.Count > 1 ? $"Converter {Files.Count} arquivos" : "Converter";
    }

    private async void OnPickFiles(object? sender, TappedEventArgs e)
    {
        if (_busy) return;
        try
        {
            var picked = await FilePicker.Default.PickMultipleAsync(new PickOptions
            {
                PickerTitle = "Escolha os PDFs",
                FileTypes = FilePickerFileType.Pdf,
            });
            if (picked is null) return;

            var files = new List<(string, string, long)>();
            foreach (var file in picked.Where(f => f is not null))
            {
                // Copia para o cache do app: o original só é lido, nunca alterado.
                var dir = Path.Combine(FileSystem.CacheDirectory, "entrada", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                var local = Path.Combine(dir, file!.FileName);
                await using (var input = await file.OpenReadAsync())
                await using (var output = File.Create(local))
                    await input.CopyToAsync(output);
                files.Add((file.FileName, local, new FileInfo(local).Length));
            }
            AddFiles(files);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Não foi possível abrir", ex.Message, "OK");
        }
    }

    private async void OnConvert(object? sender, EventArgs e)
    {
        var queue = Files.ToList();
        int ok = 0, failed = 0;
        string? savedTo = null;
        _busy = true;
        RefreshState();
        Progress.Progress = 0;

        for (int i = 0; i < queue.Count; i++)
        {
            var item = queue[i];
            ProgressText.Text = $"Convertendo {i + 1} de {queue.Count}: {item.Name}";
            item.Status = FileStatus.Working;
            try
            {
                item.Outputs = await MobileConverter.ConvertAsync(item.LocalCopy, item.Name, _format, CancellationToken.None);
                savedTo = await Task.Run(() => DownloadsSaver.Save(item.Outputs));
                item.Detail = $"Salvo em {savedTo}  ·  verificado ✓";
                item.Status = FileStatus.Done;
                ok++;
            }
            catch (Exception ex)
            {
                item.Detail = ex.Message;
                item.Status = FileStatus.Failed;
                failed++;
            }
            Progress.Progress = (i + 1.0) / queue.Count;
        }

        ProgressText.Text = failed == 0
            ? $"Pronto! {ok} arquivo(s) convertido(s) e salvo(s) em {savedTo}."
            : $"{ok} convertido(s), {failed} com erro.";
        _busy = false;
        RefreshState();
    }

    private async void OnOpen(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not FileItem { Outputs.Count: > 0 } item) return;
        try
        {
            await Launcher.Default.OpenAsync(new OpenFileRequest(item.Name, new ReadOnlyFile(item.Outputs[0], MobileConverter.Mime(item.Outputs[0]))));
        }
        catch
        {
            await DisplayAlertAsync("Nenhum app para abrir",
                "Instale um app que abra este tipo de arquivo (por exemplo Microsoft Word, Google Docs ou WPS Office), ou use Compartilhar.", "OK");
        }
    }

    private async void OnShare(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not FileItem { Outputs.Count: > 0 } item) return;
        await Share.Default.RequestAsync(new ShareMultipleFilesRequest
        {
            Title = item.Name,
            Files = item.Outputs.Select(f => new ShareFile(f, MobileConverter.Mime(f))).ToList(),
        });
    }

    private void OnClear(object? sender, EventArgs e)
    {
        if (!_busy) Files.Clear();
    }
}
