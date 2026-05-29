using System.Globalization;
using System.Text.RegularExpressions;

namespace DataMaker.Expressions.Functions;

/// <summary>
/// Expression function library for Data Maker calculated fields and query
/// expressions. Seeded from FileOrganizer's Fn library (project_expression_gaps),
/// adapted for form-field domain (path-specific helpers removed; keep array,
/// string, date, branch, convert, aggregate tiers).
///
/// Tier 1 — Primitives
///   Split, Concat, At, First, Last, Take, Skip, Slice, IndexOf, Where, Except,
///   If, Coalesce, Switch, ToInt, ToLong, IntToStr
/// Tier 2 — Transforms
///   Insert, Remove, Filter, Reverse, Sort, Distinct, Map, Union, Intersect, SetExcept,
///   Lower, Upper, TitleCase, Trim, Pad, PadRight, Left, Right, Match, Capture, Replace,
///   Len, LenStr, Any, All, CountOf, Min, Max,
///   Year, Month, Day, AddDays, DiffDays
/// </summary>
public static class Fn
{
    // ── Decompose ───────────────────────────────────────────────────

    public static string[] Split(string input, string separator)
        => input.Split(separator, StringSplitOptions.RemoveEmptyEntries);

    // ── Compose ─────────────────────────────────────────────────────

    public static string Join(string[] parts, string separator)
        => string.Join(separator, parts);

    public static string Concat(string a, string b) => a + b;
    public static string Concat3(string a, string b, string c) => a + b + c;
    public static string Concat4(string a, string b, string c, string d) => a + b + c + d;

    // ── Select ──────────────────────────────────────────────────────

    public static string At(string[] array, int index) => AtInternal(array, index);
    public static string First(string[] array) => array.Length > 0 ? array[0] : "";
    public static string Last(string[] array) => array.Length > 0 ? array[^1] : "";

    public static string[] Take(string[] array, int n)
        => array[..Math.Min(n, array.Length)];

    public static string[] Skip(string[] array, int n)
        => n >= array.Length ? [] : array[n..];

    public static string[] Slice(string[] array, int start, int count)
    {
        if (start < 0) start = Math.Max(0, array.Length + start);
        if (start >= array.Length) return [];
        return array[start..Math.Min(start + count, array.Length)];
    }

    public static int IndexOf(string[] array, string value)
        => Array.FindIndex(array, s => s.Equals(value, StringComparison.OrdinalIgnoreCase));

    public static string[] Where(string[] array, string value)
        => array.Where(s => s.Equals(value, StringComparison.OrdinalIgnoreCase)).ToArray();

    public static string[] Except(string[] array, string value)
        => array.Where(s => !s.Equals(value, StringComparison.OrdinalIgnoreCase)).ToArray();

    // ── Branch ──────────────────────────────────────────────────────

    public static string If(bool condition, string then, string otherwise) => condition ? then : otherwise;

    public static string Coalesce(string a, string b) => a.Length > 0 ? a : b;
    public static string Coalesce3(string a, string b, string c)
        => a.Length > 0 ? a : b.Length > 0 ? b : c;

    public static string Switch(string value, string case1, string result1, string defaultResult)
        => value.Equals(case1, StringComparison.OrdinalIgnoreCase) ? result1 : defaultResult;

    public static string Switch2(string value,
        string case1, string result1, string case2, string result2, string defaultResult)
        => value.Equals(case1, StringComparison.OrdinalIgnoreCase) ? result1
        : value.Equals(case2, StringComparison.OrdinalIgnoreCase) ? result2
        : defaultResult;

    public static string Switch3(string value,
        string case1, string result1, string case2, string result2,
        string case3, string result3, string defaultResult)
        => value.Equals(case1, StringComparison.OrdinalIgnoreCase) ? result1
        : value.Equals(case2, StringComparison.OrdinalIgnoreCase) ? result2
        : value.Equals(case3, StringComparison.OrdinalIgnoreCase) ? result3
        : defaultResult;

    // ── Convert ─────────────────────────────────────────────────────

    public static int ToInt(string s) => int.TryParse(s, out var n) ? n : 0;
    public static long ToLong(string s) => long.TryParse(s, out var n) ? n : 0;
    public static string IntToStr(int n, string format) => n.ToString(format);
    public static string IntToStr0(int n) => n.ToString();

    // ── Array transforms ────────────────────────────────────────────

    public static string[] Insert(string[] array, int index, string value)
    {
        var list = new List<string>(array);
        list.Insert(Math.Clamp(index, 0, list.Count), value);
        return list.ToArray();
    }

