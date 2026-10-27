using System;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Formats in-game dates for a language, following the publisher's date-format lockit
/// ("Uncanny_Valley_Lockit_Polden - Date_formats"). Rules are keyed by the primary language subtag, so "es" and
/// "es-ES" behave the same, and they apply even before a language has a translation column (font-only preview).
/// English, pseudolocales and unknown languages use the original English document formats, so English output is
/// byte-identical to the pre-localization strings.
/// <para>
/// <see cref="Style.Numeric"/> is the document form (English "dd/MM/yyyy"). <see cref="Style.Named"/> is the form
/// with the month written out (English "05 DEC" / "20 Oct 1989"); languages whose lockit only defines numerals
/// (Georgian) fall back to numerals, Polish uses its Roman-numeral month form, CJK use 年月日 / 년월일.
/// </para>
/// Month names come from the Documents sheet (<c>doc.month.full.N</c> / <c>doc.month.short.N</c>) when the language
/// has its own text there, otherwise from the built-in tables below.
/// </summary>
public static class LocalizedDate
{
    public enum Style
    {
        /// <summary>Numerals only (English "dd/MM/yyyy").</summary>
        Numeric,
        /// <summary>Month written out where the language's lockit allows it (English "dd MMM yyyy").</summary>
        Named
    }

    /// <summary>Formats <paramref name="date"/> in the current language (<see cref="Loc.CurrentLanguage"/>).</summary>
    /// <param name="hasYear">False renders day and month only (permit expiry "05 DEC").</param>
    /// <param name="fullMonth">Named style: full month name instead of the abbreviation.</param>
    /// <param name="uppercase">Named style: upper-case the month (document style), where the language allows it.</param>
    public static string Format(DateTime date, Style style, bool hasYear = true, bool fullMonth = false, bool uppercase = false)
        => Format(date, Loc.CurrentLanguage, style, hasYear, fullMonth, uppercase);

    /// <summary>Formats <paramref name="date"/> for an explicit language code (e.g. "ru", "zh-Hans", "pt-BR").</summary>
    public static string Format(DateTime date, string languageCode, Style style, bool hasYear = true, bool fullMonth = false, bool uppercase = false)
    {
        string lang = Primary(languageCode);
        return style == Style.Numeric
            ? FormatNumeric(date, lang, hasYear)
            : FormatNamed(date, lang, languageCode, hasYear, fullMonth, uppercase);
    }

    /// <summary>
    /// Stand-alone (nominative) month name for headings such as the wall calendar: "October", "Октябрь", "Październik",
    /// "十月", "10월". Capitalized where the script has case.
    /// </summary>
    public static string StandaloneMonthName(int month) => StandaloneMonthName(month, Loc.CurrentLanguage);

    public static string StandaloneMonthName(int month, string languageCode)
    {
        int i = Mathf.Clamp(month, 1, 12) - 1;
        string lang = Primary(languageCode);
        string name = lang switch
        {
            "ru" => RuNominative[i],
            "de" => DeFull[i],
            "fr" => FrFull[i],
            "es" => EsFull[i],
            "pt" => PtFull[i],
            "pl" => PlNominative[i],
            "ka" => KaNominative[i],
            "zh" => ZhNominative[i],
            "ja" => (i + 1).ToString(CultureInfo.InvariantCulture) + "月",
            "ko" => (i + 1).ToString(CultureInfo.InvariantCulture) + "월",
            _ => CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(i + 1)
        };
        return Capitalize(name, CultureFor(lang));
    }

    // ── Numerals ─────────────────────────────────────────────────────────────────

