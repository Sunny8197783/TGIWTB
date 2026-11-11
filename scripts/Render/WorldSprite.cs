using Godot;
using PixelMmo.Core;

namespace PixelMmo.Render;

/// <summary>
/// 서 있는 그림 한 장 (이름비·상자처럼 상태가 바뀌는 소품). 지도에 구워 넣는 소품은 World/PropBuilder 가 묶어서 그리고,
/// 게임 중에 바뀌는 것만 이걸로 따로 세운다. 그림 아랫변 가운데가 노드 원점 — 1텍셀 = 1화면픽셀 (규칙 5).
/// </summary>
public partial class WorldSprite : MeshInstance3D
{
    private readonly ShaderMaterial _mat;
    private static readonly StringName ContactAo = "contact_ao", GlowAtNight = "glow_at_night";

    public WorldSprite(Texture2D tex, float glowAtNight = 0f)
    {
        _mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/sprite.gdshader") };
        _mat.SetShaderParameter(ContactAo, 0.3f);
        _mat.SetShaderParameter(GlowAtNight, glowAtNight);
        MaterialOverride = _mat;
        CastShadow = ShadowCastingSetting.Off;
        SetTexture(tex);
    }

    public WorldSprite() { }

    public void SetTexture(Texture2D tex)
    {
        float w = tex.GetWidth() / Px.PerMeter;
        float h = tex.GetHeight() / Px.PerMeter * Px.UprightStretch;
        Mesh = new QuadMesh { Size = new Vector2(w, h), CenterOffset = new Vector3(0f, h * 0.5f, 0f) };
        _mat.SetShaderParameter(Uniform.AlbedoTex, tex);
    }

    /// <summary>빛나는 상태 (깨어난 이름비): 그림 위에 색을 얹고 그만큼 빛난다</summary>
    public void SetFlash(float amount, Color color)
    {
        _mat.SetShaderParameter(Uniform.Flash, amount);
        _mat.SetShaderParameter(Uniform.FlashColor, color);
    }
}
