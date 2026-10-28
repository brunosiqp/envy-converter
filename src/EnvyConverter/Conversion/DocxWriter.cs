using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace EnvyConverter.Conversion;

/// <summary>Grava documentos .docx com o Open XML SDK oficial da Microsoft.</summary>
public static class DocxWriter
{
    private const long EmuPerPoint = 12700;
    private const int TwipsPerPoint = 20;

    /// <summary>Documento editável a partir do layout reconstruído do PDF.</summary>
    public static void WriteEditable(DocumentLayout layout, string path)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        var body = new Body();
        main.Document = new Document(body);
        uint imageId = 1;

        for (int p = 0; p < layout.Pages.Count; p++)
        {
            var page = layout.Pages[p];
            var pageParagraphs = new List<Paragraph>();
            var floating = new List<ImageBlock>();
            foreach (var block in page.Blocks)
            {
                switch (block)
                {
                    case TextParagraph text:
                        pageParagraphs.Add(TextToParagraph(text));
                        break;
                    case ImageBlock { Floating: true } image:
                        floating.Add(image);
                        break;
                    case ImageBlock image:
                        pageParagraphs.Add(ImageParagraph(main, image.Data, image.IsPng, image.WidthPt, image.HeightPt, imageId++, image.LeftIndentPt));
                        break;
                }
            }

            if (pageParagraphs.Count == 0)
                pageParagraphs.Add(new Paragraph(new ParagraphProperties()));
            if (p > 0)
            {
                var props = pageParagraphs[0].ParagraphProperties ??= new ParagraphProperties();
                props.PrependChild(new PageBreakBefore());
            }
            // Imagens flutuantes são ancoradas na página, no primeiro parágrafo dela.
            foreach (var image in floating)
                pageParagraphs[0].InsertAfter(FloatingImageRun(main, image, imageId++), pageParagraphs[0].ParagraphProperties);

            body.Append(pageParagraphs);
        }

