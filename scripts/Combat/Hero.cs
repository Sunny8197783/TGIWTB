using System.Collections.Generic;
using Godot;
using PixelMmo.Core;
using PixelMmo.Data;
using PixelMmo.Render;
using T = PixelMmo.Combat.CombatTuning;

namespace PixelMmo.Combat;

/// <summary>
/// 주인공. 몸은 CharacterBody3D (지형·건물·나무에 막힌다), 그림은 CharacterSprite.
/// 시간은 GameRoot.HeroScale — 히트스톱엔 멈추지만 완벽 회피 슬로우엔 정상 속도 (Witch Time).
///
/// 전투 흐름 (레퍼런스 dev_in_airforce + REBUILD.md 조사):
///  - 공격해도 멈추지 않는다: 달리던 속도를 이어받아 앞으로 내딛는다 (질주 공격).
///  - 3연타. 판정 뒤에는 다음 타나 회피로 끊을 수 있다. 입력은 잠깐 기억한다(버퍼).
///  - 회피 중 공격 = 반격(강화). 공격이 닿기 직전 회피 = 완벽 회피 → 적만 느려진다.
///  - 막기 누른 직후 맞으면 패링 → 적 경직. 그냥 막으면 작은 불꽃과 함께 밀려난다.
/// </summary>
public partial class Hero : CharacterBody3D, IPlayerContext
{
    public static Hero Instance { get; private set; }

    private enum State { Move, Attack, Skill, Dash, DashRecover, Guard, Hurt, Dead }

    private CharacterSprite _sprite;
    private State _state = State.Move;
    private float _stateTime;
    private Vector3 _vel;               // 수평 속도
    private Vector3 _carry;             // 질주 공격: 이어받은 달리기 속도
    private Vector3 _dashDir;
    private float _dashCooldown;
    private float _dashAge = 99f;       // 돌진 시작 후 시간 (무적·완벽 회피 판정)
    private bool _perfectUsed;
    private float _dodgeBuffer, _attackBuffer;
    private float _ghostTimer;
    private Vector3 _look;              // 카메라 앞보기
    private int _lastStepFrame = -1;
    private int _combo;                 // 지금 치고 있는 타 (0..2)
    private bool _hitDone, _slashDone, _echoDone, _counter;
    private float _guardAge;
    private float _iframes;
    private float _hp = 100f;
    private JobDef _job = JobDef.Get(JobDef.Novice);
    private int _level = 1;
    private int _exp;
    private string _gender = "m";
    private string _artFolder;
    private float _flash;
    private Color _flashColor = Colors.White;
    private Vector3 _spawn;
    private SkillRunner _skill;
    private readonly string[] _loadout = new string[Controls.SkillSlots.Length];
    private readonly Dictionary<string, float> _cooldowns = new();
    private int _skillBuffer = -1;          // 누른 스킬 칸 (버퍼 시간 동안 기억)
    private float _skillBufferTime;
    private Mastery _mastery;
    private bool _persist;                  // 캡처(검증) 실행은 진짜 세이브를 읽지도 쓰지도 않는다
    private string _lookBase;
    private int _lookAccent;
    private readonly HashSet<string> _statues = new();
    private string _respawnStatue;
    // 무명록 (M9)
    private string _name;
    private Deeds _deeds = new();
    private readonly Dictionary<string, int> _vars = new();
    private readonly HashSet<string> _pastJobs = new();
    private int _shards;
    private int _flask = T.FlaskMax;
    private float _healLeft;                // 이슬을 마시는 남은 시간
    private int _flawless;                  // 맞지 않고 이어 쓰러뜨린 수

    public float Hp => _hp;
    public float MaxHp => _job.MaxHp(_level) * (1f + EngraveDef.Instance.HpPerStage * EngraveStage(_job.Id));
    public string JobPalette => _job.Palette;
    public JobDef Job => _job;
    /// <summary>연출 중: 적이 주인공을 보지 못한다</summary>
    public bool Hidden { get; set; }
    public string HeroName => _name;
    public int Shards => _shards;
    public int Flask => _flask;
    public int FlaskMax => T.FlaskMax + Var("flask_bonus");
    public Deeds DeedBook => _deeds;
    /// <summary>쓰러뜨리는 일격을 받는 순간 묻는다 — true 를 돌려주면 쓰러지지 않고 체력 1 로 버틴다 (이야기의 위기 연출)</summary>
    public System.Func<bool> LastStand { get; set; }
    /// <summary>이야기가 거는 힘 (나래가 돌아온 싸움). 평소 1</summary>
    public float DamageBoost { get; set; } = 1f;

    public void RefillFlask() => _flask = FlaskMax;

    /// <summary>이름이 지워진다: 주인공마저 색을 잃는다 (0 = 제 색, 1 = 잿빛)</summary>
    public void SetErased(float amount) => _sprite.SetHushed(amount);
    public int Level => _level;
    public int Exp => _exp;
    public int ExpToNext => LevelDef.Instance.Need(_level);
    public string JobId => _job.Id;
    public string JobName => _job.Name;
    /// <summary>레벨이 올린 피해 배율 (직업마다 레벨당 증가율이 다르다)</summary>
    public float Power => _job.Power(_level) * (1f + EngraveDef.Instance.PowerPerStage * EngraveStage(_job.Id)) * DamageBoost;
    public bool IsAlive => _state != State.Dead;
    public Vector3 WorldPosition => GlobalPosition;
    public IReadOnlyList<string> Skills => _loadout;
    public float CooldownRemaining(string skillId) => skillId != null && _cooldowns.TryGetValue(skillId, out float t) ? Mathf.Max(t, 0f) : 0f;
    public float MasteryProgress(string skillId) => skillId != null && SkillDef.All.TryGetValue(skillId, out var def) ? _mastery.Progress(def) : -1f;
    public float SkillDamageScale(SkillDef def) => _mastery.Multiplier(def) * Power;
    public IReadOnlySet<string> Statues => _statues;
    public string RespawnStatue => _respawnStatue;
    public string LookBase => _lookBase;
    public int LookAccent => _lookAccent;
    /// <summary>세이브에 모습이 없었다 (처음 켬) — GameRoot 가 모습 고르기를 연다</summary>
    public bool FirstLaunch { get; private set; }
    /// <summary>바라보는 쪽 (8방향 중 하나)</summary>
    public Vector3 Forward => FacingVector();

    public override void _EnterTree() => Instance = this;

