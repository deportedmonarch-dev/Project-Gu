using System;
using ProjectGu.Domain;
using Verse;

namespace ProjectGu
{
    public partial class Hediff_Aperture
    {
        // Fixed-point storage keeps even mixed-quality payments and conversions exact in the save.
        private const long UnitsPerEssence = 1_000_000_000L;
        internal const int NourishmentPulseTicks = 250;
        private const int EssenceSchema = 1;
        private EssenceStock stock;
        private RankOneStage stage;
        private long wallNourishment;
        private int wallWorkTicks;
        private bool essenceCommitInProgress;

        public RankOneStage Stage => stage;
        public float Essence => (float)ToEssence(stock.Total);
        public double WallNourishment => ToEssence(wallNourishment);
        public double WallNourishmentRequired => ToEssence(WallGoal);
        public bool CanAdvanceRankOne => WallGoal > 0 && wallNourishment == WallGoal && !essenceCommitInProgress;
        internal int WallWorkTicks => wallWorkTicks;

        private long Capacity => (long)MaxEssence * UnitsPerEssence;
        private long WallGoal => (Rank == 1 ? stage switch
        {
            RankOneStage.Initial => 24L,
            RankOneStage.Middle => 80L,
            RankOneStage.Upper => 224L,
            _ => 0L
        } : 0L) * UnitsPerEssence;

        /// <summary>Raw volume of stored Rank 1 essence of the given quality. All qualities share one capacity.</summary>
        public double EssenceOfQuality(RankOneStage quality) => ToEssence(stock.At(quality));

        /// <summary>Checks a raw essence cost. Each quality contributes at face value.</summary>
        public bool CanSpendEssence(double amount) => !essenceCommitInProgress
            && TryToUnits(amount, out long units) && stock.Total >= units;

        /// <summary>Pays a raw cost atomically, draining lowest quality first.</summary>
        public bool TrySpendEssence(double amount) => TryToUnits(amount, out long units) && TrySpendUnits(units);

        // Quantize both cumulative values before subtracting so instalments sum to the exact quoted price.
        internal bool TrySpendAccruedEssence(double paid, double due) =>
            TryToUnits(paid, out long paidUnits) && TryToUnits(due, out long dueUnits) && TrySpendUnits(dueUnits - paidUnits);

        /// <summary>Runs one synchronous cast commitment. A failed launch restores the exact original mixture.</summary>
        internal bool TrySpendEssenceForCast(double amount, Func<bool> launch)
        {
            if (essenceCommitInProgress || !TryToUnits(amount, out long units) || stock.Total < units) return false;
            var before = stock;
            Spend(units);
            essenceCommitInProgress = true;
            bool launched = false;
            try
            {
                launched = launch();
                return launched;
            }
            finally
            {
                if (!launched) stock = before;
                essenceCommitInProgress = false;
            }
        }

        /// <summary>
        /// Converts raw essence of the cultivator's current Rank 1 stage into the same volume of the next quality.
        /// The expected source must match the current stage. Peak has no valid output.
        /// </summary>
        public bool TryUpgradeEssence(RankOneStage sourceQuality, double amount)
        {
            if (essenceCommitInProgress || Rank != 1 || sourceQuality != stage || stage == RankOneStage.Peak
                || !TryToUnits(amount, out long units) || units == 0 || stock.At(sourceQuality) < units) return false;
            RankOneStage output = sourceQuality + 1;
            stock.Set(sourceQuality, stock.At(sourceQuality) - units);
            stock.Set(output, stock.At(output) + units);
            return true;
        }

        internal string CultivationStatus =>
            $"Cultivation: Rank {Rank}" + (Rank == 1 ? $" {stage}\n" : "\n")
            + $"Primeval essence: {Essence:0.##}/{MaxEssence:0}\n"
            + $"Stored quality: Initial {EssenceOfQuality(RankOneStage.Initial):0.##}, Middle {EssenceOfQuality(RankOneStage.Middle):0.##}, "
            + $"Upper {EssenceOfQuality(RankOneStage.Upper):0.##}, Peak {EssenceOfQuality(RankOneStage.Peak):0.##}\n"
            + (WallGoal > 0 ? $"Wall nourishment: {WallNourishment:0.##}/{WallNourishmentRequired:0} for {stage + 1}"
                + (CanAdvanceRankOne ? " (ready to advance)" : "") : "Rank 1 cultivation complete.");

        internal bool CanNourishWalls => Rank == 1 && WallGoal > wallNourishment && stock.Total > 0 && !essenceCommitInProgress;

