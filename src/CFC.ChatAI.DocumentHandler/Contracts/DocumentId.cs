using System.Text;
using Processor = CFC.ChatAI.DocumentProcessor.DocumentProcessor;

namespace CFC.ChatAI.DocumentHandler;

/// <summary>Validation for document IDs used in provider keys and API routes.</summary>
public static class DocumentId
{
    public static bool IsValid(string? value) => value is { Length: > 0 and <= 128 } &&
        value is not ("." or "..") &&
        value.EnumerateRunes().All(rune => rune.Value is '-' or '.' || Processor.IsDocumentIdWordCharacter(rune));
}
