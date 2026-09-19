using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace AUTOCAD_COMMANDS
{
    // ==========================================================================================
    // FOIL - BANG CHAN (BEND TABLE)
    // ------------------------------------------------------------------------------------------
    // Dinh dang TSV, mot dong mot hang, dong bat dau bang '#' la ghi chu:
    //
    //   Thickness <TAB> Radius <TAB> AngleDeg <TAB> Deduction [<TAB> Allowance]
    //   1.0       <TAB> 1.0    <TAB> 90       <TAB> 1.75
    //   1.2       <TAB> 1.2    <TAB> 90       <TAB> 2.10
    //
    // Cot Allowance la tuy chon. Neu thieu, coi BA = 0 (duong chan la mot vach duy nhat,
    // khong co vung chan co be rong) va OSSB = BD / 2.
    //
    // Muc tieu: tool KHONG chi chay bang cong thuc ly thuyet ma calibrate duoc theo may chan
    // that cua xuong. Xem them FoilCalibration.
    // ==========================================================================================

    public class FoilBendTableEntry
    {
        public double Thickness { get; set; }

        public double Radius { get; set; }

        public double AngleDeg { get; set; }

        public double BendDeduction { get; set; }

        public double BendAllowance { get; set; }

        public string SourceNote { get; set; } = string.Empty;
    }

    public class FoilBendTable
    {
        private readonly List<FoilBendTableEntry> _entries = new List<FoilBendTableEntry>();

        /// <summary>Dung sai khi khop chieu day / ban kinh.</summary>
        public double MatchTolerance { get; set; } = 0.05;

        public IList<FoilBendTableEntry> Entries { get { return _entries; } }

        public int Count { get { return _entries.Count; } }

        public string SourcePath { get; set; } = string.Empty;

        public void Add(FoilBendTableEntry entry)
        {
            if (entry != null)
            {
                _entries.Add(entry);
            }
        }

        /// <summary>
        /// Tim hang phu hop. Khop T va R theo dung sai; voi goc thi NOI SUY TUYEN TINH giua
        /// hai hang gan nhat de khong buoc xuong phai lap bang cho moi goc.
        /// </summary>
        public FoilBendTableEntry Lookup(double thickness, double radius, double angleDeg)
        {
            List<FoilBendTableEntry> candidates = new List<FoilBendTableEntry>();
            for (int i = 0; i < _entries.Count; i++)
            {
                FoilBendTableEntry e = _entries[i];
                if (Math.Abs(e.Thickness - thickness) <= MatchTolerance &&
                    Math.Abs(e.Radius - radius) <= MatchTolerance)
                {
                    candidates.Add(e);
                }
            }

            if (candidates.Count == 0)
            {
                return null;
            }

            // Khop chinh xac goc truoc.
            for (int i = 0; i < candidates.Count; i++)
            {
                if (Math.Abs(candidates[i].AngleDeg - angleDeg) <= 1e-6)
                {
                    return candidates[i];
                }
            }

            if (candidates.Count == 1)
            {
                FoilBendTableEntry only = candidates[0];
                return new FoilBendTableEntry
                {
                    Thickness = thickness,
                    Radius = radius,
                    AngleDeg = angleDeg,
                    BendDeduction = only.BendDeduction,
                    BendAllowance = only.BendAllowance,
                    SourceNote = string.Format(
                        CultureInfo.InvariantCulture,
                        "Bang chan: dung hang {0:0.##} do cho goc {1:0.##} do (chi co 1 hang).",
                        only.AngleDeg,
                        angleDeg)
                };
            }

            candidates.Sort(delegate (FoilBendTableEntry a, FoilBendTableEntry b)
            {
                return a.AngleDeg.CompareTo(b.AngleDeg);
            });

            // Ngoai khoang: kep ve hang bien gan nhat.
            if (angleDeg <= candidates[0].AngleDeg)
            {
                return Clamped(candidates[0], thickness, radius, angleDeg);
            }

            if (angleDeg >= candidates[candidates.Count - 1].AngleDeg)
            {
                return Clamped(candidates[candidates.Count - 1], thickness, radius, angleDeg);
            }

            for (int i = 0; i < candidates.Count - 1; i++)
            {
                FoilBendTableEntry lo = candidates[i];
                FoilBendTableEntry hi = candidates[i + 1];
                if (angleDeg >= lo.AngleDeg && angleDeg <= hi.AngleDeg)
                {
                    double span = hi.AngleDeg - lo.AngleDeg;
                    double f = span <= 1e-9 ? 0.0 : (angleDeg - lo.AngleDeg) / span;
                    return new FoilBendTableEntry
                    {
                        Thickness = thickness,
                        Radius = radius,
                        AngleDeg = angleDeg,
                        BendDeduction = lo.BendDeduction + f * (hi.BendDeduction - lo.BendDeduction),
                        BendAllowance = lo.BendAllowance + f * (hi.BendAllowance - lo.BendAllowance),
                        SourceNote = string.Format(
                            CultureInfo.InvariantCulture,
                            "Bang chan: noi suy giua {0:0.##} do va {1:0.##} do.",
                            lo.AngleDeg,
                            hi.AngleDeg)
                    };
                }
            }

            return candidates[0];
        }

        private static FoilBendTableEntry Clamped(
            FoilBendTableEntry source, double thickness, double radius, double angleDeg)
        {
            return new FoilBendTableEntry
            {
                Thickness = thickness,
                Radius = radius,
                AngleDeg = angleDeg,
                BendDeduction = source.BendDeduction,
                BendAllowance = source.BendAllowance,
                SourceNote = string.Format(
                    CultureInfo.InvariantCulture,
                    "Bang chan: goc {0:0.##} do nam ngoai bang, dung hang bien {1:0.##} do.",
                    angleDeg,
                    source.AngleDeg)
            };
        }

        public static FoilBendTable LoadFromFile(string path, out string error)
        {
            error = null;
            FoilBendTable table = new FoilBendTable { SourcePath = path ?? string.Empty };

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                error = "Khong tim thay file bang chan: " + (path ?? "(rong)");
                return null;
            }

            try
            {
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                int lineNo = 0;
                foreach (string raw in lines)
                {
                    lineNo++;
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        continue;
                    }

                    string line = raw.Trim();
                    if (line.StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string[] parts = line.Split(new[] { '\t', ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 4)
                    {
                        continue;
                    }

                    double t, r, a, bd;
                    if (!TryParse(parts[0], out t) ||
                        !TryParse(parts[1], out r) ||
                        !TryParse(parts[2], out a) ||
                        !TryParse(parts[3], out bd))
                    {
                        continue;
                    }

                    double ba = 0.0;
                    if (parts.Length >= 5)
                    {
                        TryParse(parts[4], out ba);
                    }

                    table.Add(new FoilBendTableEntry
                    {
                        Thickness = t,
                        Radius = r,
                        AngleDeg = a,
                        BendDeduction = bd,
                        BendAllowance = ba,
                        SourceNote = string.Format(
                            CultureInfo.InvariantCulture,
                            "Bang chan dong {0}: T={1:0.###} R={2:0.###} {3:0.##} do -> BD={4:0.####}",
                            lineNo, t, r, a, bd)
                    });
                }
            }
            catch (Exception ex)
            {
                error = "Loi doc bang chan: " + ex.Message;
                return null;
            }

            if (table.Count == 0)
            {
                error = "Bang chan khong co dong du lieu hop le: " + path;
                return null;
            }

            return table;
        }

        public bool SaveToFile(string path, out string error)
        {
            error = null;
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# DX_FOIL - BANG CHAN (BEND TABLE)");
                sb.AppendLine("# Thickness\tRadius\tAngleDeg\tDeduction\tAllowance");
                foreach (FoilBendTableEntry e in _entries)
                {
                    sb.AppendLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}\t{1}\t{2}\t{3}\t{4}",
                        e.Thickness, e.Radius, e.AngleDeg, e.BendDeduction, e.BendAllowance));
                }

                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                SourcePath = path;
                return true;
            }
            catch (Exception ex)
            {
                error = "Loi ghi bang chan: " + ex.Message;
                return false;
            }
        }

        private static bool TryParse(string text, out double value)
        {
            return double.TryParse(
                text.Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value);
        }
    }
}
