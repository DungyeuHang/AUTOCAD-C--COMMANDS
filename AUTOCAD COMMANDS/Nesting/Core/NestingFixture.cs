using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace AUTOCAD_COMMANDS.Nesting.Core
{
    /// <summary>
    /// Plain-text fixture format so that real drawings can be turned into reproducible,
    /// AutoCAD-free nesting test cases (GHOPHOI can export one; the test runner replays them).
    ///
    ///   # comment
    ///   SETTINGS  gap=5 margin=5 mirror=0 rotations=0,90,180,270 inhole=0 seed=1 budget=30 extra=3
    ///             (legacy tol=X = default part tolerance for PART lines without tol=)
    ///   SHEET     name=1500x3000 length=3000 width=1500 [material=1.2MM]
    ///   PART      id=P1 qty=12 material=1.2MM [tol=0.05] [name=...] [order=DON-A]
    ///             (tol = arc chord tolerance, mm; order = ten don hang, bo trong = khong theo don)
    ///   OUTER     x,y x,y x,y ...          (mm)
    ///   HOLE      x,y x,y x,y ...          (0..n per part)
    ///   END
    ///   EXPECT    placed=18 sheets<=2 [mixing=0]   (optional assertions for the runner)
    ///             mixing = tong so don PHAI tron them tren moi to (mot to mot don = 0)
    /// Values must not contain spaces (name is written with '_' instead of spaces).
    /// </summary>
    public static class NestingFixture
    {
        public sealed class Fixture
        {
            public Fixture()
            {
                Request = new NestingRequest();
                Expectations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            public NestingRequest Request { get; private set; }

            /// <summary>Keys like "placed", "unplaced", "sheets&lt;=" -> value.</summary>
            public Dictionary<string, string> Expectations { get; private set; }
        }

        public static void Save(string path, NestingRequest request, string comment)
        {
            File.WriteAllText(path, Write(request, comment), Encoding.UTF8);
        }

        public static string Write(NestingRequest request, string comment)
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# GHOPHOI nesting fixture v1");
            if (!string.IsNullOrEmpty(comment)) sb.AppendLine("# " + comment.Replace("\r", " ").Replace("\n", " "));

            NestingSettings s = request.Settings;
            List<string> rot = new List<string>();
            foreach (double r in s.AllowedRotations) rot.Add(r.ToString("0.###", ci));
            sb.AppendLine(string.Format(ci, "SETTINGS gap={0:0.###} margin={1:0.###} mirror={2} rotations={3} inhole={4} seed={5} budget={6:0.###} extra={7}",
                s.GapMm, s.EdgeMarginMm, s.AllowMirror ? 1 : 0, string.Join(",", rot.ToArray()),
                s.AllowPartInsideHole ? 1 : 0, s.Seed, s.TimeBudgetSeconds, s.ExtraSeededOrderings));

            if (request.DefaultSheet != null) sb.AppendLine(SheetLine(request.DefaultSheet, null));
            foreach (KeyValuePair<string, SheetSpec> kv in request.SheetByMaterial)
            {
                sb.AppendLine(SheetLine(kv.Value, kv.Key));
            }

            foreach (PartGroup g in request.Groups)
            {
                sb.AppendLine(string.Format(ci, "PART id={0} qty={1} material={2} tol={4:0.####} name={3}{5}",
                    Token(g.Id), g.Quantity, Token(g.Material), Token(g.Name), g.Shape.ToleranceMm,
                    string.IsNullOrEmpty(g.Order) ? string.Empty : " order=" + Token(g.Order)));
                sb.AppendLine("OUTER " + RingText(g.Shape.Polygon.Outer));
                foreach (IntPoint[] h in g.Shape.Polygon.Holes) sb.AppendLine("HOLE " + RingText(h));
                sb.AppendLine("END");
            }

            return sb.ToString();
        }

        public static Fixture Load(string path)
        {
            return Parse(File.ReadAllText(path, Encoding.UTF8));
        }

        public static Fixture Parse(string text)
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            Fixture fx = new Fixture();
            NestingRequest req = fx.Request;

            Dictionary<string, string> part = null;
            List<IntPoint> outer = null;
            List<List<IntPoint>> holes = null;
            int lineNo = 0;
            double defaultTol = 0.0;

            foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                lineNo++;
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;

                int sp = line.IndexOf(' ');
                string head = (sp < 0 ? line : line.Substring(0, sp)).ToUpperInvariant();
                string rest = sp < 0 ? string.Empty : line.Substring(sp + 1).Trim();

                switch (head)
                {
                    case "SETTINGS":
                        {
                            Dictionary<string, string> kv = KeyValues(rest);
                            NestingSettings s = req.Settings;
                            s.GapMm = Num(kv, "gap", s.GapMm);
                            s.EdgeMarginMm = Num(kv, "margin", s.EdgeMarginMm);
                            s.AllowMirror = Num(kv, "mirror", s.AllowMirror ? 1 : 0) != 0;
                            s.AllowPartInsideHole = Num(kv, "inhole", s.AllowPartInsideHole ? 1 : 0) != 0;
                            defaultTol = Num(kv, "tol", 0.0);   // legacy: default tolerance for PART lines without tol=
                            s.Seed = (int)Num(kv, "seed", s.Seed);
                            s.TimeBudgetSeconds = Num(kv, "budget", s.TimeBudgetSeconds);
                            s.ExtraSeededOrderings = (int)Num(kv, "extra", s.ExtraSeededOrderings);
                            string rot;
                            if (kv.TryGetValue("rotations", out rot))
                            {
                                s.AllowedRotations = new List<double>();
                                foreach (string r in rot.Split(','))
                                {
                                    if (r.Trim().Length > 0) s.AllowedRotations.Add(double.Parse(r, ci));
                                }
                            }

                            break;
                        }

                    case "SHEET":
                        {
                            Dictionary<string, string> kv = KeyValues(rest);
                            SheetSpec sheet = new SheetSpec(
                                kv.ContainsKey("name") ? kv["name"] : "SHEET",
                                Num(kv, "length", 0), Num(kv, "width", 0));
                            string material;
                            if (kv.TryGetValue("material", out material) && material.Length > 0)
                            {
                                req.SheetByMaterial[material] = sheet;
                            }
                            else
                            {
                                req.DefaultSheet = sheet;
                            }

                            break;
                        }

                    case "PART":
                        part = KeyValues(rest);
                        outer = null;
                        holes = new List<List<IntPoint>>();
                        break;

                    case "OUTER":
                        outer = ParseRing(rest, lineNo);
                        break;

                    case "HOLE":
                        if (holes == null) throw new FormatException("HOLE ngoai PART o dong " + lineNo);
                        holes.Add(ParseRing(rest, lineNo));
                        break;

                    case "END":
                        {
                            if (part == null || outer == null) throw new FormatException("END thieu PART/OUTER o dong " + lineNo);
                            List<IList<IntPoint>> hs = new List<IList<IntPoint>>();
                            foreach (List<IntPoint> h in holes) hs.Add(h);
                            PartGroup g = new PartGroup(
                                part["id"],
                                new PartShape(PolyShape.Create(outer, hs), Num(part, "tol", defaultTol)),
                                (int)Num(part, "qty", 1),
                                part.ContainsKey("material") ? part["material"] : "1.2MM",
                                part.ContainsKey("order") ? Untoken(part["order"]) : null);
                            if (part.ContainsKey("name")) g.Name = Untoken(part["name"]);
                            req.Groups.Add(g);
                            part = null;
                            break;
                        }

                    case "EXPECT":
                        foreach (string token in rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            int op = token.IndexOfAny(new[] { '=', '<', '>' });
                            if (op <= 0) continue;
                            int valueStart = op;
                            while (valueStart < token.Length && "=<>".IndexOf(token[valueStart]) >= 0) valueStart++;
                            fx.Expectations[token.Substring(0, valueStart)] = token.Substring(valueStart);
                        }

                        break;

                    default:
                        throw new FormatException("Tu khoa khong hop le '" + head + "' o dong " + lineNo);
                }
            }

            return fx;
        }

        private static string SheetLine(SheetSpec sheet, string material)
        {
            return string.Format(CultureInfo.InvariantCulture, "SHEET name={0} length={1:0.###} width={2:0.###}{3}",
                Token(sheet.Name), sheet.LengthMm, sheet.WidthMm,
                material != null ? " material=" + Token(material) : string.Empty);
        }

        /// <summary>
        /// Ma hoa mot gia tri de no khong lam vo cach tach dong (tach bang dau cach, cap
        /// khoa=gia tri).
        ///
        /// Truoc day dau cach bi doi thanh gach duoi, tuc la doc lai KHONG ra duoc ten cu:
        /// "DON ABC 001" thanh "DON_ABC_001". Ten don la du lieu cua nguoi dung, doi mot ky
        /// tu cung la sai. Gio ma hoa kieu phan tram nen doc lai duoc nguyen van.
        ///
        /// Fixture cu viet gach duoi van doc binh thuong - giai ma chi dong den cac chuoi co
        /// %20 / %3D / %25, ma nhung chuoi do thi truoc day khong sinh ra bao gio.
        /// </summary>
        private static string Token(string s)
        {
            if (string.IsNullOrEmpty(s)) return "-";
            return s.Replace("%", "%25").Replace(" ", "%20").Replace("=", "%3D");
        }

        /// <summary>Nguoc cua <see cref="Token"/>.</summary>
        private static string Untoken(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace("%3D", "=").Replace("%20", " ").Replace("%25", "%");
        }

        private static string RingText(IntPoint[] ring)
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            StringBuilder sb = new StringBuilder();
            foreach (IntPoint p in ring)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(NestUnits.ToMm(p.X).ToString("0.###", ci)).Append(',').Append(NestUnits.ToMm(p.Y).ToString("0.###", ci));
            }

            return sb.ToString();
        }

        private static List<IntPoint> ParseRing(string text, int lineNo)
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            List<IntPoint> pts = new List<IntPoint>();
            foreach (string token in text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] xy = token.Split(',');
                if (xy.Length != 2) throw new FormatException("Toa do khong hop le '" + token + "' o dong " + lineNo);
                pts.Add(IntPoint.FromMm(double.Parse(xy[0], ci), double.Parse(xy[1], ci)));
            }

            return pts;
        }

        private static Dictionary<string, string> KeyValues(string text)
        {
            Dictionary<string, string> kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string token in text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = token.IndexOf('=');
                if (eq <= 0) continue;
                kv[token.Substring(0, eq)] = token.Substring(eq + 1);
            }

            return kv;
        }

        private static double Num(Dictionary<string, string> kv, string key, double fallback)
        {
            string v;
            double d;
            if (kv.TryGetValue(key, out v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            return fallback;
        }
    }
}