    public override void _Ready()
    {
        Name = "Hero";
        CollisionLayer = 1 << 1;
        CollisionMask = 1;
        // 지형은 높이맵을 직접 읽어 발을 붙인다 (WorldData.ConstrainMove). 물리는 건물·나무에 막히는 것만.
        MotionMode = MotionModeEnum.Floating;
        AddChild(new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = 0.3f, Height = 1.5f },
            Position = new Vector3(0f, 0.75f, 0f),
        });
        _skill = new SkillRunner(this);
        SuppressSave = false;

        _mastery = new Mastery(SkillDef.All.Values);
        _persist = !Dev.DevCapture.Requested();
        string lookBase = null;
        int lookAccent = 0;
        if (_persist)
        {
            var save = SaveData.Load();
            lookBase = save.LookBase;
            _lookForge = save.LookForge;
            lookAccent = save.LookAccent;
            FirstLaunch = lookBase == null;
            _mastery.Load(save);
            _statues.UnionWith(save.Statues ?? new List<string>());
            _respawnStatue = save.RespawnStatue;
            _level = Mathf.Clamp(save.Level, 1, LevelDef.Instance.MaxLevel);
            _exp = save.Exp;
            _job = JobDef.Get(save.Job);
            _name = save.Name;
            _deeds = new Deeds(save.Deeds);
            foreach (var (k, v) in save.Vars ?? new Dictionary<string, int>())
                _vars[k] = v;
            _pastJobs.UnionWith(save.PastJobs ?? new List<string>());
            _shards = save.Shards;
        }
        Dev.DevCapture.JobOverride(ref _level, ref _job);
        foreach (var (id, value) in Dev.DevCapture.MasterySeeds())
            _mastery.Seed(id, value);
        Dev.DevCapture.LookOverride(ref lookBase, ref lookAccent);
        _lookForge = Dev.DevCapture.ForgeOverride() ?? _lookForge;
        SetLook(lookBase, lookAccent);
        RebuildLoadout();
        _hp = MaxHp;
        _flask = FlaskMax;
    }

    /// <summary>모습을 바꾼다: 바탕 원화가 다르면 스프라이트를 새로 만들고, 옷 색은 불러올 때 칠한다 (Render/Recolor).</summary>
    public void SetLook(string baseId, int accent)
    {
        var look = AppearanceDef.Instance;
        var b = look.Find(baseId);
        accent = Mathf.Clamp(accent, 0, look.Accents.Count - 1);
        // 직업 옷이 있으면 그 원화 (성별은 처음 고른 모습을 따른다), 견습생은 고른 모습 그대로
        var art = _job.ArtFor(b.Gender);
        if (art == null || !ResourceLoader.Exists(art.Art + "/rot.png"))
            art = b; // 직업 원화가 아직 없으면 (생성 중) 고른 모습으로
        // 빚은 모습이 있으면 그것이 먼저다 — 이름(직업)이 바뀌어도 내 몸은 그대로 (무기는 빛으로 나타난다)
        _forged = _lookForge != null && Forge.Ready(_lookForge);
        string folder = _forged ? Forge.Folder(_lookForge) : art.Art;
        if (_sprite != null && b.Id == _lookBase && accent == _lookAccent && folder == _artFolder)
            return;
        int dir = _sprite?.Dir ?? 0;
        if (_sprite != null)
        {
            RemoveChild(_sprite);
            _sprite.QueueFree();
        }
        _sprite = new CharacterSprite(folder, _forged ? null : Recolor.For(art, look.Accents[accent]));
        _artFolder = folder;
        _gender = b.Gender;
        AddChild(_sprite);
        _sprite.ExemptFromSlow();
        _sprite.ExemptFromHush();
        _sprite.Dir = dir;
        _lookBase = b.Id;
        _lookAccent = accent;
    }

    private string _lookForge;
    private bool _forged;
    public string LookForge => _lookForge;

    /// <summary>빚은 모습을 입는다 (null = 벗고 기성 모습으로). 동작이 다 완성된 것만 입혀진다 (Core/Forge)</summary>
    public void SetForge(string forgeId)
    {
        _lookForge = forgeId;
        string look = _lookBase;
        _artFolder = null;   // SetLook 이 같은 모습이라고 건너뛰지 않게
        SetLook(look, _lookAccent);
    }

    public override void _ExitTree() => Save();

    /// <summary>'새로 시작'이 기록을 지운 뒤 장면을 다시 부르는 동안: 나가면서 다시 쓰지 않게</summary>
    public static bool SuppressSave { get; set; }

    public void Save()
    {
        if (!_persist || SuppressSave)
            return;
        var save = new SaveData
        {
            LookBase = _lookBase, LookAccent = _lookAccent, LookForge = _lookForge,
            Statues = new List<string>(_statues), RespawnStatue = _respawnStatue,
            Level = _level, Exp = _exp, Job = _job.Id,
            Name = _name, Deeds = _deeds.Snapshot(), Vars = new Dictionary<string, int>(_vars),
            PastJobs = new List<string>(_pastJobs), Shards = _shards,
        };
        _mastery.Save(save);
        save.Write();
    }

    /// <summary>순간이동 (시작 위치·캡처). 처음 부르면 부활 자리로 기억한다.</summary>
    public void Teleport(Vector3 feet)
    {
        if (_spawn == Vector3.Zero)
            _spawn = feet;
        GlobalPosition = feet + Vector3.Up * 0.05f;
        _vel = _carry = Vector3.Zero;
        Velocity = Vector3.Zero;
        _look = Vector3.Zero;
        GameRoot.Instance.Rig.SnapNext();
    }

    public bool Pray(string statueId, Vector3 respawnFeet)
    {
        bool first = _statues.Add(statueId);
        _respawnStatue = statueId;
        _spawn = respawnFeet;
        _hp = MaxHp;
        _flask = FlaskMax;
        if (first)
            _deeds.Add("statue");
        Save();
        return first;
    }

    // ── 무명록: 이름·행적·각인·이야기 값 ─────────────────

    public void SetHeroName(string name)
    {
        _name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        Save();
    }

    public int Deed(string key) => _deeds.Get(key);
    public void CountDeed(string key, int by = 1) => _deeds.Add(key, by);
    public bool HeldJob(string jobId) => jobId == _job.Id || _pastJobs.Contains(jobId);
    public int EngraveStage(string jobId) => EngraveDef.Instance.Stage(_deeds.Get("engrave:" + jobId));
    public float EngraveProgress(string jobId) => EngraveDef.Instance.Progress(_deeds.Get("engrave:" + jobId));
    public int Var(string key) => key != null && _vars.TryGetValue(key, out int v) ? v : 0;
    public bool HasFlag(string flag) => Var(flag) != 0;

    public void SetVar(string key, int value)
    {
        if (Var(key) == value)
            return;
        _vars[key] = value;
        Save();
    }

    public void AddShards(int n)
    {
        _shards = Mathf.Max(0, _shards + n);
    }

    /// <summary>체력을 채운다 (이야기 연출·나래의 등불)</summary>
    public void Heal(float amount) => _hp = Mathf.Min(MaxHp, _hp + amount);

    /// <summary>
    /// 적을 쓰러뜨렸다: 경험치, 행적, 각인. 제 레벨보다 한참 낮은 적은 각인에 세지 않는다 (규칙 3 — 유효한 것만).
    /// </summary>
    public void OnKill(Enemy e)
    {
        bool low = e.Level < _level - T.EngraveLowLevelGap;
        _deeds.Add("kill");
        _deeds.Add("kill:" + e.Def.Id);
        if (GameRoot.Instance.DayCycle.IsNight)
            _deeds.Add("kill_night");
        if (e.Hushed)
        {
            _deeds.Add("kill_hushed");
            AddShards(e.Def.Boss ? 20 : 1);
        }
        if (e.Def.Boss)
            _deeds.Add("boss:" + e.Def.Id);
        _flawless++;
        _deeds.Max("flawless", _flawless);
        if (!low || e.Def.Boss)
        {
            int before = EngraveStage(_job.Id);
            _deeds.Add("engrave:" + _job.Id, e.Def.Boss ? T.EngraveBossPoints : 1);
            int after = EngraveStage(_job.Id);
            if (after > before)
                AnnounceEngrave(after);
        }
        GrantExp(Mathf.Max(1, Mathf.RoundToInt(e.ExpValue * (low ? T.LowLevelExp : 1f))));
    }

    private void AnnounceEngrave(int stage)
    {
        var pal = CombatFx.PaletteOf(_job.Palette);
        CombatFx.Shockwave(GlobalPosition, 3f, 0.6f, pal);
        CombatFx.SparkleBurst(GlobalPosition + Vector3.Up * 1f, 40, 1.4f, pal);
        Sfx.Play("impactBell_heavy", -5f, 1.1f, 0f, "UI");
        _hp = MaxHp;
        Hud.Announce($"{_job.Name} — {EngraveDef.Instance.Title(stage)}", "이름이 한 겹 더 깊이 새겨졌다", pal.Bright);
        Save();
    }

    /// <summary>이슬 한 모금: 서서히 찬다. 맞으면 끊긴다</summary>
    private void TryDrink()
    {
        if (_flask <= 0 || _healLeft > 0f || _hp >= MaxHp || !IsAlive)
        {
            if (_flask <= 0 && _hp < MaxHp)
                Sfx.Play("error", -12f, 1f, 0f, "UI");
            return;
        }
        _flask--;
        _healLeft = T.FlaskTime;
        var pal = CombatFx.PaletteOf("leaf");
        CombatFx.SparkleBurst(GlobalPosition + Vector3.Up * 0.9f, 18, 0.8f, pal);
        Sfx.Play("drop", -4f, 1.2f, 0.05f, "Hero");
        _flash = 0.6f;
        _flashColor = pal.Bright;
    }

    public void TravelTo(Vector3 feet)
    {
        Teleport(feet);
        Enter(State.Move);
        _iframes = T.HurtIFrames;
    }

    // ── 레벨·직업 ───────────────────────────────────────

    /// <summary>경험치를 얻는다 (적을 쓰러뜨렸을 때). 넘치면 몇 단계든 오른다 — 오를 때마다 체력이 차고 칸이 열린다</summary>
    public void GrantExp(int amount)
    {
        if (amount <= 0 || !IsAlive)
            return;
        var levels = LevelDef.Instance;
        _exp += amount;
        Hud.Exp(GlobalPosition + Vector3.Up * 2.2f, amount);
        bool up = false;
        while (_level < levels.MaxLevel && _exp >= levels.Need(_level))
        {
            _exp -= levels.Need(_level);
            _level++;
            up = true;
        }
        if (_level >= levels.MaxLevel)
            _exp = 0;
        if (!up)
            return;
        var unlocked = RebuildLoadout();
        _hp = MaxHp;
        _iframes = T.LevelUpIFrames;
        var pal = CombatFx.PaletteOf("thunder");
        CombatFx.Shockwave(GlobalPosition, 2.6f, 0.5f, pal);
        CombatFx.SparkleBurst(GlobalPosition + Vector3.Up * 0.9f, 36, 1.2f, pal);
        Sfx.Play("confirmation", -4f, 1f, 0f, "UI");
        Sfx.Play("impactBell_heavy", -8f, 1.6f, 0f, "UI");
        string sub = unlocked != null ? $"새 기술: {unlocked}" : _job.Id == JobDef.Novice && CanChangeJobSomewhere() ? "이름지기를 찾아가면 이름을 받을 수 있다" : $"{JobName}";
        Hud.Announce($"레벨 {_level}", sub, new Color(1f, 0.92f, 0.5f));
        Save();
    }

    private bool CanChangeJobSomewhere()
    {
        foreach (var j in JobDef.All.Values)
            if (CanBecome(j))
                return true;
        return false;
    }

    /// <summary>이 직업으로 전직할 수 있나 — 레벨과 지금 직업만 본다 (교관 대화가 묻는다)</summary>
    public bool CanBecome(JobDef job) => job.Id != _job.Id && !job.Sealed && job.From.Contains(_job.Id) && _level >= job.RequiredLevel
        && (job.Requires == null || job.Requires.Met(this));

    /// <summary>전직: 원화·평타·스킬 칸이 그 직업으로 바뀌고 체력이 찬다</summary>
    public void ChangeJob(string jobId)
    {
        var job = JobDef.Get(jobId);
        if (!CanBecome(job))
            return;
        _pastJobs.Add(_job.Id);
        _deeds.Add("named");
        _job = job;
        string look = _lookBase;
        _lookBase = null; // SetLook 이 같은 모습이라고 건너뛰지 않게
        SetLook(look, _lookAccent);
        RebuildLoadout();
        _hp = MaxHp;
        _combo = 0;
        Enter(State.Move);
        var pal = CombatFx.PaletteOf(job.Palette);
        CombatFx.Shockwave(GlobalPosition, 4f, 0.7f, pal);
        CombatFx.SparkleBurst(GlobalPosition + Vector3.Up * 1f, 60, 1.8f, pal);
        CombatFx.Flash(Colors.White, 0.6f);
        Sfx.Play("impactBell_heavy", -2f, 0.8f, 0f, "UI");
        Hud.Announce($"이름을 받았다 — {job.Name}", job.Description, pal.Bright);
        Save();
    }

    /// <summary>
    /// 스킬 칸 = 직업이 정한 스킬 중 레벨이 찬 것. 숙련으로 진화한 스킬은 진화형으로 (숙련은 진화형 이름으로 옮겨져 있다).
    /// 새로 열린 스킬 이름을 돌려준다 (없으면 null).
    /// </summary>
    private string RebuildLoadout()
    {
        string unlocked = null;
        for (int i = 0; i < _loadout.Length; i++)
        {
            string id = null;
            if (i < _job.Skills.Count && _level >= _job.Skills[i].Level && SkillDef.All.ContainsKey(_job.Skills[i].Id))
            {
                id = _job.Skills[i].Id;
                while (SkillDef.All[id].Mastery?.Evolution?.Into is string into && SkillDef.All.ContainsKey(into) && _mastery.Get(into) > 0f)
                    id = into;
            }
            if (id != null && _loadout[i] == null && SkillDef.All.TryGetValue(id, out var def))
                unlocked = def.Name;
            _loadout[i] = id;
        }
        return unlocked;
    }

    // ── 매 물리 프레임 ─────────────────────────────────────

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta * GameRoot.Instance.HeroScale;
        if (dt <= 0f)
            return; // 히트스톱
        _stateTime += dt;
        _dashCooldown -= dt;
        _dodgeBuffer -= dt;
        _attackBuffer -= dt;
        _dashAge += dt;
        _guardAge += dt;
        _iframes -= dt;
        _skillBufferTime -= dt;
        foreach (var id in _loadout)
            if (id != null && _cooldowns.ContainsKey(id))
                _cooldowns[id] -= dt;
        if (Pressed(Controls.Heal))
            TryDrink();
        if (_healLeft > 0f)
        {
            _healLeft -= dt;
            _hp = Mathf.Min(MaxHp, _hp + MaxHp * T.FlaskHeal / T.FlaskTime * dt);
        }

        Vector2 stick = GameRoot.Instance.InputLocked ? Vector2.Zero : Input.GetVector(Controls.Left, Controls.Right, Controls.Up, Controls.Down);
        var wish = new Vector3(stick.X, 0f, stick.Y); // 화면 위 = 북(-Z)
        if (Pressed(Controls.Dodge))
            _dodgeBuffer = T.InputBuffer;
        if (Pressed(Controls.Attack))
            _attackBuffer = T.InputBuffer;
        if (Pressed(Controls.Guard))
        {
            _guardAge = 0f;
            // 막기를 누르면 가장 가까운 적 쪽으로 몸을 돌린다 (8방향이라 정확히 겨누기 어렵다 — 록온 대신)
            var threat = NearestEnemy(FacingVector(), T.GuardTurnRange, 180f, threatsOnly: true)
                         ?? NearestEnemy(FacingVector(), T.GuardTurnRange, 180f);
            if (threat != null && _state is State.Move or State.Guard)
                Face(Flat(threat.GlobalPosition - GlobalPosition), snap: true);
        }
        bool guardHeld = !GameRoot.Instance.InputLocked && Input.IsActionPressed(Controls.Guard);
        for (int i = 0; i < Controls.SkillSlots.Length; i++)
        {
            if (Pressed(Controls.SkillSlots[i]))
            {
                _skillBuffer = i;
                _skillBufferTime = T.InputBuffer;
            }
        }

        switch (_state)
        {
            case State.Move:
                if (_dodgeBuffer > 0f && _dashCooldown <= 0f) { StartDash(wish); break; }
                if (TryStartSkill(wish)) break;
                if (_attackBuffer > 0f) { StartAttack(0, wish, counter: false); break; }
                if (guardHeld) { Enter(State.Guard); break; }
                Run(wish, 1f, dt);
                break;

            case State.Guard:
                if (_dodgeBuffer > 0f && _dashCooldown <= 0f) { StartDash(wish); break; }
                if (TryStartSkill(wish)) break;
                if (_attackBuffer > 0f) { StartAttack(0, wish, counter: false); break; }
                if (!guardHeld) { Enter(State.Move); break; }
                Run(wish, T.GuardMoveScale, dt);
                break;

            case State.Attack:
                UpdateAttack(wish, dt);
                break;

            case State.Skill:
                _skill.Update(dt);
                // 돌진 이벤트가 몸을 밀고, 아니면 멈춘다
                _vel = _skill.DashVelocity.LengthSquared() > 0f
                    ? _skill.DashVelocity
                    : _vel.MoveToward(Vector3.Zero, T.RunSpeed / T.DecelTime * dt);
                if (_skill.DashVelocity.LengthSquared() > 0f)
                {
                    _ghostTimer -= dt;
                    if (_ghostTimer <= 0f)
                    {
                        _ghostTimer = T.GhostInterval;
                        _sprite.SpawnGhost(CombatFx.PaletteOf(_skill.Def.Palette).Mid, T.GhostLife);
                    }
                }
                if (_skill.Cancelable && _dodgeBuffer > 0f && _dashCooldown <= 0f) { StartDash(wish); break; }
                if (_skill.Done)
                    Enter(State.Move);
                break;

            case State.Dash:
                // 앞부분은 최고속, 끝으로 가며 살짝 줄여 착지감을 준다
                float k = _stateTime / T.DashTime;
                _vel = _dashDir * T.DashSpeed * (k < 0.7f ? 1f : Mathf.Lerp(1f, 0.35f, (k - 0.7f) / 0.3f));
                _ghostTimer -= dt;
                if (_ghostTimer <= 0f)
                {
                    _ghostTimer = T.GhostInterval;
                    bool witch = GameRoot.Instance.WorldSlowed;
                    _sprite.SpawnGhost(witch ? new Color(0.75f, 0.45f, 1f) : new Color(0.35f, 0.75f, 1f),
                        witch ? T.WitchGhostLife : T.GhostLife);
                }
                if (_stateTime >= T.DashTime)
                {
                    // 회피 중 누른 공격 → 착지 없이 바로 반격
                    if (_attackBuffer > 0f)
                        StartAttack(Mathf.Min(1, _job.Attack.Count - 1), wish, counter: true);
                    else
                        Enter(State.DashRecover);
                }
                break;

            case State.DashRecover:
                _vel = _vel.MoveToward(Vector3.Zero, T.RunSpeed / T.DecelTime * dt);
                if (TryStartSkill(wish)) break;
                if (_attackBuffer > 0f) { StartAttack(Mathf.Min(1, _job.Attack.Count - 1), wish, counter: true); break; }
                if (_stateTime >= T.DashRecovery)
                    Enter(State.Move);
                break;

            case State.Hurt:
                _vel = _vel.MoveToward(Vector3.Zero, 20f * dt);
                if (_stateTime >= T.HurtTime)
                    Enter(State.Move);
                break;

            case State.Dead:
                _vel = _vel.MoveToward(Vector3.Zero, 20f * dt);
                if (_stateTime >= 2.2f)
                    Respawn();
                break;
        }

        StepOnGround(this, ref _vel, dt, GameRoot.Instance.HeroScale);
    }

    /// <summary>
    /// 캐릭터 한 걸음 (주인공·몬스터 공용): 절벽·깊은 물을 피하고, 건물·나무에 미끄러지고, 발을 지면에 붙인다.
    /// timeScale 은 MoveAndSlide 가 실제 delta 로 움직이기 때문에 속도에 곱한다.
    /// </summary>
    public static void StepOnGround(CharacterBody3D body, ref Vector3 vel, float dt, float timeScale)
    {
        var world = GameRoot.Instance.World;
        vel = world.ConstrainMove(body.GlobalPosition, vel, dt, T.WadeDepth, T.MaxSlope);
        body.Velocity = new Vector3(vel.X, 0f, vel.Z) * timeScale;
        body.MoveAndSlide();
        Vector3 p = body.GlobalPosition;
        p.Y = world.WalkHeightAt(p.X, p.Z);
        body.GlobalPosition = p;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta * GameRoot.Instance.HeroScale;
        var world = GameRoot.Instance.World;
        Vector3 feet = GlobalPosition;
        float speed = new Vector2(_vel.X, _vel.Z).Length();

        if (_state is State.Move or State.Guard)
        {
            if (_state == State.Guard)
                _sprite.PlayOnce(Pick("guard"), 0.1f, keepIfSame: true);
            else if (speed > 0.4f)
            {
                // 발이 땅에서 미끄러지지 않도록 걸음 폭에 맞춰 애니 속도를 정한다
                _sprite.Play(Pick("run"), speed / T.StrideLength * _sprite.FrameCount, true);
                FootstepCheck(feet, world);
            }
            else
            {
                _sprite.Play(Pick("idle"), T.IdleFps, true);
                _lastStepFrame = -1;
            }
        }
        if (GameRoot.Instance.MenuOpen)
            _sprite.Dir = (int)(Time.GetTicksMsec() / 700 % 8); // 모습 고르기: 천천히 제자리에서 돈다
        if (_spinLeft > 0f)
        {
            _spinLeft -= dt;
            int step = Mathf.Min(7, (int)((1f - _spinLeft / _spinTime) * 8f));
            _sprite.Dir = _spinLeft > 0f ? (_spinDir0 + 4 + step) % 8 : _spinDir0;
        }
        _flash = Mathf.MoveToward(_flash, 0f, (float)delta * 6f);
        // 무적 동안 깜빡인다
        bool blink = _iframes > 0f && _state != State.Dash && Mathf.PosMod(_iframes, 0.12f) < 0.05f;
        _sprite.SetFlash(Mathf.Max(_flash, blink ? 0.45f : 0f), _flash > 0f ? _flashColor : Colors.White);
        _sprite.Advance(dt);
        _sprite.PlaceAt(feet, world.WalkHeightAt(feet.X, feet.Z));
        // 앞을 가린 나무·지붕을 바둑판으로 비울 자리 (sprite.gdshader)
        RenderingServer.GlobalShaderParameterSet(Uniform.HeroScreen, GameRoot.Instance.View.Camera.UnprojectPosition(_sprite.GlobalPosition + Vector3.Up * SeeThroughHeight));
        RenderingServer.GlobalShaderParameterSet(Uniform.HeroZ, feet.Z);

        // 카메라: 달리는 쪽을 조금 앞서 본다
        Vector3 lookTarget = _state == State.Move ? _vel / T.RunSpeed * T.LookAhead : _look;
        _look = _look.Lerp(lookTarget, 1f - Mathf.Exp(-T.LookAheadRate * (float)delta));
        GameRoot.Instance.Rig.Target = feet + _look;
    }

    // ── 이동 ────────────────────────────────────────────

    private void Run(Vector3 wish, float scale, float dt)
    {
        // 가속·감속을 따로: 출발은 빠르게, 멈춤은 더 빠르게
        Vector3 target = wish * T.RunSpeed * scale;
        float rate = (target.LengthSquared() > _vel.LengthSquared() ? T.RunSpeed / T.AccelTime : T.RunSpeed / T.DecelTime) * dt;
        _vel = _vel.MoveToward(target, rate);
        if (wish.LengthSquared() > 0.01f)
            Face(wish);
    }

    // ── 공격 ────────────────────────────────────────────

    private void StartAttack(int step, Vector3 wish, bool counter)
    {
        _attackBuffer = 0f;
        _combo = step;
        _counter = counter;
        _hitDone = _slashDone = _echoDone = false;
        var s = _job.Attack[step];
        // 방향: 입력 쪽, 없으면 가까운 적 쪽 (8방향이라 정확히 겨누기 어렵다 — 살짝 도와준다)
        Vector3 aim = wish.LengthSquared() > 0.01f ? wish.Normalized() : FacingVector();
        // 투사체는 사거리 안 앞쪽 적을 겨눈다 (좁은 원뿔), 근접은 칼이 닿을 만큼만
        var target = s.Projectile != null ? NearestEnemy(aim, s.Projectile.Range, 35f) : NearestEnemy(aim, s.Reach + 1.2f, 70f);
        if (target != null)
            aim = Flat(target.GlobalPosition - GlobalPosition).Normalized();
        Face(aim, snap: true);
        _carry = _state == State.Move ? _vel * T.RunCarry : Vector3.Zero;
        Enter(State.Attack);
        // 빚은 몸(무기를 들지 않은 원화)에는 활·지팡이 동작이 없다 — 대신 쓸 동작(AnimAlt)으로
        string anim = _forged && s.AnimAlt != null && _sprite.Has(s.AnimAlt) ? s.AnimAlt
            : _sprite.Has(s.Anim) ? s.Anim : s.AnimAlt != null && _sprite.Has(s.AnimAlt) ? s.AnimAlt : null;
        if (anim != null)
            _sprite.PlayKeyed(anim, s.Duration, s.StartFrame, s.HitFrame, s.HitAt);
        else
            _sprite.PlayOnce("rot", s.Duration);
        Sfx.Play(s.Sound, -3f, step % 2 == 1 ? 1.12f : 1f, 0.06f, "Hero");
        Sfx.Play("cloth", -12f, 1f, 0.1f, "Hero");
        if (_job.Fx.Ghost)
            _sprite.SpawnGhost(CombatFx.PaletteOf(_job.Palette).Bright, T.GhostLife * 0.7f);
    }

    private void UpdateAttack(Vector3 wish, float dt)
    {
        var s = _job.Attack[_combo];
        float t = _stateTime;
        // 판정 전까지 앞으로 내딛고, 이어받은 달리기 속도는 금방 잦아든다
        const float LungeFrom = 0.04f;
        Vector3 fwd = FacingVector();
        float lunge = t > LungeFrom && t < s.HitAt ? s.Lunge / (s.HitAt - LungeFrom) : 0f;
        _carry = _carry.MoveToward(Vector3.Zero, T.RunSpeed * 4f * dt);
        _vel = fwd * lunge + _carry;

        // 칼이 지나가는 궤적은 판정 직전에 그린다
        if (!_slashDone && t >= s.HitAt - SlashLead(s))
        {
            _slashDone = true;
            DrawSlash(s);
        }
        // 메아리: 같은 궤적이 조금 더 크고 가늘게 한 번 더 따라 긋는다 (JobDef.Fx.Echo)
        if (!_echoDone && _job.Fx.Echo > 0f && t >= s.HitAt - SlashLead(s) + _job.Fx.Echo)
        {
            _echoDone = true;
            DrawSlash(s, echo: true);
        }
        if (!_hitDone && t >= s.HitAt)
        {
            _hitDone = true;
            if (s.Projectile != null)
                Shoot(s);
            else
                ResolveHits(s);
        }
        if (t >= s.CancelAt)
        {
            if (_dodgeBuffer > 0f && _dashCooldown <= 0f) { StartDash(wish); return; }
            if (TryStartSkill(wish)) return;
            if (_attackBuffer > 0f && _combo < _job.Attack.Count - 1 && t / s.Duration >= T.ComboBufferFrom)
            {
                StartAttack(_combo + 1, wish, counter: false);
                return;
            }
        }
        if (t >= s.Duration)
            Enter(State.Move);
    }

    private static float SlashLead(JobDef.StepDef s) => s.Heavy ? 0.1f : 0.05f;

    private void DrawSlash(JobDef.StepDef s, bool echo = false)
    {
        Vector3 f = FacingVector();
        Vector3 left = Vector3.Up.Cross(f);
        Vector3 chest = GlobalPosition + Vector3.Up * 0.85f + f * 0.2f;
        float thick = (_counter ? 1.45f : 1f) * s.SlashScale;
        float r = s.Reach * Mathf.Min(s.SlashScale, 1.1f);
        float heavyThick = 1.35f * s.SlashScale, stabThick = 1.6f;
        if (echo)
        {
            r *= JobFx.EchoRadius;
            thick = JobFx.EchoThick(thick);
            heavyThick = JobFx.EchoThick(heavyThick);
            stabThick = JobFx.EchoThick(stabThick);
        }
        var pal = CombatFx.PaletteOf(_job.Palette);
        bool ink = JobFx.Ink(_job);
        switch (s.Slash)
        {
            case "horizontal": // 오른쪽 → 왼쪽, 왼쪽이 살짝 올라가는 사선
                CombatFx.Slash(chest, f, (left + Vector3.Up * 0.35f).Normalized(), r, 200f, 0.22f, false, thick, pal, ink);
                break;
            case "reverse": // 왼쪽 → 오른쪽 역베기
                CombatFx.Slash(chest, f, (left - Vector3.Up * 0.3f).Normalized(), r, 200f, 0.22f, true, thick, pal, ink);
                break;
            case "vertical": // 머리 위에서 앞 땅으로 내려찍기
                CombatFx.Slash(GlobalPosition + Vector3.Up * 0.5f, (f + Vector3.Down * 0.2f).Normalized(), Vector3.Up,
                    r, 170f, 0.26f, true, heavyThick, pal, ink);
                break;
            case "stab": // 앞으로 찌르기: 좁은 호를 길게
                CombatFx.Slash(chest, f, (left + Vector3.Up * 0.1f).Normalized(), r, 40f, 0.16f, false, stabThick, pal, ink);
                break;
        }
    }

    private float StepDamage(JobDef.StepDef s) =>
        s.Damage * Power * (_counter ? T.CounterDamage : 1f) * (GameRoot.Instance.WorldSlowed ? T.WitchDamage : 1f);

    /// <summary>투사체 평타: 가슴께에서 바라보는 쪽으로 (가까운 적을 이미 향해 섰다)</summary>
    private void Shoot(JobDef.StepDef s)
    {
        Projectile.Fire(GlobalPosition, FacingVector(), s.Projectile, StepDamage(s), s.Heavy || _counter, CombatFx.PaletteOf(_job.Palette));
        JobFx.Cast(_job, GlobalPosition, FacingVector(), s.Heavy || _counter);
    }

    private void ResolveHits(JobDef.StepDef s)
    {
        Vector3 f = FacingVector();
        float damage = StepDamage(s);
        bool heavy = s.Heavy || _counter;
        int hits = 0;
        foreach (var e in Enemy.All.ToArray())
        {
            if (!e.Alive)
                continue;
            Vector3 to = Flat(e.GlobalPosition - GlobalPosition);
            float d = to.Length();
            bool inArc = d - e.Radius <= s.Reach && (d < 0.4f || Mathf.RadToDeg(f.AngleTo(to)) <= s.HalfArcDeg);
            // 내려찍기는 떨어진 자리 둘레도 친다
            bool inSlam = s.Heavy && Flat(e.GlobalPosition - (GlobalPosition + f * 1.4f)).Length() - e.Radius <= 1.5f;
            if (!inArc && !inSlam)
                continue;
            hits++;
            Strike(e, damage, d > 0.01f ? to / d : f, heavy);
        }
        if (hits > 0)
            ApplyImpact(s.Heavy ? T.HitHeavy : _counter ? T.HitCounter : T.HitNormal, f);
        if (s.Heavy)
        {
            // 맞든 안 맞든 땅을 찍는다
            Vector3 slam = GlobalPosition + f * 1.3f;
            Dust.Puff(slam, 12, f * 2.5f);
            Dust.Puff(slam, 8, Vector3.Up.Cross(f) * 2.5f);
            Dust.Puff(slam, 8, -Vector3.Up.Cross(f) * 2.5f);
            Sfx.Play("chop", -4f, 0.6f, 0.05f, "Hero");
            if (hits == 0)
                CombatFx.Shake(0.22f);
        }
    }

    /// <summary>적 하나를 벤다: 피해 + 불꽃 + 임팩트 별 + 베는 소리. 기본 공격과 스킬이 같이 쓴다.</summary>
    public void Strike(Enemy e, float damage, Vector3 dir, bool heavy)
    {
        bool wasStaggered = e.Staggered;
        e.TakeHit(damage, dir, heavy);
        Vector3 at = e.GlobalPosition + Vector3.Up * 0.5f - dir * e.Radius * 0.5f;
        // 불꽃과 별은 지금 가진 이름의 빛으로 물든다
        var pal = CombatFx.PaletteOf(_job.Palette);
        CombatFx.Sparks(at, dir, heavy ? CombatFx.Spark.Heavy : CombatFx.Spark.Hit, pal);
        CombatFx.Star(at, heavy || wasStaggered ? 1.0f : 0.6f, heavy ? pal.Bright : Colors.White, heavy ? 0.14f : 0.08f);
        JobFx.Hit(_job, e, at, dir, heavy);
        Sfx.Play("knifeSlice", -5f, 1.1f, 0.1f, "Hero");
    }

    /// <summary>한 번 휘두름(또는 스킬 판정 하나)에 한 번: 히트스톱·킥·흔들림 + 맞는 소리 층.</summary>
    public void ApplyImpact(Impact impact, Vector3 kickDir)
    {
        bool heavy = impact.HitStop >= T.HitHeavy.HitStop;
        Apply(impact, kickDir);
        Sfx.Play(heavy ? "impactPunch_heavy" : "impactPunch_medium", -2f, 1f, 0.08f, "Hero");
        if (heavy)
        {
            Sfx.Play("impactPlate_heavy", -6f, 0.7f, 0.05f, "Hero");
            CombatFx.Flash(Colors.White, 0.3f); // 임팩트 프레임
        }
    }

    // ── 스킬 ────────────────────────────────────────────

    /// <summary>버퍼에 스킬 입력이 있고 대기시간이 끝났으면 쓴다.</summary>
    private bool TryStartSkill(Vector3 wish)
    {
        if (_skillBuffer < 0 || _skillBufferTime <= 0f)
            return false;
        string id = _loadout[_skillBuffer];
        _skillBuffer = -1;
        if (id == null || !SkillDef.All.TryGetValue(id, out var def) || CooldownRemaining(id) > 0f)
            return false;
        _cooldowns[id] = def.Cooldown;
        Vector3 aim = wish.LengthSquared() > 0.01f ? wish.Normalized() : FacingVector();
        var target = NearestEnemy(aim, 4f, 70f);
        if (target != null)
            aim = Flat(target.GlobalPosition - GlobalPosition).Normalized();
        Face(aim, snap: true);
        _carry = Vector3.Zero;
        Enter(State.Skill);
        _ghostTimer = 0f;
        if (_sprite.Has(def.Anim))
            _sprite.PlayKeyed(def.Anim, def.Duration, 0, def.AnimHitFrame, def.AnimHitAt);
        _skill.Start(def);
        return true;
    }

    /// <summary>메뉴가 떠 있으면 전투 입력은 없는 것으로 친다.</summary>
    private static bool Pressed(StringName action) => !GameRoot.Instance.InputLocked && Input.IsActionJustPressed(action);

    // ── 숙련·진화·히든 ──────────────────────────────────

    /// <summary>스킬이 산 적에게 처음 맞았다 (SkillRunner 가 시전마다 한 번 부른다).</summary>
    public void CreditSkill(SkillDef def, Enemy target)
    {
        var r = _mastery.Use(def, target.GetInstanceId(), Time.GetTicksMsec() / 1000.0);
        if (r.EvolvedInto != null && SkillDef.All.TryGetValue(r.EvolvedInto, out var next))
            Evolve(def, next);
    }

    /// <summary>칸의 스킬을 진화형으로 바꾼다. 지금 쓰는 시전은 옛 모습 그대로 끝난다.</summary>
    private void Evolve(SkillDef from, SkillDef to)
    {
        int slot = System.Array.IndexOf(_loadout, from.Id);
        if (slot < 0)
            return;
        _loadout[slot] = to.Id;
        _mastery.Transfer(from.Id, to.Id);
        _cooldowns[to.Id] = CooldownRemaining(from.Id);
        var pal = CombatFx.PaletteOf(to.Palette);
        CombatFx.Shockwave(GlobalPosition, 3.5f, 0.6f, pal);
        CombatFx.SparkleBurst(GlobalPosition + Vector3.Up * 0.9f, 40, 1.4f, pal);
        Sfx.Play("impactBell_heavy", -2f, 0.8f, 0f, "UI");
        Hud.Announce(from.Mastery.Evolution.Announce, $"{from.Name} → {to.Name}", pal.Bright);
        Save();
    }

    /// <summary>히든 조건이 걸린 사건. 무엇이 몇 번인지는 data/skills 만 안다 (규칙 4).</summary>
    private void CountHidden(string counter)
    {
        var learned = _mastery.Count(counter, _hp / MaxHp);
        if (learned == null)
            return;
        Sfx.Play("impactBell_heavy", -4f, 1.2f, 0f, "UI");
        Hud.Announce(learned.Learn.Announce, learned.Name, new Color(0.85f, 0.8f, 1f));
        Save();
    }

    /// <summary>투시 타원의 가운데 높이 (m) — 가슴께</summary>
    private const float SeeThroughHeight = 0.9f;

    private string _spinAnim;
    private int _spinFrame, _spinDir0;
    private float _spinTime, _spinLeft;

    /// <summary>
    /// 회전 베기: 칼을 뻗은 칸에 세운 채 8방향 그림을 차례로 넘겨 한 바퀴 돈다 (젤다식).
    /// 등 뒤에서 시작해 오른쪽 → 앞 → 왼쪽 — 회전 궤적(CombatFx.Slash spin)이 쓸고 가는 순서와 같다.
    /// </summary>
    public void Spin(string anim, int frame, float time)
    {
        if (!_sprite.Has(anim))
            return;
        _spinAnim = anim;
        _spinFrame = frame;
        _spinTime = _spinLeft = Mathf.Max(time, 0.05f);
        _spinDir0 = _sprite.Dir;
        _sprite.Hold(anim, frame);
    }

    // ── 회피 ────────────────────────────────────────────

    private void StartDash(Vector3 wish)
    {
        _dodgeBuffer = 0f;
        _dashCooldown = T.DashCooldown;
        _dashAge = 0f;
        _perfectUsed = false;
        // 방향 입력이 없으면 바라보던 쪽으로
        _dashDir = wish.LengthSquared() > 0.01f ? wish.Normalized() : FacingVector();
        Face(_dashDir, snap: true);
        Enter(State.Dash);
        _ghostTimer = 0f;
        _sprite.PlayOnce(Pick("dash"), T.DashTime);
        Sfx.Play("dash", -4f, 1f, 0.08f, "Hero");
        Sfx.Play("cloth", -8f, 1f, 0.1f, "Hero");
        Dust.Puff(GlobalPosition, 8, -_dashDir * 2.2f);
    }

    /// <summary>적 공격이 닿을 뻔한 순간 막 돌진했으면 완벽 회피. 적이 판정 프레임마다 묻는다.</summary>
    public bool TryPerfectDodge(Enemy src)
    {
        if (_state != State.Dash || _dashAge > _mastery.Passive(p => p.PerfectDodgeWindow, T.PerfectDodgeWindow))
            return false;
        if (!_perfectUsed)
        {
            _perfectUsed = true;
            CountHidden("perfect_dodge");
            _deeds.Add("perfect_dodge");
            Apply(T.PerfectDodge, Vector3.Zero);
            GameRoot.Instance.SlowWorld(T.WitchScale, T.WitchTime);
            CombatFx.Ring(GlobalPosition + Vector3.Up * 0.8f);
            CombatFx.Flash(new Color(0.7f, 0.5f, 1f), 0.25f);
            Sfx.Play("impactBell_heavy", -2f, 0.55f, 0.02f, "Hero");
            Sfx.Play("dash", -3f, 0.7f, 0.02f, "Hero");
        }
        return true;
    }

    // ── 맞기 ────────────────────────────────────────────

    /// <summary>공격을 받는다. from = 공격이 온 자리 (적의 몸, 탄, 내려찍은 자리) — 그쪽을 보고 막아야 막힌다</summary>
    public HitResult ReceiveHit(Vector3 from, float damage, Vector3 dir)
    {
        if (!IsAlive || Hidden)
            return HitResult.Dodged;
        if ((_state == State.Dash && _dashAge <= T.DashIFrames) || _iframes > 0f || (_state == State.Skill && _skill.Invulnerable))
            return HitResult.Dodged;
        Vector3 toEnemy = Flat(from - GlobalPosition);
        toEnemy = toEnemy.LengthSquared() > 0.0001f ? toEnemy.Normalized() : -Flat(dir).Normalized();
        bool facingIt = Mathf.RadToDeg(FacingVector().AngleTo(toEnemy)) <= T.GuardArcDeg;
        Vector3 contact = GlobalPosition + Vector3.Up * 0.8f + toEnemy * 0.45f;

        if (_state == State.Guard && facingIt)
        {
            if (_guardAge <= _mastery.Passive(p => p.ParryWindow, T.ParryWindow))
            {
                CountHidden("parry");
                _deeds.Add("parry");
                // 패링: 큰 불꽃 다발 + 높고 큰 금속음 — 소리만 들어도 막기와 구분된다 (세키로)
                Apply(T.Parried, toEnemy);
                CombatFx.Sparks(contact, toEnemy, CombatFx.Spark.Parry);
                CombatFx.Star(contact, 1.3f, new Color(1f, 0.9f, 0.5f), 0.2f);
                CombatFx.Flash(Colors.White, 0.55f);
                Sfx.Play("impactBell_heavy", 0f, 1.35f, 0.03f, "Hero");
                Sfx.Play("impactMetal_heavy", -2f, 1.25f, 0.05f, "Hero");
                _guardAge = 99f; // 한 번 패링하면 다음은 다시 눌러야
                return HitResult.Parried;
            }
            // 막기: 작은 주황 불꽃 + 낮은 금속음, 뒤로 밀린다
            Apply(T.Blocked, dir);
            CombatFx.Sparks(contact, toEnemy, CombatFx.Spark.Block);
            Sfx.Play("impactMetal_light", -3f, 0.85f, 0.06f, "Hero");
            _vel = Flat(dir).Normalized() * T.BlockPush;
            return HitResult.Blocked;
        }

        if (_hp - damage <= 0f && LastStand != null && LastStand())
            damage = _hp - 1f;   // 쓰러지는 대신 — 이름이 지워진다 (World/Story 의 위기)
        _hp = Mathf.Max(0f, _hp - damage);
        _flawless = 0;
        _healLeft = 0f;
        Apply(T.HeroHurt, dir);
        CombatFx.Vignette(new Color(0.8f, 0.1f, 0.1f), 0.8f, 0.12f);
        CombatFx.Sparks(contact, dir, CombatFx.Spark.Hit);
        Sfx.Play("impactPunch_heavy", -1f, 0.8f, 0.05f, "Hero");
        _flash = 1f;
        _flashColor = new Color(1f, 0.35f, 0.3f);
        _vel = Flat(dir).Normalized() * T.HurtPush;
        _carry = Vector3.Zero;
        _iframes = T.HurtIFrames;
        if (_hp <= 0f)
        {
            Enter(State.Dead);
            _deeds.Add("death");
            _sprite.PlayOnce(Pick("hurt"), 0.5f);
            CombatFx.Vignette(new Color(0.1f, 0f, 0f), 1f, 2f);
        }
        else
        {
            Enter(State.Hurt);
            _sprite.PlayOnce(Pick("hurt"), T.HurtTime);
        }
        return HitResult.Hit;
    }

    private void Respawn()
    {
        _hp = MaxHp;
        _iframes = T.HurtIFrames;
        Teleport(_spawn);
        Enter(State.Move);
    }

    // ── 공용 ────────────────────────────────────────────

    /// <summary>히트스톱 + 카메라 킥 + 흔들림을 한 프레임에 같이 건다.</summary>
    private static void Apply(Impact i, Vector3 kickDir)
    {
        GameRoot.Instance.HitStop(i.HitStop);
        if (i.Kick > 0f)
            CombatFx.Kick(kickDir, i.Kick);
        CombatFx.Shake(i.Trauma);
    }

    private void Enter(State s)
    {
        _state = s;
        _stateTime = 0f;
        if (s == State.DashRecover)
            Dust.Puff(GlobalPosition, 5, _dashDir * 0.8f);
    }

    /// <summary>발이 땅에 닿는 프레임에 발소리 + 먼지.</summary>
    private void FootstepCheck(Vector3 feet, World.WorldData world)
    {
        int f = _sprite.Frame;
        if (f == _lastStepFrame)
            return;
        _lastStepFrame = f;
        if (System.Array.IndexOf(T.RunStepFrames, f) < 0)
            return;
        Sfx.Play(StepSound(world.GroundAt(feet.X, feet.Z)), -11f, 1f, 0.1f, "Hero");
        if (world.WaterAt(feet.X, feet.Z) <= World.WorldData.NoWater + 1f)
            Dust.Puff(feet, 3, -_vel.Normalized() * 0.6f);
    }

    /// <summary>지면 번호는 tools/mapgen/mapgen.py 의 GRASS..PEBBLE 순서.</summary>
    private static string StepSound(byte ground) => ground switch
    {
        3 or 5 or 8 or 9 => "footstep",          // 흙·모래·자갈·조약돌
        4 => "footstep_concrete",                 // 돌길
        _ => "footstep_grass",
    };

    /// <summary>시트가 아직 없으면 원화로 대신한다 (PixelLab 생성 중일 때).</summary>
    private string Pick(string anim) => _sprite.Has(anim) ? anim : "rot";

    private void Face(Vector3 dir, bool snap = false)
    {
        float angle = Mathf.RadToDeg(Mathf.Atan2(dir.X, dir.Z));
        // 대각선 경계에서 스틱이 떨리면 방향이 깜빡인다 — 지금 방향 칸을 조금 넓게 봐 준다
        float current = _sprite.Dir * 45f;
        float diff = Mathf.Abs(Mathf.Wrap(angle - current, -180f, 180f));
        if (!snap && diff < 22.5f + T.FacingHysteresisDeg)
            return;
        _sprite.Dir = Mathf.PosMod(Mathf.RoundToInt(angle / 45f), 8);
    }

    private Vector3 FacingVector()
    {
        // 회전 베기 중에는 그림만 돌고 몸이 보는 쪽은 그대로
        float a = Mathf.DegToRad((_spinLeft > 0f ? _spinDir0 : _sprite.Dir) * 45f);
        return new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
    }

    private Enemy NearestEnemy(Vector3 aim, float range, float halfArcDeg, bool threatsOnly = false)
    {
        Enemy best = null;
        float bestD = range;
        foreach (var e in Enemy.All)
        {
            if (!e.Alive || (threatsOnly && !e.Threatening))
                continue;
            Vector3 to = Flat(e.GlobalPosition - GlobalPosition);
            float d = to.Length();
            if (d < bestD && (d < 0.3f || Mathf.RadToDeg(aim.AngleTo(to)) <= halfArcDeg))
            {
                best = e;
                bestD = d;
            }
        }
        return best;
    }

    private static Vector3 Flat(Vector3 v) => new(v.X, 0f, v.Z);
}
