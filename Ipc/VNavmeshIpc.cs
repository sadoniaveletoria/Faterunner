using System.Numerics;
using Dalamud.Plugin.Ipc;

namespace FateRunner.Ipc;

/// <summary>vnavmesh IPC (names from awgil/ffxiv_navmesh IPCProvider.cs).</summary>
public sealed class VNavmeshIpc : IpcBase
{
    protected override string InternalName => "vnavmesh";

    private readonly ICallGateSubscriber<bool> isReady;
    private readonly ICallGateSubscriber<float> buildProgress;
    private readonly ICallGateSubscriber<int> numWaypoints;
    private readonly ICallGateSubscriber<float> getTolerance;
    private readonly ICallGateSubscriber<float, object> setTolerance;
    private readonly ICallGateSubscriber<bool> getMovementAllowed;
    private readonly ICallGateSubscriber<bool, object> setMovementAllowed;
    private readonly ICallGateSubscriber<Vector3, bool, float, bool> moveCloseTo;
    private readonly ICallGateSubscriber<object> stop;
    private readonly ICallGateSubscriber<bool> isRunning;
    private readonly ICallGateSubscriber<bool> pathfindInProgress;
    private readonly ICallGateSubscriber<Vector3, bool, float, Vector3?> pointOnFloor;
    private readonly ICallGateSubscriber<Vector3, float, float, Vector3?> nearestPoint;

    public VNavmeshIpc()
    {
        var pi = Plugin.PluginInterface;
        isReady = pi.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
        buildProgress = pi.GetIpcSubscriber<float>("vnavmesh.Nav.BuildProgress");
        numWaypoints = pi.GetIpcSubscriber<int>("vnavmesh.Path.NumWaypoints");
        getTolerance = pi.GetIpcSubscriber<float>("vnavmesh.Path.GetTolerance");
        setTolerance = pi.GetIpcSubscriber<float, object>("vnavmesh.Path.SetTolerance");
        getMovementAllowed = pi.GetIpcSubscriber<bool>("vnavmesh.Path.GetMovementAllowed");
        setMovementAllowed = pi.GetIpcSubscriber<bool, object>("vnavmesh.Path.SetMovementAllowed");
        moveCloseTo = pi.GetIpcSubscriber<Vector3, bool, float, bool>("vnavmesh.SimpleMove.PathfindAndMoveCloseTo");
        stop = pi.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
        isRunning = pi.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
        pathfindInProgress = pi.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress");
        pointOnFloor = pi.GetIpcSubscriber<Vector3, bool, float, Vector3?>("vnavmesh.Query.Mesh.PointOnFloor");
        nearestPoint = pi.GetIpcSubscriber<Vector3, float, float, Vector3?>("vnavmesh.Query.Mesh.NearestPoint");
    }

    public bool IsReady => Safe(() => isReady.InvokeFunc(), false, "IsReady");

    /// <summary>Mesh build progress 0..1 while the zone's navmesh is being built (or loaded the first time).</summary>
    public float BuildProgress => Safe(() => buildProgress.InvokeFunc(), 0f, "BuildProgress");

    /// <summary>True while a path is being computed or followed.</summary>
    public bool IsBusy => IsPathfinding || IsMoving;

    /// <summary>Still computing a route — the character won't move yet. Big zones/flying paths can take a while.</summary>
    public bool IsPathfinding => Safe(() => pathfindInProgress.InvokeFunc(), false, "PathfindInProgress");

    /// <summary>Following a computed route.</summary>
    public bool IsMoving => Safe(() => isRunning.InvokeFunc(), false, "IsRunning");

    public bool MoveCloseTo(Vector3 dest, float range, bool fly = false)
        => Safe(() => moveCloseTo.InvokeFunc(dest, fly, range), false, "PathfindAndMoveCloseTo");

    public void Stop() => Safe(() => stop.InvokeAction(), "Stop");

    /// <summary>
    /// Other plugins can change vnavmesh's path tolerance / movement switch and leave them changed until restart.
    /// A huge tolerance makes the follower treat every waypoint as reached and finish instantly without moving;
    /// movement disabled means it never moves. Reset both to vnavmesh's defaults before each route.
    /// </summary>
    public void PrepareMove()
    {
        Safe(() => setTolerance.InvokeAction(0.25f), "SetTolerance");
        Safe(() => setMovementAllowed.InvokeAction(true), "SetMovementAllowed");
    }

    /// <summary>What vnavmesh's path follower holds right now — for the log.</summary>
    public string Diag()
        => $"waypoints={Safe(() => numWaypoints.InvokeFunc(), -1, "NumWaypoints")} " +
           $"tolerance={Safe(() => getTolerance.InvokeFunc(), -1f, "GetTolerance"):0.##} " +
           $"movementAllowed={Safe(() => getMovementAllowed.InvokeFunc(), false, "GetMovementAllowed")} " +
           $"pathfinding={IsPathfinding} running={IsMoving}";

    /// <summary>
    /// Turns an X/Z with unknown height into a point on the mesh: probe straight down first,
    /// then fall back to the nearest mesh point around the player's current height.
    /// </summary>
    public Vector3? GroundPoint(float x, float z, float playerY)
    {
        foreach (var startY in new[] { playerY + 50f, 1024f })
        {
            var p = Safe(() => pointOnFloor.InvokeFunc(new Vector3(x, startY, z), false, 5f), null, "PointOnFloor");
            if (p != null) return p;
        }
        return Safe(() => nearestPoint.InvokeFunc(new Vector3(x, playerY, z), 15f, 1000f), null, "NearestPoint");
    }
}