        var first = layout.Pages.FirstOrDefault();
        body.Append(new SectionProperties(
            new PageSize { Width = Twips(first?.WidthPt ?? 595), Height = Twips(first?.HeightPt ?? 842) },
            new PageMargin
            {
                Top = (int)Twips(layout.MarginTopPt), Bottom = (int)Twips(layout.MarginBottomPt),
                Left = Twips(layout.MarginLeftPt), Right = Twips(layout.MarginRightPt),
                Header = 567, Footer = 567, Gutter = 0,
            }));
        main.Document.Save();
    }

    /// <summary>Documento com cada página como imagem: aparência idêntica ao PDF.</summary>
    public static void WritePageImages(IReadOnlyList<(byte[] Png, double WidthPt, double HeightPt)> pages, string path)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        var body = new Body();
        main.Document = new Document(body);

        for (int i = 0; i < pages.Count; i++)
        {
            var (png, w, h) = pages[i];
            // 1pt de folga evita que o Word crie uma página em branco extra.
            var paragraph = ImageParagraph(main, png, true, w - 1, h - 1, (uint)(i + 1), 0);
            var section = new SectionProperties(
                new PageSize { Width = Twips(w), Height = Twips(h) },
                new PageMargin { Top = 0, Bottom = 0, Left = 0, Right = 0, Header = 0, Footer = 0, Gutter = 0 });

            if (i < pages.Count - 1)
                paragraph.ParagraphProperties!.Append(section);
            body.Append(paragraph);
            if (i == pages.Count - 1)
                body.Append(section);
        }
        main.Document.Save();
    }

    private static Paragraph TextToParagraph(TextParagraph block)
    {
        // A ordem dos elementos em <w:pPr> é exigida pelo esquema: spacing, ind, jc.
        var props = new ParagraphProperties(
            new SpacingBetweenLines { Before = Twips(block.SpaceBeforePt).ToString(), After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto });

        if (block.LeftIndentPt > 0.5 || Math.Abs(block.FirstLineIndentPt) > 0.5)
        {
            var indent = new Indentation { Left = Twips(block.LeftIndentPt).ToString() };
            if (block.FirstLineIndentPt > 0.5) indent.FirstLine = Twips(block.FirstLineIndentPt).ToString();
            else if (block.FirstLineIndentPt < -0.5) indent.Hanging = Twips(-block.FirstLineIndentPt).ToString();
            props.Append(indent);
        }

        props.Append(new Justification
        {
            Val = block.Align switch
            {
                ParagraphAlign.Center => JustificationValues.Center,
                ParagraphAlign.Justify => JustificationValues.Both,
                _ => JustificationValues.Left,
            },
        });

        var paragraph = new Paragraph(props);
        foreach (var run in block.Runs)
        {
            var runProps = new RunProperties(
                new RunFonts { Ascii = run.Font, HighAnsi = run.Font, ComplexScript = run.Font, EastAsia = run.Font });
            if (run.Bold) runProps.Append(new Bold());
            if (run.Italic) runProps.Append(new Italic());
            runProps.Append(new FontSize { Val = ((int)Math.Round(run.SizePt * 2)).ToString() });
            paragraph.Append(new Run(runProps, new Text(run.Text) { Space = SpaceProcessingModeValues.Preserve }));
        }
        return paragraph;
    }

    private static Paragraph ImageParagraph(MainDocumentPart main, byte[] data, bool isPng, double widthPt, double heightPt, uint id, double leftIndentPt)
    {
        var part = main.AddImagePart(isPng ? ImagePartType.Png : ImagePartType.Jpeg);
        using (var stream = new MemoryStream(data))
            part.FeedData(stream);
        var relId = main.GetIdOfPart(part);

        long cx = (long)(widthPt * EmuPerPoint);
        long cy = (long)(heightPt * EmuPerPoint);
        var name = $"Imagem {id}";

        var inline = new DW.Inline(
            new DW.Extent { Cx = cx, Cy = cy },
            new DW.EffectExtent { LeftEdge = 0, TopEdge = 0, RightEdge = 0, BottomEdge = 0 },
            new DW.DocProperties { Id = id, Name = name },
            new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
            Graphic(relId, name, cx, cy))
        {
            DistanceFromTop = 0, DistanceFromBottom = 0, DistanceFromLeft = 0, DistanceFromRight = 0,
        };

        var props = new ParagraphProperties(new SpacingBetweenLines { Before = "0", After = "0" });
        if (leftIndentPt > 0.5) props.Append(new Indentation { Left = Twips(leftIndentPt).ToString() });
        return new Paragraph(props, new Run(new Drawing(inline)));
    }

    private static Run FloatingImageRun(MainDocumentPart main, ImageBlock image, uint id)
    {
        var part = main.AddImagePart(image.IsPng ? ImagePartType.Png : ImagePartType.Jpeg);
        using (var stream = new MemoryStream(image.Data))
            part.FeedData(stream);
        var relId = main.GetIdOfPart(part);

        long cx = (long)(image.WidthPt * EmuPerPoint);
        long cy = (long)(image.HeightPt * EmuPerPoint);
        var name = $"Imagem {id}";

        var anchor = new DW.Anchor(
            new DW.SimplePosition { X = 0, Y = 0 },
            new DW.HorizontalPosition(new DW.PositionOffset(((long)(image.PageLeftPt * EmuPerPoint)).ToString()))
                { RelativeFrom = DW.HorizontalRelativePositionValues.Page },
            new DW.VerticalPosition(new DW.PositionOffset(((long)(image.PageTopPt * EmuPerPoint)).ToString()))
                { RelativeFrom = DW.VerticalRelativePositionValues.Page },
            new DW.Extent { Cx = cx, Cy = cy },
            new DW.EffectExtent { LeftEdge = 0, TopEdge = 0, RightEdge = 0, BottomEdge = 0 },
            new DW.WrapNone(),
            new DW.DocProperties { Id = id, Name = name },
            new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
            Graphic(relId, name, cx, cy))
        {
            DistanceFromTop = 0, DistanceFromBottom = 0, DistanceFromLeft = 0, DistanceFromRight = 0,
            SimplePos = false, RelativeHeight = 1, BehindDoc = false, Locked = false,
            LayoutInCell = true, AllowOverlap = true,
        };
        return new Run(new Drawing(anchor));
    }

    private static A.Graphic Graphic(string relId, string name, long cx, long cy) =>
            new A.Graphic(new A.GraphicData(
                new PIC.Picture(
                    new PIC.NonVisualPictureProperties(
                        new PIC.NonVisualDrawingProperties { Id = 0, Name = name },
                        new PIC.NonVisualPictureDrawingProperties()),
                    new PIC.BlipFill(
                        new A.Blip { Embed = relId },
                        new A.Stretch(new A.FillRectangle())),
                    new PIC.ShapeProperties(
                        new A.Transform2D(new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = cx, Cy = cy }),
                        new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
            { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" });

    private static uint Twips(double points) => (uint)Math.Max(0, Math.Round(points * TwipsPerPoint));
}
