namespace EnvyConverter.Conversion;

public interface IConversionEngine
{
    string Name { get; }

    bool IsAvailable { get; }

    bool Supports(string inputExtension, OutputFormat format);

    /// <summary>
    /// Converte <paramref name="input"/> e grava o resultado em <paramref name="outputBase"/>
    /// (um arquivo, ou uma pasta para formatos por página). Retorna os arquivos gerados.
    /// </summary>
    Task<IReadOnlyList<string>> ConvertAsync(string input, string outputBase, OutputFormat format, CancellationToken ct);
}
