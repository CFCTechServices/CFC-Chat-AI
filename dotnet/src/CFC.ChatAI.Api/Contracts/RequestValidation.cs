using System.Globalization;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CFC.ChatAI.Api.Contracts;

internal sealed record ValidatedBody(JsonElement? Body, IResult? Error);

internal static class RequestValidation
{
    internal static IResult Detail(int status, string detail) => Results.Json(new { detail }, statusCode: status);
    private static object Error(string type, object[] loc, string msg, object? input, object? ctx = null)
    {
        var error = new Dictionary<string, object?> { ["type"] = type, ["loc"] = loc, ["msg"] = msg, ["input"] = input };
        if (ctx is not null) error["ctx"] = ctx;
        return error;
    }
    private static IResult Errors(IEnumerable<object> errors) => Results.Json(new { detail = errors }, statusCode: 422);
    internal static async Task<ValidatedBody> ReadAsync(HttpRequest request, string[] fields, bool required, CancellationToken ct)
    {
        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var raw = await reader.ReadToEndAsync(ct);
        if (string.IsNullOrWhiteSpace(raw) || raw.Trim() == "null")
            return new(null, Errors([Error("missing", ["body"], "Field required", null)]));
        JsonElement body;
        try { using var document = JsonDocument.Parse(raw); body = document.RootElement.Clone(); }
        catch (JsonException ex)
        {
            var lines = raw.Split('\n');
            var line = (int)(ex.LineNumber ?? 0);
            var position = lines.Take(line).Sum(s => s.Length + 1) + (int)(ex.BytePositionInLine ?? 0);
            // Stable compatibility shape; do not expose System.Text.Json exceptions.
            var reason = raw.TrimEnd().EndsWith('{') ? "Expecting property name enclosed in double quotes" : "Invalid JSON";
            return new(null, Errors([Error("json_invalid", ["body", position], "JSON decode error", new { }, new { error = reason })]));
        }
        if (body.ValueKind != JsonValueKind.Object)
            return new(null, Errors([Error("model_attributes_type", ["body"],
                "Input should be a valid dictionary or object to extract fields from", body)]));
        var errors = new List<object>();
        foreach (var field in fields)
        {
            if (!body.TryGetProperty(field, out var value))
            {
                if (required) errors.Add(Error("missing", ["body", field], "Field required", body));
            }
            else if (value.ValueKind != JsonValueKind.String && (required || value.ValueKind != JsonValueKind.Null))
                errors.Add(Error("string_type", ["body", field], "Input should be a valid string", value));
        }
        return errors.Count > 0 ? new(null, Errors(errors)) : new(body, null);
    }
    internal static string? Optional(JsonElement body, string field) =>
        body.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    internal static (string? Email, IResult? Error) InvitationEmail(string input)
    {
        var email = input.Trim().Normalize(NormalizationForm.FormC);
        // EmailStr accepts display-name addresses and strips surrounding whitespace.
        if (email.Contains('<') && email.EndsWith('>'))
        {
            try { email = new MailAddress(email).Address; }
            catch (FormatException) { return Invalid("An email address must have an @-sign."); }
        }
        var at = email.LastIndexOf('@');
        if (at < 0) return Invalid("An email address must have an @-sign.");
        var local = email[..at];
        var domain = email[(at + 1)..];
        if (local.Length == 0) return Invalid("There must be something before the @-sign.");
        if (domain.Length == 0) return Invalid("There must be something after the @-sign.");
        if (local.StartsWith('.') || local.EndsWith('.') || local.Contains("..")
            || !Regex.IsMatch(local, @"^[\p{L}\p{M}\p{N}!#$%&'*+/=?^_`{|}~.\-]+$"))
            return Invalid("The email address contains invalid characters before the @-sign.");
        try
        {
            var idn = new IdnMapping { UseStd3AsciiRules = true };
            domain = idn.GetUnicode(idn.GetAscii(domain)).ToLowerInvariant();
            var ascii = idn.GetAscii(domain);
            if (!ascii.Contains('.')) return Invalid("The part after the @-sign is not valid. It should have a period.");
            if (ascii.Length > 253 || ascii.Split('.').Any(label => label.Length == 0 || label.Length > 63
                || label.StartsWith('-') || label.EndsWith('-') || !Regex.IsMatch(label, "^[a-zA-Z0-9-]+$"))
                || !Regex.IsMatch(ascii.Split('.')[^1], "[a-zA-Z]"))
                return Invalid("The part after the @-sign is not valid.");
            if (ascii is "localhost" || new[] { "localhost", "local", "invalid", "test", "example", "onion" }
                .Any(tld => ascii.EndsWith("." + tld, StringComparison.Ordinal)))
                return Invalid("The part after the @-sign is a special-use or reserved name that cannot be used with email.");
        }
        catch (ArgumentException) { return Invalid("The part after the @-sign is not valid."); }
        if (Encoding.UTF8.GetByteCount(local + "@" + domain) > 254)
            return Invalid("The email address is too long.");
        return (local + "@" + domain, null);

        (string?, IResult?) Invalid(string reason) => (null, Errors([Error("value_error", ["body", "email"],
            "value is not a valid email address: " + reason, input, new { reason })]));
    }
}
