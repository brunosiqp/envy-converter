using Android.Content;
using Android.OS;
using Android.Provider;

namespace EnvyConverter.Mobile.Services;

/// <summary>
/// Salva uma cópia em Downloads/EnvyConverter (Android 10+), sem pedir permissão.
/// Se já existir um arquivo com o mesmo nome, o Android cria "nome (1)" — nada é sobrescrito.
/// </summary>
public static class DownloadsSaver
{
    public static string Save(IReadOnlyList<string> files)
    {
        var context = Android.App.Application.Context;

        if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
        {
            var resolver = context.ContentResolver!;
            foreach (var file in files)
            {
                var values = new ContentValues();
                values.Put(MediaStore.IMediaColumns.DisplayName, System.IO.Path.GetFileName(file));
                values.Put(MediaStore.IMediaColumns.MimeType, MobileConverter.Mime(file));
                values.Put(MediaStore.IMediaColumns.RelativePath, Android.OS.Environment.DirectoryDownloads + "/EnvyConverter");
                values.Put(MediaStore.IMediaColumns.IsPending, 1);

                var uri = resolver.Insert(MediaStore.Downloads.ExternalContentUri!, values)
                    ?? throw new IOException("Não foi possível criar o arquivo em Downloads.");
                try
                {
                    using (var output = resolver.OpenOutputStream(uri) ?? throw new IOException("Downloads indisponível."))
                    using (var input = File.OpenRead(file))
                        input.CopyTo(output);

                    values.Clear();
                    values.Put(MediaStore.IMediaColumns.IsPending, 0);
                    resolver.Update(uri, values, null, null);
                }
                catch
                {
                    resolver.Delete(uri, null, null); // não deixa arquivo pela metade
                    throw;
                }
            }
            return "Downloads/EnvyConverter";
        }

        // Android 8 e 9: pasta do app no armazenamento (sem permissão extra).
        var dir = context.GetExternalFilesDir(Android.OS.Environment.DirectoryDownloads)!.AbsolutePath;
        foreach (var file in files)
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(file);
            var ext = System.IO.Path.GetExtension(file);
            var target = System.IO.Path.Combine(dir, name + ext);
            for (int i = 1; File.Exists(target); i++)
                target = System.IO.Path.Combine(dir, $"{name} ({i}){ext}");
            File.Copy(file, target);
        }
        return dir;
    }
}
