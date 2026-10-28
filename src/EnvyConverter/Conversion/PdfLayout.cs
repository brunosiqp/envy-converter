using System.Text.RegularExpressions;
using UglyToad.PdfPig.Content;
using PigDocument = UglyToad.PdfPig.PdfDocument;

namespace EnvyConverter.Conversion;

public enum ParagraphAlign { Left, Center, Justify }

public sealed record StyledText(string Text, bool Bold, bool Italic, double SizePt, string Font);

public abstract class LayoutBlock
{
    public double TopPt { get; init; }
}

public sealed class TextParagraph : LayoutBlock
{
    public List<StyledText> Runs { get; } = new();
    public ParagraphAlign Align { get; set; }
    public double LeftIndentPt { get; set; }
    public double FirstLineIndentPt { get; set; }
    public double SpaceBeforePt { get; set; }

    public string PlainText => string.Concat(Runs.Select(r => r.Text));
}

public sealed class ImageBlock : LayoutBlock
{
    public required byte[] Data { get; init; }
    public required bool IsPng { get; init; }
    public double WidthPt { get; init; }
    public double HeightPt { get; init; }
    public double LeftIndentPt { get; init; }

    /// <summary>Imagem ao lado de texto: posicionada na página em vez de ocupar uma linha própria.</summary>
    public bool Floating { get; set; }
    public double PageLeftPt { get; init; }
    public double PageTopPt { get; init; }
}

public sealed class PageLayout
{
    public double WidthPt { get; init; }
    public double HeightPt { get; init; }
    public List<LayoutBlock> Blocks { get; } = new();
}

public sealed class DocumentLayout
{
    public List<PageLayout> Pages { get; } = new();
    public double MarginLeftPt { get; set; } = 72;
    public double MarginRightPt { get; set; } = 72;
    public double MarginTopPt { get; set; } = 56;
    public double MarginBottomPt { get; set; } = 56;
}

/// <summary>
/// Reconstrói parágrafos editáveis a partir do texto posicionado de um PDF:
/// agrupa palavras em linhas, linhas em parágrafos e preserva negrito, itálico,
/// tamanho, fonte, recuo, alinhamento e imagens.
/// </summary>
public static class PdfLayoutReader
{
    private static readonly Regex ListMarker = new(@"^(\d+(\.\d+)*[\.\)]|[a-zA-Z][\.\)]|[•▪●◦\-–])(\s|$)", RegexOptions.Compiled);

    private sealed class Line
    {
        public List<Word> Words { get; } = new();
        public double Baseline;
        public double Left => Words.Min(w => w.BoundingBox.Left);
        public double Right => Words.Max(w => w.BoundingBox.Right);
        public double Top => Words.Max(w => w.BoundingBox.Top);
        public double Bottom => Words.Min(w => w.BoundingBox.Bottom);
        public double Size;
        public string Text => string.Join(" ", Words.Select(w => w.Text));
    }

    public static DocumentLayout Read(string pdfPath)
    {
        var layout = new DocumentLayout();
        using var pdf = PigDocument.Open(pdfPath);

        var pages = new List<(Page Page, List<Line> Lines)>();
        foreach (var page in pdf.GetPages())
            pages.Add((page, BuildLines(page)));

        var allLines = pages.SelectMany(p => p.Lines).ToList();
        double textLeft = allLines.Count > 0 ? Percentile(allLines.Select(l => l.Left), 0.05) : 72;
        double textRight = allLines.Count > 0 ? Percentile(allLines.Select(l => l.Right), 0.95) : 523;
        double pageWidth = pages.Count > 0 ? pages[0].Page.Width : 595;
        layout.MarginLeftPt = Math.Clamp(textLeft, 18, 144);
        layout.MarginRightPt = Math.Clamp(pageWidth - textRight, 18, 144);

        foreach (var (page, lines) in pages)
        {
            var pageLayout = new PageLayout { WidthPt = page.Width, HeightPt = page.Height };
            pageLayout.Blocks.AddRange(BuildParagraphs(lines, textLeft, textRight));
            foreach (var image in ReadImages(page, textLeft))
            {
                // Imagem que divide a mesma faixa vertical com alguma linha de texto (ex.: logo
                // ao lado do cabeçalho) fica flutuante, para não empurrar o texto para baixo.
                double imageBottom = image.TopPt - image.HeightPt;
                image.Floating = lines.Any(l => l.Bottom < image.TopPt - 2 && l.Top > imageBottom + 2);
                pageLayout.Blocks.Add(image);
            }
            pageLayout.Blocks.Sort((a, b) => b.TopPt.CompareTo(a.TopPt)); // de cima para baixo
            layout.Pages.Add(pageLayout);
        }

        // Margens superior/inferior a partir do conteúdo real, e o primeiro bloco de cada
        // página começa na mesma altura em que estava no PDF.
        var flowTops = layout.Pages
            .Select(p => (Page: p, First: p.Blocks.FirstOrDefault(b => b is not ImageBlock { Floating: true })))
            .Where(x => x.First is not null)
            .ToList();
        if (flowTops.Count > 0)
            layout.MarginTopPt = Math.Clamp(flowTops.Min(x => x.Page.HeightPt - x.First!.TopPt), 18, 90);
        if (allLines.Count > 0)
            layout.MarginBottomPt = Math.Clamp(allLines.Min(l => l.Bottom) * 0.6, 18, 72);
        foreach (var (page, first) in flowTops)
        {
            double offset = page.HeightPt - first!.TopPt - layout.MarginTopPt;
            if (first is TextParagraph paragraph) paragraph.SpaceBeforePt = Math.Clamp(offset, 0, 200);
        }
        return layout;
    }

