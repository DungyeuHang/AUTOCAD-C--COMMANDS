using System;
using System.IO;
using AUTOCAD_COMMANDS.Nesting.SelfTests;

namespace NestingCore.Tests
{
    /// <summary>
    /// Runs the GHOPHOI core/recognition self-tests and replays fixture files WITHOUT AutoCAD.
    ///   NestingCore.Tests.exe [fixtureFolder]
    /// Default fixture folder: "Fixtures" next to the executable, then ..\..\Fixtures (project dir).
    /// Exit code 0 = all passed.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            string fixtures = args.Length > 0 ? args[0] : FindFixtureFolder();
            NestingTestReport report = NestingSelfTests.Run(fixtures);
            foreach (string line in report.Lines)
            {
                Console.WriteLine(line);
            }

            return report.AllPassed ? 0 : 1;
        }

        private static string FindFixtureFolder()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates =
            {
                Path.Combine(baseDir, "Fixtures"),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "Fixtures")),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Fixtures"))
            };

            foreach (string c in candidates)
            {
                if (Directory.Exists(c)) return c;
            }

            return null;
        }
    }
}
