using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using AUTOCAD_COMMANDS.Nesting.Core;

namespace AUTOCAD_COMMANDS.Nesting
{
    /// <summary>
    /// Persists GHOPHOI settings and the company sheet catalog as TSV files in
    /// %AppData%\DUNGX\AUTOCAD_COMMANDS (same mechanism as FoilSettingsStore / AutoCutSettingsStore).
    ///   ghophoi_settings.tsv   key \t value
    ///   ghophoi_sheets.tsv     Name \t Width(Y, mm) \t Length(X, mm) \t Materials (comma separated, empty = all)
    /// The sheet catalog is the single place where approved sheet sizes live; edit it from the
    /// settings dialog or directly in the file.
    /// </summary>
    internal static class GhoPhoiSettingsStore
    {
        private static readonly object SyncRoot = new object();

        public static string Folder
        {
            get
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (string.IsNullOrWhiteSpace(appData))
                {
                    appData = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
                }

                return Path.Combine(appData, "DUNGX", "AUTOCAD_COMMANDS");
            }
        }

        public static string SettingsPath { get { return Path.Combine(Folder, "ghophoi_settings.tsv"); } }

        public static string CatalogPath { get { return Path.Combine(Folder, "ghophoi_sheets.tsv"); } }

        public static string FixtureFolder { get { return Path.Combine(Folder, "ghophoi_fixtures"); } }

        /// <summary>
        /// Written to ghophoi_sheets.tsv on first use only, as an editable starting point.
        /// ASSUMPTION: these are the example sizes from the specification, not verified company values.
        /// </summary>
        private static readonly string[] SeedCatalog =
        {
            "1250x2500\t1250\t2500\t",
            "1500x3000\t1500\t3000\t",
            "1500x6000\t1500\t6000\t"
        };

        public static GhoPhoiSettings Load()
        {
            lock (SyncRoot)
            {
                GhoPhoiSettings s = new GhoPhoiSettings();
                if (!File.Exists(SettingsPath)) return s;

                try
                {
                    foreach (string raw in File.ReadAllLines(SettingsPath, Encoding.UTF8))
                    {
                        string[] parts = raw.Split(new[] { '\t' }, 2);
                        if (parts.Length < 2) continue;
                        Apply(s, parts[0].Trim(), parts[1].Trim());
                    }
                }
                catch
                {
                    // A corrupt settings file must never block the command - fall back to defaults.
                    return new GhoPhoiSettings();
                }

                return s;
            }
        }