        /// <summary>Accumulates seated work. Every completed pulse consumes up to one raw essence, highest quality first.</summary>
        internal bool TickWallNourishment()
        {
            if (!CanNourishWalls) return false;
            if (++wallWorkTicks < NourishmentPulseTicks) return true;
            wallWorkTicks = 0;
            long availableWork = UnitsPerEssence;
            long goal = WallGoal;
            for (var quality = RankOneStage.Peak; quality >= RankOneStage.Initial && availableWork > 0 && wallNourishment < goal; quality--)
            {
                int potency = 1 << (int)quality;
                long remaining = goal - wallNourishment;
                long consumed = Math.Min(availableWork, Math.Min(stock.At(quality), (remaining + potency - 1) / potency));
                stock.Set(quality, stock.At(quality) - consumed);
                wallNourishment += Math.Min(remaining, consumed * potency);
                availableWork -= consumed;
            }
            return true;
        }

        internal bool TryAdvanceRankOne(RankOneStage expectedStage)
        {
            if (stage != expectedStage || !CanAdvanceRankOne) return false;
            stage++;
            wallNourishment = 0;
            wallWorkTicks = 0;
            return true;
        }

        internal void RecoverEssence()
        {
            if (essenceCommitInProgress) return;
            long step = (long)RegenerationPerHour * (UnitsPerEssence / 10);
            stock.Set(stage, stock.At(stage) + Math.Min(step, Capacity - stock.Total));
        }

        private void InitializeCultivation()
        {
            stage = RankOneStage.Initial;
            wallNourishment = 0;
            wallWorkTicks = 0;
            stock = new EssenceStock { Initial = Capacity / 2 };
        }

        private void Spend(long remaining)
        {
            for (var quality = RankOneStage.Initial; quality <= RankOneStage.Peak && remaining > 0; quality++)
            {
                long available = stock.At(quality);
                long consumed = Math.Min(available, remaining);
                stock.Set(quality, available - consumed);
                remaining -= consumed;
            }
        }

        private bool TrySpendUnits(long units)
        {
            if (essenceCommitInProgress || units < 0 || stock.Total < units) return false;
            Spend(units);
            return true;
        }

        private static double ToEssence(long units) => (double)units / UnitsPerEssence;

        private static bool TryToUnits(double amount, out long units)
        {
            units = 0;
            if (!double.IsFinite(amount) || amount < 0 || amount > int.MaxValue) return false;
            units = (long)Math.Round(amount * UnitsPerEssence, MidpointRounding.AwayFromZero);
            return amount == 0 || units > 0;
        }

        private void ExposeCultivation()
        {
            int version = EssenceSchema;
            Scribe_Values.Look(ref version, "guEssenceSchema", 0);
            Scribe_Values.Look(ref stage, "guRankOneStage", RankOneStage.Initial);
            Scribe_Values.Look(ref stock.Initial, "guEssenceInitial", 0L);
            Scribe_Values.Look(ref stock.Middle, "guEssenceMiddle", 0L);
            Scribe_Values.Look(ref stock.Upper, "guEssenceUpper", 0L);
            Scribe_Values.Look(ref stock.Peak, "guEssencePeak", 0L);
            Scribe_Values.Look(ref wallNourishment, "guWallNourishment", 0L);
            Scribe_Values.Look(ref wallWorkTicks, "guWallWorkTicks", 0);
            if (Scribe.mode != LoadSaveMode.LoadingVars) return;

            if (version == 0)
            {
                float legacy = 0f;
                Scribe_Values.Look(ref legacy, "guEssence", 0f);
                stock = default;
                if (float.IsFinite(legacy) && TryToUnits(Math.Clamp(legacy, 0f, MaxEssence), out long units))
                    stock.Initial = units;
                stage = RankOneStage.Initial;
                wallNourishment = 0;
                wallWorkTicks = 0;
            }
            if (stage is < RankOneStage.Initial or > RankOneStage.Peak) stage = RankOneStage.Initial;
            long room = Capacity;
            for (var quality = RankOneStage.Initial; quality <= RankOneStage.Peak; quality++)
            {
                long kept = Math.Clamp(stock.At(quality), 0L, room);
                stock.Set(quality, kept);
                room -= kept;
            }
            wallNourishment = Math.Clamp(wallNourishment, 0L, WallGoal);
            wallWorkTicks = wallNourishment == WallGoal ? 0 : Math.Clamp(wallWorkTicks, 0, NourishmentPulseTicks - 1);
        }

        private struct EssenceStock
        {
            internal long Initial, Middle, Upper, Peak;
            internal readonly long Total => Initial + Middle + Upper + Peak;

            internal readonly long At(RankOneStage quality) => quality switch
            {
                RankOneStage.Initial => Initial,
                RankOneStage.Middle => Middle,
                RankOneStage.Upper => Upper,
                RankOneStage.Peak => Peak,
                _ => 0L
            };

            internal void Set(RankOneStage quality, long amount)
            {
                switch (quality)
                {
                    case RankOneStage.Initial: Initial = amount; break;
                    case RankOneStage.Middle: Middle = amount; break;
                    case RankOneStage.Upper: Upper = amount; break;
                    case RankOneStage.Peak: Peak = amount; break;
                }
            }
        }
    }
}
