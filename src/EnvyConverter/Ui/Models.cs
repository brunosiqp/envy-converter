using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace EnvyConverter.Ui;

public sealed class ChipOption<T>
{
    public required string Label { get; init; }
    public required string Description { get; init; }
    public required T Value { get; init; }
    public bool IsAvailable { get; init; } = true;
}

public enum FileStatus { Waiting, Working, Done, Failed }

public sealed class FileItem : INotifyPropertyChanged
{
    private FileStatus _status = FileStatus.Waiting;
    private string _detail;

    public FileItem(string path)
    {
        Path = path;
        var info = new FileInfo(path);
        _detail = FormatSize(info.Exists ? info.Length : 0) + " · " + System.IO.Path.GetDirectoryName(path);
    }

    public string Path { get; }
    public string Name => System.IO.Path.GetFileName(Path);
    public string Extension => System.IO.Path.GetExtension(Path).TrimStart('.').ToUpperInvariant();
    public string? OutputPath { get; set; }

    public string Detail
    {
        get => _detail;
        set { _detail = value; OnChanged(); }
    }

    public FileStatus Status
    {
        get => _status;
        set
        {
            _status = value;
            OnChanged();
            OnChanged(nameof(StatusText));
            OnChanged(nameof(StatusBrush));
            OnChanged(nameof(OpenVisibility));
        }
    }

    public string StatusText => Status switch
    {
        FileStatus.Working => "Convertendo…",
        FileStatus.Done => "Concluído",
        FileStatus.Failed => "Erro",
        _ => "Aguardando",
    };

    public Brush StatusBrush => (Brush)Application.Current.Resources[Status switch
    {
        FileStatus.Working => "AccentBrush",
        FileStatus.Done => "SuccessBrush",
        FileStatus.Failed => "ErrorBrush",
        _ => "MutedBrush",
    }];

    public Visibility OpenVisibility => Status == FileStatus.Done ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024):0.#} MB",
    };
}
