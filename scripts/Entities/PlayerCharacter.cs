using System;
using System.Collections.Generic;
using Godot;
using PixelMmo.Balance;
using PixelMmo.Data;
using PixelMmo.Runtime;

namespace PixelMmo.Combat;

public enum PlayerState
{
    Normal,
    Dash,
    Dead,
}

/// <summary>
/// IPlayerContext 구현체. 전투/세이브/디버그 어느 쪽도 이 클래스를 직접 참조하지 않고
/// 인터페이스만 본다. (CLAUDE.md 규칙 2)
///
/// 시각 표현은 파란 사각형 ColorRect 하나. (§A 아트 방침)
/// </summary>
public partial class PlayerCharacter : CharacterBody2D, IPlayerContext
{
    private ColorRect _body;
    private ColorRect _facingMarker;

    private readonly HashSet<string> _flags = new();
    private readonly Dictionary<string, int> _counters = new();
    private readonly Dictionary<string, int> _baseStats = new();

    private PlayerState _state = PlayerState.Normal;

    private Vector2 _facing = Vector2.Right;
    private float _dashTimer;
    private float _dashCooldownTimer;
    private float _dashInvulnTimer;
    private Vector2 _dashDirection = Vector2.Right;
    private float _respawnTimer;

    /// <summary>이 값이 0 보다 크면 이 엔티티만 시간이 멈춘다. (§C-3)</summary>
    private float _hitstopTimer;

    public JobState Jobs { get; } = new();
    public MasteryTracker Mastery { get; } = new();

    public int Level { get; private set; } = PlayerTuning.StartLevel;
    public float Hp { get; private set; } = PlayerTuning.BaseMaxHp;
    public float MaxHp { get; private set; } = PlayerTuning.BaseMaxHp;
    public float HpRatio => MaxHp > 0f ? Hp / MaxHp : 0f;
    public bool IsAlive => _state != PlayerState.Dead;
    public Vector2 WorldPosition => GlobalPosition;

    public PlayerState State => _state;
    public Vector2 Facing => _facing;
    public bool IsDashing => _state == PlayerState.Dash;
    public bool IsInvulnerable => _dashInvulnTimer > 0f;
    public float DashCooldownRemaining => Mathf.Max(0f, _dashCooldownTimer);

    /// <summary>화면 중앙 한 줄 메시지. World 가 받아서 띄운다.</summary>
    public event Action<string> Announced;

    /// <summary>대시 무적으로 공격을 흘렸을 때. sk_hidden_deathline 트리거가 여기에 붙는다. (§F)</summary>
    public event Action DodgeSucceeded;

    /// <summary>몬스터가 플레이어를 찾는 그룹 이름.</summary>
    public const string Group = "player";

    public override void _Ready()
    {
        Name = "Player";
        AddToGroup(Group);
        CollisionLayer = CollisionLayers.Player;
        CollisionMask = CollisionLayers.World;
        MotionMode = MotionModeEnum.Floating;

        BuildShapes();
        ResetToNewGame();
    }

