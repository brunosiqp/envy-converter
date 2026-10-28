using Android.Graphics;
using Android.OS;
using AndroidPdf = Android.Graphics.Pdf;

namespace EnvyConverter.Mobile.Services;

/// <summary>Renderiza páginas de PDF com o leitor de PDF nativo do Android.</summary>
public static class AndroidPdfPages
{
    public sealed record Page(byte[] Data, double WidthPt, double HeightPt);

    public static IReadOnlyList<Page> Render(string pdfPath, int dpi, bool jpeg, CancellationToken ct)
    {
        var pages = new List<Page>();
        using var descriptor = ParcelFileDescriptor.Open(new Java.IO.File(pdfPath), ParcelFileMode.ReadOnly)
            ?? throw new IOException("Não foi possível abrir o PDF.");
        using var renderer = new AndroidPdf.PdfRenderer(descriptor);

        for (int i = 0; i < renderer.PageCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            using var page = renderer.OpenPage(i);
            // Tamanho da página vem em pontos (1/72 de polegada).
            int width = Math.Max(1, (int)Math.Round(page.Width * dpi / 72.0));
            int height = Math.Max(1, (int)Math.Round(page.Height * dpi / 72.0));

            using var bitmap = Bitmap.CreateBitmap(width, height, Bitmap.Config.Argb8888!)!;
            bitmap.EraseColor(Android.Graphics.Color.White);
            page.Render(bitmap, null, null, AndroidPdf.PdfRenderMode.ForDisplay);

            using var buffer = new MemoryStream();
            bitmap.Compress(jpeg ? Bitmap.CompressFormat.Jpeg! : Bitmap.CompressFormat.Png!, 92, buffer);
            pages.Add(new Page(buffer.ToArray(), page.Width, page.Height));
            bitmap.Recycle();
        }
        return pages;
    }
}
