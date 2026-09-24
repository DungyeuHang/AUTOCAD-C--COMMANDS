using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using AUTOCAD_COMMANDS.Nesting.Core;

namespace AUTOCAD_COMMANDS.Nesting.SelfTests
{
    /// <summary>Entry point for all AutoCAD-free GHOPHOI tests (core, recognition, fixtures).</summary>
    public static class NestingSelfTests
    {
        public static NestingTestReport Run(string fixtureFolder)
        {
            NestingTestReport report = new NestingTestReport();

            report.Lines.Add("---- NESTING CORE ----");
            NestingCoreSelfTests.Run(report);

            report.Lines.Add(string.Empty);
            report.Lines.Add("---- RECOGNITION (TEXT / CONTOUR) ----");
            RecognitionSelfTests.Run(report);

            report.Lines.Add(string.Empty);
            report.Lines.Add("---- FIXTURES ----");
            RunFixtures(report, fixtureFolder);

            NestingTestHarness.Summary(report);
            return report;
        }

        /// <summary>
        /// Replays every *.nest file: the result must validate, account for every quantity and
        /// meet the optional EXPECT lines (placed=, unplaced=, sheets=, sheets&lt;=).
        /// </summary>
        public static void RunFixtures(NestingTestReport report, string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                report.Lines.Add("  (khong co thu muc fixture)");
                return;
            }

            string[] files = Directory.GetFiles(folder, "*.nest");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            if (files.Length == 0) report.Lines.Add("  (thu muc fixture rong)");

            foreach (string file in files)
            {
                string path = file;
                NestingTestHarness.Run(report, "FX " + Path.GetFileName(path), () =>
                {
                    NestingFixture.Fixture fx = NestingFixture.Load(path);
                    NestingResult res = new SimpleNestingEngine().Nest(fx.Request, CancellationToken.None, null);

                    NestingTestHarness.True(res.Validation.IsValid,
                        "validator: " + (res.Validation.IsValid ? string.Empty : res.Validation.Issues[0].ToString()));

                    int requested = 0;
                    foreach (PartGroup g in fx.Request.Groups) requested += g.Quantity;
                    NestingTestHarness.Equal(requested, res.Statistics.PlacedQuantity + res.Statistics.UnplacedQuantity, "accounted");

                    double usedLength = 0;
                    foreach (SheetResult s in res.Sheets) usedLength += s.UsedLengthMm;

                    foreach (KeyValuePair<string, string> e in fx.Expectations)
                    {
                        double v = double.Parse(e.Value, CultureInfo.InvariantCulture);
                        double gap = res.Validation.MinPartDistanceMm, edge = res.Validation.MinEdgeDistanceMm;
                        switch (e.Key.ToLowerInvariant())
                        {
                            case "placed=": NestingTestHarness.Equal((int)v, res.Statistics.PlacedQuantity, "placed"); break;
                            case "unplaced=": NestingTestHarness.Equal((int)v, res.Statistics.UnplacedQuantity, "unplaced"); break;
                            case "sheets=": NestingTestHarness.Equal((int)v, res.Sheets.Count, "sheets"); break;
                            case "sheets<=": NestingTestHarness.True(res.Sheets.Count <= v, "sheets " + res.Sheets.Count + " > " + v); break;
                            case "usedlength<=": NestingTestHarness.True(usedLength <= v + 1e-6, "used length " + usedLength + " > " + v); break;
                            case "mingap=": NestingTestHarness.Close(v, gap, 1e-6, "measured min gap"); break;
                            case "mingap>=": NestingTestHarness.True(gap >= v - 1e-9, "min gap " + gap + " < " + v); break;
                            case "minedge=": NestingTestHarness.Close(v, edge, 1e-6, "measured min edge distance"); break;
                            case "minedge>=": NestingTestHarness.True(edge >= v - 1e-9, "min edge " + edge + " < " + v); break;
                            default: throw new NestingAssertException("EXPECT khong ho tro: " + e.Key);
                        }
                    }
                });
            }
        }
    }
}
