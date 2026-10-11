using System;
using System.Collections.Generic;
using FamilyTimeAircraft.Core;
using GAT;
using UnityEngine;

namespace FamilyTimeAircraft;

public sealed class AircraftBoarding : IDisposable
{
    private const float BoardingDistance = 2;
    private const float BoardingTimeout = 30;
    private sealed class Reservation
    {
        public int AircraftId;
        public int Seat;
        public int PlayerId;
        public string Owner = "";
        public Handle Handle;
        public CommandOwnershipToken? Token;
        public FloodPathRequest? Path;
        public float Deadline;
        public bool Seated;
    }

    private readonly AircraftRuntime _runtime;
    private readonly Dictionary<int, Reservation> _children = new();

    public AircraftBoarding(AircraftRuntime runtime) => _runtime = runtime;
    public IReadOnlyCollection<int> ChildIds => _children.Keys;
    public bool Contains(int childId) => _children.ContainsKey(childId);

    public bool Begin(int aircraftId, int playerId, string owner, Handle child, int seat)
    {
        if (_children.ContainsKey(child.stableIndex)) return false;
        Aircraft plane = _runtime.World.FindAircraft(aircraftId);
        if (!plane.CanBoard || seat > 8) return false;
        if (_runtime.CanCommandChild != null && !_runtime.CanCommandChild(playerId, child.stableIndex)) return false;
        var reservation = new Reservation { AircraftId = aircraftId, PlayerId = playerId, Owner = owner,
            Handle = child, Seat = seat, Deadline = Time.time + BoardingTimeout };
        if (!Claim(reservation)) return false;
        _children.Add(child.stableIndex, reservation);
        try { StartPath(reservation, plane); }
        catch { Release(child.stableIndex); throw; }
        return true;
    }

    private bool Claim(Reservation reservation)
    {
        if (_runtime.TryClaimChild != null) return _runtime.TryClaimChild(reservation.PlayerId, reservation.Handle.stableIndex);
        return NativeAircraftGame.ClaimChild(reservation.Handle, out reservation.Token);
    }

    private static void StartPath(Reservation reservation, Aircraft plane)
    {
        Vector3 destination = GameFlightEnvironment.ToUnity(plane.Position) + GameFlightEnvironment.ToUnity(plane.Rotation) * new Vector3(4, 0, 0);
        reservation.Path = World.navManager.CreateFloodPathRequest(Vector3Int.RoundToInt(destination), 2, 2);
        reservation.Path.AssignAgents(new[] { reservation.Handle });
        reservation.Path.SetEnd(Vector3Int.RoundToInt(destination));
    }

    public void CancelPendingForPlayer(int playerId)
    {
        var ids = new List<int>();
        foreach (KeyValuePair<int, Reservation> pair in _children)
            if (pair.Value.PlayerId == playerId && !pair.Value.Seated) ids.Add(pair.Key);
        foreach (int id in ids) Release(id);
    }

    public void RestoreSeats()
    {
        foreach (Aircraft plane in _runtime.World.Aircrafts)
            foreach (KeyValuePair<int, Occupant> seat in plane.Seats)
                RestoreClaim(plane.Id, seat.Key, seat.Value);
        foreach (EjectedOccupant passenger in _runtime.World.Ejections)
            RestoreClaim(passenger.AircraftId, -1, passenger.Occupant);
    }

    private void RestoreClaim(int aircraftId, int seat, Occupant occupant)
    {
        if (occupant.Kind != OccupantKind.Child) return;
        Handle handle = _runtime.ResolveChildHandle?.Invoke(occupant.Id) ?? new Handle { stableIndex = checked((ushort)occupant.Id) };
        var reservation = new Reservation { AircraftId = aircraftId, Owner = occupant.Owner, Handle = handle, Seat = seat, Seated = true };
        if (!NativeAircraftGame.ClaimChild(handle, out reservation.Token)) throw new InvalidOperationException("Cannot restore aircraft child command reservation.");
        _children.Add(occupant.Id, reservation);
    }

    public bool IsSeatReserved(int aircraftId, int seat)
    {
        foreach (Reservation reservation in _children.Values)
            if (reservation.AircraftId == aircraftId && reservation.Seat == seat) return true;
        return false;
    }

    public void Tick()
    {
        var expired = new List<int>();
        foreach (KeyValuePair<int, Reservation> entry in _children)
        {
            Reservation reservation = entry.Value;
            if (!World.gat.IsHandleValid(reservation.Handle) || (!reservation.Seated && Time.time > reservation.Deadline)) expired.Add(entry.Key);
            else if (!reservation.Seated)
            {
                try { TryBoard(reservation); }
                catch (InvalidOperationException error) { _runtime.SetNotice(error.Message); expired.Add(entry.Key); }
            }
        }
        foreach (int id in expired) Release(id);
    }

    private void TryBoard(Reservation reservation)
    {
        Aircraft plane = _runtime.World.FindAircraft(reservation.AircraftId);
        if (!plane.CanBoard) return;
        Vector3 door = GameFlightEnvironment.ToUnity(plane.Position) + GameFlightEnvironment.ToUnity(plane.Rotation) * new Vector3(4, 0, 0);
        Matrix4x4? matrix = World.gat.GetInstanceMatrixImmediate(reservation.Handle);
        if (!matrix.HasValue || Vector3.Distance(matrix.Value.GetPosition(), door) > BoardingDistance) return;
        _runtime.World.Board(plane.Id, new Occupant(OccupantKind.Child, reservation.Handle.stableIndex, reservation.Owner), reservation.Seat);
        reservation.Seated = true;
        reservation.Path?.RemoveFromFlowField(new[] { reservation.Handle });
        reservation.Path?.Dispose();
        reservation.Path = null;
    }

    public void Release(int childId)
    {
        if (!_children.TryGetValue(childId, out Reservation reservation)) return;
        _children.Remove(childId);
        reservation.Path?.RemoveFromFlowField(new[] { reservation.Handle });
        reservation.Path?.Dispose();
        if (reservation.Token != null) World.commandOwnershipService?.ReleaseCommandOwnership(reservation.Token);
        else _runtime.ReleaseChild?.Invoke(reservation.PlayerId, reservation.Handle.stableIndex);
        RestoreMovement(reservation.Handle);
    }

    private static void RestoreMovement(Handle handle)
    {
        if (World.gat == null || !World.gat.IsHandleValid(handle)) return;
        Matrix4x4? matrix = World.gat.GetInstanceMatrixImmediate(handle);
        if (matrix.HasValue) World.gat.SetInstanceMatrix(handle, matrix.Value, true, true, true);
    }

    public void Dispose()
    {
        var ids = new List<int>(_children.Keys);
        foreach (int id in ids) Release(id);
    }
}
