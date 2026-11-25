using System.Diagnostics;

namespace ProjectGu.Interface
{
    /// <summary>
    /// Cheap counters for the aperture orb: how often it composites and uploads, and how long each takes. Two timestamp
    /// reads per measured step; no allocation. Used by the performance harness and available for diagnostics.
    /// </summary>
    public static class ApertureOrbStats
    {
        public static int Compositions { get; private set; }
        public static int Uploads { get; private set; }
        public static int Skipped { get; private set; }
        public static int StaticRebuilds { get; private set; }
        public static long PrepareTicks { get; private set; }
        public static double PrepareAverageMs => Compositions == 0 ? 0 : Ms(PrepareTicks) / Compositions;
        public static long ComposeTicks { get; private set; }
        public static long ComposeMaxTicks { get; private set; }
        public static long UploadTicks { get; private set; }
        public static long UploadMaxTicks { get; private set; }
        /// <summary>Managed bytes allocated on the main thread inside composition and upload.</summary>
        public static long ComposeBytes { get; private set; }
        public static long UploadBytes { get; private set; }

        public static double ComposeAverageMs => Compositions == 0 ? 0 : Ms(ComposeTicks) / Compositions;
        public static double ComposeMaxMs => Ms(ComposeMaxTicks);
        public static double UploadAverageMs => Uploads == 0 ? 0 : Ms(UploadTicks) / Uploads;
        public static double UploadMaxMs => Ms(UploadMaxTicks);

        public static void Reset()
        {
            Compositions = Uploads = Skipped = StaticRebuilds = 0;
            PrepareTicks = 0;
            ComposeTicks = ComposeMaxTicks = UploadTicks = UploadMaxTicks = ComposeBytes = UploadBytes = 0;
        }

        internal static long Now() => Stopwatch.GetTimestamp();
        internal static long Bytes() => System.GC.GetAllocatedBytesForCurrentThread();

        internal static void Composed(long start, long startBytes)
        {
            ComposeBytes += Bytes() - startBytes;
            long elapsed = Stopwatch.GetTimestamp() - start;
            Compositions++;
            ComposeTicks += elapsed;
            if (elapsed > ComposeMaxTicks) ComposeMaxTicks = elapsed;
        }

        internal static void Uploaded(long start, long startBytes)
        {
            UploadBytes += Bytes() - startBytes;
            long elapsed = Stopwatch.GetTimestamp() - start;
            Uploads++;
            UploadTicks += elapsed;
            if (elapsed > UploadMaxTicks) UploadMaxTicks = elapsed;
        }

        internal static void Skip() => Skipped++;
        internal static void Rebuilt() => StaticRebuilds++;
        internal static void Prepared(long start) => PrepareTicks += Stopwatch.GetTimestamp() - start;

        private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
    }
}
