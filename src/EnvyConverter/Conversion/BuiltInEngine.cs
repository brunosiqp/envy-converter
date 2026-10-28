using System.IO;
using System.Text;

namespace EnvyConverter.Conversion;

/// <summary>
/// Motor próprio, sem depender de nenhum programa instalado.
/// PDF → DOCX editável, DOCX fiel (páginas como imagem), TXT, PNG e JPG.
/// </summary>
public sealed class BuiltInEngine : IConversionEngine
{
    private const int ImageDpi = 200;

    public string Name => "Interno";

    public bool IsAvailable => true;

    public bool Supports(string inputExtension, OutputFormat format) =>
        inputExtension == ".pdf" &&
        format is OutputFormat.Docx or OutputFormat.DocxFiel or OutputFormat.Txt or OutputFormat.Png or OutputFormat.Jpg;

    public async Task<IReadOnlyList<string>> ConvertAsync(string input, string outputBase, OutputFormat format, CancellationToken ct)
    {
        switch (format)
        {
            case OutputFormat.Docx:
            {
                var layout = await Task.Run(() => PdfLayoutReader.Read(input), ct);
                if (!layout.Pages.Any(p => p.Blocks.OfType<TextParagraph>().Any()))
                    throw new InvalidOperationException(
                        "O PDF não tem texto selecionável (parece digitalizado). Use \"DOCX fiel\" ou o Microsoft Word.");
                await Task.Run(() => DocxWriter.WriteEditable(layout, outputBase), ct);
                return new[] { outputBase };
            }

            case OutputFormat.DocxFiel:
            {
                var pages = await PdfRenderer.RenderAsync(input, ImageDpi, jpeg: false, ct);
                await Task.Run(() => DocxWriter.WritePageImages(
                    pages.Select(p => (p.Data, p.WidthPt, p.HeightPt)).ToList(), outputBase), ct);
                return new[] { outputBase };
            }

            case OutputFormat.Txt:
            {
                var layout = await Task.Run(() => PdfLayoutReader.Read(input), ct);
                var text = new StringBuilder();
                foreach (var page in layout.Pages)
                {
                    foreach (var paragraph in page.Blocks.OfType<TextParagraph>())
                        text.AppendLine(paragraph.PlainText.TrimEnd());
                    text.AppendLine();
                }
                await File.WriteAllTextAsync(outputBase, text.ToString(), new UTF8Encoding(true), ct);
                return new[] { outputBase };
            }

            case OutputFormat.Png:
            case OutputFormat.Jpg:
            {
                bool jpeg = format == OutputFormat.Jpg;
                var pages = await PdfRenderer.RenderAsync(input, ImageDpi, jpeg, ct);
                Directory.CreateDirectory(outputBase);
                var files = new List<string>();
                int digits = Math.Max(2, pages.Count.ToString().Length);
                for (int i = 0; i < pages.Count; i++)
                {
                    var file = Path.Combine(outputBase, $"pagina-{(i + 1).ToString().PadLeft(digits, '0')}{OutputFormats.Extension(format)}");
                    await File.WriteAllBytesAsync(file, pages[i].Data, ct);
                    files.Add(file);
                }
                return files;
            }

            default:
                throw new NotSupportedException($"O motor interno não gera {format}.");
        }
    }
}