    private static string FormatNumeric(DateTime date, string lang, bool hasYear)
    {
        string d = date.Day.ToString(CultureInfo.InvariantCulture);
        string m = date.Month.ToString(CultureInfo.InvariantCulture);
        string dd = date.Day.ToString("00", CultureInfo.InvariantCulture);
        string mm = date.Month.ToString("00", CultureInfo.InvariantCulture);
        string y = date.Year.ToString("0000", CultureInfo.InvariantCulture);

        switch (lang)
        {
            // Lockit: YYYY年M月D日, no leading zeros.
            case "zh":
            case "ja":
                return hasYear ? $"{y}年{m}月{d}日" : $"{m}月{d}日";
            // Lockit (official administrative documents): YYYY. M. D. — periods and spaces required, no leading zeros.
            case "ko":
                return hasYear ? $"{y}. {m}. {d}." : $"{m}. {d}.";
            // Lockit: DD.MM.YYYY (official documents use leading zeros; Georgian/Polish months always two digits).
            case "ru":
            case "ka":
            case "pl":
            case "es":
                return hasYear ? $"{dd}.{mm}.{y}" : $"{dd}.{mm}";
            // German writes a day-month-only date with a closing period ("05.12.").
            case "de":
                return hasYear ? $"{dd}.{mm}.{y}" : $"{dd}.{mm}.";
            // Lockit: no leading zeros for day and month (strict recommendation).
            case "pt":
                return hasYear ? $"{d}/{m}/{y}" : $"{d}/{m}";
            // fr: DD/MM/YYYY. English: original document format.
            default:
                return hasYear ? $"{dd}/{mm}/{y}" : $"{dd}/{mm}";
        }
    }

    // ── Month written out ────────────────────────────────────────────────────────

    private static string FormatNamed(DateTime date, string lang, string languageCode, bool hasYear, bool fullMonth, bool uppercase)
    {
        string d = date.Day.ToString(CultureInfo.InvariantCulture);
        string y = date.Year.ToString("0000", CultureInfo.InvariantCulture);
        CultureInfo culture = CultureFor(lang);

        switch (lang)
        {
            case "zh":
            case "ja":
                return FormatNumeric(date, lang, hasYear);
            // Lockit (running text): YYYY년 M월 D일.
            case "ko":
            {
                string m = date.Month.ToString(CultureInfo.InvariantCulture);
                return hasYear ? $"{y}년 {m}월 {d}일" : $"{m}월 {d}일";
            }
            // Lockit (verbal-digital): D [month, genitive] YYYY г. — "г." is mandatory, no leading zero.
            case "ru":
            {
                string month = Upper(Month(lang, languageCode, date.Month, full: true), uppercase, culture);
                return hasYear ? $"{d} {month} {y} г." : $"{d} {month}";
            }
            // Lockit: "1. Januar 2026" / "1. Jan. 2026", no leading zero on the day.
            case "de":
            {
                string month = Upper(Month(lang, languageCode, date.Month, fullMonth), uppercase, culture);
                return hasYear ? $"{d}. {month} {y}" : $"{d}. {month}";
            }
            // Lockit: "1er janvier 2026" / "1er jan. 2026".
            case "fr":
            {
                string day = date.Day == 1 ? "1er" : d;
                string month = Upper(Month(lang, languageCode, date.Month, fullMonth), uppercase, culture);
                return hasYear ? $"{day} {month} {y}" : $"{day} {month}";
            }
            // Lockit (es): "18 de agosto de 2024", month always lower-case. Portuguese follows the same pattern.
            case "es":
            case "pt":
            {
                string month = Month(lang, languageCode, date.Month, fullMonth);
                if (fullMonth) return hasYear ? $"{d} de {month} de {y}" : $"{d} de {month}";
                return hasYear ? $"{d} {month} {y}" : $"{d} {month}";
            }
            // Lockit: Roman-numeral month, "20 X 2026".
            case "pl":
            {
                string month = RomanMonths[date.Month - 1];
                return hasYear ? $"{d} {month} {y}" : $"{d} {month}";
            }
            // Lockit only defines numerals.
            case "ka":
                return FormatNumeric(date, lang, hasYear);
            // English: original document format ("05 DEC", "02 March 1990", "20 Oct 1989").
            default:
            {
                string month = CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(date.Month);
                if (fullMonth) month = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(date.Month);
                if (uppercase) month = month.ToUpperInvariant();
                string dd = date.Day.ToString("00", CultureInfo.InvariantCulture);
                return hasYear ? $"{dd} {month} {y}" : $"{dd} {month}";
            }
        }
    }

