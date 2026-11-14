namespace SunsetMP.Services;

/// <summary>Поиск без учёта регистра, ё = е. Запрос из нескольких слов: должны встретиться все.</summary>
public static class SearchHelper
{
    static string Normalize(string s) => s.ToLowerInvariant().Replace('ё', 'е');

    public static string[] Terms(string? query) =>
        string.IsNullOrWhiteSpace(query)
            ? Array.Empty<string>()
            : Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static bool Matches(string[] terms, string? text)
    {
        if (terms.Length == 0) return true;
        if (string.IsNullOrEmpty(text)) return false;
        var haystack = Normalize(text);
        foreach (var t in terms)
            if (!haystack.Contains(t)) return false;
        return true;
    }
}
