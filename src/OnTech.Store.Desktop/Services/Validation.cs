using System.ComponentModel.DataAnnotations;

namespace OnTech.Store.Desktop.Services;

/// <summary>Checks done before sending, with the same rules as the server's DataAnnotations, so the user gets a clear message at once.</summary>
public static class Validation
{
    private static readonly EmailAddressAttribute EmailRule = new();

    /// <summary>Empty is fine (the field is optional); otherwise the server's [EmailAddress] rule.</summary>
    public static bool IsEmail(string? text) => string.IsNullOrWhiteSpace(text) || EmailRule.IsValid(text.Trim());
}
