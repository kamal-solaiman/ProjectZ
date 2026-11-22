using System.Globalization;

namespace ProjectZ.Services;

/// <summary>
/// Validates barcode text and encodes it as Code128 for printing.
/// Uses code set B (ASCII 32..126) and switches to code set C for runs of four or more digits,
/// so long numeric barcodes stay short. The check symbol and stop pattern are always appended,
/// which is what makes the printed sticker readable by a barcode scanner.
/// </summary>
public static class Code128Encoder
{
    public const string EmptyMessage = "الصنف بلا باركود. أضف باركودًا من شاشة تعديل المنتج أولًا.";
    public const string UnsupportedMessage = "الباركود يحتوي رموزًا لا يدعمها Code128. استخدم حروفًا إنجليزية وأرقامًا ورموزًا ظاهرة.";

    private const int StartCodeB = 104;
    private const int CodeB = 100;
    private const int CodeC = 99;
    private const int StopCode = 106;

    /// <summary>Returns null when the value can be printed, otherwise an Arabic message for the user.</summary>
    public static string? Validate(string? barcode)
    {
        var text = (barcode ?? string.Empty).Trim();
        if (text.Length == 0) return EmptyMessage;
        return text.All(character => character is >= ' ' and <= '~') ? null : UnsupportedMessage;
    }

    /// <summary>
    /// Encodes the value as alternating bar/space widths in modules, excluding the quiet zones.
    /// The caller scales these widths to the printable sticker width.
    /// </summary>
    /// <exception cref="ArgumentException">The value is empty or contains a character outside ASCII 32..126.</exception>
    public static IReadOnlyList<int> Encode(string barcode)
    {
        if (Validate(barcode) is { } message) throw new ArgumentException(message, nameof(barcode));

        var text = barcode.Trim();
        var symbols = new List<int>(text.Length + 3) { StartCodeB };
        var index = 0;
        var inCodeC = false;

        while (index < text.Length)
        {
            var digits = DigitRunLength(text, index);
            if (digits >= 4)
            {
                if (!inCodeC)
                {
                    symbols.Add(CodeC);
                    inCodeC = true;
                }

                while (digits >= 2)
                {
                    symbols.Add(int.Parse(text.AsSpan(index, 2), NumberStyles.None, CultureInfo.InvariantCulture));
                    index += 2;
                    digits -= 2;
                }

                if (digits == 1)
                {
                    // An odd digit run keeps its last digit in code set B.
                    symbols.Add(CodeB);
                    inCodeC = false;
                    symbols.Add(text[index] - ' ');
                    index++;
                }

                continue;
            }

            if (inCodeC)
            {
                symbols.Add(CodeB);
                inCodeC = false;
            }

            symbols.Add(text[index] - ' ');
            index++;
        }

        var checksum = symbols[0];
        for (var position = 1; position < symbols.Count; position++)
            checksum += symbols[position] * position;
        symbols.Add(checksum % 103);
        symbols.Add(StopCode);

        var widths = new List<int>(symbols.Count * 6);
        foreach (var symbol in symbols)
            foreach (var digit in Patterns[symbol])
                widths.Add(digit - '0');

        return widths;
    }

    private static int DigitRunLength(string text, int index)
    {
        var length = 0;
        while (index + length < text.Length && char.IsAsciiDigit(text[index + length])) length++;
        return length;
    }

    /// <summary>
    /// Module widths for the 107 Code128 symbols (ISO/IEC 15417 Annex A), one digit per element;
    /// bars and spaces alternate. The stop symbol has a seventh element: the two-module termination bar.
    /// </summary>
    public static readonly string[] Patterns =
    [
        "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213",
        "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132",
        "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
        "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
        "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331",
        "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
        "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214",
        "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
        "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
        "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141",
        "114131", "311141", "411131", "211412", "211214", "211232", "2331112"
    ];
}
