namespace AIO3.Core.Game
{
    /// <summary>The four Death Knight rune kinds. A spent Blood/Frost/Unholy rune refreshes into a <see cref="Death"/>
    /// rune (via talents/abilities); a Death rune can pay for ANY rune cost, so rotation affordability checks count
    /// the specific type PLUS the Death pool. Game-agnostic — the adapter maps this to WRobot's own RuneTypes enum.</summary>
    public enum RuneType
    {
        Blood,
        Frost,
        Unholy,
        Death
    }
}
