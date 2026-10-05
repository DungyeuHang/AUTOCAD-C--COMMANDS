using System;
using System.Collections.Generic;

namespace AUTOCAD_COMMANDS.Nesting.Core
{
    /// <summary>
    /// So sanh HAI ket qua ghep CA LENH (che do "so sanh thuat toan"). Cung thu tu uu tien voi
    /// bo xep hang trong luc ghep (<see cref="LexicographicSolutionEvaluator"/>), cong them dieu
    /// kien dau tien la qua validator:
    ///   1. qua validator,
    ///   2. it chi tiet chua xep hon,
    ///   3. it to hon,
    ///   4. tong chieu dai da dung ngan hon (chenh duoi 1 mm coi nhu bang).
    /// </summary>
    public static class NestingResultComparer
    {
        /// <summary>Am khi <paramref name="a"/> tot hon, duong khi <paramref name="b"/> tot hon, 0 khi ngang nhau.</summary>
        public static int Compare(NestingResult a, NestingResult b)
        {
            if (a == null) return b == null ? 0 : 1;
            if (b == null) return -1;

            bool va = a.Validation != null && a.Validation.IsValid, vb = b.Validation != null && b.Validation.IsValid;
            if (va != vb) return va ? -1 : 1;

            int c = a.Statistics.UnplacedQuantity.CompareTo(b.Statistics.UnplacedQuantity);
            if (c != 0) return c;

            c = a.Statistics.SheetCount.CompareTo(b.Statistics.SheetCount);
            if (c != 0) return c;

            double la = TotalUsedLengthMm(a), lb = TotalUsedLengthMm(b);
            if (Math.Abs(la - lb) > 1.0) return la.CompareTo(lb);
            return 0;
        }

        public static double TotalUsedLengthMm(NestingResult r)
        {
            double sum = 0;
            foreach (SheetResult s in r.Sheets) sum += s.UsedLengthMm;
            return sum;
        }

        /// <summary>Ban sao yeu cau, chi doi thuat toan (nhom chi tiet / kho phoi dung chung, khong bi sua).</summary>
        public static NestingRequest WithAlgorithm(NestingRequest request, NestingAlgorithm algorithm)
        {
            NestingRequest copy = new NestingRequest { DefaultSheet = request.DefaultSheet };
            copy.Settings = (request.Settings ?? new NestingSettings()).Clone();
            copy.Settings.Algorithm = algorithm;
            copy.Groups.AddRange(request.Groups);
            foreach (KeyValuePair<string, SheetSpec> kv in request.SheetByMaterial) copy.SheetByMaterial[kv.Key] = kv.Value;
            return copy;
        }
    }
}
