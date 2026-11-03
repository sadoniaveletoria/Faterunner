using Dalamud.Plugin.Ipc;

namespace FateRunner.Ipc;

/// <summary>Lifestream teleport IPC (optional; FateRunner falls back to the native teleport).</summary>
public sealed class LifestreamIpc : IpcBase
{
    protected override string InternalName => "Lifestream";

    private readonly ICallGateSubscriber<uint, byte, bool> teleport;
    private readonly ICallGateSubscriber<bool> isBusy;
    private readonly ICallGateSubscriber<bool> teleportToHome;
    private readonly ICallGateSubscriber<bool> teleportToApartment;
    private readonly ICallGateSubscriber<bool> teleportToFc;
    private readonly ICallGateSubscriber<bool?> hasPrivateHouse;
    private readonly ICallGateSubscriber<bool?> hasApartment;
    private readonly ICallGateSubscriber<bool?> hasFcHouse;
    private readonly ICallGateSubscriber<uint, bool> aethernetTeleportById;

    public LifestreamIpc()
    {
        var pi = Plugin.PluginInterface;
        teleport = pi.GetIpcSubscriber<uint, byte, bool>("Lifestream.Teleport");
        isBusy = pi.GetIpcSubscriber<bool>("Lifestream.IsBusy");
        teleportToHome = pi.GetIpcSubscriber<bool>("Lifestream.TeleportToHome");
        teleportToApartment = pi.GetIpcSubscriber<bool>("Lifestream.TeleportToApartment");
        teleportToFc = pi.GetIpcSubscriber<bool>("Lifestream.TeleportToFC");
        hasPrivateHouse = pi.GetIpcSubscriber<bool?>("Lifestream.HasPrivateHouse");
        hasApartment = pi.GetIpcSubscriber<bool?>("Lifestream.HasApartment");
        hasFcHouse = pi.GetIpcSubscriber<bool?>("Lifestream.HasFreeCompanyHouse");
        aethernetTeleportById = pi.GetIpcSubscriber<uint, bool>("Lifestream.AethernetTeleportById");
    }

    public bool Teleport(uint aetheryteId) => Safe(() => teleport.InvokeFunc(aetheryteId, 0), false, "Teleport");

    public bool IsBusy => Safe(() => isBusy.InvokeFunc(), false, "IsBusy");

    // Housing. The Has* calls return null when Lifestream doesn't know yet (e.g. it hasn't seen your estate info).
    public bool? HasPrivateHouse => Safe(() => hasPrivateHouse.InvokeFunc(), null, "HasPrivateHouse");
    public bool? HasApartment => Safe(() => hasApartment.InvokeFunc(), null, "HasApartment");
    public bool? HasFcHouse => Safe(() => hasFcHouse.InvokeFunc(), null, "HasFreeCompanyHouse");

    public bool TeleportToHome() => Safe(() => teleportToHome.InvokeFunc(), false, "TeleportToHome");
    public bool TeleportToApartment() => Safe(() => teleportToApartment.InvokeFunc(), false, "TeleportToApartment");
    /// <summary>Walks to the nearest shard in the current city and takes the aethernet to <paramref name="aetheryteRowId"/>.</summary>
    public bool AethernetTeleportById(uint aetheryteRowId)
        => Safe(() => aethernetTeleportById.InvokeFunc(aetheryteRowId), false, "AethernetTeleportById");

    public bool TeleportToFc() => Safe(() => teleportToFc.InvokeFunc(), false, "TeleportToFC");
}
