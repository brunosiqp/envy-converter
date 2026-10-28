using EnvyConverter.Conversion;

// Uso: dotnet run -- entrada.pdf saida.docx
if (args.Length != 2)
{
    Console.Error.WriteLine("Uso: EnvyConverter.Smoke <entrada.pdf> <saida.docx>");
    return 2;
}

var layout = PdfLayoutReader.Read(args[0]);
DocxWriter.WriteEditable(layout, args[1]);
OutputValidator.Validate(new[] { args[1] }, OutputFormat.Docx);

int paragraphs = layout.Pages.Sum(p => p.Blocks.OfType<TextParagraph>().Count());
int images = layout.Pages.Sum(p => p.Blocks.OfType<ImageBlock>().Count());
Console.WriteLine($"OK: {layout.Pages.Count} página(s), {paragraphs} parágrafo(s), {images} imagem(ns) → {args[1]}");
return 0;
