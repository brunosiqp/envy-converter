namespace EnvyConverter.Conversion;

public enum OutputFormat
{
    Docx,
    DocxFiel,
    Doc,
    Rtf,
    Odt,
    Txt,
    Html,
    Pdf,
    Png,
    Jpg,
}

public static class OutputFormats
{
    public static string Extension(OutputFormat format) => format switch
    {
        OutputFormat.Docx or OutputFormat.DocxFiel => ".docx",
        OutputFormat.Doc => ".doc",
        OutputFormat.Rtf => ".rtf",
        OutputFormat.Odt => ".odt",
        OutputFormat.Txt => ".txt",
        OutputFormat.Html => ".html",
        OutputFormat.Pdf => ".pdf",
        OutputFormat.Png => ".png",
        OutputFormat.Jpg => ".jpg",
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    /// <summary>Formatos de imagem geram uma pasta com uma imagem por página.</summary>
    public static bool IsPerPage(OutputFormat format) => format is OutputFormat.Png or OutputFormat.Jpg;

    public static readonly string[] SupportedInputs =
        { ".pdf", ".docx", ".doc", ".rtf", ".odt", ".txt", ".html", ".htm" };
}
