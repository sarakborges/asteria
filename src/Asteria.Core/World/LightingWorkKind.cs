namespace Asteria.Core.World;

/// <summary>Execution scope required to reconcile a small lighting edit.</summary>
internal enum LightingWorkKind : byte
{
    None,
    BlockLightOnly,
    SkyOnly,
    Full,
}
