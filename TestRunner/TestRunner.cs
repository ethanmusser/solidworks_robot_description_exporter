using System;
using System.IO;
using System.Threading;
using SW2RD.Validation;
using Xunit.Abstractions;
using Xunit.Runners;

namespace TestRunner
{
    static public class Program
    {
        // We use consoleLock because messages can arrive in parallel, so we want to make sure we get
        // consistent console output.
        static readonly object consoleLock = new object();

        // Use an event to know when we're done
        static readonly ManualResetEvent finished = new ManualResetEvent(false);

        // Start out assuming success; we'll set this to 1 if we get a failed test
        static int result = 0;

        static string TestNameFilter = "";

        public static int Main(string[] args)
        {
            // `TestRunner diff <baselineDir> <candidateDir>` runs the standalone
            // differential output comparison (no SolidWorks, no xunit) instead of
            // the test suite. Useful for an A/B compare of two builds' exports.
            if (args != null && args.Length >= 1 &&
                string.Equals(args[0], "diff", StringComparison.OrdinalIgnoreCase))
            {
                return RunDiff(args);
            }

            string solutionDir =
                Path.GetDirectoryName( // sw2rd
                Path.GetDirectoryName( // TestRunner
                Path.GetDirectoryName( // bin
                Path.GetDirectoryName( // x64
                Path.GetDirectoryName( // net452
                    AppDomain.CurrentDomain.BaseDirectory // Debug
                )))));

            string testAssembly = Path.Combine(solutionDir, "SW2RD\\bin\\x64\\Debug\\SW2RD.dll");
            string typeName = null;

            using (var runner = AssemblyRunner.WithAppDomain(testAssembly))
            {
                if (null != args && args.Length > 0)
                {
                    TestNameFilter = args[0];
                    runner.TestCaseFilter += FilterByClass;
                }
                runner.OnDiscoveryComplete = OnDiscoveryComplete;
                runner.OnExecutionComplete = OnExecutionComplete;
                runner.OnTestFailed = OnTestFailed;
                runner.OnTestSkipped = OnTestSkipped;

                Console.WriteLine("Discovering...");
                runner.Start(typeName);

                finished.WaitOne();
                finished.Dispose();
                return result;
            }
        }

        // Exit codes: 0 = equivalent, 2 = differences found, 3 = usage / IO error.
        static int RunDiff(string[] args)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: TestRunner diff <baselineDir> <candidateDir>");
                Console.Error.WriteLine(
                    "  Compares two export trees with numeric tolerance (ignores comments,");
                Console.Error.WriteLine(
                    "  version stamps, attribute order). Exit 0 = equivalent, 2 = differs.");
                return 3;
            }

            try
            {
                ExportDiffReport report = ExportTreeComparer.CompareDirectories(args[1], args[2]);
                Console.WriteLine(report.Describe());
                if (report.AreEqual)
                {
                    return 0;
                }
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Differences found.");
                Console.ResetColor();
                return 2;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("diff failed: " + e.Message);
                return 3;
            }
        }

        public static bool FilterByClass(ITestCase testCase)
        {
            if (null != testCase && testCase.DisplayName.Contains(TestNameFilter))
            {
                return true;
            }
            return false;
        }

        static void OnDiscoveryComplete(DiscoveryCompleteInfo info)
        {
            lock (consoleLock)
                Console.WriteLine($"Running {info.TestCasesToRun} of {info.TestCasesDiscovered} tests...");
        }

        static void OnExecutionComplete(ExecutionCompleteInfo info)
        {
            lock (consoleLock)
                Console.WriteLine(
                    $"Finished: {info.TotalTests} tests in" + 
                    $"{Math.Round(info.ExecutionTime, 3)}s " + 
                    $"({info.TestsFailed} failed, " + 
                    $"{info.TestsSkipped} skipped)");

            finished.Set();
        }

        static void OnTestFailed(TestFailedInfo info)
        {
            lock (consoleLock)
            {
                Console.ForegroundColor = ConsoleColor.Red;

                Console.WriteLine("[FAIL] {0}: {1}", info.TestDisplayName, info.ExceptionMessage);
                if (info.ExceptionStackTrace != null)
                    Console.WriteLine(info.ExceptionStackTrace);

                Console.ResetColor();
            }

            result = 1;
        }

        static void OnTestSkipped(TestSkippedInfo info)
        {
            lock (consoleLock)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[SKIP] {0}: {1}", info.TestDisplayName, info.SkipReason);
                Console.ResetColor();
            }
        }
    }
}
