using Content.Server.Chemistry.Containers.EntitySystems;
using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;
using Robust.Shared.Audio;

namespace Content.Server.Chemistry.Components
{
    [RegisterComponent]
    public sealed partial class HyposprayComponent : SharedHyposprayComponent
    {
        [Dependency] private readonly IEntityManager _entMan = default!;

        [DataField("clumsyFailChance")]
        [ViewVariables(VVAccess.ReadWrite)]
        public float ClumsyFailChance { get; set; } = 0.5f;

        [DataField("transferAmount")]
        [ViewVariables(VVAccess.ReadWrite)]
        public FixedPoint2 TransferAmount { get; set; } = FixedPoint2.New(5);

        [DataField("injectSound")]
        public SoundSpecifier InjectSound = new SoundPathSpecifier("/Audio/Items/hypospray.ogg");

        /// <summary>
        ///     Whether the hypospray uses a needle (i.e. medipens)
        ///     or sci fi bullshit that sprays into the bloodstream directly (i.e. hypos)
        /// </summary>
        [DataField("pierceArmor")]
        public bool PierceArmor = false;

        /// <summary>
        /// Whether or not the hypo is able to inject only into mobs. On false you can inject into beakers/jugs
        /// </summary>
        [DataField("onlyMobs")]
        public bool OnlyMobs = false;
    }
}
