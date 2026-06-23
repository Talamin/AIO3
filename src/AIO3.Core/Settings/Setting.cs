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

        /// <summary>Tab/group the setting is shown under in the overlay (e.g. "Spec", "Rotation", "General").</summary>
        public string Category { get; set; } = "General";

        /// <summary>If set, the overlay only shows this setting while the named spec is active (matched against
        /// <c>IClassModule.ActiveSpec</c>, e.g. "Demonology"). Null = shown for every spec. Lets a spec-only knob
        /// hide itself in the specs it doesn't apply to, so the panel only shows what's relevant to the build.</summary>
        public string Spec { get; set; }

        /// <summary>True if this setting should be shown for the given active spec. A setting with no
        /// <see cref="Spec"/> tag always applies; a null active spec (unknown/not spec-aware) disables filtering.</summary>
        public bool AppliesTo(string activeSpec) => Spec == null || activeSpec == null || Spec == activeSpec;

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

    /// <summary>A choice among fixed options, rendered as a cycle button (e.g. spec selection).</summary>
    public sealed class ChoiceSetting : Setting
    {
        public string Value;
        public readonly string[] Options;

        public ChoiceSetting(string key, string label, string value, string[] options) : base(key, label)
        {
            Value = value;
            Options = options;
        }

        public override string Serialize() => Value ?? "";

        public override void Deserialize(string raw)
        {
            foreach (string o in Options)
                if (o == raw) { Value = raw; return; }
            // unknown option → keep current value
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