    private void BuildShapes()
    {
        float size = PlayerTuning.BodySize;

        var shape = new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = new Vector2(size, size) },
        };
        AddChild(shape);

        _body = new ColorRect
        {
            Size = new Vector2(size, size),
            Position = new Vector2(-size * 0.5f, -size * 0.5f),
            Color = PlayerTuning.BodyColor,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(_body);

        float marker = PlayerTuning.FacingMarkerSize;
        _facingMarker = new ColorRect
        {
            Size = new Vector2(marker, marker),
            Color = PlayerTuning.FacingColor,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(_facingMarker);
    }

    /// <summary>새 게임 상태. 세이브가 없거나 로드가 실패했을 때의 폴백. (§H)</summary>
    public void ResetToNewGame()
    {
        _flags.Clear();
        _counters.Clear();
        _baseStats.Clear();
        foreach (var pair in PlayerTuning.BaseStats)
            _baseStats[pair.Key] = pair.Value;

        Level = PlayerTuning.StartLevel;
        MaxHp = PlayerTuning.BaseMaxHp;
        Hp = MaxHp;
        _state = PlayerState.Normal;
        Velocity = Vector2.Zero;
        GlobalPosition = WorldLayout.SpawnPoint;

        Mastery.Restore(null);

        // P1 은 job_warrior R1 고정. 랭크 1 스킬을 GameDb 에서 실제로 지급받는다. (§D-1)
        Jobs.RestoreFrom("", null, null, null, null);
        Jobs.StartJob(PlayerTuning.StartingJobId, GameDatabase.Instance);
        SetFlag($"held_{PlayerTuning.StartingJobId}");
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        // 히트스톱: 이 엔티티만 멈춘다. 화면 전체가 아니다. (§C-3)
        if (_hitstopTimer > 0f)
        {
            _hitstopTimer -= dt;
            Velocity = Vector2.Zero;
            MoveAndSlide();
            return;
        }

        TickTimers(dt);

        switch (_state)
        {
            case PlayerState.Dead:
                Velocity = Vector2.Zero;
                break;
            case PlayerState.Dash:
                UpdateDash(dt);
                break;
            default:
                UpdateNormal(dt);
                break;
        }

        MoveAndSlide();
        UpdateVisuals(dt);
    }

    private void TickTimers(float dt)
    {
        if (_dashCooldownTimer > 0f)
            _dashCooldownTimer -= dt;
        if (_dashInvulnTimer > 0f)
            _dashInvulnTimer -= dt;

        if (_state == PlayerState.Dead)
        {
            _respawnTimer -= dt;
            if (_respawnTimer <= 0f)
                Respawn();
        }
    }

    private void UpdateNormal(float dt)
    {
        Vector2 input = ReadMoveInput();
        if (input != Vector2.Zero)
            _facing = input;

        if (Input.IsActionJustPressed(InputSetup.Dash) && _dashCooldownTimer <= 0f)
        {
            StartDash(input);
            return;
        }

        ApplyAcceleration(input * CombatTuning.MoveSpeed * MoveSpeedScale(), dt, input != Vector2.Zero);
    }

    private void UpdateDash(float dt)
    {
        Velocity = _dashDirection * CombatTuning.DashSpeed;

        _dashTimer -= dt;
        if (_dashTimer <= 0f)
        {
            _state = PlayerState.Normal;
            // 대시 종료 속도를 그대로 물려주면 미끄러진다. 이동 속도까지 즉시 깎는다.
            Velocity = _dashDirection * CombatTuning.MoveSpeed;
        }
    }

    private void StartDash(Vector2 input)
    {
        _dashDirection = input != Vector2.Zero ? input : _facing;
        _facing = _dashDirection;
        _state = PlayerState.Dash;
        _dashTimer = CombatTuning.DashDuration;
        _dashCooldownTimer = CombatTuning.DashCooldown;
        _dashInvulnTimer = DashInvulnSeconds();
    }

    /// <summary>
    /// 기본 0.10s. sk_hidden_deathline 을 습득했다면 그 패시브 값(0.20s)으로 덮어쓴다. (§E)
    /// </summary>
    private float DashInvulnSeconds()
    {
        float value = CombatTuning.DashInvuln;

        foreach (string skillId in Jobs.LearnedSkills)
        {
            var passive = GameDatabase.Instance?.GetSkill(skillId)?.Passive;
            if (passive != null && passive.DashInvulnSeconds > value)
                value = passive.DashInvulnSeconds;
        }

        return value;
    }

    /// <summary>선딜·가드 등으로 느려지는 배율. M2 에서는 항상 1.</summary>
    protected virtual float MoveSpeedScale() => 1f;

    /// <summary>
    /// 가속/감속. 목표 속도까지 걸리는 시간이 AccelTime / DecelTime 이 되도록
    /// 초당 변화량을 계산한다. 프레임레이트에 의존하지 않는다. (§C-1)
    /// </summary>
    private void ApplyAcceleration(Vector2 target, float dt, bool accelerating)
    {
        float rampTime = accelerating ? CombatTuning.AccelTime : CombatTuning.DecelTime;
        if (rampTime <= 0f)
        {
            Velocity = target;
            return;
        }

        float ratePerSecond = CombatTuning.MoveSpeed / rampTime;
        Velocity = Velocity.MoveToward(target, ratePerSecond * dt);
    }

    /// <summary>8방향. 대각선은 반드시 정규화한다. (§C-1)</summary>
    private static Vector2 ReadMoveInput()
    {
        var input = new Vector2(
            Input.GetActionStrength(InputSetup.MoveRight) - Input.GetActionStrength(InputSetup.MoveLeft),
            Input.GetActionStrength(InputSetup.MoveDown) - Input.GetActionStrength(InputSetup.MoveUp));

        return input.LengthSquared() > 1f ? input.Normalized() : input;
    }

    private void UpdateVisuals(float dt)
    {
        _facingMarker.Position = _facing * PlayerTuning.FacingMarkerDistance
            - Vector2.One * (PlayerTuning.FacingMarkerSize * 0.5f);

        _body.Color = _state == PlayerState.Dash
            ? PlayerTuning.BodyColor.Lightened(0.35f)
            : PlayerTuning.BodyColor;

        Visible = _state != PlayerState.Dead;
    }

    // --- 피해 / 사망 -------------------------------------------------------

    /// <summary>이 엔티티만 dt 초 동안 멈춘다. (§C-3)</summary>
    public void ApplyHitstop(float seconds)
    {
        if (seconds > _hitstopTimer)
            _hitstopTimer = seconds;
    }

    private void Respawn()
    {
        GlobalPosition = WorldLayout.SpawnPoint;
        Hp = MaxHp;
        Velocity = Vector2.Zero;
        _state = PlayerState.Normal;
        _hitstopTimer = 0f;
    }

    protected void Die()
    {
        _state = PlayerState.Dead;
        Hp = 0f;
        Velocity = Vector2.Zero;
        _respawnTimer = PlayerTuning.RespawnDelay;

        SetFlag(PlayerTuning.FlagDiedOnce);
        AddCounter(PlayerTuning.CounterDeaths);
    }

    protected void RaiseDodgeSucceeded() => DodgeSucceeded?.Invoke();

    // --- IPlayerContext ----------------------------------------------------

    public int GetBaseStat(string stat)
        => _baseStats.TryGetValue(stat, out int value) ? value : 0;

    /// <summary>기본 스탯 + 직업 랭크 보너스. 전투 계산은 항상 이쪽. (§D-1)</summary>
    public int GetStat(string stat) => GetBaseStat(stat) + Jobs.StatTotal(stat);

    public bool HasFlag(string flag) => _flags.Contains(flag);

    public void SetFlag(string flag)
    {
        if (!string.IsNullOrEmpty(flag))
            _flags.Add(flag);
    }

    public IReadOnlyCollection<string> Flags => _flags;
    public IReadOnlyDictionary<string, int> Counters => _counters;

    public int GetCounter(string key)
        => _counters.TryGetValue(key, out int value) ? value : 0;

    public int AddCounter(string key, int delta = 1)
    {
        int value = GetCounter(key) + delta;
        _counters[key] = value;
        return value;
    }

    public void Announce(string message)
    {
        if (!string.IsNullOrEmpty(message))
            Announced?.Invoke(message);
    }

    public void ReplaceSkill(string oldSkillId, string newSkillId)
        => Jobs.Replace(oldSkillId, newSkillId);

    public void LearnSkill(string skillId) => Jobs.Learn(skillId);

    public SaveData CaptureSave() => new()
    {
        Version = SaveSystem.CurrentVersion,
        Level = Level,
        Hp = Hp,
        Position = new SavePosition { X = GlobalPosition.X, Y = GlobalPosition.Y },
        BaseStats = new Dictionary<string, int>(_baseStats),
        JobState = new SaveJobState
        {
            CurrentJobId = Jobs.CurrentJobId,
            History = Jobs.CopyHistory(),
            LearnedSkills = Jobs.CopyLearnedSkills(),
            CarriedSkillSlots = Jobs.CopyCarriedSlots(),
            InheritedStats = Jobs.CopyInheritedStats(),
        },
        Mastery = Mastery.Snapshot(),
        Flags = new List<string>(_flags),
        Counters = new Dictionary<string, int>(_counters),
    };

    public void RestoreSave(SaveData data)
    {
        if (data == null)
        {
            ResetToNewGame();
            return;
        }

        _baseStats.Clear();
        if (data.BaseStats != null && data.BaseStats.Count > 0)
        {
            foreach (var pair in data.BaseStats)
                _baseStats[pair.Key] = pair.Value;
        }
        else
        {
            foreach (var pair in PlayerTuning.BaseStats)
                _baseStats[pair.Key] = pair.Value;
        }

        Level = Mathf.Max(1, data.Level);
        MaxHp = PlayerTuning.BaseMaxHp;
        Hp = Mathf.Clamp(data.Hp, 1f, MaxHp);

        var job = data.JobState ?? new SaveJobState();
        Jobs.RestoreFrom(job.CurrentJobId, job.History, job.LearnedSkills,
            job.CarriedSkillSlots, job.InheritedStats);

        // 세이브가 비었거나 깨졌으면 시작 직업으로 되돌린다 — 크래시 대신 폴백. (§H)
        if (string.IsNullOrEmpty(Jobs.CurrentJobId))
            Jobs.StartJob(PlayerTuning.StartingJobId, GameDatabase.Instance);

        Mastery.Restore(data.Mastery);

        _flags.Clear();
        if (data.Flags != null)
        {
            foreach (string flag in data.Flags)
                SetFlag(flag);
        }

        _counters.Clear();
        if (data.Counters != null)
        {
            foreach (var pair in data.Counters)
                _counters[pair.Key] = pair.Value;
        }

        if (data.Position != null)
            GlobalPosition = new Vector2(data.Position.X, data.Position.Y);

        _state = PlayerState.Normal;
        Velocity = Vector2.Zero;
        _hitstopTimer = 0f;
    }
}
