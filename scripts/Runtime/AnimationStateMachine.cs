using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace PixelMmo.Runtime;

/// <summary>data/animations.json 의 상태 1개.</summary>
public sealed class AnimationStateDef
{
    public string Id { get; set; } = "";
    public bool Loop { get; set; }
    public bool Cancelable { get; set; }
    public int Frames { get; set; }
    public float Fps { get; set; }
    public bool HitboxActive { get; set; }
    public bool Invulnerable { get; set; }

    /// <summary>지속시간이 CombatTuning 에서 오는 경우 그 키. null 이면 DurationSeconds 사용.</summary>
    public string TuningKey { get; set; }
    public float? DurationSeconds { get; set; }

    /// <summary>이 상태의 지속시간(초). 튜닝 키가 있으면 F3 로 바뀐 현재 값을 따른다.</summary>
    public float ResolveDuration()
    {
        if (!string.IsNullOrEmpty(TuningKey))
        {
            foreach (var entry in Combat.CombatTuning.All)
            {
                if (entry.Key == TuningKey)
                    return entry.Value;
            }
        }
        return DurationSeconds ?? 0f;
    }
}

/// <summary>
/// 애니메이션 상태머신 — 기존 코드를 감싸기만 하는 관찰자(observer)다.
///
/// 대상(IAnimationDriver)의 상태를 매 프레임 '읽어서' 전이를 감지하고 시그널을 쏜다.
/// 대상을 조종하지 않으므로, 이 노드를 떼어내도 게임은 그대로 돌아간다.
/// 스프라이트가 들어오면 여기서 Play("attack_active") 를 호출하는 자리가 된다.
///
/// 메타데이터는 res://data/animations.json — 상태를 추가해도 코어 코드를 안 고친다.
/// </summary>
public partial class AnimationStateMachine : Node
{
    private const string DataPath = "res://data/animations.json";

    /// <summary>상태에 들어감 / 나감. (id)</summary>
    public event Action<string> StateEntered;
    public event Action<string> StateExited;

    /// <summary>히트박스가 열림 / 닫힘. (상태 id)</summary>
    public event Action<string> HitFrameStarted;
    public event Action<string> HitFrameEnded;

    private readonly Dictionary<string, AnimationStateDef> _defs = new();
    private IAnimationDriver _driver;

    private string _state = "";
    private bool _hitOpen;
    private float _elapsed;

    /// <summary>현재 상태 id. 아직 아무것도 안 잡혔으면 빈 문자열.</summary>
    public string State => _state;

    /// <summary>현재 상태에 머문 시간(초).</summary>
    public float Elapsed => _elapsed;

    public AnimationStateDef Def(string id)
        => _defs.TryGetValue(id, out var def) ? def : null;

    /// <summary>현재 상태의 진행도 0~1. 지속시간이 없는(루프) 상태는 항상 0.</summary>
    public float Progress
    {
        get
        {
            var def = Def(_state);
            float duration = def?.ResolveDuration() ?? 0f;
            return duration > 0f ? Mathf.Clamp(_elapsed / duration, 0f, 1f) : 0f;
        }
    }

    public void Bind(IAnimationDriver driver) => _driver = driver;

    public override void _Ready()
    {
        Name = "AnimationStateMachine";
        LoadDefs();
    }

    private void LoadDefs()
    {
        using var file = FileAccess.Open(DataPath, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PushWarning($"[Anim] {DataPath} 를 열 수 없다 — 상태 메타데이터 없이 동작한다.");
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(file.GetAsText());
            if (!doc.RootElement.TryGetProperty("states", out var states))
                return;

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            foreach (var element in states.EnumerateArray())
            {
                var def = element.Deserialize<AnimationStateDef>(options);
                if (def != null && !string.IsNullOrEmpty(def.Id))
                    _defs[def.Id] = def;
            }
            GD.Print($"[Anim] states={_defs.Count}");
        }
        catch (Exception e)
        {
            // 데이터가 깨져도 게임은 계속 돈다 — 이 머신은 연출 보조일 뿐이다.
            GD.PushWarning($"[Anim] animations.json 파싱 실패: {e.Message}");
        }
    }

    public override void _Process(double delta)
    {
        if (_driver == null)
            return;

        string next = _driver.CurrentAnimationState;
        if (next != _state)
        {
            if (!string.IsNullOrEmpty(_state))
                StateExited?.Invoke(_state);

            _state = next;
            _elapsed = 0f;
            StateEntered?.Invoke(_state);
        }
        else
        {
            _elapsed += (float)delta;
        }

        // 히트 프레임 — 드라이버가 실제로 판정을 돌리는 구간과 일치시킨다.
        bool hit = _driver.AnimationHitboxActive;
        if (hit && !_hitOpen)
        {
            _hitOpen = true;
            HitFrameStarted?.Invoke(_state);
        }
        else if (!hit && _hitOpen)
        {
            _hitOpen = false;
            HitFrameEnded?.Invoke(_state);
        }
    }
}
