using System;
using System.Globalization;

namespace AIO3.Core.Settings
{
    /// <summary>
    /// A single live-tunable rotation setting. The object IS the storage: the rotation reads
    /// its <c>Value</c> every tick, and the in-game overlay writes to it. <see cref="Key"/> is a
    /// stable identifier used as the Lua bridge key and the persistence key.
    /// </summary>
    public abstract class Setting
    {
        public string Key { get; }
        public string Label { get; }

        protected Setting(string key, string label)
        {
            Key = key;
            Label = label;
        }

        /// <summary>Serialize the current value to a flat string (for persistence).</summary>
        public abstract string Serialize();

        /// <summary>Restore the value from a serialized string (ignored if invalid).</summary>
        public abstract void Deserialize(string raw);
    }

    /// <summary>A boolean toggle, rendered as a checkbox.</summary>
    public sealed class ToggleSetting : Setting
    {
        public bool Value;

        public ToggleSetting(string key, string label, bool value) : base(key, label)
        {
            Value = value;
        }

        public override string Serialize() => Value ? "1" : "0";

        public override void Deserialize(string raw)
        {
            Value = raw == "1" || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>An integer with bounds and a step, rendered as a value with [-]/[+] buttons.</summary>
    public sealed class IntSetting : Setting
    {
        public int Value;
        public int Min;
        public int Max;
        public int Step;

        public IntSetting(string key, string label, int value, int min, int max, int step) : base(key, label)
        {
            Value = value;
            Min = min;
            Max = max;
            Step = step;
        }

        public override string Serialize() => Value.ToString(CultureInfo.InvariantCulture);

        public override void Deserialize(string raw)
        {
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                Value = Math.Max(Min, Math.Min(Max, v));
        }
    }
}
