using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace AUTOCAD_COMMANDS.Nesting.SelfTests
{
    // ==========================================================================================
    // GHOPHOI self-test harness (no AutoCAD reference) - same convention as FoilSelfTests:
    //   1. inside AutoCAD via GHOPHOI_TEST,
    //   2. outside AutoCAD via the NestingCore.Tests console project.
    // ==========================================================================================

    public sealed class NestingTestReport
    {
        public int Passed { get; set; }

        public int Failed { get; set; }

        public List<string> Lines { get; private set; } = new List<string>();

        public int Total { get { return Passed + Failed; } }

        public bool AllPassed { get { return Failed == 0; } }
    }

    public sealed class NestingAssertException : Exception
    {
        public NestingAssertException(string message) : base(message)
        {
        }
    }

    internal static class NestingTestHarness
    {
        public static void Run(NestingTestReport report, string name, Action test)
        {
            Stopwatch watch = Stopwatch.StartNew();
            try
            {
                test();
                report.Passed++;
                report.Lines.Add(string.Format(CultureInfo.InvariantCulture, "  PASS  {0}  ({1} ms)", name, watch.ElapsedMilliseconds));
            }
            catch (NestingAssertException ex)
            {
                report.Failed++;
                report.Lines.Add("  FAIL  " + name + " -> " + ex.Message);
            }
            catch (Exception ex)
            {
                report.Failed++;
                report.Lines.Add("  FAIL  " + name + " -> " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        public static void Summary(NestingTestReport report)
        {
            report.Lines.Add(string.Empty);
            report.Lines.Add("==================================================");
            report.Lines.Add(string.Format(CultureInfo.InvariantCulture,
                " KET QUA: {0} PASS / {1} FAIL  (tong {2})", report.Passed, report.Failed, report.Total));
            report.Lines.Add("==================================================");
        }

        public static void True(bool condition, string message)
        {
            if (!condition) throw new NestingAssertException(message);
        }

        public static void Equal<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new NestingAssertException(string.Format(CultureInfo.InvariantCulture,
                    "{0}: expected {1}, actual {2}", message, expected, actual));
            }
        }

        public static void Close(double expected, double actual, double tol, string message)
        {
            if (double.IsNaN(actual) || Math.Abs(expected - actual) > tol)
            {
                throw new NestingAssertException(string.Format(CultureInfo.InvariantCulture,
                    "{0}: expected {1:0.####}, actual {2:0.####}", message, expected, actual));
            }
        }
    }
}
