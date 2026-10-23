// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace Files.App.Linux.Services;

/// <summary>
/// Localized strings. Reuses the Windows app's .resw translations (copied next to the binary under Strings/),
/// so every language Files supports on Windows is also available on Linux.
/// </summary>
public static class Loc
{
	private static Dictionary<string, string> _strings = new(StringComparer.Ordinal);
	private static Dictionary<string, string> _fallback = new(StringComparer.Ordinal);
	private static bool _loaded;

	public static string CurrentLanguage { get; private set; } = "en-US";

	public static void Load(string? language = null)
	{
		var baseDir = Path.Combine(AppContext.BaseDirectory, "Strings");
		_fallback = Read(Path.Combine(baseDir, "en-US", "Resources.resw"));

		var lang = language;
		if (string.IsNullOrEmpty(lang))
			lang = CultureInfo.CurrentUICulture.Name;

		var dir = ResolveLanguageDir(baseDir, lang);
		CurrentLanguage = dir ?? "en-US";
		_strings = dir is null ? _fallback : Read(Path.Combine(baseDir, dir, "Resources.resw"));
		_loaded = true;
	}

	public static IReadOnlyList<string> AvailableLanguages()
	{
		var baseDir = Path.Combine(AppContext.BaseDirectory, "Strings");
		if (!Directory.Exists(baseDir))
			return ["en-US"];
		return Directory.GetDirectories(baseDir).Select(Path.GetFileName).Where(x => x is not null).Cast<string>().Order().ToList();
	}

	private static string? ResolveLanguageDir(string baseDir, string lang)
	{
		if (!Directory.Exists(baseDir) || string.IsNullOrEmpty(lang))
			return null;

		var dirs = Directory.GetDirectories(baseDir).Select(Path.GetFileName).Where(x => x is not null).Cast<string>().ToList();
		var exact = dirs.FirstOrDefault(d => d.Equals(lang, StringComparison.OrdinalIgnoreCase));
		if (exact is not null)
			return exact;

		var neutral = lang.Split('-', '_')[0];
		return dirs.FirstOrDefault(d => d.Equals(neutral, StringComparison.OrdinalIgnoreCase))
			?? dirs.FirstOrDefault(d => d.StartsWith(neutral + "-", StringComparison.OrdinalIgnoreCase));
	}

	private static Dictionary<string, string> Read(string path)
	{
		var result = new Dictionary<string, string>(StringComparer.Ordinal);
		try
		{
			if (!File.Exists(path))
				return result;

			var doc = XDocument.Load(path);
			foreach (var data in doc.Root?.Elements("data") ?? [])
			{
				var name = data.Attribute("name")?.Value;
				var value = data.Element("value")?.Value;
				if (name is not null && value is not null)
					result[name] = value;
			}
		}
		catch
		{
			// Ignore malformed resource files and fall back to English.
		}
		return result;
	}

	/// <summary>Gets the string for <paramref name="key"/>, or <paramref name="fallback"/> when no translation exists.</summary>
	public static string Get(string key, string? fallback = null)
	{
		if (!_loaded)
			Load();

		if (_strings.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
			return value;
		if (_fallback.TryGetValue(key, out value) && !string.IsNullOrEmpty(value))
			return value;
		return fallback ?? key;
	}

	/// <summary>Formats a string that may use the ICU "{0, plural, one {...} other {...}}" syntax.</summary>
	public static string Format(string key, string fallback, params object[] args)
		=> FormatPattern(Get(key, fallback), args);

	internal static string FormatPattern(string pattern, params object[] args)
	{
		var sb = new StringBuilder();
		int i = 0;
		while (i < pattern.Length)
		{
			var c = pattern[i];
			if (c != '{')
			{
				sb.Append(c);
				i++;
				continue;
			}

			var end = FindMatchingBrace(pattern, i);
			if (end < 0)
			{
				sb.Append(pattern, i, pattern.Length - i);
				break;
			}

			var inner = pattern.Substring(i + 1, end - i - 1);
			sb.Append(FormatPlaceholder(inner, args));
			i = end + 1;
		}
		return sb.ToString();
	}

	private static string FormatPlaceholder(string inner, object[] args)
	{
		var parts = inner.Split(',', 3);
		if (!int.TryParse(parts[0].Trim(), out var index) || index < 0 || index >= args.Length)
			return "{" + inner + "}";

		var arg = args[index];
		if (parts.Length < 3 || parts[1].Trim() != "plural")
			return Convert.ToString(arg, CultureInfo.CurrentCulture) ?? string.Empty;

		var number = Convert.ToInt64(arg, CultureInfo.InvariantCulture);
		var options = ParsePluralOptions(parts[2]);
		string? chosen = null;
		if (options.TryGetValue("=" + number, out var exact))
			chosen = exact;
		else if (number == 1 && options.TryGetValue("one", out var one))
			chosen = one;
		else if (options.TryGetValue("other", out var other))
			chosen = other;
		chosen ??= options.Values.FirstOrDefault() ?? string.Empty;

		return FormatPattern(chosen.Replace("#", number.ToString(CultureInfo.CurrentCulture)), args);
	}

	private static Dictionary<string, string> ParsePluralOptions(string text)
	{
		var result = new Dictionary<string, string>(StringComparer.Ordinal);
		int i = 0;
		while (i < text.Length)
		{
			while (i < text.Length && char.IsWhiteSpace(text[i]))
				i++;
			int start = i;
			while (i < text.Length && text[i] != '{' && !char.IsWhiteSpace(text[i]))
				i++;
			var selector = text[start..i];
			while (i < text.Length && text[i] != '{')
				i++;
			if (i >= text.Length)
				break;
			var end = FindMatchingBrace(text, i);
			if (end < 0)
				break;
			result[selector] = text.Substring(i + 1, end - i - 1);
			i = end + 1;
		}
		return result;
	}

	private static int FindMatchingBrace(string s, int open)
	{
		int depth = 0;
		for (int i = open; i < s.Length; i++)
		{
			if (s[i] == '{')
				depth++;
			else if (s[i] == '}' && --depth == 0)
				return i;
		}
		return -1;
	}
}
