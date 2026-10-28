using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace EnvyConverter.Mobile;

public enum FileStatus { Waiting, Working, Done, Failed }

public sealed class FileItem : INotifyPropertyChanged
{
    private FileStatus _status = FileStatus.Waiting;
    private string _detail;

    public FileItem(string name, string localCopy, long size)
    {
        Name = name;
        LocalCopy = localCopy;
        _detail = FormatSize(size);
    }

    public string Name { get; }

    /// <summary>Cópia do PDF no cache do app. O arquivo original nunca é aberto para escrita.</summary>
    public string LocalCopy { get; }

    public IReadOnlyList<string> Outputs { get; set; } = Array.Empty<string>();

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
            OnChanged(nameof(StatusColor));
            OnChanged(nameof(IsDone));
        }
    }

    public bool IsDone => Status == FileStatus.Done;

    public string StatusText => Status switch
    {
        FileStatus.Working => "Convertendo…",
        FileStatus.Done => "Concluído",
        FileStatus.Failed => "Erro",
        _ => "Aguardando",
    };

    public Color StatusColor => Status switch
    {
        FileStatus.Working => Color.FromArgb("#22D3EE"),
        FileStatus.Done => Color.FromArgb("#34D399"),
        FileStatus.Failed => Color.FromArgb("#F87171"),
        _ => Color.FromArgb("#9AA1C9"),
    };

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
