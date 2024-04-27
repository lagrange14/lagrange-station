using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared.Body.Events;

[Serializable, NetSerializable]
public sealed partial class CPRDoAfterEvent : SimpleDoAfterEvent
{
}
