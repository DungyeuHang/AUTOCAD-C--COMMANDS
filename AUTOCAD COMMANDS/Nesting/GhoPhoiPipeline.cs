using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AUTOCAD_COMMANDS.Nesting.Core;
using AUTOCAD_COMMANDS.Nesting.Recognition;

namespace AUTOCAD_COMMANDS.Nesting
{
    // ==========================================================================================
    // GHOPHOI - PHAN CHUNG GIUA LENH CO HOP THOAI VA BAN CHAY HANG LOAT
    // ------------------------------------------------------------------------------------------
    // Hai ham nay truoc day nam trong GhoPhoiCommand. Chung khong dung den hop thoai nao, nhung
    // GhoPhoiCommand thi co - ma accoreconsole (AutoCAD khong giao dien) lai khong nap duoc
    // tang giao dien. Tach ra day de ban chay hang loat tren ban ve THAT goi duoc DUNG code
    // san xuat, thay vi phai chep lai mot ban thu hai roi lech nhau dan.
    //
    // NOI DUNG HAI HAM GIU NGUYEN TUNG DONG.
    // ==========================================================================================
    internal static class GhoPhoiPipeline
    {
        /// <summary>
        /// Gan TEN DON HANG cho tung ban ghi nhan dang, lay tu luot quet da chon duong bao
        /// cua no.
        ///
        /// Khong doc tu chu, khong doc tu layer, khong suy tu hinh hoc: don hang la thu nguoi
        /// dung khai o bang don, va chi di theo duong nguoi dung da quet.
        ///
        /// Neu mot chi tiet co duong bao ghep tu nhieu luot quet KHAC don nhau thi khong tu y
        /// chon bua mot cai - danh dau MO HO de nguoi dung tu quyet o bang kiem tra.
        /// </summary>
        /// <returns>So ban ghi bi vat sang mo ho vi dinh vao hai don.</returns>
        public static int AssignOrders(NestReadResult read, RecognitionResult recognition)
        {
            int conflicts = 0;
            foreach (RecognizedPart part in recognition.Parts)
            {
                List<int> sources = part.Outer != null ? part.Outer.Sources : part.GeometrySources;
                List<string> orders = new List<string>();
                foreach (int src in sources)
                {
                    if (src < 0 || src >= read.Sources.Count) continue;
                    string order = read.Sources[src].Order;
                    if (string.IsNullOrEmpty(order) || orders.Contains(order)) continue;
                    orders.Add(order);
                }

                if (orders.Count == 0) continue;

                orders.Sort(StringComparer.Ordinal);
                part.Order = orders[0];
                if (orders.Count == 1) continue;

                conflicts++;
                part.Escalate(PartStatus.Ambiguous, string.Format(CultureInfo.InvariantCulture,
                    "Duong bao ghep tu nhieu don ({0}) - chon lai don cho chi tiet nay.",
                    string.Join(", ", orders.ToArray())));
            }

            return conflicts;
        }

