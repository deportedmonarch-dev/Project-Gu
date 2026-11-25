using System;

namespace ProjectGu.Interface
{
    /// <summary>
    /// Pure geometry for the primeval sea. Essence fills the aperture by area, not by height, and each quality forms its
    /// own layer. Higher-quality essence is denser and settles lowest: Peak at the bottom, Initial on top. The numbers in
    /// the interface stay authoritative; this only decides where each layer is drawn. No engine types are used, so the
    /// managed tests exercise it directly.
    /// </summary>
    public static class PrimevalSeaLayout
    {
        /// <summary>Number of Rank 1 qualities, Initial..Peak.</summary>
        public const int Qualities = 4;

        /// <summary>
        /// Converts a filled share of the sea's area into a row, measured from the top. <paramref name="rowCoverage"/> holds
        /// the sea's area in each row from top to bottom. Returns a fractional row: the surface lies between rows.
        /// </summary>
        public static float RowForFilledFraction(float[] rowCoverage, double fraction)
        {
            if (rowCoverage == null || rowCoverage.Length == 0) return 0f;
            double total = 0;
            for (int i = 0; i < rowCoverage.Length; i++) total += Math.Max(0f, rowCoverage[i]);
            int rows = rowCoverage.Length;
            if (total <= 0 || !(fraction > 0)) return Bottom(rowCoverage);
            if (fraction >= 1) return Top(rowCoverage);
            double wanted = fraction * total, filled = 0;
            for (int row = rows - 1; row >= 0; row--)
            {
                double area = Math.Max(0f, rowCoverage[row]);
                if (filled + area >= wanted)
                {
                    double part = area <= 0 ? 0 : (wanted - filled) / area;
                    return (float)(row + 1 - part);
                }
                filled += area;
            }
            return Top(rowCoverage);
        }

        /// <summary>
        /// Layer boundaries for a mixed sea. <paramref name="amounts"/> are raw volumes per quality (Initial..Peak) and
        /// <paramref name="capacity"/> is the shared capacity. Writes <paramref name="boundaries"/>[q] = the top row of
        /// quality q's layer, stacking Peak lowest; <paramref name="boundaries"/>[Qualities] is the bottom of the sea.
        /// A quality with essence keeps a visible layer of at least <paramref name="minRows"/> rows so trace amounts are
        /// not hidden. Returns the surface row (the top of the highest non-empty layer).
        /// </summary>
        public static float Layers(float[] rowCoverage, double[] amounts, double capacity, float[] boundaries, float minRows = 1.5f)
        {
            if (boundaries == null || boundaries.Length < Qualities + 1) throw new ArgumentException("boundaries needs Qualities + 1 entries");
            float bottom = Bottom(rowCoverage), top = Top(rowCoverage);
            boundaries[Qualities] = bottom;
            double cumulative = 0;
            float previous = bottom;
            for (int quality = Qualities - 1; quality >= 0; quality--)
            {
                double amount = amounts != null && quality < amounts.Length && amounts[quality] > 0 ? amounts[quality] : 0;
                cumulative += amount;
                float row = capacity > 0 ? RowForFilledFraction(rowCoverage, Math.Min(1, cumulative / capacity)) : bottom;
                if (amount > 0 && previous - row < minRows) row = Math.Max(top, previous - minRows);
                if (row > previous) row = previous;
                boundaries[quality] = row;
                previous = row;
            }
            return previous;
        }

        /// <summary>The quality whose layer contains a row, or -1 above the surface.</summary>
        public static int QualityAt(float row, float[] boundaries)
        {
            if (row >= boundaries[Qualities]) return -1;
            for (int quality = 0; quality < Qualities; quality++)
                if (row >= boundaries[quality] && row < boundaries[quality + 1]) return quality;
            return -1;
        }

        /// <summary>Wall progress as a 0..1 arc. Peak (no further wall) shows a complete ring.</summary>
        public static float WallArc(double nourishment, double required) =>
            required <= 0 ? 1f : (float)Math.Clamp(nourishment / required, 0, 1);

        private static float Top(float[] rowCoverage)
        {
            for (int row = 0; row < rowCoverage.Length; row++) if (rowCoverage[row] > 0) return row;
            return 0;
        }

        private static float Bottom(float[] rowCoverage)
        {
            for (int row = rowCoverage.Length - 1; row >= 0; row--) if (rowCoverage[row] > 0) return row + 1;
            return rowCoverage.Length;
        }
    }
}
