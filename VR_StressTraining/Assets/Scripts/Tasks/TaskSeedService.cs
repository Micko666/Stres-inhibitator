using System;
using StressTraining.Data;

namespace StressTraining.Tasks
{
    /// <summary>
    /// Deterministic seed derivation. Given the same master seed, block index and
    /// task type, the derived block seed — and therefore the full stimulus
    /// sequence — is always identical. This is what lets a Neutral and a Pressure
    /// session present identical task content (spec §11, §20).
    /// </summary>
    public static class TaskSeedService
    {
        // Named sub-seed salts — every production sub-system derives its own
        // deterministic seed from the master seed so one persisted master seed
        // reproduces selection, order, pressure and layout (spec §34).
        public const int SaltTaskSelection = 101;
        public const int SaltBlockOrder = 202;
        public const int SaltPressure = 303;
        public const int SaltConsoleLayout = 404;

        public static int NewMasterSeed() => Environment.TickCount ^ Guid.NewGuid().GetHashCode();

        public static int DeriveBlockSeed(int masterSeed, int blockIndex, TaskType taskType)
        {
            unchecked
            {
                int h = masterSeed;
                h = h * 486187739 + blockIndex + 1;
                h = h * 486187739 + (int)taskType * 7919;
                return h;
            }
        }

        /// <summary>Deterministic named sub-seed (selection/order/pressure/layout).</summary>
        public static int DeriveNamedSeed(int masterSeed, int salt)
        {
            unchecked
            {
                int h = masterSeed;
                h = h * 486187739 + salt * 92821;
                h ^= h >> 15;
                return h;
            }
        }
    }
}