    private static List<Line> BuildLines(Page page)
    {
        var words = page.GetWords()
            .Where(w => !string.IsNullOrWhiteSpace(w.Text) && w.Letters.Count > 0)
            .OrderByDescending(w => w.Letters[0].StartBaseLine.Y)
            .ThenBy(w => w.BoundingBox.Left)
            .ToList();

        var lines = new List<Line>();
        foreach (var word in words)
        {
            double baseline = word.Letters[0].StartBaseLine.Y;
            double size = Math.Max(1, word.Letters.Average(l => l.PointSize));
            var line = lines.LastOrDefault(l => Math.Abs(l.Baseline - baseline) < Math.Max(2, 0.4 * Math.Max(size, l.Size)));
            if (line is null)
            {
                line = new Line { Baseline = baseline, Size = size };
                lines.Add(line);
            }
            line.Words.Add(word);
        }

        foreach (var line in lines)
        {
            line.Words.Sort((a, b) => a.BoundingBox.Left.CompareTo(b.BoundingBox.Left));
            line.Size = Median(line.Words.SelectMany(w => w.Letters).Select(l => l.PointSize));
        }
        lines.Sort((a, b) => b.Baseline.CompareTo(a.Baseline));
        return lines;
    }

    private static IEnumerable<TextParagraph> BuildParagraphs(List<Line> lines, double textLeft, double textRight)
    {
        var result = new List<TextParagraph>();
        double textCenter = (textLeft + textRight) / 2;
        double textWidth = Math.Max(1, textRight - textLeft);

        Line? prev = null;
        TextParagraph? current = null;
        var currentLines = new List<Line>();

        void Finish()
        {
            if (current is null || currentLines.Count == 0) return;
            var first = currentLines[0];
            var second = currentLines.Count > 1 ? currentLines[1] : first;
            bool centered = IsCentered(first, textLeft, textCenter);
            current.Align = centered ? ParagraphAlign.Center
                : currentLines.Count > 1 ? ParagraphAlign.Justify : ParagraphAlign.Left;
            if (!centered)
            {
                current.LeftIndentPt = Math.Max(0, second.Left - textLeft);
                current.FirstLineIndentPt = first.Left - second.Left;
                if (current.LeftIndentPt + current.FirstLineIndentPt < 0) current.FirstLineIndentPt = -current.LeftIndentPt;
            }
            result.Add(current);
        }

        foreach (var line in lines)
        {
            bool newParagraph = prev is null;
            double gap = prev is null ? 0 : prev.Baseline - line.Baseline;
            if (prev is not null)
            {
                double size = Math.Max(prev.Size, line.Size);
                newParagraph =
                    gap > 1.45 * size ||
                    gap < 0 ||
                    Math.Abs(prev.Size - line.Size) > 1.5 ||
                    ListMarker.IsMatch(line.Text) ||
                    prev.Right < textRight - Math.Max(36, 0.12 * textWidth) ||
                    IsCentered(prev, textLeft, textCenter) != IsCentered(line, textLeft, textCenter);
            }

            if (newParagraph)
            {
                Finish();
                current = new TextParagraph
                {
                    TopPt = line.Top,
                    SpaceBeforePt = prev is null ? 0 : Math.Clamp(gap - 1.2 * Math.Max(prev.Size, line.Size), 0, 36),
                };
                currentLines = new List<Line>();
            }
            else
            {
                // Junta com a linha anterior. Palavra hifenizada no fim da linha não ganha espaço.
                var last = current!.Runs[^1];
                if (!last.Text.EndsWith('-'))
                    current.Runs[^1] = last with { Text = last.Text + " " };
            }

            AppendLine(current!, line);
            currentLines.Add(line);
            prev = line;
        }
        Finish();
        return result;
    }

