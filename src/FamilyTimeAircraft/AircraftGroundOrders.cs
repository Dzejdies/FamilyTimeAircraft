using System;
using System.Collections.Generic;
using System.Reflection;
using FamilyTimeAircraft.Core;
using GAT;
using HarmonyLib;
using UnityEngine;
using NativeAgents = GAT.GAT;
using NativeWorld = GAT.World;
using TargetVector = System.Numerics.Vector3;

namespace FamilyTimeAircraft;

public sealed class AircraftGroundOrders : IDisposable
{
    private const float ConservativeWalkSpeed = 0.5f;
    private const float NavigationAllowanceSeconds = 180;
    private const int MaximumGroundOrders = 4096;
    private const int DestinationRadius = 2;
    private static readonly FieldInfo AssignedAgents = RequireAssignedAgentsField();
    private static readonly FieldInfo? AssignedRange = AccessTools.Field(typeof(FloodPathRequest), "_hasAssignedRange");

    private sealed class Order
    {
        public int ChildId;
        public int Generation;
        public Handle Handle;
        public NativeWorld World = null!;
        public NativeAgents Agents = null!;
        public CommandOwnershipService Ownership = null!;
        public CommandOwnershipToken Token = null!;
        public FloodPathRequest? Path;
        public Action<Handle>? Reached;
        public float Deadline;
    }

    private readonly AircraftRuntime _runtime;
    private readonly Dictionary<int, Order> _orders = new();
    private int _generation;
    private bool _disposed;

    public AircraftGroundOrders(AircraftRuntime runtime) => _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

    public void MoveChild(Occupant occupant, TargetVector target)
    {
        RequireReady(occupant, target);
        Release(occupant.Id);
        Order order = CreateOrder(occupant);
        _orders.Add(occupant.Id, order);
        try { StartPath(order, GameFlightEnvironment.ToUnity(target)); }
        catch { Release(occupant.Id); throw; }
    }

