using System.Collections.Generic;
using AIO3.Core.Settings;

namespace AIO3.Core.Engine
{
    /// <summary>
    /// A rotation builds its steps once. Layer 4 specs implement this; they should be
    /// thin (a baseline + a class-specific filler), not the giant inline lambda lists
    /// of the old code.
    /// </summary>
    public interface IRotation
    {
        string Name { get; }

        /// <summary>Live-tunable settings exposed to the in-game overlay (empty if none).</summary>
        IReadOnlyList<Setting> Settings { get; }

        IReadOnlyList<RotationStep> BuildSteps();
    }
}
