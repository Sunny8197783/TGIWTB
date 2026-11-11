using System;
using Godot;
using PixelMmo.Data;
using PixelMmo.Render;

namespace PixelMmo.Combat;

/// <summary>
/// 이름(직업)마다 다른 손맛. 무엇을 쓸지는 직업 JSON 의 "fx" 가 정하고(규칙 1), 여기는 그 낱말을 그림으로 옮긴다.
///   서약검 — 금빛 호가 메아리처럼 한 번 더 따라 긋고, 강타가 맞으면 땅에 맹세의 인장이 찍힌다
///   묵영   — 빛이 아니라 먹으로 긋는다. 맞은 자리에 먹이 튀고, 내디딜 때마다 잔상이 남는다
///   풍문사 — 쏠 때 빛의 활이 휘고, 맞은 자리에 회오리가 돈다
///   호명사 — 부를 때 발밑에 고리가 퍼지고, 맞은 자리에서 울림이 번진다
/// 강타가 맞으면 이름 글자(誓·墨·風·呼)가 적 머리 위에 찍힌다.
/// </summary>
public static class JobFx
{
    /// <summary>메아리 궤적은 본 궤적보다 이만큼 크고</summary>
    public const float EchoRadius = 1.18f;
    /// <summary>이만큼 가늘다</summary>
    private const float EchoThickness = 0.42f;
    private const float GustSmall = 1.2f, GustBig = 1.7f;   // 회오리 반지름 (m)
    private const float BowSmall = 1.5f, BowBig = 1.9f;    // 빛의 활 반지름 (m)
    private const float GlyphGap = 0.45f;                    // 이름 글자가 겹쳐 찍히지 않게 두는 틈 (초)

    private static ulong _lastGlyphMs;

    /// <summary>시작할 때 한 번: 여기서 쓰는 호 모양을 미리 만들어 둔다 (처음 쓰는 순간 끊기지 않게).</summary>
    public static void Warm(Action<float, float, bool> warmArc)
    {
        warmArc(GustSmall, 360f, false);
        warmArc(GustBig, 360f, false);
        warmArc(BowSmall, 150f, false);
        warmArc(BowBig, 150f, false);
    }

    public static bool Ink(JobDef job) => job.Fx.Swing == "ink";

    public static float EchoThick(float thickness) => thickness * EchoThickness;

    /// <summary>투사체를 쏘는 순간 (Hero.Shoot).</summary>
    public static void Cast(JobDef job, Vector3 feet, Vector3 facing, bool heavy)
    {
        var pal = CombatFx.PaletteOf(job.Palette);
        Vector3 left = Vector3.Up.Cross(facing);
        Vector3 chest = feet + Vector3.Up * 0.85f;
        switch (job.Fx.Cast)
        {
            case "bow":
                // 빛의 활: 몸 앞에 눕혀 든 호가 한 번 휘었다 흩어진다. 세우면 북쪽을 볼 때 몸에 다 가려져서 눕힌다
                CombatFx.Slash(chest + facing * 0.15f, facing, (left + Vector3.Up * 0.3f).Normalized(),
                    heavy ? BowBig : BowSmall, 150f, heavy ? 0.3f : 0.22f, false, heavy ? 1.3f : 1.0f, pal);
                if (heavy)
                {
                    CombatFx.Shockwave(feet, 1.5f, 0.3f, pal);
                    Dust.Puff(feet, 8, -facing * 2.5f);
                }
                break;
            case "ring":
                // 부름 고리: 발밑에서 퍼지고, 내민 손끝에서 반짝인다
                CombatFx.Shockwave(feet, heavy ? 1.9f : 1.05f, heavy ? 0.4f : 0.28f, pal);
                CombatFx.SparkleBurst(chest + facing * 0.6f, heavy ? 14 : 6, heavy ? 0.5f : 0.3f, pal);
                if (heavy)
                    CombatFx.Star(chest + facing * 0.7f, 0.9f, pal.Bright, 0.12f);
                break;
        }
    }

    /// <summary>적 하나가 맞은 순간 (Hero.Strike) — 불꽃·별 위에 얹는 이름의 흔적.</summary>
    public static void Hit(JobDef job, Enemy e, Vector3 at, Vector3 dir, bool heavy)
    {
        var pal = CombatFx.PaletteOf(job.Palette);
        Vector3 feet = e.GlobalPosition;
        switch (job.Fx.Hit)
        {
            case "seal":
                // 맹세의 인장: 강타면 적 발밑에 금빛 고리가 찍힌다
                if (heavy)
                {
                    CombatFx.Shockwave(feet, 1.5f + e.Radius, 0.32f, pal);
                    CombatFx.SparkleBurst(at + Vector3.Up * 0.3f, 10, 0.5f, pal);
                }
                break;
            case "blot":
                CombatFx.InkDrops(at, dir, heavy ? 14 : 7);
                break;
            case "gust":
                // 회오리: 적을 감고 도는 바람 한 바퀴
                CombatFx.Slash(feet + Vector3.Up * 0.45f, dir, Vector3.Up.Cross(dir).Normalized(),
                    heavy ? GustBig : GustSmall, 360f, heavy ? 0.36f : 0.28f, false, heavy ? 1.5f : 1.2f, pal);
                break;
            case "chime":
                // 울림: 불린 이름이 고리로 번진다
                CombatFx.Shockwave(feet, (heavy ? 1.8f : 1.0f) + e.Radius, heavy ? 0.42f : 0.3f, pal);
                if (heavy)
                    CombatFx.SparkleBurst(at + Vector3.Up * 0.3f, 12, 0.6f, pal);
                break;
        }
        if (heavy && job.Fx.Glyph && !string.IsNullOrEmpty(job.Glyph))
        {
            ulong now = Time.GetTicksMsec();
            if (now - _lastGlyphMs >= (ulong)(GlyphGap * 1000f))
            {
                _lastGlyphMs = now;
                Hud.Float(feet + Vector3.Up * (e.Radius * 2f + 0.9f), job.Glyph, pal.Bright);
            }
        }
    }
}
