using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AUTOCAD_COMMANDS.Nesting.Recognition
{
    public enum MetadataKind
    {
        Quantity,

        /// <summary>Do day ("1.2MM").</summary>
        Material,

        /// <summary>Loai vat lieu ("INOX", "INOX 304", "THEP", "MA KEM"...).</summary>
        MaterialType
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

        /// <summary>
        /// Phan chu CON LAI sau khi bo SL / do day / loai vat lieu (vd. MText 3 dong
        /// "SL: 1" / "0.75MM" / "CHAN DOI XUNG" -> "CHAN DOI XUNG"). Dung lam TEN chi tiet de
        /// nhan ra tren bang kiem tra va tren nhan. Rong khi khong con gi dang ke.
        /// </summary>
        public string Remainder { get; set; } = string.Empty;

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

        /// <summary>
        /// Loai vat lieu khi ban ve KHONG ghi loai (vd. "THEP"). Rong = chi co do day nhu cach
        /// cu ("1.2MM"). Chu ghi loai tren ban ve ("INOX", "SUS304"...) luon thang.
        /// </summary>
        public string DefaultMaterialType { get; set; } = string.Empty;

        /// <summary>
        /// Cach doc LOAI vat lieu: "TEN CHUAN=regex". Regex chay tren chu da BO DAU, viet hoa,
        /// va phai dung RIENG (khong dinh chu cai hai ben). Nhom ten "g" (neu co) la MAC
        /// ("304") va duoc noi vao ten chuan: "SUS304" -> "INOX 304".
        ///
        /// Khong co "AL" / "GI" / "DONG" dung mot minh: qua de trung voi ma chi tiet / chu
        /// thuong ("DONG GOI"). Thu tu quan trong: mau truoc "an" chu truoc ("THEP KHONG GI" la
        /// INOX, "TON DEN" la THEP, "TON LANH" khac "TON KEM" - roi moi den "TON" tron).
        /// </summary>
        public List<string> MaterialTypePatterns { get; set; } = new List<string>
        {
            @"INOX=(?:INOX|SUS)\s*-?\s*(?<g>201|304L?|316L?|430)?|THEP\s+KHONG\s+GI|KHONG\s+GI|STAINLESS(?:\s+STEEL)?",
            @"INOX=SS\s*-?\s*(?<g>201|304L?|316L?|430)",
            @"THEP=THEP(?:\s+(?:DEN|TAM|CAN\s+NGUOI|CAN\s+NONG))?|TON\s+DEN|SPCC|SPHC|SS\s*-?\s*400|CT\s*3|Q\s*235",
            @"MA KEM=(?:TON\s+)?MA\s+KEM|TON\s+KEM|SGCC|SECC|GALV(?:ANI[SZ]ED)?",
            @"TON LANH=TON\s+LANH|GALVALUME",
            @"NHOM=NHOM|ALU(?:MINIUM|MINUM)?|AL\s*-?\s*\d{4}|A\s*5052|A\s*6061",
            @"DONG=DONG\s+(?:DO|THAU|VANG)",
            @"TON=TON|TOLE"
        };

        /// <summary>
        /// QUY DOI loai vat lieu cua RIENG xuong nay, dang "CHU=LOAI" (vd. "TON=THEP",
        /// "TOLE=THEP", "TON LANH=MA KEM"). Hai tac dung:
        ///   - CHU (cum tu, khong phan biet dau / hoa thuong) xuat hien tren ban ve -> doc la LOAI,
        ///     uu tien hon moi mau co san;
        ///   - loai DA NHAN RA trung CHU (vd. "TON") -> doi ten thanh LOAI.
        /// Rong = khong quy doi.
        /// </summary>
        public List<string> MaterialTypeAliases { get; set; } = new List<string>();

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

        /// <summary>Chu "don vi" / chu dem - khong phai ten chi tiet ("SL: 2 CAI", "T1.2MM").</summary>
        private static readonly HashSet<string> UnitWords = new HashSet<string>(StringComparer.Ordinal)
        {
            "CAI", "CHIEC", "TAM", "BO", "PCS", "PC", "CON", "THANH", "MIENG", "SL", "X", "T", "DAY", "VL", "VAT", "LIEU"
        };

        private readonly MetadataRules _rules;
        private readonly Regex _keyword;
        private readonly List<Regex> _material = new List<Regex>();
        private readonly List<KeyValuePair<string, Regex>> _types = new List<KeyValuePair<string, Regex>>();
        private readonly Dictionary<string, string> _aliasOf = new Dictionary<string, string>(StringComparer.Ordinal);

        // ---- Cach ghi DO DAY quen thuoc o xuong, doi ve dang "...MM" truoc khi doc ----

        /// <summary>"1 LY 2" (doc mieng: mot ly hai) -> 1.2MM.</summary>
        private static readonly Regex LySpoken = new Regex(
            @"(?<![\p{L}\p{N}.,])(\d{1,2})\s*(?:LY|LI)\s*(\d)(?![\d.,])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>"1.2 LY", "1,5ly", "2 LI" -> MM.</summary>
        private static readonly Regex LyUnit = new Regex(
            @"(?<![\p{L}\p{N}.,])(\d{1,2}(?:[.,]\d{1,3})?)\s*(?:LY|LI)(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>"T=1.2", "T: 1.5", "t = 2", "δ1.2", "δ=1.2" -> MM.</summary>
        private static readonly Regex ThicknessSymbol = new Regex(
            @"(?<![\p{L}\p{N}])(?:[Tt]\s*[=:]|[δΔ]\s*[=:]?)\s*(\d{1,2}(?:[.,]\d{1,3})?)(?![\d.,])(?!\s*MM)", RegexOptions.CultureInvariant);

        /// <summary>"1.2T", "2T" -> MM.</summary>
        private static readonly Regex ThicknessSuffixT = new Regex(
            @"(?<![\p{L}\p{N}.,])(\d{1,2}(?:[.,]\d{1,3})?)\s*T(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>"DAY 1.2", "dày: 1.5" (khong don vi) -> MM.</summary>
        private static readonly Regex ThicknessWord = new Regex(
            @"(?<![\p{L}])(D[AÀÁ]Y\s*[:=]?\s*)(\d{1,2}(?:[.,]\d{1,3})?)(?![\d.,])(?!\s*MM)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>So TRAN ngay sau ten loai ("TON 1.2", "INOX: 2") - doc la do day.</summary>
        private static readonly Regex BareNumber = new Regex(
            @"\G\s*[:=]?\s*(?<v>[1-9]\d?(?:[.,]\d{1,3})?|0[.,]\d{1,3})(?![\d.,])(?!\s*[A-Z%])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Doi cac cach ghi do day o xuong ve dang "...MM" ma bo doc da hieu.</summary>
        internal static string NormalizeThicknessWriting(string text)
        {
            string t = LySpoken.Replace(text, "$1.$2MM");
            t = LyUnit.Replace(t, "$1MM");
            t = ThicknessSymbol.Replace(t, "$1MM");
            t = ThicknessWord.Replace(t, "$1$2MM");
            t = ThicknessSuffixT.Replace(t, "$1MM");
            return t;
        }

        public MetadataParser(MetadataRules rules)
        {
            _rules = rules ?? new MetadataRules();
            _keyword = new Regex(_rules.QuantityKeywordPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            foreach (string p in _rules.MaterialPatterns) _material.Add(new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
            // Quy doi cua nguoi dung chay TRUOC moi mau co san.
            foreach (KeyValuePair<string, string> alias in ParseAliases(_rules.MaterialTypeAliases))
            {
                _aliasOf[alias.Key] = alias.Value;
                string words = string.Join(@"\s+", Array.ConvertAll(alias.Key.Split(' '), Regex.Escape));
                _types.Add(new KeyValuePair<string, Regex>(alias.Value, TypeRegex(words)));
            }

            foreach (string p in _rules.MaterialTypePatterns ?? new List<string>())
            {
                int eq = p == null ? -1 : p.IndexOf('=');
                if (eq <= 0) continue;
                _types.Add(new KeyValuePair<string, Regex>(p.Substring(0, eq).Trim().ToUpperInvariant(), TypeRegex(p.Substring(eq + 1))));
            }
        }

        /// <summary>
        /// Mau loai vat lieu dung RIENG: khong dinh chu / so phia truoc, khong dinh chu phia sau,
        /// va khong phai MA CHI TIET ("TON-01": gach noi roi so ngay sau).
        /// </summary>
        private static Regex TypeRegex(string body)
        {
            return new Regex(@"(?<![\p{L}\p{N}])(?:" + body + @")(?![\p{L}])(?!-\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        /// <summary>"CHU=LOAI" -> cap (CHU, LOAI) da bo dau, viet hoa, gon khoang trang. Bo dong sai.</summary>
        public static List<KeyValuePair<string, string>> ParseAliases(IEnumerable<string> aliases)
        {
            List<KeyValuePair<string, string>> list = new List<KeyValuePair<string, string>>();
            if (aliases == null) return list;
            foreach (string a in aliases)
            {
                int eq = a == null ? -1 : a.IndexOf('=');
                if (eq <= 0) continue;
                string from = CleanWords(a.Substring(0, eq)), to = CleanWords(a.Substring(eq + 1));
                if (from.Length == 0 || to.Length == 0 || from == to) continue;
                list.Add(new KeyValuePair<string, string>(from, to));
            }

            return list;
        }

        private static string CleanWords(string s)
        {
            return Regex.Replace(StripDiacritics(s ?? string.Empty).ToUpperInvariant(), @"[^A-Z0-9]+", " ").Trim();
        }

        /// <summary>Loai da nhan ra -> loai sau quy doi cua nguoi dung (khong co thi giu nguyen).</summary>
        public string MapType(string type)
        {
            string mapped;
            return type != null && _aliasOf.TryGetValue(type, out mapped) ? mapped : type;
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

            // "1.2 LY", "T=1.2", "1 LY 2"... -> "1.2MM" truoc, roi doc nhu cu.
            text = NormalizeThicknessWriting(text);

            // Doc SL truoc va XOA phan da doc, de "SL 12" khong bao gio bi doc thanh vat lieu.
            char[] rest = text.ToCharArray();
            ClassifyQuantities(text, rest, reading);
            ClassifyMaterials(new string(rest), rest, reading);
            ClassifyTypes(rest, reading);
            reading.Remainder = Remainder(rest);
            return reading;
        }

        /// <summary>
        /// Loai vat lieu ("INOX", "SUS 304", "THEP", "MA KEM"...). Doc tren chu da BO DAU (go
        /// "THÉP" hay "THEP" deu duoc), chi tren phan con lai sau khi da xoa SL / do day, va
        /// xoa luon phan da doc.
        /// </summary>
        private void ClassifyTypes(char[] rest, MetadataReading reading)
        {
            if (_types.Count == 0) return;
            string plain = StripDiacritics(new string(rest)).ToUpperInvariant();
            bool[] taken = new bool[plain.Length];

            // Gom MOI cho khop cua moi mau, roi lay theo DO DAI giam dan (bang nhau thi mau dung
            // truoc thang - quy doi cua nguoi dung dung dau): "TON KEM" luon thang "TON", "THEP
            // KHONG GI" luon thang "THEP", du "TON" / "THEP" co trong bang quy doi.
            List<int[]> hits = new List<int[]>();          // {index, length, pattern}
            for (int k = 0; k < _types.Count; k++)
            {
                foreach (Match m in _types[k].Value.Matches(plain))
                {
                    if (m.Length > 0) hits.Add(new[] { m.Index, m.Length, k });
                }
            }

            hits.Sort((x, y) => x[1] != y[1] ? y[1].CompareTo(x[1]) : (x[2] != y[2] ? x[2].CompareTo(y[2]) : x[0].CompareTo(y[0])));

            List<int[]> chosen = new List<int[]>();
            foreach (int[] h in hits)
            {
                bool overlap = false;
                for (int c = h[0]; c < h[0] + h[1] && !overlap; c++) overlap = taken[c];
                if (overlap) continue;
                for (int c = h[0]; c < h[0] + h[1]; c++) taken[c] = true;
                chosen.Add(h);
            }

            chosen.Sort((x, y) => x[0].CompareTo(y[0]));
            foreach (int[] h in chosen)
            {
                KeyValuePair<string, Regex> t = _types[h[2]];
                Match m = t.Value.Match(plain, h[0]);
                Group g = m.Success && m.Index == h[0] ? m.Groups["g"] : null;
                string value = MapType(g != null && g.Success && g.Length > 0 ? t.Key + " " + g.Value : t.Key);
                if (!reading.Facts.Exists(f => f.Kind == MetadataKind.MaterialType && f.Value == value))
                {
                    reading.Facts.Add(new MetadataFact(MetadataKind.MaterialType, value, 0));
                }

                for (int c = h[0]; c < h[0] + h[1]; c++) rest[c] = ' ';

                // "TON 1.2", "INOX: 2" - so tran ngay sau ten loai la DO DAY (chi khi chu nay
                // chua co do day nao, va so nam trong khoang do day hop le).
                if (reading.Facts.Exists(f => f.Kind == MetadataKind.Material)) continue;
                Match num = BareNumber.Match(plain, h[0] + h[1]);
                double th;
                if (num.Success &&
                    double.TryParse(num.Groups["v"].Value.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out th) &&
                    th >= _rules.MinThicknessMm && th <= _rules.MaxThicknessMm)
                {
                    reading.Facts.Add(new MetadataFact(MetadataKind.Material, NormalizeMaterial(th), 0));
                    for (int c = num.Index; c < num.Index + num.Length; c++)
                    {
                        taken[c] = true;
                        rest[c] = ' ';
                    }
                }
            }
        }

        /// <summary>
        /// Ten loai vat lieu chuan tu chu nguoi dung go ("inox", "sus304", "thép") - null neu
        /// khong nhan ra. Dung cho o sua tay o bang kiem tra.
        /// </summary>
        public string ParseType(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            MetadataReading r = new MetadataReading();
            ClassifyTypes(text.ToCharArray(), r);
            MetadataFact f = r.Facts.Find(x => x.Kind == MetadataKind.MaterialType);
            return f != null ? f.Value : null;
        }

        /// <summary>
        /// Bo dau tieng Viet, GIU NGUYEN DO DAI chuoi (moi ky tu -> mot ky tu) de vi tri khop
        /// tren chuoi bo dau dung y vi tri tren chuoi goc.
        /// </summary>
        internal static string StripDiacritics(string s)
        {
            char[] r = new char[s.Length];
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\u0110') { r[i] = 'D'; continue; }
                if (c == '\u0111') { r[i] = 'd'; continue; }
                string d = c.ToString().Normalize(System.Text.NormalizationForm.FormD);
                r[i] = d.Length > 0 ? d[0] : c;
            }

            return new string(r);
        }

        /// <summary>Phan chu con lai co nghia (it nhat 3 chu cai, bo chu don vi / dau cau).</summary>
        private static string Remainder(char[] rest)
        {
            List<string> words = new List<string>();
            int letters = 0;
            char[] separators = { ' ', '\t', '\r', '\n', ',', ';', '/', '|', ':', '=', '(', ')', '[', ']' };
            foreach (string raw in new string(rest).Split(separators, StringSplitOptions.RemoveEmptyEntries))
            {
                string w = raw.Trim('-', '.', '+', '*', '_', '"', '\'');
                if (w.Length == 0) continue;
                if (UnitWords.Contains(StripDiacritics(w).ToUpperInvariant())) continue;
                words.Add(w);
                foreach (char c in w)
                {
                    if (char.IsLetter(c)) letters++;
                }
            }

            if (letters < 3) return string.Empty;
            string joined = string.Join(" ", words.ToArray());
            return joined.Length > 40 ? joined.Substring(0, 40).Trim() : joined;
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

        private void ClassifyMaterials(string remaining, char[] rest, MetadataReading reading)
        {
            bool[] covered = new bool[remaining.Length];
            foreach (Regex rx in _material)
            {
                foreach (Match m in rx.Matches(remaining))
                {
                    Group g = m.Groups["v"];
                    for (int c = m.Index; c < m.Index + m.Length; c++) covered[c] = true;
                    Classify(remaining, g.Index, g.Value, m.Value.Trim(), true, reading);
                    Blank(rest, m.Index, m.Length, remaining);
                }
            }

            // So KHONG nam trong mau vat lieu (150MM, 1200 MM, 1.2345MM...): van phai goi dung ten
            // (kich thuoc / am), neu khong no se bi coi la chu cat tren chi tiet.
            foreach (Match m in AnyMmNumber.Matches(remaining))
            {
                if (covered[m.Groups["v"].Index]) continue;
                Classify(remaining, m.Groups["v"].Index, m.Groups["v"].Value, m.Value.Trim(), false, reading);
                Blank(rest, m.Index, m.Length, remaining);
            }
        }

        /// <summary>Xoa doan da doc (kem dau tru / tien to R, D, O dinh lien truoc) khoi phan con lai.</summary>
        private void Blank(char[] rest, int start, int length, string remaining)
        {
            if (start > 0 && (remaining[start - 1] == '-' ||
                              _rules.DimensionPrefixes.IndexOf(char.ToUpperInvariant(remaining[start - 1])) >= 0))
            {
                start--;
                length++;
            }

            for (int c = start; c < start + length && c < rest.Length; c++) rest[c] = ' ';
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
