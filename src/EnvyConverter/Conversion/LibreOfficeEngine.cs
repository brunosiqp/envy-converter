using System.Diagnostics;
using System.IO;

namespace EnvyConverter.Conversion;

/// <summary>Usa o LibreOffice instalado em modo invisível (gratuito).</summary>
public sealed class LibreOfficeEngine : IConversionEngine
{
    private static readonly Lazy<string?> Soffice = new(FindSoffice);

    private static readonly string[] Inputs = { ".pdf", ".docx", ".doc", ".rtf", ".odt", ".txt", ".html", ".htm" };

    public string Name => "LibreOffice";

    public bool IsAvailable => Soffice.Value is not null;

    public bool Supports(string inputExtension, OutputFormat format) =>
        Inputs.Contains(inputExtension) && Filter(format) is not null &&
        !(inputExtension == ".pdf" && format == OutputFormat.Pdf);

    private static string? Filter(OutputFormat format) => format switch
    {
        OutputFormat.Docx => "docx:MS Word 2007 XML",
        OutputFormat.Doc => "doc:MS Word 97",
        OutputFormat.Rtf => "rtf:Rich Text Format",
        OutputFormat.Odt => "odt:writer8",
        OutputFormat.Txt => "txt:Text (encoded):UTF8",
        OutputFormat.Html => "html:HTML (StarWriter)",
        OutputFormat.Pdf => "pdf:writer_pdf_Export",
        _ => null,
    };

    public async Task<IReadOnlyList<string>> ConvertAsync(string input, string outputBase, OutputFormat format, CancellationToken ct)
    {
        var soffice = Soffice.Value ?? throw new InvalidOperationException("LibreOffice não está instalado.");
        var workDir = Path.GetDirectoryName(outputBase)!;
        var outDir = Path.Combine(workDir, "lo-out");
        var profile = Path.Combine(workDir, "lo-profile");
        Directory.CreateDirectory(outDir);

        var psi = new ProcessStartInfo(soffice)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        // Perfil isolado: não conflita com um LibreOffice que esteja aberto.
        psi.ArgumentList.Add("-env:UserInstallation=" + new Uri(profile + Path.DirectorySeparatorChar).AbsoluteUri);
        psi.ArgumentList.Add("--headless");
        psi.ArgumentList.Add("--norestore");
        psi.ArgumentList.Add("--nolockcheck");
        if (Path.GetExtension(input).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            psi.ArgumentList.Add("--infilter=writer_pdf_import");
        psi.ArgumentList.Add("--convert-to");
        psi.ArgumentList.Add(Filter(format)!);
        psi.ArgumentList.Add("--outdir");
        psi.ArgumentList.Add(outDir);
        psi.ArgumentList.Add(input);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Não foi possível iniciar o LibreOffice.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("O LibreOffice demorou demais para responder.");
        }

        var produced = Directory.GetFiles(outDir).FirstOrDefault()
            ?? throw new InvalidOperationException("O LibreOffice não gerou nenhum arquivo. " + await process.StandardError.ReadToEndAsync());
        File.Move(produced, outputBase, overwrite: true);
        return new[] { outputBase };
    }

    private static string? FindSoffice()
    {
        var candidates = new List<string>();
        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                 })
        {
            if (string.IsNullOrEmpty(root)) continue;
            candidates.Add(Path.Combine(root, "LibreOffice", "program", "soffice.exe"));
        }
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (!string.IsNullOrWhiteSpace(dir)) candidates.Add(Path.Combine(dir.Trim(), "soffice.exe"));
        }
        return candidates.FirstOrDefault(File.Exists);
    }
}