        /// <summary>
        /// Attaches every engraving TEXT / MTEXT to the part that contains it, and reports the
        /// ones that belong to no part instead of dropping them quietly.
        ///
        /// Shared by the interactive command and the batch runner on purpose: the batch runner
        /// exists to exercise the production path, which it cannot do if it owns a second copy
        /// of this step.
        /// </summary>
        public static int AttachEngravings(
            NestReadResult read, RecognitionResult recognition, GhoPhoiSettings settings)
        {
            int unattached = 0;
            foreach (KeyValuePair<int, Pt> engraving in read.Engravings)
            {
                if (!PartRecognizer.AttachEngraving(recognition.Parts, engraving.Key, engraving.Value))
                {
                    unattached++;
                }
            }

            if (unattached > 0)
            {
                recognition.GlobalWarnings.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0} chu tren layer khac ({1}) khong nam trong chi tiet nao - KHONG duoc khac.",
                    unattached, string.Join(", ", settings.EngravingLayers.ToArray())));
            }

            return unattached;
        }

        internal static List<OutputPart> BuildOutputParts(List<PartGroup> groups, NestReadResult read)
        {
            List<OutputPart> outputs = new List<OutputPart>();
            foreach (PartGroup g in groups)
            {
                RecognizedPart r = (RecognizedPart)g.SourceReference;
                Pt o = PartRecognizer.LocalOrigin(r);
                OutputPart op = new OutputPart { Group = g, Origin = new Point3d(o.X, o.Y, 0) };
                HashSet<ObjectId> seen = new HashSet<ObjectId>();
                foreach (int s in r.GeometrySources)
                {
                    ObjectId id = read.Sources[s].Id;
                    if (seen.Add(id)) op.SourceIds.Add(id);
                }

                outputs.Add(op);
            }

            return outputs;
        }

        internal static string BuildReport(NestingRequest request, NestingResult result)
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            StringBuilder sb = new StringBuilder();
            NestingStatistics st = result.Statistics;
            Dictionary<string, PartGroup> groups = new Dictionary<string, PartGroup>(StringComparer.Ordinal);
            foreach (PartGroup g in request.Groups) groups[g.Id] = g;

            sb.AppendLine("==================================================");
            sb.AppendLine(" GHOPHOI - KET QUA GHEP PHOI (V1)");
            sb.AppendLine("==================================================");
            sb.AppendLine(string.Format(ci, " Nhom chi tiet     : {0}", st.PartGroupCount));
            sb.AppendLine(string.Format(ci, " Tong SL yeu cau   : {0}", st.RequestedQuantity));
            sb.AppendLine(string.Format(ci, " Da xep            : {0}", st.PlacedQuantity));
            sb.AppendLine(string.Format(ci, " Chua xep          : {0}", st.UnplacedQuantity));
            sb.AppendLine(string.Format(ci, " So to phoi        : {0}", st.SheetCount));
            sb.AppendLine(string.Format(ci, " Khe cat / le mep  : {0:0.##} / {1:0.##} mm   (lat guong: {2})",
                request.Settings.GapMm, request.Settings.EdgeMarginMm, request.Settings.AllowMirror ? "CO" : "KHONG"));
            sb.AppendLine(" Chi tiet trong lo kin: " + (request.Settings.AllowPartInsideHole ? "CHO PHEP" : "KHONG (hoc lom ho van duoc phep)"));
            sb.AppendLine(string.Format(ci, " Thoi gian         : {0:0.0} s{1}", st.ElapsedSeconds, result.Cancelled ? "   (NGUOI DUNG DA DUNG SOM)" : string.Empty));

            foreach (MaterialStatistics m in st.Materials)
            {
                sb.AppendLine();
                sb.AppendLine(string.Format(ci, " [{0}]  kho {1}", m.Material, m.SheetName ?? "-"));
                sb.AppendLine(string.Format(ci, "   So to {0} | xep {1}/{2} | dai da dung {3:0} mm | su dung {4:0.0}%",
                    m.SheetCount, m.Placed, m.Requested, m.UsedLengthMm, m.Utilization * 100));
                sb.AppendLine(string.Format(ci, "   Phe lieu uoc tinh {0:0.###} m2 | phan du (dai cuoi to) {1:0.###} m2",
                    m.WasteAreaMm2 / 1e6, m.RemnantAreaMm2 / 1e6));
                // Bao CA HAI con so: khoi luong viec da dinh (khong phu thuoc may) va thoi
                // gian thuc te (phu thuoc may). Hai cai bang nhau tuc la da tim du.
                sb.AppendLine(string.Format(ci, "   Da thu {0}/{1} thu tu xep trong {2:0.0}s, tot nhat: {3}",
                    m.OrderingsTried, m.OrderingsPlanned, m.ElapsedSeconds, m.BestOrdering ?? "-"));

                // Het gio thi mot so thu tu xep KHONG duoc chay, nen ket qua chua chac la cai
                // tot nhat may co the tim ra. Phai noi ro hau qua, chu ghi moi chu "het thoi
                // gian cho phep" o cuoi dong thi nguoi dung khong biet no anh huong den cai gi.
                if (m.TimeBudgetHit)
                {
                    sb.AppendLine("   (!) HET THOI GIAN CHO PHEP - con thu tu xep chua duoc chay.");
                    sb.AppendLine("       Ket qua van dung va an toan, nhung co the chua phai cach xep tiet kiem nhat,");
                    sb.AppendLine("       va may khac toc do co the ra bo cuc khac.");
                    sb.AppendLine(string.Format(ci,
                        "       Cach xu ly: bat 'Tim du so luot xep' trong bang cai dat, hoac tang 'Thoi gian toi da' (dang de {0:0} giay).",
                        request.Settings.TimeBudgetSeconds));
                }

                foreach (SheetResult s in result.Sheets)
                {
                    if (s.Material != m.Material) continue;
                    sb.AppendLine(string.Format(ci, "   - To {0}: {1} chi tiet, dai dung {2:0} mm, su dung {3:0.0}% ({4:0.0}% ca to), phan du {5:0} mm",
                        s.NumberInMaterial, s.Placements.Count, s.UsedLengthMm, s.Utilization * 100, s.SheetUtilization * 100, s.RemnantLengthMm));

                    // Ten don ghi DAY DU o day, khong rut gon: nhan ve tren to co the phai
                    // cat bot cho vua, con ban bao cao thi khong duoc thieu ten don nao.
                    if (s.Orders.Count > 0)
                    {
                        sb.AppendLine("     Don: " + string.Join(", ", s.Orders.ToArray()));
                    }
                }
            }

            sb.AppendLine();
            sb.AppendLine(" SO LUONG THEO CHI TIET (yeu cau = da xep + chua xep):");
            Dictionary<string, int> placed = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (Placement p in result.Placements)
            {
                int n;
                placed.TryGetValue(p.PartGroupId, out n);
                placed[p.PartGroupId] = n + 1;
            }

            Dictionary<string, List<string>> reasons = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            Dictionary<string, int> unplaced = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (UnplacedPart u in result.Unplaced)
            {
                int n;
                unplaced.TryGetValue(u.PartGroupId, out n);
                unplaced[u.PartGroupId] = n + 1;
                List<string> list;
                if (!reasons.TryGetValue(u.PartGroupId, out list))
                {
                    list = new List<string>();
                    reasons[u.PartGroupId] = list;
                }

                if (!list.Contains(u.Reason)) list.Add(u.Reason);
            }

            foreach (PartGroup g in request.Groups)
            {
                int p, u;
                placed.TryGetValue(g.Id, out p);
                unplaced.TryGetValue(g.Id, out u);
                // Ten don di kem tung dong: chay nhieu don thi cau hoi dau tien khi thay mot
                // dong "chua xep" luon la "cua don nao?". Chay mot don thi cot nay rong.
                string order = string.IsNullOrEmpty(g.Order) ? string.Empty : "  [" + g.Order + "]";
                sb.AppendLine(string.Format(ci, "   {0,-20} {1,-8} yeu cau {2,4} | xep {3,4} | chua xep {4,4}{5}",
                    g.Name, g.Material, g.Quantity, p, u, order));
                List<string> why;
                if (reasons.TryGetValue(g.Id, out why))
                {
                    foreach (string w in why) sb.AppendLine("        ly do: " + w);
                }
            }

            sb.AppendLine();
            ValidationResult v = result.Validation;
            if (v != null && v.IsValid)
            {
                sb.AppendLine(" VALIDATOR: DAT");
                if (!double.IsNaN(v.MinPartDistanceMm)) sb.AppendLine(string.Format(ci, "   Khe nho nhat do duoc : {0:0.###} mm", v.MinPartDistanceMm));
                if (!double.IsNaN(v.MinEdgeDistanceMm)) sb.AppendLine(string.Format(ci, "   Cach mep nho nhat    : {0:0.###} mm", v.MinEdgeDistanceMm));
            }
            else
            {
                sb.AppendLine(" VALIDATOR: KHONG DAT - KHONG DUOC DUNG KET QUA NAY DE CAT!");
                if (v != null)
                {
                    int shown = 0;
                    foreach (ValidationIssue i in v.Issues)
                    {
                        if (shown++ >= 30)
                        {
                            sb.AppendLine("   ... va " + (v.Issues.Count - 30).ToString(ci) + " loi khac");
                            break;
                        }

                        sb.AppendLine("   (X) " + i);
                    }
                }
            }

            foreach (string w in result.Warnings) sb.AppendLine(" (!) " + w);
            sb.AppendLine("==================================================");
            return sb.ToString();
        }
    }
}
