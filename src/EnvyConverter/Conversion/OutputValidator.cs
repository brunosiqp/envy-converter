using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using DocumentFormat.OpenXml.Packaging;

namespace EnvyConverter.Conversion;

/// <summary>
/// Abre cada arquivo gerado e confere a estrutura antes de entregá-lo.
/// Se algo estiver errado, o arquivo nunca chega à pasta de destino.
/// </summary>
public static class OutputValidator
{
    public static void Validate(IReadOnlyList<string> files, OutputFormat format)
    {
        if (files.Count == 0)
            throw new InvalidDataException("Nenhum arquivo foi gerado.");

        foreach (var file in files)
        {
            var info = new FileInfo(file);
            if (!info.Exists || info.Length == 0)
                throw new InvalidDataException($"Arquivo gerado vazio: {info.Name}");

            switch (Path.GetExtension(file).ToLowerInvariant())
            {
                case ".docx":
                    using (var doc = WordprocessingDocument.Open(file, false))
                    {
                        // Acessar Document força a leitura e o parse do XML principal.
                        if (doc.MainDocumentPart?.Document?.Body is null)
                            throw new InvalidDataException("DOCX sem corpo de documento.");
                    }
                    break;

                case ".odt":
                    using (var zip = ZipFile.OpenRead(file))
                    {
                        var content = zip.GetEntry("content.xml") ?? throw new InvalidDataException("ODT sem content.xml.");
                        using var stream = content.Open();
                        using var reader = XmlReader.Create(stream);
                        while (reader.Read()) { }
                    }
                    break;

                case ".doc":
                    RequireHeader(file, new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }, "DOC");
                    break;

                case ".rtf":
                    RequireHeader(file, Encoding.ASCII.GetBytes(@"{\rtf"), "RTF");
                    break;

                case ".pdf":
                    RequireHeader(file, Encoding.ASCII.GetBytes("%PDF-"), "PDF");
                    using (var pdf = UglyToad.PdfPig.PdfDocument.Open(file))
                    {
                        if (pdf.NumberOfPages < 1) throw new InvalidDataException("PDF sem páginas.");
                    }
                    break;

                case ".png":
                    RequireHeader(file, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "PNG");
                    break;

                case ".jpg":
                    RequireHeader(file, new byte[] { 0xFF, 0xD8, 0xFF }, "JPG");
                    break;

                case ".html":
                    if (!File.ReadAllText(file, Encoding.Latin1).Contains('<'))
                        throw new InvalidDataException("O HTML gerado está vazio.");
                    break;

                case ".txt":
                    // Leitura estrita: falha se o texto não for UTF-8/UTF-16 válido.
                    using (var reader = new StreamReader(file, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true))
                        reader.ReadToEnd();
                    break;
            }
        }
    }

    private static void RequireHeader(string file, byte[] header, string kind)
    {
        var buffer = new byte[header.Length];
        using var stream = File.OpenRead(file);
        if (stream.Read(buffer, 0, buffer.Length) != buffer.Length || !buffer.AsSpan().SequenceEqual(header))
            throw new InvalidDataException($"O arquivo gerado não é um {kind} válido.");
    }
}
