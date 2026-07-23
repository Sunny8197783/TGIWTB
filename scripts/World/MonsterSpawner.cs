using System;
using System.Collections.Generic;
using Godot;
using PixelMmo.Combat;

namespace PixelMmo.Runtime;

/// <summary>
/// 구역별 몬스터 스폰 + 리스폰. 슬롯 1개 = 몬스터 1마리 자리. (§G)
/// 처치되면 그 자리에만 타이머가 걸리고, 시간이 지나면 같은 구역 임의 지점에 다시 선다.
/// </summary>
public partial class MonsterSpawner : Node2D
{
    /// <summary>플레이어 코앞에 튀어나오지 않게 하는 최소 거리.</summary>
    private static readonly float MinDistanceFromPlayer = 96f;

    private static readonly int PlacementAttempts = 48;

    private sealed class Slot
    {
        public Func<MonsterBase> Factory;
        public ZoneDef Zone;
        public float RespawnSeconds;
        public float Timer;
        public MonsterBase Current;
    }

    private readonly List<Slot> _slots = new();
    private readonly RandomNumberGenerator _rng = new();
    private TileWorld _tiles;
    private Node2D _player;

    public int AliveCount
    {
        get
        {
            int count = 0;
            foreach (var slot in _slots)
            {
                if (slot.Current != null && IsInstanceValid(slot.Current))
                    count++;
            }
            return count;
        }
    }

    public override void _Ready()
    {
        Name = "MonsterSpawner";
        _rng.Randomize();
    }

    public void Configure(TileWorld tiles, Node2D player)
    {
        _tiles = tiles;
        _player = player;
    }

    /// <summary>구역에 count 마리 자리를 만들고 즉시 채운다.</summary>
    public void AddGroup(Func<MonsterBase> factory, ZoneDef zone, int count, float respawnSeconds)
    {
        for (int i = 0; i < count; i++)
        {
            var slot = new Slot
            {
                Factory = factory,
                Zone = zone,
                RespawnSeconds = respawnSeconds,
            };
            _slots.Add(slot);
            Spawn(slot);
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        foreach (var slot in _slots)
        {
            if (slot.Current != null && IsInstanceValid(slot.Current))
                continue;

            slot.Timer -= dt;
            if (slot.Timer <= 0f)
                Spawn(slot);
        }
    }

    private void Spawn(Slot slot)
    {
        MonsterBase monster = slot.Factory();
        monster.Position = RandomSpawnPoint(slot.Zone);
        monster.Died += _ => OnDied(slot);

        slot.Current = monster;
        AddChild(monster);
    }

    private void OnDied(Slot slot)
    {
        slot.Current = null;
        slot.Timer = slot.RespawnSeconds;
    }

    /// <summary>구역 안의 걸을 수 있는 임의 지점. 플레이어 근처는 피한다.</summary>
    private Vector2 RandomSpawnPoint(ZoneDef zone)
    {
        Rect2I tiles = zone.Tiles;
        Vector2 fallback = zone.WorldCenter;

        for (int i = 0; i < PlacementAttempts; i++)
        {
            int x = _rng.RandiRange(tiles.Position.X, tiles.Position.X + tiles.Size.X - 1);
            int y = _rng.RandiRange(tiles.Position.Y, tiles.Position.Y + tiles.Size.Y - 1);

            var point = new Vector2(
                (x + 0.5f) * WorldLayout.TileSize,
                (y + 0.5f) * WorldLayout.TileSize);

            if (_tiles != null && !_tiles.IsWalkable(point))
                continue;

            if (_player != null && IsInstanceValid(_player)
                && _player.GlobalPosition.DistanceTo(point) < MinDistanceFromPlayer)
                continue;

            return point;
        }

        return fallback;
    }
}