    /// <summary>Translator text from the Documents sheet when the language has its own value, else the built-in table.</summary>
    private static string Month(string lang, string languageCode, int month, bool full)
    {
        if (languageCode == Loc.CurrentLanguage &&
            Loc.TryGetTranslated((full ? "doc.month.full." : "doc.month.short.") + month, out string translated))
            return translated;

        int i = month - 1;
        return lang switch
        {
            "ru" => RuGenitive[i],
            "de" => full ? DeFull[i] : DeShort[i],
            "fr" => full ? FrFull[i] : FrShort[i],
            "es" => full ? EsFull[i] : EsShort[i],
            "pt" => full ? PtFull[i] : PtShort[i],
            _ => full
                ? CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month)
                : CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(month)
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>"zh-Hans" becomes "zh", "pt-BR" becomes "pt". Pseudolocales map to English rules.</summary>
    private static string Primary(string languageCode)
    {
        if (string.IsNullOrEmpty(languageCode) || PseudoLocalizer.IsPseudo(languageCode)) return Loc.SourceLanguage;
        int dash = languageCode.IndexOfAny(new[] { '-', '_' });
        return (dash > 0 ? languageCode.Substring(0, dash) : languageCode).ToLowerInvariant();
    }

    private static CultureInfo CultureFor(string lang)
    {
        try { return CultureInfo.GetCultureInfo(lang); }
        catch (CultureNotFoundException) { return CultureInfo.InvariantCulture; }
    }

    private static string Upper(string text, bool uppercase, CultureInfo culture)
        => uppercase && !string.IsNullOrEmpty(text) ? text.ToUpper(culture) : text;

    private static string Capitalize(string text, CultureInfo culture)
    {
        if (string.IsNullOrEmpty(text) || !char.IsLower(text[0])) return text;
        return char.ToUpper(text[0], culture) + text.Substring(1);
    }

    // ── Built-in month tables (used until the Documents sheet has the language) ──

    private static readonly string[] RuGenitive = { "января", "февраля", "марта", "апреля", "мая", "июня", "июля", "августа", "сентября", "октября", "ноября", "декабря" };
    private static readonly string[] RuNominative = { "январь", "февраль", "март", "апрель", "май", "июнь", "июль", "август", "сентябрь", "октябрь", "ноябрь", "декабрь" };
    private static readonly string[] DeFull = { "Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember" };
    private static readonly string[] DeShort = { "Jan.", "Feb.", "März", "Apr.", "Mai", "Juni", "Juli", "Aug.", "Sept.", "Okt.", "Nov.", "Dez." };
    private static readonly string[] FrFull = { "janvier", "février", "mars", "avril", "mai", "juin", "juillet", "août", "septembre", "octobre", "novembre", "décembre" };
    private static readonly string[] FrShort = { "janv.", "févr.", "mars", "avr.", "mai", "juin", "juil.", "août", "sept.", "oct.", "nov.", "déc." };
    private static readonly string[] EsFull = { "enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre" };
    private static readonly string[] EsShort = { "ene.", "feb.", "mar.", "abr.", "may.", "jun.", "jul.", "ago.", "sept.", "oct.", "nov.", "dic." };
    private static readonly string[] PtFull = { "janeiro", "fevereiro", "março", "abril", "maio", "junho", "julho", "agosto", "setembro", "outubro", "novembro", "dezembro" };
    private static readonly string[] PtShort = { "jan.", "fev.", "mar.", "abr.", "maio", "jun.", "jul.", "ago.", "set.", "out.", "nov.", "dez." };
    private static readonly string[] PlNominative = { "styczeń", "luty", "marzec", "kwiecień", "maj", "czerwiec", "lipiec", "sierpień", "wrzesień", "październik", "listopad", "grudzień" };
    private static readonly string[] KaNominative = { "იანვარი", "თებერვალი", "მარტი", "აპრილი", "მაისი", "ივნისი", "ივლისი", "აგვისტო", "სექტემბერი", "ოქტომბერი", "ნოემბერი", "დეკემბერი" };
    private static readonly string[] ZhNominative = { "一月", "二月", "三月", "四月", "五月", "六月", "七月", "八月", "九月", "十月", "十一月", "十二月" };
    private static readonly string[] RomanMonths = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI", "XII" };
}