    private static bool IsCentered(Line line, double textLeft, double textCenter) =>
        line.Left > textLeft + 24 && Math.Abs((line.Left + line.Right) / 2 - textCenter) < 14;

    private static void AppendLine(TextParagraph block, Line line)
    {
        for (int i = 0; i < line.Words.Count; i++)
        {
            var word = line.Words[i];
            var fontName = word.Letters[0].FontName ?? "";
            var run = new StyledText(
                word.Text + (i < line.Words.Count - 1 ? " " : ""),
                IsBold(fontName),
                IsItalic(fontName),
                Math.Round(Median(word.Letters.Select(l => l.PointSize)) * 2) / 2,
                FontFamily(fontName));

            if (block.Runs.Count > 0)
            {
                var last = block.Runs[^1];
                if (last.Bold == run.Bold && last.Italic == run.Italic && last.SizePt == run.SizePt && last.Font == run.Font)
                {
                    block.Runs[^1] = last with { Text = last.Text + run.Text };
                    continue;
                }
            }
            block.Runs.Add(run);
        }
    }

    private static IEnumerable<ImageBlock> ReadImages(Page page, double textLeft)
    {
        var images = new List<ImageBlock>();
        IEnumerable<IPdfImage> pdfImages;
        try { pdfImages = page.GetImages().ToList(); }
        catch { return images; }

        foreach (var image in pdfImages)
        {
            try
            {
                byte[]? data = null;
                bool isPng = false;
                var raw = image.RawBytes.ToArray();
                if (raw.Length > 3 && raw[0] == 0xFF && raw[1] == 0xD8 && raw[2] == 0xFF)
                {
                    data = raw;
                }
                else if (image.TryGetPng(out var png))
                {
                    data = png;
                    isPng = true;
                }
                if (data is null || image.Bounds.Width < 4 || image.Bounds.Height < 4) continue;

                images.Add(new ImageBlock
                {
                    Data = data,
                    IsPng = isPng,
                    TopPt = image.Bounds.Top,
                    WidthPt = image.Bounds.Width,
                    HeightPt = image.Bounds.Height,
                    LeftIndentPt = Math.Max(0, image.Bounds.Left - textLeft),
                    PageLeftPt = image.Bounds.Left,
                    PageTopPt = page.Height - image.Bounds.Top,
                });
            }
            catch
            {
                // Imagem num formato que não conseguimos extrair: segue sem ela.
            }
        }
        return images;
    }

    private static bool IsBold(string font) =>
        font.Contains("Bold", StringComparison.OrdinalIgnoreCase) ||
        font.Contains("Black", StringComparison.OrdinalIgnoreCase) ||
        font.Contains("Heavy", StringComparison.OrdinalIgnoreCase) ||
        font.Contains("Semibold", StringComparison.OrdinalIgnoreCase);

    private static bool IsItalic(string font) =>
        font.Contains("Italic", StringComparison.OrdinalIgnoreCase) ||
        font.Contains("Oblique", StringComparison.OrdinalIgnoreCase);

    /// <summary>"BCDJEE+TimesNewRomanPS-BoldMT" → "Times New Roman".</summary>
    public static string FontFamily(string pdfFontName)
    {
        var name = pdfFontName;
        int plus = name.IndexOf('+');
        if (plus is > 0 and <= 7) name = name[(plus + 1)..];
        name = name.Split('-', ',')[0];
        name = Regex.Replace(name, "(PSMT|PS|MT)$", "");
        name = Regex.Replace(name, "(Bold|Italic|Oblique|Regular)+$", "", RegexOptions.IgnoreCase);
        name = Regex.Replace(name, "(?<=[a-z])(?=[A-Z])", " ").Trim();
        return string.IsNullOrEmpty(name) ? "Calibri" : name;
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted.Count == 0 ? 11 : sorted[sorted.Count / 2];
    }

    private static double Percentile(IEnumerable<double> values, double p)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted[(int)Math.Clamp(Math.Round(p * (sorted.Count - 1)), 0, sorted.Count - 1)];
    }
}
