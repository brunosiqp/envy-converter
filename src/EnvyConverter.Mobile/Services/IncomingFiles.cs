using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;
using Android.Content;
using Android.Provider;

namespace EnvyConverter.Mobile.Services;

/// <summary>Recebe PDFs enviados por outros apps ("Compartilhar" / "Abrir com").</summary>
public static class IncomingFiles
{
    public static event Action<IReadOnlyList<(string Name, string Path, long Size)>>? Received;

    private static readonly List<(string, string, long)> Pending = new();

    public static void Handle(Intent? intent)
    {
        if (intent is null) return;
        var uris = new List<Android.Net.Uri>();

        if (intent.Action == Intent.ActionView && intent.Data is { } data)
            uris.Add(data);
        else if (intent.Action == Intent.ActionSend && intent.GetParcelableExtra(Intent.ExtraStream) is Android.Net.Uri one)
            uris.Add(one);
        else if (intent.Action == Intent.ActionSendMultiple &&
                 intent.GetParcelableArrayListExtra(Intent.ExtraStream) is { } many)
            uris.AddRange(many.OfType<Android.Net.Uri>());

        if (uris.Count == 0) return;
        var files = new List<(string, string, long)>();
        foreach (var uri in uris)
        {
            try { files.Add(CopyToCache(uri)); }
            catch { }
        }
        intent.SetAction(null); // não processa de novo ao girar a tela

        if (Received is null) Pending.AddRange(files);
        else Received.Invoke(files);
    }

    public static IReadOnlyList<(string Name, string Path, long Size)> TakePending()
    {
        var copy = Pending.ToList();
        Pending.Clear();
        return copy;
    }

    private static (string, string, long) CopyToCache(Android.Net.Uri uri)
    {
        var resolver = Android.App.Application.Context.ContentResolver!;
        string name = uri.LastPathSegment is { Length: > 0 } last ? System.IO.Path.GetFileName(last) : "documento.pdf";
        try
        {
            using var cursor = resolver.Query(uri, new[] { IOpenableColumns.DisplayName }, null, null, null);
            if (cursor is not null && cursor.MoveToFirst())
                name = cursor.GetString(0) ?? name;
        }
        catch
        {
            // Alguns apps não informam o nome; fica o nome do caminho.
        }
        if (!name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) name += ".pdf";

        var dir = System.IO.Path.Combine(FileSystem.CacheDirectory, "entrada", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = System.IO.Path.Combine(dir, name);
        using (var input = resolver.OpenInputStream(uri) ?? throw new IOException("Arquivo indisponível."))
        using (var output = File.Create(path))
            input.CopyTo(output);
        return (name, path, new FileInfo(path).Length);
    }
}
