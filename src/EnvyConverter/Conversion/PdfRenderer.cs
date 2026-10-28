using System.IO;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace EnvyConverter.Conversion;

/// <summary>Renderiza páginas de PDF com o leitor de PDF nativo do Windows 10/11.</summary>
public static class PdfRenderer
{
    public sealed record RenderedPage(byte[] Data, double WidthPt, double HeightPt);

    public static async Task<IReadOnlyList<RenderedPage>> RenderAsync(string pdfPath, int dpi, bool jpeg, CancellationToken ct)
    {
        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(pdfPath));
        var pdf = await PdfDocument.LoadFromFileAsync(file);
        var pages = new List<RenderedPage>();

        for (uint i = 0; i < pdf.PageCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            using var page = pdf.GetPage(i);
            // page.Size está em DIPs (1/96 de polegada).
            var options = new PdfPageRenderOptions
            {
                DestinationWidth = (uint)Math.Round(page.Size.Width * dpi / 96.0),
                DestinationHeight = (uint)Math.Round(page.Size.Height * dpi / 96.0),
                BitmapEncoderId = jpeg ? BitmapEncoder.JpegEncoderId : BitmapEncoder.PngEncoderId,
            };

            using var stream = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(stream, options);
            stream.Seek(0);
            using var managed = stream.AsStreamForRead();
            using var buffer = new MemoryStream();
            await managed.CopyToAsync(buffer, ct);

            pages.Add(new RenderedPage(buffer.ToArray(), page.Size.Width * 72 / 96.0, page.Size.Height * 72 / 96.0));
        }
        return pages;
    }
}
