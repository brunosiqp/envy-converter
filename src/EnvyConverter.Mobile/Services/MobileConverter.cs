using System.Text;
using EnvyConverter.Conversion;

namespace EnvyConverter.Mobile.Services;

/// <summary>
/// Mesma lógica segura da versão Windows: gera numa pasta temporária, valida,
/// e só então entrega. Usa o motor interno (não depende de nenhum app instalado).
/// </summary>
public static class MobileConverter
{
    private const int ImageDpi = 170;

    public static readonly OutputFormat[] Formats =
        { OutputFormat.Docx, OutputFormat.DocxFiel, OutputFormat.Txt, OutputFormat.Png, OutputFormat.Jpg };

    public static async Task<IReadOnlyList<string>> ConvertAsync(string pdfPath, string displayName, OutputFormat format, CancellationToken ct)
    {
        var work = System.IO.Path.Combine(FileSystem.CacheDirectory, "conversao", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var produced = await Task.Run(() => Produce(pdfPath, work, format, ct), ct);
            OutputValidator.Validate(produced, format);

            // Copia para a pasta do app com nomes amigáveis (usada para Abrir/Compartilhar).
            var baseName = System.IO.Path.GetFileNameWithoutExtension(displayName) + (format == OutputFormat.DocxFiel ? " (fiel)" : "");
            var outDir = System.IO.Path.Combine(FileSystem.AppDataDirectory, "convertidos", Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(outDir);
            var finals = new List<string>();
            for (int i = 0; i < produced.Count; i++)
            {
                var name = OutputFormats.IsPerPage(format)
                    ? $"{baseName} - {System.IO.Path.GetFileName(produced[i])}"
                    : baseName + OutputFormats.Extension(format);
                var target = System.IO.Path.Combine(outDir, name);
                File.Copy(produced[i], target);
                finals.Add(target);
            }
            return finals;
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { }
        }
    }

    private static IReadOnlyList<string> Produce(string pdf, string work, OutputFormat format, CancellationToken ct)
    {
        switch (format)
        {
            case OutputFormat.Docx:
            {
                var layout = PdfLayoutReader.Read(pdf);
                if (!layout.Pages.Any(p => p.Blocks.OfType<TextParagraph>().Any()))
                    throw new InvalidOperationException("Este PDF não tem texto selecionável (parece digitalizado). Use \"DOCX fiel\".");
                var output = System.IO.Path.Combine(work, "saida.docx");
                DocxWriter.WriteEditable(layout, output);
                return new[] { output };
            }

            case OutputFormat.DocxFiel:
            {
                var pages = AndroidPdfPages.Render(pdf, ImageDpi, jpeg: false, ct);
                var output = System.IO.Path.Combine(work, "saida.docx");
                DocxWriter.WritePageImages(pages.Select(p => (p.Data, p.WidthPt, p.HeightPt)).ToList(), output);
                return new[] { output };
            }

            case OutputFormat.Txt:
            {
                var layout = PdfLayoutReader.Read(pdf);
                var text = new StringBuilder();
                foreach (var page in layout.Pages)
                {
                    foreach (var paragraph in page.Blocks.OfType<TextParagraph>())
                        text.AppendLine(paragraph.PlainText.TrimEnd());
                    text.AppendLine();
                }
                var output = System.IO.Path.Combine(work, "saida.txt");
                File.WriteAllText(output, text.ToString(), new UTF8Encoding(true));
                return new[] { output };
            }

            case OutputFormat.Png:
            case OutputFormat.Jpg:
            {
                var pages = AndroidPdfPages.Render(pdf, ImageDpi, format == OutputFormat.Jpg, ct);
                int digits = Math.Max(2, pages.Count.ToString().Length);
                var files = new List<string>();
                for (int i = 0; i < pages.Count; i++)
                {
                    var file = System.IO.Path.Combine(work, $"pagina-{(i + 1).ToString().PadLeft(digits, '0')}{OutputFormats.Extension(format)}");
                    File.WriteAllBytes(file, pages[i].Data);
                    files.Add(file);
                }
                return files;
            }

            default:
                throw new NotSupportedException("Formato não disponível no Android.");
        }
    }

    public static string Label(OutputFormat format) => format == OutputFormat.DocxFiel ? "DOCX fiel" : format.ToString().ToUpperInvariant();

    public static string Description(OutputFormat format) => format switch
    {
        OutputFormat.Docx => "Word editável. Texto, negrito, recuos e imagens reconstruídos.",
        OutputFormat.DocxFiel => "Word com cada página como imagem: idêntico ao PDF, mas o texto não é editável.",
        OutputFormat.Txt => "Somente o texto, em UTF-8.",
        OutputFormat.Png => "Uma imagem PNG por página.",
        OutputFormat.Jpg => "Uma imagem JPG por página.",
        _ => "",
    };

    public static string Mime(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".txt" => "text/plain",
        ".png" => "image/png",
        ".jpg" => "image/jpeg",
        _ => "application/octet-stream",
    };
}