    public static string[] Remove(string[] array, int index)
    {
        if (index < 0) index = array.Length + index;
        if (index < 0 || index >= array.Length) return array;
        var list = new List<string>(array);
        list.RemoveAt(index);
        return list.ToArray();
    }

    public static string[] Filter(string[] array, string value)
        => array.Where(s => !s.Equals(value, StringComparison.OrdinalIgnoreCase)).ToArray();

    public static string[] Reverse(string[] array)
    { var copy = (string[])array.Clone(); Array.Reverse(copy); return copy; }

    public static string[] Sort(string[] array)
    { var copy = (string[])array.Clone(); Array.Sort(copy, StringComparer.OrdinalIgnoreCase); return copy; }

    public static string[] Distinct(string[] array)
        => array.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public static string[] Map(string[] array, string prefix, string suffix)
        => array.Select(s => prefix + s + suffix).ToArray();

    public static string[] Union(string[] a, string[] b)
        => a.Union(b, StringComparer.OrdinalIgnoreCase).ToArray();

    public static string[] Intersect(string[] a, string[] b)
        => a.Intersect(b, StringComparer.OrdinalIgnoreCase).ToArray();

    public static string[] SetExcept(string[] a, string[] b)
        => a.Except(b, StringComparer.OrdinalIgnoreCase).ToArray();

    // ── String transforms ───────────────────────────────────────────

    public static string Lower(string s) => s.ToLowerInvariant();
    public static string Upper(string s) => s.ToUpperInvariant();
    public static string Trim(string s) => s.Trim();

    public static string TitleCase(string s)
        => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());

    public static string Pad(string s, int width, string padChar)
        => s.PadLeft(width, padChar.Length > 0 ? padChar[0] : ' ');

    public static string PadRight(string s, int width, string padChar)
        => s.PadRight(width, padChar.Length > 0 ? padChar[0] : ' ');

    public static string Left(string s, int n) => s[..Math.Min(n, s.Length)];
    public static string Right(string s, int n) => n >= s.Length ? s : s[^n..];

    // ── Regex ───────────────────────────────────────────────────────

    public static bool Match(string input, string pattern)
        => Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase);

    public static string Capture(string input, string pattern, int group)
    {
        var m = Regex.Match(input, pattern, RegexOptions.IgnoreCase);
        return m.Success && group < m.Groups.Count ? m.Groups[group].Value : "";
    }

    public static string Replace(string input, string pattern, string replacement)
        => Regex.Replace(input, pattern, replacement, RegexOptions.IgnoreCase);

    // ── Aggregates ──────────────────────────────────────────────────

    public static int Len(string[] array) => array.Length;
    public static int LenStr(string s) => s.Length;

    public static bool Any(string[] array, string value)
        => array.Any(s => s.Equals(value, StringComparison.OrdinalIgnoreCase));

    public static bool All(string[] array, string value)
        => array.All(s => s.Equals(value, StringComparison.OrdinalIgnoreCase));

    public static int CountOf(string[] array, string value)
        => array.Count(s => s.Equals(value, StringComparison.OrdinalIgnoreCase));

    public static int Min(int a, int b) => Math.Min(a, b);
    public static int Max(int a, int b) => Math.Max(a, b);
    public static decimal MinD(decimal a, decimal b) => Math.Min(a, b);
    public static decimal MaxD(decimal a, decimal b) => Math.Max(a, b);

    // ── Date operations ─────────────────────────────────────────────

    public static int Year(string dateStr) => TryParseDate(dateStr, out var d) ? d.Year : 0;
    public static int Month(string dateStr) => TryParseDate(dateStr, out var d) ? d.Month : 0;
    public static int Day(string dateStr) => TryParseDate(dateStr, out var d) ? d.Day : 0;

    public static string AddDays(string dateStr, int days)
        => TryParseDate(dateStr, out var d) ? d.AddDays(days).ToString("yyyy-MM-dd") : dateStr;

    public static int DiffDays(string dateStr1, string dateStr2)
        => TryParseDate(dateStr1, out var d1) && TryParseDate(dateStr2, out var d2)
            ? (int)(d1 - d2).TotalDays : 0;

    public static string Today() => DateTime.UtcNow.ToString("yyyy-MM-dd");
    public static string Now() => DateTime.UtcNow.ToString("O");

    // ── Internal helpers ────────────────────────────────────────────

    private static string AtInternal(string[] array, int index)
    {
        if (index < 0) index = array.Length + index;
        return index >= 0 && index < array.Length ? array[index] : "";
    }

    private static bool TryParseDate(string s, out DateTime result)
        => DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
}
