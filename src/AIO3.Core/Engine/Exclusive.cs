namespace AIO3.Core.Engine
{
    /// <summary>
    /// A token a step can "consume" on a target so that lower-priority steps cannot
    /// override the effect (e.g. two buffs that share a slot). Ported from the old
    /// AIO Exclusive system, which was one of its genuinely good ideas.
    /// Identity-compared: create one shared instance per mutually-exclusive group.
    /// </summary>
    public sealed class Exclusive
    {
        public string Name { get; }

        public Exclusive(string name) => Name = name;

        public override string ToString() => Name;
    }
}
