namespace Kukolony.Colonies
{
    /// <summary>
    ///     The kinds of thing a colony owns. Each is a separate list on the colony's ZDO,
    ///     kept apart rather than in one bag so the UI and the AI can ask for exactly what
    ///     they need without loading and type-testing every member.
    /// </summary>
    internal enum ColonyMemberKind
    {
        /// <summary>The people.</summary>
        Villager,

        /// <summary>Storage the colony may deposit into and draw from.</summary>
        Container,

        /// <summary>Work posts, and later crafting stations.</summary>
        Station,

        /// <summary>Beds. A villager's home is one of these.</summary>
        Home
    }
}