        private static void Apply(GhoPhoiSettings s, string key, string val)
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            double d;
            int n;
            bool b;
            switch (key)
            {
                case "GapMm": if (TryD(val, out d) && d >= 0) s.GapMm = d; break;
                case "EdgeMarginMm": if (TryD(val, out d) && d >= 0) s.EdgeMarginMm = d; break;
                case "AllowMirror": if (bool.TryParse(val, out b)) s.AllowMirror = b; break;
                case "AllowPartInsideHole": if (bool.TryParse(val, out b)) s.AllowPartInsideHole = b; break;
                case "RotationMode":
                    GhoPhoiRotationMode mode;
                    if (Enum.TryParse(val, out mode)) s.RotationMode = mode;
                    break;
                case "TimeBudgetSeconds": if (TryD(val, out d) && d > 0) s.TimeBudgetSeconds = d; break;
                case "Seed": if (int.TryParse(val, NumberStyles.Integer, ci, out n)) s.Seed = n; break;
                case "ExtraSeededOrderings": if (int.TryParse(val, NumberStyles.Integer, ci, out n) && n >= 0) s.ExtraSeededOrderings = n; break;
                case "ArcToleranceMm": if (TryD(val, out d) && d > 0) s.ArcToleranceMm = d; break;
                case "JoinToleranceMm": if (TryD(val, out d) && d > 0) s.JoinToleranceMm = d; break;
                case "MaxTextDistanceMm": if (TryD(val, out d) && d > 0) s.MaxTextDistanceMm = d; break;
                case "DefaultMaterial": if (val.Length > 0) s.DefaultMaterial = val; break;
                case "DefaultQuantity": if (int.TryParse(val, NumberStyles.Integer, ci, out n) && n >= 1) s.DefaultQuantity = n; break;
                case "MarkingLayers": s.MarkingLayers = SplitList(val); break;
                case "DefaultSheetName": s.DefaultSheetName = val; break;
                case "MaterialSheets":
                    s.MaterialSheets.Clear();
                    foreach (string pair in SplitList(val))
                    {
                        int eq = pair.IndexOf('=');
                        if (eq > 0) s.MaterialSheets[pair.Substring(0, eq)] = pair.Substring(eq + 1);
                    }

                    break;
                case "OutputAsBlocks": if (bool.TryParse(val, out b)) s.OutputAsBlocks = b; break;
                case "LabelParts": if (bool.TryParse(val, out b)) s.LabelParts = b; break;
                case "SheetSpacingMm": if (TryD(val, out d) && d >= 0) s.SheetSpacingMm = d; break;
                case "OpenOutputDrawing": if (bool.TryParse(val, out b)) s.OpenOutputDrawing = b; break;
                case "SaveFixture": if (bool.TryParse(val, out b)) s.SaveFixture = b; break;
                case "AutoZoomInReview": if (bool.TryParse(val, out b)) s.AutoZoomInReview = b; break;
            }
        }

        public static void Save(GhoPhoiSettings s)
        {
            lock (SyncRoot)
            {
                CultureInfo ci = CultureInfo.InvariantCulture;
                List<string> pairs = new List<string>();
                foreach (KeyValuePair<string, string> kv in s.MaterialSheets) pairs.Add(kv.Key + "=" + kv.Value);

                string[] lines =
                {
                    "GapMm\t" + s.GapMm.ToString("R", ci),
                    "EdgeMarginMm\t" + s.EdgeMarginMm.ToString("R", ci),
                    "AllowMirror\t" + s.AllowMirror,
                    "AllowPartInsideHole\t" + s.AllowPartInsideHole,
                    "RotationMode\t" + s.RotationMode,
                    "TimeBudgetSeconds\t" + s.TimeBudgetSeconds.ToString("R", ci),
                    "Seed\t" + s.Seed.ToString(ci),
                    "ExtraSeededOrderings\t" + s.ExtraSeededOrderings.ToString(ci),
                    "ArcToleranceMm\t" + s.ArcToleranceMm.ToString("R", ci),
                    "JoinToleranceMm\t" + s.JoinToleranceMm.ToString("R", ci),
                    "MaxTextDistanceMm\t" + s.MaxTextDistanceMm.ToString("R", ci),
                    "DefaultMaterial\t" + s.DefaultMaterial,
                    "DefaultQuantity\t" + s.DefaultQuantity.ToString(ci),
                    "MarkingLayers\t" + string.Join(";", s.MarkingLayers.ToArray()),
                    "DefaultSheetName\t" + s.DefaultSheetName,
                    "MaterialSheets\t" + string.Join(";", pairs.ToArray()),
                    "OutputAsBlocks\t" + s.OutputAsBlocks,
                    "LabelParts\t" + s.LabelParts,
                    "SheetSpacingMm\t" + s.SheetSpacingMm.ToString("R", ci),
                    "OpenOutputDrawing\t" + s.OpenOutputDrawing,
                    "SaveFixture\t" + s.SaveFixture,
                    "AutoZoomInReview\t" + s.AutoZoomInReview
                };

                try
                {
                    Directory.CreateDirectory(Folder);
                    File.WriteAllLines(SettingsPath, lines, Encoding.UTF8);
                }
                catch
                {
                    // Saving preferences is best effort.
                }
            }
        }

        public static List<SheetSpec> LoadCatalog(out string error)
        {
            error = null;
            List<SheetSpec> sheets = new List<SheetSpec>();
            lock (SyncRoot)
            {
                try
                {
                    if (!File.Exists(CatalogPath))
                    {
                        Directory.CreateDirectory(Folder);
                        List<string> seed = new List<string>(Header());
                        seed.AddRange(SeedCatalog);
                        File.WriteAllLines(CatalogPath, seed, Encoding.UTF8);
                    }

                    int lineNo = 0;
                    foreach (string raw in File.ReadAllLines(CatalogPath, Encoding.UTF8))
                    {
                        lineNo++;
                        string line = raw.Trim();
                        if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;

                        string[] parts = raw.Split('\t');
                        double w, l;
                        if (parts.Length < 3 || !TryD(parts[1], out w) || !TryD(parts[2], out l) || w <= 0 || l <= 0)
                        {
                            error = string.Format(CultureInfo.InvariantCulture,
                                "Dong {0} trong {1} khong hop le (can: Ten<TAB>Rong<TAB>Dai<TAB>VatLieu).", lineNo, CatalogPath);
                            continue;
                        }

                        SheetSpec sheet = new SheetSpec(parts[0].Trim(), Math.Max(w, l), Math.Min(w, l));
                        if (parts.Length > 3) sheet.Materials.AddRange(SplitList(parts[3].Replace(',', ';')));
                        sheets.Add(sheet);
                    }
                }
                catch (Exception ex)
                {
                    error = "Khong doc duoc danh muc kho phoi: " + ex.Message;
                }
            }

            return sheets;
        }

        public static void SaveCatalog(IEnumerable<SheetSpec> sheets)
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            List<string> lines = new List<string>(Header());
            foreach (SheetSpec s in sheets)
            {
                lines.Add(string.Format(ci, "{0}\t{1:0.###}\t{2:0.###}\t{3}",
                    s.Name, s.WidthMm, s.LengthMm, string.Join(",", s.Materials.ToArray())));
            }

            lock (SyncRoot)
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllLines(CatalogPath, lines, Encoding.UTF8);
            }
        }

        private static IEnumerable<string> Header()
        {
            yield return "# GHOPHOI - danh muc kho phoi duoc phep dung";
            yield return "# Ten<TAB>Rong (mm)<TAB>Dai (mm)<TAB>Vat lieu tuong thich (cach nhau dau phay, bo trong = tat ca)";
        }

        private static bool TryD(string s, out double d)
        {
            return double.TryParse((s ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d);
        }

        private static List<string> SplitList(string val)
        {
            List<string> r = new List<string>();
            foreach (string p in (val ?? string.Empty).Split(';'))
            {
                if (p.Trim().Length > 0) r.Add(p.Trim());
            }

            return r;
        }
    }
}