    private void RequireReady(Occupant occupant, TargetVector target)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AircraftGroundOrders));
        if (occupant == null) throw new ArgumentNullException(nameof(occupant));
        if (!_runtime.IsAuthoritative) throw new InvalidOperationException("Only the host can assign aircraft ground orders.");
        if (occupant.Kind != OccupantKind.Child) throw new InvalidOperationException("Aircraft ground orders require a child occupant.");
        if (_orders.Count >= MaximumGroundOrders && !_orders.ContainsKey(occupant.Id))
            throw new InvalidOperationException("Aircraft ground order limit reached.");
        if (!IsFinite(target) || !_runtime.Environment.IsInWorld(target)) throw new InvalidOperationException("The aircraft ground destination is invalid.");
        if (NativeWorld.Instance == null || NativeWorld.gat == null || NativeWorld.navManager == null || NativeWorld.commandOwnershipService == null)
            throw new InvalidOperationException("Native child navigation is unavailable.");
    }

    private static bool IsFinite(TargetVector value) => !float.IsNaN(value.X) && !float.IsInfinity(value.X)
        && !float.IsNaN(value.Y) && !float.IsInfinity(value.Y) && !float.IsNaN(value.Z) && !float.IsInfinity(value.Z);

    private Order CreateOrder(Occupant occupant)
    {
        Handle handle = _runtime.ResolveChildHandle?.Invoke(occupant.Id) ?? new Handle { stableIndex = checked((ushort)occupant.Id) };
        if (!NativeWorld.gat.IsHandleValid(handle)) throw new InvalidOperationException("The aircraft child is unavailable.");
        if (!NativeAircraftGame.ClaimChild(handle, out CommandOwnershipToken? token) || token == null)
            throw new InvalidOperationException("The aircraft child already has another command.");
        return new Order { ChildId = occupant.Id, Generation = _generation, Handle = handle, Token = token,
            World = NativeWorld.Instance, Agents = NativeWorld.gat, Ownership = NativeWorld.commandOwnershipService };
    }

    private void StartPath(Order order, Vector3 target)
    {
        Vector3 origin = RestoreMovement(order);
        order.Deadline = CalculateDeadline(origin, target);
        order.Path = NativeWorld.navManager.CreateFloodPathRequest(Vector3Int.RoundToInt(target), DestinationRadius, DestinationRadius);
        order.Reached = handle => OnReached(order, handle);
        order.Path.OnAgentReachedEnd += order.Reached;
        NativeWorld.gatUtilities?.SetInstancesScreenIconIndex(new[] { order.Handle }, ScreenIcon.NAVIGATING);
        order.Path.AssignAgents(new[] { order.Handle });
        order.Path.SetEnd(Vector3Int.RoundToInt(target));
    }

    private float CalculateDeadline(Vector3 origin, Vector3 target)
    {
        TargetVector nativeOrigin = GameFlightEnvironment.ToCore(origin);
        if (!IsFinite(nativeOrigin) || !_runtime.Environment.IsInWorld(nativeOrigin))
            throw new InvalidOperationException("The aircraft child's native position is outside the world.");
        return Time.realtimeSinceStartup + Vector3.Distance(origin, target) / ConservativeWalkSpeed + NavigationAllowanceSeconds;
    }

    private void OnReached(Order order, Handle handle)
    {
        if (handle != order.Handle || !IsCurrentOrder(order)) return;
        Release(order.ChildId);
    }

    private bool IsCurrentOrder(Order order) => order.Generation == _generation
        && _orders.TryGetValue(order.ChildId, out Order current) && ReferenceEquals(current, order);

    public void Tick()
    {
        if (_disposed) return;
        var finished = new List<int>();
        foreach (Order order in _orders.Values)
            if (ShouldFinish(order)) finished.Add(order.ChildId);
        foreach (int id in finished) Release(id);
    }

    private static bool ShouldFinish(Order order) => !HasNativeWorld(order) || !order.Agents.IsHandleValid(order.Handle)
        || !OwnsChild(order) || order.Ownership.IsCommandLocked(order.Handle) || order.Path == null || order.Path.isDisposed
        || order.Path.state == FloodPathRequest.State.FAILURE || Time.realtimeSinceStartup > order.Deadline;

    private static bool HasNativeWorld(Order order) => ReferenceEquals(NativeWorld.Instance, order.World)
        && ReferenceEquals(NativeWorld.gat, order.Agents) && ReferenceEquals(NativeWorld.commandOwnershipService, order.Ownership);

    private static bool OwnsChild(Order order)
    {
        if (!order.Token.IsActive) return false;
        foreach (ushort id in order.Token.StableIndices)
            if (id == order.Handle.stableIndex) return true;
        return false;
    }

    private void Release(int childId)
    {
        if (!_orders.TryGetValue(childId, out Order order)) return;
        _orders.Remove(childId);
        bool restore = HasNativeWorld(order) && OwnsChild(order) && order.Agents.IsHandleValid(order.Handle);
        try { ReleasePath(order); }
        finally
        {
            try { if (restore) FinishMovement(order); }
            finally { order.Ownership.ReleaseCommandOwnership(order.Token); }
        }
    }

    private static void ReleasePath(Order order)
    {
        FloodPathRequest? path = order.Path;
        order.Path = null;
        if (path == null) return;
        path.OnAgentReachedEnd -= order.Reached;
        try
        {
            if (HasNativeWorld(order)) path.RemoveFromFlowField(new[] { order.Handle });
            else DetachObsoleteAgents(path);
        }
        finally { path.Dispose(); }
    }

    private static void DetachObsoleteAgents(FloodPathRequest path)
    {
        ((HashSet<Handle>)AssignedAgents.GetValue(path)).Clear();
        if (AssignedRange != null) AssignedRange.SetValue(path, false);
    }

    private static FieldInfo RequireAssignedAgentsField()
    {
        FieldInfo field = AccessTools.Field(typeof(FloodPathRequest), "_assignedAgents")
            ?? throw new MissingFieldException("Native path assigned-agent tracking is unavailable.");
        if (field.FieldType != typeof(HashSet<Handle>)) throw new InvalidOperationException("Native path assigned-agent tracking has changed.");
        FieldInfo? range = AccessTools.Field(typeof(FloodPathRequest), "_hasAssignedRange");
        if (range != null && range.FieldType != typeof(bool)) throw new InvalidOperationException("Native path range tracking has changed.");
        return field;
    }

    private static Vector3 RestoreMovement(Order order)
    {
        Matrix4x4? matrix = order.Agents.GetInstanceMatrixImmediate(order.Handle);
        if (!matrix.HasValue) throw new InvalidOperationException("The aircraft child's native pose is unavailable.");
        order.Agents.SetInstanceMatrix(order.Handle, matrix.Value, true, true, true);
        order.Agents.SetInstanceVelocity(order.Handle, Vector3.zero);
        return matrix.Value.GetPosition();
    }

    private static void FinishMovement(Order order)
    {
        RestoreMovement(order);
        ScreenIcon icon = order.Ownership.IsCommandLocked(order.Handle) ? ScreenIcon.LOCKED : ScreenIcon.NONE;
        NativeWorld.gatUtilities?.SetInstancesScreenIconIndex(new[] { order.Handle }, icon);
    }

    public void Clear()
    {
        _generation++;
        var failures = new List<Exception>();
        foreach (int id in new List<int>(_orders.Keys))
            try { Release(id); }
            catch (Exception exception) { failures.Add(exception); }
        if (failures.Count > 0) throw new AggregateException("Aircraft ground orders could not release all native resources.", failures);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Clear();
    }
}
