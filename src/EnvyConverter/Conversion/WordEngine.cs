using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace EnvyConverter.Conversion;

/// <summary>
/// Usa o Microsoft Word instalado (automação COM). É o motor de maior fidelidade:
/// o Word reconstrói o PDF como documento editável ("PDF Reflow").
/// </summary>
public sealed class WordEngine : IConversionEngine
{
    private static readonly Lazy<Type?> WordType = new(() => Type.GetTypeFromProgID("Word.Application"));

    private static readonly string[] Inputs = { ".pdf", ".docx", ".doc", ".rtf", ".odt", ".txt", ".html", ".htm" };

    public string Name => "Microsoft Word";

    public bool IsAvailable => WordType.Value is not null;

    public bool Supports(string inputExtension, OutputFormat format) =>
        Inputs.Contains(inputExtension) && SaveFormat(format) is not null &&
        !(inputExtension == ".pdf" && format == OutputFormat.Pdf);

    // Valores de WdSaveFormat
    private static int? SaveFormat(OutputFormat format) => format switch
    {
        OutputFormat.Docx => 16,
        OutputFormat.Doc => 0,
        OutputFormat.Rtf => 6,
        OutputFormat.Odt => 23,
        OutputFormat.Txt => 7,
        OutputFormat.Html => 10,
        OutputFormat.Pdf => 17,
        _ => null,
    };

    public Task<IReadOnlyList<string>> ConvertAsync(string input, string outputBase, OutputFormat format, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var type = WordType.Value ?? throw new InvalidOperationException("Microsoft Word não está instalado.");
        var saveFormat = SaveFormat(format)!.Value;
        DisablePdfConversionPrompt();

        return StaThread.Run<IReadOnlyList<string>>(() =>
        {
            dynamic? word = null;
            dynamic? doc = null;
            try
            {
                word = Activator.CreateInstance(type)!;
                word.Visible = false;
                word.DisplayAlerts = 0; // wdAlertsNone
                doc = word.Documents.Open(
                    FileName: input,
                    ConfirmConversions: false,
                    ReadOnly: true,
                    AddToRecentFiles: false,
                    Visible: false);
                if (saveFormat == 7) // texto: força UTF-8
                    doc.SaveAs2(FileName: outputBase, FileFormat: saveFormat, Encoding: 65001);
                else
                    doc.SaveAs2(FileName: outputBase, FileFormat: saveFormat);
                return new[] { outputBase };
            }
            finally
            {
                if (doc is not null)
                {
                    try { doc.Close(SaveChanges: 0); } catch { }
                    Marshal.FinalReleaseComObject(doc);
                }
                if (word is not null)
                {
                    try { word.Quit(SaveChanges: 0); } catch { }
                    Marshal.FinalReleaseComObject(word);
                }
            }
        });
    }

    /// <summary>
    /// O Word mostra um aviso "o Word vai converter o PDF..." que travaria a automação.
    /// Esta opção do próprio Word desliga o aviso para o usuário atual.
    /// </summary>
    private static void DisablePdfConversionPrompt()
    {
        foreach (var version in new[] { "16.0", "15.0" })
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey($@"Software\Microsoft\Office\{version}\Word\Options");
                key?.SetValue("DisableConvertPdfWarning", 1, RegistryValueKind.DWord);
            }
            catch { }
        }
    }
}
