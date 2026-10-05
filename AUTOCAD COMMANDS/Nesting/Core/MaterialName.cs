using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AUTOCAD_COMMANDS.Nesting.Core
{
    /// <summary>
    /// Ten vat lieu = LOAI + DO DAY: "INOX 304 1.2MM", "THEP 1.2MM". Loai co the trong
    /// ("1.2MM" - cach ghi cu, chua phan biet thep / inox).
    ///
    /// Moi vat lieu KHAC NHAU (khac loai HOAC khac do day) duoc ghep tren kho phoi rieng va
    /// khong bao gio tron chung - INOX 1.2MM va THEP 1.2MM la hai vat lieu.
    ///
    /// Do day LUON nam cuoi cung, nen tach duoc nguoc lai ma khong can bang tra cuu.
    /// </summary>
    public static class MaterialName
    {
        private static readonly Regex ThicknessAtEnd = new Regex(
            @"(?:^|\s)(?<t>\d+(?:\.\d+)?MM)$", RegexOptions.CultureInvariant);

        private static readonly Regex Thickness = new Regex(
            @"^\d+(?:\.\d+)?MM$", RegexOptions.CultureInvariant);

        /// <summary>Ghep loai + do day. Loai trong thi chi con do day.</summary>
        public static string Compose(string type, string thickness)
        {
            string t = Clean(type), d = Clean(thickness);
            if (t.Length == 0) return d;
            if (d.Length == 0) return t;
            return t + " " + d;
        }

        /// <summary>"INOX 304 1.2MM" -> loai "INOX 304", do day "1.2MM". Khong co do day -> ca chuoi la loai.</summary>
        public static void Split(string material, out string type, out string thickness)
        {
            string m = Clean(material);
            Match match = ThicknessAtEnd.Match(m);
            if (match.Success)
            {
                thickness = match.Groups["t"].Value;
                type = m.Substring(0, match.Index).Trim();
                return;
            }

            thickness = string.Empty;
            type = m;
        }

        public static string TypeOf(string material)
        {
            string type, thickness;
            Split(material, out type, out thickness);
            return type;
        }

        public static string ThicknessOf(string material)
        {
            string type, thickness;
            Split(material, out type, out thickness);
            return thickness;
        }

        /// <summary>Do day dang so (mm) de sap xep; khong co thi NaN.</summary>
        public static double ThicknessMm(string material)
        {
            string t = ThicknessOf(material);
            if (t.Length < 3) return double.NaN;
            double v;
            return double.TryParse(t.Substring(0, t.Length - 2), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out v)
                ? v : double.NaN;
        }

        /// <summary>Doi loai, giu do day.</summary>
        public static string WithType(string material, string type)
        {
            return Compose(type, ThicknessOf(material));
        }

        /// <summary>
        /// Mot muc trong cot "Vat lieu tuong thich" cua danh muc kho phoi co dung cho vat lieu
        /// nay khong. Muc co the ghi:
        ///   - day du      "INOX 1.2MM"  -> phai trung ca hai,
        ///   - chi do day  "1.2MM"       -> moi loai co do day do (danh muc cu van dung duoc),
        ///   - chi loai    "INOX"        -> moi do day cua loai do (ca "INOX 304"),
        ///                 "INOX 304"    -> chi dung mac do.
        /// </summary>
        public static bool EntryMatches(string entry, string material)
        {
            string e = Clean(entry), m = Clean(material);
            if (e.Length == 0) return true;
            if (string.Equals(e, m, StringComparison.Ordinal)) return true;

            string eType, eThick, mType, mThick;
            Split(e, out eType, out eThick);
            Split(m, out mType, out mThick);

            if (eThick.Length > 0 && !string.Equals(eThick, mThick, StringComparison.Ordinal)) return false;
            if (eType.Length == 0) return eThick.Length > 0;

            return string.Equals(eType, mType, StringComparison.Ordinal) ||
                   mType.StartsWith(eType + " ", StringComparison.Ordinal);
        }

        public static bool IsThickness(string s)
        {
            return Thickness.IsMatch(Clean(s));
        }

        /// <summary>
        /// So sanh de SAP XEP danh sach vat lieu: theo loai, roi do day tang dan (so, khong
        /// theo chu - "10MM" phai dung sau "2MM").
        /// </summary>
        public static int Compare(string a, string b)
        {
            int c = string.CompareOrdinal(TypeOf(a), TypeOf(b));
            if (c != 0) return c;
            double ta = ThicknessMm(a), tb = ThicknessMm(b);
            if (!double.IsNaN(ta) && !double.IsNaN(tb) && ta != tb) return ta.CompareTo(tb);
            return string.CompareOrdinal(Clean(a), Clean(b));
        }

        private static string Clean(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            return Regex.Replace(s.Trim().ToUpperInvariant(), @"\s+", " ");
        }
    }
}
