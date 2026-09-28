using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AUTOCAD_COMMANDS.Nesting.Recognition
{
    public enum MetadataKind
    {
        Quantity,
        Material
    }

    public sealed class MetadataFact
    {
        public MetadataFact(MetadataKind kind, string value, int quantity)
        {
            Kind = kind;
            Value = value;
            QuantityValue = quantity;
        }

        public MetadataKind Kind { get; private set; }

        /// <summary>Normalised value ("12", "1.2MM").</summary>
        public string Value { get; private set; }

        public int QuantityValue { get; private set; }
    }

    /// <summary>
    /// Chu CO DANG thong tin nhung KHONG dung duoc. Tach rieng khoi <see cref="MetadataFact"/> de
    /// ben goi khong the vo tinh coi no la gia tri hop le, cung khong coi no la chu cat.
    /// </summary>
    public enum MetadataIssueKind
    {
        /// <summary>Ro rang la SL ("SL:", "SL=", "SL -1"...) nhung gia tri sai -> phai xac nhan / sua.</summary>
        InvalidQuantity,

        /// <summary>Do day AM ("-1.2MM") - khong bao gio doi dau thanh vat lieu hop le.</summary>
        InvalidMaterial,

        /// <summary>Chu KICH THUOC (R12.5MM, D10MM, 150MM): khong phai vat lieu, khong phai chu cat.</summary>
        Dimension
    }

    public sealed class MetadataIssue
    {
        public MetadataIssue(MetadataIssueKind kind, string token)
        {
            Kind = kind;
            Token = token;
        }

        public MetadataIssueKind Kind { get; private set; }

        /// <summary>Doan chu gay ra van de (de bao cho nguoi dung).</summary>
        public string Token { get; private set; }
    }

    /// <summary>Ket qua phan loai MOT chu: gia tri hop le + cac van de.</summary>
    public sealed class MetadataReading
    {
        public MetadataReading()
        {
            Facts = new List<MetadataFact>();
            Issues = new List<MetadataIssue>();
        }

        public List<MetadataFact> Facts { get; private set; }

        public List<MetadataIssue> Issues { get; private set; }

        /// <summary>Chu nay la THONG TIN (dung hoac sai) - khong bao gio la chu cat.</summary>
        public bool IsMetadata
        {
            get { return Facts.Count > 0 || Issues.Exists(i => i.Kind != MetadataIssueKind.Dimension); }
        }

        /// <summary>Chi co chu kich thuoc: bo qua (khong phai thong tin, khong phai chu cat).</summary>
        public bool IsDimensionOnly
        {
            get { return Facts.Count == 0 && Issues.Count > 0 && Issues.TrueForAll(i => i.Kind == MetadataIssueKind.Dimension); }
        }
    }

    /// <summary>
    /// Configurable metadata rules. Each pattern must expose a named group "v".
    /// Defaults accept: "SL: 12", "SL:12", "SL 12", "sl: 12", "SL=12" and
    /// "1.2MM", "1.2 MM", "1,2MM", "1,2 MM", "1.2mm". Thickness outside
    /// [MinThicknessMm, MaxThicknessMm] is rejected so ordinary dimensions ("150MM") are not
    /// mistaken for a material.
    /// </summary>
    public sealed class MetadataRules
    {
        /// <summary>
        /// Mau GIU LAI cho tuong thich (cau hinh cu). <see cref="MetadataParser.Classify"/> doc SL
        /// bang <see cref="QuantityKeywordPattern"/> + quy tac gia tri trong code, de phan biet duoc
        /// SL HOP LE va SL SAI - viec mot regex "khop / khong khop" khong lam duoc.
        /// </summary>
        public List<string> QuantityPatterns { get; set; } = new List<string>
        {
            @"(?<![A-Z0-9])SL\s*[:=]?\s*(?<v>\d{1,5})(?![\d.,])"
        };

        /// <summary>
        /// Tu khoa SL dung RIENG: truoc khong phai chu / so, sau khong phai chu - nen "SLOT",
        /// "SLIDE", "SLEEVE", "ASL" khong bao gio bi coi la SL.
        /// </summary>
        public string QuantityKeywordPattern { get; set; } = @"(?<![\p{L}\p{N}])SL(?!\p{L})";

        public List<string> MaterialPatterns { get; set; } = new List<string>
        {
            @"(?<![\d.,])(?<v>\d{1,2}(?:[.,]\d{1,3})?)\s*MM(?![A-Z])"
        };

        /// <summary>Tien to KICH THUOC dinh lien truoc so (ban kinh, duong kinh): "R12.5MM", "D10MM", "Ø8MM".</summary>
        public string DimensionPrefixes { get; set; } = "RDØ⌀Φ";

        public double MinThicknessMm { get; set; } = 0.3;

        public double MaxThicknessMm { get; set; } = 25.0;

        public int MaxQuantity { get; set; } = 100000;

        public string DefaultMaterial { get; set; } = "1.2MM";

        public int DefaultQuantity { get; set; } = 1;
    }

    public sealed class MetadataParser
    {
        /// <summary>Mot vat lieu hop le bat dau dung tai vi tri nay ("1.2MM", "10 mm").</summary>
        private static readonly Regex MaterialToken = new Regex(
            @"\G\d{1,2}(?:[.,]\d{1,3})?\s*MM(?![A-Z])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>MOI so dung truoc "MM" (ke ca so lon / am / nhieu chu so le) - de goi dung ten no.</summary>
        private static readonly Regex AnyMmNumber = new Regex(
            @"(?<![\p{N}.,])(?<v>\d+(?:[.,]\d+)?)\s*MM(?![A-Z])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private readonly MetadataRules _rules;
        private readonly Regex _keyword;
        private readonly List<Regex> _material = new List<Regex>();

        public MetadataParser(MetadataRules rules)
        {
            _rules = rules ?? new MetadataRules();
            _keyword = new Regex(_rules.QuantityKeywordPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            foreach (string p in _rules.MaterialPatterns) _material.Add(new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        }

        public MetadataRules Rules { get { return _rules; } }

        public static string NormalizeMaterial(double thickness)
        {
            return thickness.ToString("0.###", CultureInfo.InvariantCulture) + "MM";
        }

        /// <summary>
        /// Returns all VALID quantity / material facts found in one text (in order of appearance).
        /// Chu co dang thong tin nhung sai KHONG xuat hien o day - dung <see cref="Classify"/>.
        /// </summary>
        public List<MetadataFact> Parse(string text)
        {
            return Classify(text).Facts;
        }

        /// <summary>
        /// Phan loai mot chu: SL hop le, vat lieu hop le, SL SAI, vat lieu AM, chu kich thuoc.
        ///
        /// SL: tu khoa SL dung rieng, roi ":" / "=" hoac mot so. Gia tri hop le = so nguyen
        /// 1..MaxQuantity (cho phep so 0 o dau, chu theo sau nhu "2 cai"). Dau phay SAU so chi la
        /// dau phan cach khi KHONG dinh lien chu so, hoac khi dinh lien mot vat lieu
        /// ("SL: 2, 1.2MM", "SL: 10,1.5MM"); "1,5" / "1.5" la so thap phan -> SAI. Co dang SL ma
        /// gia tri sai ("SL: 0", "SL: abc", "SL:", "SL: -1", "SL: 99999999999") -> InvalidQuantity.
        ///
        /// Vat lieu: nhu truoc (0.3..25 mm; "1.2MM", "1,2 mm", "T1.2MM"). Them: dau tru dinh lien
        /// truoc so -> InvalidMaterial (khong bao gio doi dau); R / D / Ø dinh lien truoc so, hoac so
        /// ngoai khoang do day -> Dimension (bo qua).
        /// </summary>
        public MetadataReading Classify(string text)
        {
            MetadataReading reading = new MetadataReading();
            if (string.IsNullOrWhiteSpace(text)) return reading;

            // Doc SL truoc va XOA phan da doc, de "SL 12" khong bao gio bi doc thanh vat lieu.
            char[] rest = text.ToCharArray();
            ClassifyQuantities(text, rest, reading);
            ClassifyMaterials(new string(rest), reading);
            return reading;
        }

        private void ClassifyQuantities(string text, char[] rest, MetadataReading reading)
        {
            int n = text.Length;
            foreach (Match m in _keyword.Matches(text))
            {
                int i = m.Index + m.Length;
                while (i < n && char.IsWhiteSpace(text[i])) i++;
                // Ca dau hai cham FULL-WIDTH (U+FF1A) - go bang bo go IME rat hay ra ky tu nay.
                bool separator = i < n && (text[i] == ':' || text[i] == '=' || text[i] == '\uFF1A');
                if (separator)
                {
                    i++;
                    while (i < n && char.IsWhiteSpace(text[i])) i++;
                }

                bool signed = i < n && (text[i] == '-' || text[i] == '+');

                // Khong co ":" / "=" thi chi coi la SL khi co SO (hoac dau) di ngay sau: "SL 5",
                // "SL -1". "SL ABC" hay chu "SL" dung mot minh van la chu thuong nhu truoc.
                if (!separator && !signed && !(i < n && char.IsDigit(text[i]))) continue;

                int k = signed ? i + 1 : i;
                int digitsStart = k;
                while (k < n && char.IsDigit(text[k])) k++;
                string digits = text.Substring(digitsStart, k - digitsStart);

                bool valid = digits.Length > 0 && !signed;
                if (valid && k + 1 < n && text[k] == '.' && char.IsDigit(text[k + 1])) valid = false;                               // 1.5
                if (valid && k + 1 < n && text[k] == ',' && char.IsDigit(text[k + 1]) && !MaterialToken.IsMatch(text, k + 1)) valid = false;   // 1,5

                int q = 0;
                if (valid)
                {
                    string trimmed = digits.TrimStart('0');
                    valid = trimmed.Length > 0 && trimmed.Length <= 9 &&
                            int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out q) &&
                            q >= 1 && q <= _rules.MaxQuantity;
                }

                int end = k;
                if (valid)
                {
                    reading.Facts.Add(new MetadataFact(MetadataKind.Quantity, q.ToString(CultureInfo.InvariantCulture), q));

                    // Nuot luon dau phay phan cach: "SL: 10,1.5MM" - neu de lai, mau vat lieu (cam dau
                    // phay dung truoc de loai "12,5.3MM") se khong doc duoc "1.5MM".
                    if (end < n && text[end] == ',') end++;
                }
                else
                {
                    // Lay ca "tu" gia tri de bao, dung o khoang trang hoac o dau phay phan cach.
                    while (end < n && !char.IsWhiteSpace(text[end]))
                    {
                        if (text[end] == ',' && !(end + 1 < n && char.IsDigit(text[end + 1]) && !MaterialToken.IsMatch(text, end + 1))) break;
                        end++;
                    }

                    reading.Issues.Add(new MetadataIssue(MetadataIssueKind.InvalidQuantity, text.Substring(m.Index, end - m.Index).Trim()));
                }

                for (int c = m.Index; c < end; c++) rest[c] = ' ';
            }
        }

        private void ClassifyMaterials(string remaining, MetadataReading reading)
        {
            bool[] covered = new bool[remaining.Length];
            foreach (Regex rx in _material)
            {
                foreach (Match m in rx.Matches(remaining))
                {
                    Group g = m.Groups["v"];
                    for (int c = m.Index; c < m.Index + m.Length; c++) covered[c] = true;
                    Classify(remaining, g.Index, g.Value, m.Value.Trim(), true, reading);
                }
            }

            // So KHONG nam trong mau vat lieu (150MM, 1200 MM, 1.2345MM...): van phai goi dung ten
            // (kich thuoc / am), neu khong no se bi coi la chu cat tren chi tiet.
            foreach (Match m in AnyMmNumber.Matches(remaining))
            {
                if (covered[m.Groups["v"].Index]) continue;
                Classify(remaining, m.Groups["v"].Index, m.Groups["v"].Value, m.Value.Trim(), false, reading);
            }
        }

        private void Classify(string remaining, int start, string value, string token, bool materialShape, MetadataReading reading)
        {
            // Dau tru dinh lien truoc so = do day AM: bao, KHONG bao gio bo dau.
            if (start > 0 && remaining[start - 1] == '-')
            {
                reading.Issues.Add(new MetadataIssue(MetadataIssueKind.InvalidMaterial, "-" + token));
                return;
            }

            // R / D / Ø dinh lien truoc so, va chinh chu do dung rieng (khong phai duoi mot tu).
            if (start > 0 && _rules.DimensionPrefixes.IndexOf(char.ToUpperInvariant(remaining[start - 1])) >= 0 &&
                (start == 1 || !char.IsLetterOrDigit(remaining[start - 2])))
            {
                reading.Issues.Add(new MetadataIssue(MetadataIssueKind.Dimension, remaining[start - 1] + token));
                return;
            }

            double t;
            if (materialShape &&
                double.TryParse(value.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out t) &&
                t >= _rules.MinThicknessMm && t <= _rules.MaxThicknessMm)
            {
                reading.Facts.Add(new MetadataFact(MetadataKind.Material, NormalizeMaterial(t), 0));
                return;
            }

            reading.Issues.Add(new MetadataIssue(MetadataIssueKind.Dimension, token));
        }
    }
}
