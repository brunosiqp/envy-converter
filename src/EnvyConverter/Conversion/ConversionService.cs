using System.IO;
using System.Security.Cryptography;

namespace EnvyConverter.Conversion;

public enum EngineChoice { Auto, Word, LibreOffice, BuiltIn }

public sealed record ConversionResult(string OutputPath, string EngineName, int FileCount);

/// <summary>
/// Orquestra a conversão com segurança:
/// 1. trabalha numa cópia do original (o arquivo original nunca é aberto para escrita);
/// 2. gera o resultado numa pasta temporária;
/// 3. valida o resultado; se falhar, tenta o próximo motor;
/// 4. só então move para o destino, sem sobrescrever nada que já exista.
/// </summary>
public sealed class ConversionService
{
    public WordEngine Word { get; } = new();
    public LibreOfficeEngine LibreOffice { get; } = new();
    public BuiltInEngine BuiltIn { get; } = new();

    public IEnumerable<IConversionEngine> EnginesFor(EngineChoice choice) => choice switch
    {
        EngineChoice.Word => new IConversionEngine[] { Word },
        EngineChoice.LibreOffice => new IConversionEngine[] { LibreOffice },
        EngineChoice.BuiltIn => new IConversionEngine[] { BuiltIn },
        // Automático: Word tem a melhor fidelidade; o interno não depende de nada;
        // o LibreOffice fica como última alternativa.
        _ => new IConversionEngine[] { Word, BuiltIn, LibreOffice },
    };

    public bool CanConvert(string inputPath, OutputFormat format, EngineChoice choice)
    {
        var ext = Path.GetExtension(inputPath).ToLowerInvariant();
        return EnginesFor(choice).Any(e => e.IsAvailable && e.Supports(ext, format));
    }

    public async Task<ConversionResult> ConvertAsync(
        string inputPath, OutputFormat format, EngineChoice choice, string? outputFolder, CancellationToken ct)
    {
        var ext = Path.GetExtension(inputPath).ToLowerInvariant();
        var engines = EnginesFor(choice).Where(e => e.Supports(ext, format)).ToList();
        if (engines.Count == 0)
            throw new NotSupportedException($"Não é possível converter {ext} para {Label(format)} com o motor escolhido.");

        var available = engines.Where(e => e.IsAvailable).ToList();
        if (available.Count == 0)
            throw new InvalidOperationException($"Instale o {string.Join(" ou o ", engines.Select(e => e.Name))} para esta conversão.");

        var originalHash = await HashAsync(inputPath, ct);
        var work = Path.Combine(Path.GetTempPath(), "EnvyConverter", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var inputCopy = Path.Combine(work, "entrada" + ext);
            File.Copy(inputPath, inputCopy);
            File.SetAttributes(inputCopy, FileAttributes.ReadOnly);

            var errors = new List<string>();
            foreach (var engine in available)
            {
                ct.ThrowIfCancellationRequested();
                var attemptDir = Path.Combine(work, engine.Name.Replace(' ', '_'));
                Directory.CreateDirectory(attemptDir);
                var outputBase = Path.Combine(attemptDir, "saida" + (OutputFormats.IsPerPage(format) ? "" : OutputFormats.Extension(format)));
                try
                {
                    var produced = await engine.ConvertAsync(inputCopy, outputBase, format, ct);
                    OutputValidator.Validate(produced, format);

                    if (await HashAsync(inputPath, ct) != originalHash)
                        throw new IOException("O arquivo original foi alterado por outro programa durante a conversão.");

                    var destination = Deliver(inputPath, outputBase, format, outputFolder);
                    return new ConversionResult(destination, engine.Name, produced.Count);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    errors.Add($"{engine.Name}: {ex.Message}");
                }
            }
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }
        finally
        {
            TryDelete(work);
        }
    }

    private static string Deliver(string inputPath, string produced, OutputFormat format, string? outputFolder)
    {
        var folder = string.IsNullOrWhiteSpace(outputFolder) ? Path.GetDirectoryName(Path.GetFullPath(inputPath))! : outputFolder;
        Directory.CreateDirectory(folder);
        var name = Path.GetFileNameWithoutExtension(inputPath) + (format == OutputFormat.DocxFiel ? " (fiel)" : "");

        if (OutputFormats.IsPerPage(format))
        {
            var target = Unique(Path.Combine(folder, $"{name} ({OutputFormats.Extension(format).TrimStart('.').ToUpperInvariant()})"), "");
            CopyDirectory(produced, target);
            return target;
        }

        var file = Unique(Path.Combine(folder, name), OutputFormats.Extension(format));
        // Copia para um nome temporário e renomeia: o destino nunca fica com um arquivo pela metade.
        var partial = file + ".parcial";
        File.Copy(produced, partial, overwrite: true);
        File.Move(partial, file);
        return file;
    }

    private static string Unique(string pathWithoutExt, string ext)
    {
        var candidate = pathWithoutExt + ext;
        for (int i = 1; File.Exists(candidate) || Directory.Exists(candidate); i++)
            candidate = $"{pathWithoutExt} ({i}){ext}";
        return candidate;
    }

    private static void CopyDirectory(string source, string target)
    {
        var partial = target + ".parcial";
        Directory.CreateDirectory(partial);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(partial, Path.GetFileName(file)));
        Directory.Move(partial, target);
    }

    private static async Task<string> HashAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
    }

    private static void TryDelete(string dir)
    {
        try
        {
            foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // Pasta temporária: o Windows limpa depois se não der agora.
        }
    }

    public static string Label(OutputFormat format) => format switch
    {
        OutputFormat.DocxFiel => "DOCX fiel",
        _ => format.ToString().ToUpperInvariant(),
    };
}
