using Microsoft.AspNetCore.Components.Forms;

namespace CFC.ChatAI.Web.Services;

public sealed class ProfileAvatar
{
    public const int MaxBytes = 512 * 1024;
    public const string AcceptedTypes = "image/png,image/jpeg,image/webp,image/gif";
    private static readonly string[] Types = AcceptedTypes.Split(',');
    private ProfileAvatar(string dataUrl) => DataUrl = dataUrl;
    public string DataUrl { get; }

    public static async Task<ProfileAvatar> ReadAsync(IBrowserFile file, CancellationToken cancellationToken)
    {
        var type = file.ContentType.ToLowerInvariant();
        if (!Types.Contains(type))
            throw new InvalidDataException("Choose a PNG, JPEG, WebP or GIF image.");
        if (file.Size is <= 0 or > MaxBytes)
            throw new InvalidDataException("Choose a nonempty image of 512 KiB or smaller.");

        await using var stream = file.OpenReadStream(MaxBytes, cancellationToken);
        var bytes = new byte[(int)file.Size];
        await stream.ReadExactlyAsync(bytes, cancellationToken);
        if (!MatchesType(bytes, type) || await stream.ReadAsync(new byte[1], cancellationToken) != 0)
            throw new InvalidDataException("The image contents do not match the selected file type or size.");
        return new($"data:{type};base64,{Convert.ToBase64String(bytes)}");
    }

    private static bool MatchesType(ReadOnlySpan<byte> bytes, string type) => type switch
    {
        "image/png" => bytes.StartsWith((ReadOnlySpan<byte>)[137, 80, 78, 71, 13, 10, 26, 10]),
        "image/jpeg" => bytes.StartsWith((ReadOnlySpan<byte>)[255, 216, 255]),
        "image/gif" => bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8),
        "image/webp" => bytes.Length >= 12 && bytes.StartsWith("RIFF"u8) && bytes[8..].StartsWith("WEBP"u8),
        _ => false
    };

    public static string? DisplayUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (Types.Any(type => value.StartsWith($"data:{type};base64,", StringComparison.OrdinalIgnoreCase)))
            return value;
        return Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme is "https" or "http" &&
            url.UserInfo.Length == 0 ? url.AbsoluteUri : null;
    }
}
