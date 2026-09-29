using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace PixelMmo.World;

/// <summary>
/// tools/mapgen/mapgen.py 가 만든 지도. 높이·수면은 꼭짓점 격자 (W+1)x(H+1), 지면 종류는 칸 격자 WxH.
/// </summary>
public sealed class WorldData
{
    public const float NoWater = -1000f;
    private const string Dir = "res://data/world/";

    public int Width { get; private set; }
    public int Height { get; private set; }
    public float[] Heights { get; private set; }
    public float[] Water { get; private set; }
    public byte[] Ground { get; private set; }
    public List<PropDef> Props { get; } = new();
    public JsonElement Meta { get; private set; }

    public int VertexWidth => Width + 1;

    public static WorldData Load()
    {
        var data = new WorldData();
        data.Meta = JsonDocument.Parse(FileAccess.GetFileAsString(Dir + "meta.json")).RootElement;
        data.Width = data.Meta.GetProperty("width").GetInt32();
        data.Height = data.Meta.GetProperty("height").GetInt32();
        data.Heights = ReadFloats(Dir + "terrain.f32");
        data.Water = ReadFloats(Dir + "water.f32");
        data.Ground = FileAccess.GetFileAsBytes(Dir + "ground.u8");

        using var doc = JsonDocument.Parse(FileAccess.GetFileAsString(Dir + "props.json"));
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            data.Props.Add(new PropDef
            {
                Type = e.GetProperty("type").GetString(),
                X = e.GetProperty("x").GetSingle(),
                Z = e.GetProperty("z").GetSingle(),
                Variant = e.TryGetProperty("v", out var v) ? v.GetInt32() : 0,
                FrontZ = e.TryGetProperty("front_z", out var f) ? f.GetSingle() : float.NaN,
            });
        }

        int expected = (data.Width + 1) * (data.Height + 1);
        if (data.Heights.Length != expected || data.Water.Length != expected)
            throw new InvalidOperationException($"지도 크기 불일치: {data.Heights.Length} != {expected}");
        return data;
    }

    private static float[] ReadFloats(string path)
    {
        byte[] bytes = FileAccess.GetFileAsBytes(path);
        var result = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
        return result;
    }

    public float VertexHeight(int x, int z)
    {
        x = Math.Clamp(x, 0, Width);
        z = Math.Clamp(z, 0, Height);
        return Heights[z * VertexWidth + x];
    }

    /// <summary>임의 지점의 지면 높이 — 칸 안에서 두 삼각형 보간(메시와 같은 분할).</summary>
    public float HeightAt(float x, float z)
    {
        x = Math.Clamp(x, 0f, Width - 0.001f);
        z = Math.Clamp(z, 0f, Height - 0.001f);
        int ix = (int)x, iz = (int)z;
        float fx = x - ix, fz = z - iz;
        float h00 = VertexHeight(ix, iz), h10 = VertexHeight(ix + 1, iz);
        float h01 = VertexHeight(ix, iz + 1), h11 = VertexHeight(ix + 1, iz + 1);
        // TerrainBuilder 의 삼각형 분할(00-10-11, 00-11-01)과 맞춘다
        return fx > fz
            ? h00 + (h10 - h00) * fx + (h11 - h10) * fz
            : h00 + (h11 - h01) * fx + (h01 - h00) * fz;
    }

    /// <summary>
    /// 캐릭터 이동 제약 (지형 충돌체 대신). 가파른 절벽과 깊은 물로는 못 간다.
    /// 막히면 축을 하나씩 살려 벽·물가를 따라 미끄러진다. 높이맵 충돌 계산은 몸통 하나에 0.3ms 라 쓰지 않는다.
    /// </summary>
    /// <param name="probe">몸 반지름만큼 앞을 미리 본다 (m)</param>
    public Vector3 ConstrainMove(Vector3 p, Vector3 vel, float dt, float wadeDepth, float maxSlope, float probe = 0.35f)
    {
        bool Blocked(Vector3 d)
        {
            if (d.LengthSquared() < 1e-6f)
                return false;
            Vector3 q = p + d.Normalized() * probe + d * dt;
            if (WaterAt(q.X, q.Z) - HeightAt(q.X, q.Z) > wadeDepth)
                return true;
            float run = new Vector2(q.X - p.X, q.Z - p.Z).Length();
            return MathF.Abs(HeightAt(q.X, q.Z) - HeightAt(p.X, p.Z)) > maxSlope * run;
        }
        if (!Blocked(vel))
            return vel;
        var onlyX = new Vector3(vel.X, 0f, 0f);
        if (!Blocked(onlyX))
            return onlyX;
        var onlyZ = new Vector3(0f, 0f, vel.Z);
        return Blocked(onlyZ) ? Vector3.Zero : onlyZ;
    }

    public float WaterAt(float x, float z)
    {
        int ix = Math.Clamp((int)MathF.Round(x), 0, Width);
        int iz = Math.Clamp((int)MathF.Round(z), 0, Height);
        return Water[iz * VertexWidth + ix];
    }

    public byte GroundAt(float x, float z)
    {
        int ix = Math.Clamp((int)x, 0, Width - 1);
        int iz = Math.Clamp((int)z, 0, Height - 1);
        return Ground[iz * Width + ix];
    }

    public Vector2 Vec2(string key)
    {
        var a = Meta.GetProperty(key);
        return new Vector2(a[0].GetSingle(), a[1].GetSingle());
    }
}

public sealed class PropDef
{
    public string Type;
    public float X;
    public float Z;
    public int Variant;
    public float FrontZ;
}
