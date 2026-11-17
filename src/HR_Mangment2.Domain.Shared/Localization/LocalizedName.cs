using System;
using System.Globalization;

namespace HR_Mangment2.Localization;

public static class LocalizedName
{
    /// <summary>True when the request language selects the English name. Arabic is the default.</summary>
    public static bool IsEnglish => string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "en", StringComparison.OrdinalIgnoreCase);

    /// <summary>The English name is optional (decision 2026-11-16): without one, English requests get the Arabic name.</summary>
    public static string Pick(string nameAr, string? nameEn) => IsEnglish && !string.IsNullOrWhiteSpace(nameEn) ? nameEn : nameAr;
}
