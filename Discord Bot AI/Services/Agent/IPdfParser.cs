namespace Discord_Bot_AI.Services.Agent;

/// <summary>
/// Extracts text content from PDF documents for agent task preprocessing.
/// </summary>
public interface IPdfParser
{
    Task<string> ExtractTextAsync(Stream pdfStream, CancellationToken cancellationToken = default);
}
