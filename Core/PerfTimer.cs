using System.Diagnostics;

namespace PUP_AUTO.Core
{
    /// <summary>
    /// Times a block and writes "[PERF] name: N ms" to the log file on dispose. Counts and timings only, never data.
    /// Usage: <c>using (PerfTimer.Measure(_logger, "ComputeRegisterGeometry")) { ... }</c>
    /// </summary>
    public sealed class PerfTimer : IDisposable
    {
        private readonly Logger? _logger;
        private readonly string _name;
        private readonly Stopwatch _watch = Stopwatch.StartNew();

        private PerfTimer(Logger? logger, string name)
        {
            _logger = logger;
            _name = name;
        }

        public static PerfTimer Measure(Logger? logger, string name) => new PerfTimer(logger, name);

        public long ElapsedMs => _watch.ElapsedMilliseconds;

        /// <summary>"12.3 s" for the one summary line per report in the window.</summary>
        public string ElapsedText => (_watch.ElapsedMilliseconds / 1000.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s";

        public void Dispose()
        {
            _watch.Stop();
            _logger?.LogPerf($"{_name}: {_watch.ElapsedMilliseconds} ms");
        }

        /// <summary>One "[PERF] Memory ..." line: managed heap, private bytes and working set in MB.</summary>
        public static void LogMemory(Logger? logger, string when)
        {
            if (logger == null) return;
            using (Process process = Process.GetCurrentProcess())
            {
                logger.LogPerf($"Memory {when}: managed {GC.GetTotalMemory(false) / 1048576} MB, " +
                               $"private {process.PrivateMemorySize64 / 1048576} MB, working set {process.WorkingSet64 / 1048576} MB");
            }
        }
    }
}
