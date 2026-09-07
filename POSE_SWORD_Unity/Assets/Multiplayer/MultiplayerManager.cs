using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using PoseSword.Multiplayer;
using TMPro;
using UnityEngine;

// Runs after the controllers' FixedUpdates. Manual simulation gives one explicit
// post-physics boundary for all collision callbacks and simultaneous eliminations.
[DefaultExecutionOrder(10000)]
public class MultiplayerManager : MonoBehaviour
{
    public bool IsHost { get; private set; }
    public bool IsPlaying { get { return phase == "PLAYING"; } }
    public bool Active { get { return config != null; } }
    private MultiplayerConfig config;
    private MatchRules rules;
    private readonly Dictionary<string, SwordBattle> swords = new Dictionary<string, SwordBattle>();
    private readonly Dictionary<string, Rigidbody2D> bodies = new Dictionary<string, Rigidbody2D>();
    private readonly Dictionary<string, string> targets = new Dictionary<string, string>();
    private readonly Dictionary<string, int> sequences = new Dictionary<string, int>();
    private readonly Dictionary<string, float> inputTimes = new Dictionary<string, float>();
    private readonly Dictionary<string, bool> invulnerable = new Dictionary<string, bool>();
    private readonly Dictionary<string, MultiplayerPlayerState> syncTargets = new Dictionary<string, MultiplayerPlayerState>();
    private readonly List<Hit> hits = new List<Hit>();
    private readonly HashSet<string> forfeits = new HashSet<string>();
    private GameObject arena;
    private Sprite wallSprite;
    private string phase = "IDLE";
    private int physicsTick, syncTick, receivedTick = -1;
    private float countdownEnd, nextSync, nextTargetUpdate;
    private SimulationMode2D previousSimulationMode;
    private bool ownsSimulation;
    private BattleCamera battleCamera;

    void Start() { Emit("READY", new MultiplayerCommand()); }

    [UnityEngine.Scripting.Preserve]
    public void InitializeMultiplayer(string json)
    {
        var next = JsonUtility.FromJson<MultiplayerConfig>(json);
        if (next == null || string.IsNullOrEmpty(next.matchId)) return;
        if (config != null && config.matchId == next.matchId) return;
        StopCurrent();
        config = next;
        IsHost = config.isHost;
        phase = "LOADING";
        try
        {
            ValidateConfig();
            var scene = GetComponent<SceneController>();
            var network = GetComponent<NetworkManager>();
            if (scene == null || network == null || network.hostSword == null || scene.hostGenerator == null)
                throw new InvalidOperationException("Missing sword template in scene.");
            scene.StopAllCoroutines();
            scene.autoTestOnStart = false;
            network.enabled = false;
            network.isHost = IsHost;
            scene.hostGenerator.generateOnStart = false;
            scene.clientGenerator.generateOnStart = false;
            network.hostSword.SetActive(false);
            network.clientSword.SetActive(false);
            if (network.swordStage != null) network.swordStage.SetActive(false);
            if (network.komaStage != null) network.komaStage.SetActive(false);
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None)) canvas.enabled = false;
            foreach (var tutorial in FindObjectsByType<TutorialManager>(FindObjectsSortMode.None)) tutorial.enabled = false;
            if (BackgroundManager.Instance != null) BackgroundManager.Instance.enabled = false;
            if (CutinManager.Instance != null) { CutinManager.Instance.StopAllCoroutines(); CutinManager.Instance.enabled = false; }
            Time.timeScale = 1;
            SwordController.isKomaMode = config.gameMode == "1";
            SwordBattle.isRoundStarted = false;
            SwordBattle.matchEnded = false;
            battleCamera = Camera.main == null ? null : Camera.main.GetComponent<BattleCamera>();
            if (battleCamera != null) battleCamera.enabled = false;

            previousSimulationMode = Physics2D.simulationMode;
            Physics2D.simulationMode = SimulationMode2D.Script;
            ownsSimulation = true;
            arena = new GameObject("MultiplayerArena");
            BuildArena();
            rules = new MatchRules(config.players.Select(p => p.playerId).ToArray(), config.players.Select(p => p.swordData.hp).ToArray());
            foreach (var player in config.players) CreateSword(player, network.hostSword, scene.hostGenerator);
            physicsTick = 0; syncTick = 0; receivedTick = -1; nextSync = 0; nextTargetUpdate = 0;
            StartCoroutine(CompleteInitialization());
        }
        catch (Exception e)
        {
            Debug.LogError("Multiplayer initialization failed: " + e);
            string matchId = config.matchId;
            StopCurrent();
            Emit("LOAD_FAILED", new MultiplayerCommand { matchId = matchId });
        }
    }

    void ValidateConfig()
    {
        if (config.players == null || !new[] { 2, 4 }.Contains(config.players.Length) || !new[] { "0", "1" }.Contains(config.gameMode) ||
            !config.players.Any(p => p != null && p.playerId == config.localPlayerId) ||
            config.players.Any(p => p == null || p.swordData == null || string.IsNullOrEmpty(p.playerId)) ||
            config.players.Select(p => p.playerId).Distinct().Count() != config.players.Length ||
            config.players.Select(p => p.slotIndex).OrderBy(i => i).Where((slot, i) => slot != i).Any() ||
            config.players.Select(p => p.spawnIndex).OrderBy(i => i).Where((slot, i) => slot != i).Any())
            throw new ArgumentException("Invalid multiplayer roster.");
        config.players = config.players.OrderBy(p => p.slotIndex).ToArray();
    }

    void CreateSword(MultiplayerPlayerConfig player, GameObject template, SwordGenerator sourceGenerator)
    {
        var obj = Instantiate(template, arena.transform);
        obj.name = "Sword_" + player.playerId;
        obj.transform.position = SpawnPosition(player.spawnIndex);
        obj.transform.rotation = Quaternion.identity;
        var battle = obj.GetComponent<SwordBattle>();
        battle.hpBar = null; battle.delayHpBar = null; battle.nameText = null; battle.hpText = null;
        battle.spGaugeBar = null; battle.spText = null;
        battle.ConfigureMultiplayer(this, player.playerId);
        var controller = obj.GetComponent<SwordController>();
        controller.multiplayer = this;
        controller.isLocalControlled = player.playerId == config.localPlayerId;
        var rb = obj.GetComponent<Rigidbody2D>();
        rb.simulated = false;
        rb.bodyType = IsHost ? RigidbodyType2D.Dynamic : RigidbodyType2D.Kinematic;
        var blade = obj.transform.Find("Blade");
        var generator = obj.AddComponent<SwordGenerator>();
        generator.generateOnStart = false;
        generator.targetBladeWidth = sourceGenerator.targetBladeWidth;
        generator.targetSpriteRenderer = blade.GetComponent<SpriteRenderer>();
        generator.bladeCollider = blade.GetComponent<PolygonCollider2D>();
        generator.swordRigidbody = rb;
        generator.swordBattle = battle;
        generator.handleObject = controller.handleObject;
        generator.GenerateSwordFromJson(JsonUtility.ToJson(player.swordData));
        if (!generator.LastGenerationSucceeded) throw new InvalidOperationException("Could not generate player sword.");
        // Keep extreme photo aspect ratios inside the arena and clear of spawn neighbours.
        var renderer = generator.targetSpriteRenderer;
        float height = renderer.bounds.size.y;
        if (height > 8f) blade.localScale *= 8f / height;
        controller.ApplyPhysicsMode();
        swords.Add(player.playerId, battle); bodies.Add(player.playerId, rb);
        sequences[player.playerId] = 0; inputTimes[player.playerId] = -100;
        targets[player.playerId] = null;
        var marker = new GameObject("PlayerNumber").AddComponent<TextMeshPro>();
        marker.transform.SetParent(obj.transform, false);
        marker.transform.localPosition = new Vector3(0, -0.8f, -0.2f);
        marker.text = "P" + (player.slotIndex + 1);
        marker.fontSize = 6; marker.alignment = TextAlignmentOptions.Center;
        if (sourceGenerator.swordBattle.nameText != null) marker.font = sourceGenerator.swordBattle.nameText.font;
        marker.color = new[] { new Color(.2f, .5f, 1), new Color(1, .25f, .3f), new Color(.2f, .9f, .4f), new Color(.8f, .4f, 1) }[player.slotIndex];
        obj.SetActive(true);
    }

    Vector3 SpawnPosition(int slot)
    {
        if (config.players.Length == 2) return new Vector3(slot == 0 ? -8 : 8, 0, 0);
        if (config.gameMode == "0") return new Vector3(-15 + slot * 10, 0, 0);
        return new Vector3(slot % 2 == 0 ? -8 : 8, slot < 2 ? -6 : 6, 0);
    }

    void BuildArena()
    {
        wallSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height), new Vector2(.5f, .5f), Texture2D.whiteTexture.width);
        float bottom = config.gameMode == "0" ? -4 : -14;
        float top = 24;
        Wall(new Vector2(0, bottom), new Vector2(50, 1));
        Wall(new Vector2(0, top), new Vector2(50, 1));
        Wall(new Vector2(-24, (bottom + top) / 2), new Vector2(1, top - bottom));
        Wall(new Vector2(24, (bottom + top) / 2), new Vector2(1, top - bottom));
    }
    void Wall(Vector2 position, Vector2 size)
    {
        var wall = new GameObject("ArenaWall"); wall.transform.SetParent(arena.transform);
        wall.transform.position = position; wall.transform.localScale = size;
        wall.AddComponent<BoxCollider2D>();
        var renderer = wall.AddComponent<SpriteRenderer>(); renderer.sprite = wallSprite; renderer.color = new Color(.2f, .22f, .27f);
    }

    IEnumerator CompleteInitialization()
    {
        // Wait for deferred collider replacement and all cloned component Starts.
        yield return null;
        Physics2D.SyncTransforms();
        foreach (var rb in bodies.Values) rb.simulated = false;
        UpdateTargets(true);
        Emit("INITIALIZED", new MultiplayerCommand { matchId = config.matchId });
    }

    [UnityEngine.Scripting.Preserve]
    public void BeginMultiplayer(string json)
    {
        var msg = JsonUtility.FromJson<MultiplayerCommand>(json);
        if (!Matches(msg) || !IsHost || phase != "LOADING") return;
        phase = "COUNTDOWN"; countdownEnd = Time.unscaledTime + 3;
    }

    void Update()
    {
        if (config == null) return;
        if (IsHost && phase == "COUNTDOWN" && Time.unscaledTime >= countdownEnd)
        {
            phase = "PLAYING"; SwordBattle.isRoundStarted = true;
            foreach (var rb in bodies.Values) rb.simulated = true;
            Emit("PLAYING", new MultiplayerCommand { matchId = config.matchId });
        }
        if (IsHost && (phase == "COUNTDOWN" || IsPlaying) && Time.unscaledTime >= nextSync)
        {
            nextSync = Time.unscaledTime + 1f / 30;
            Emit("SYNC", Snapshot());
        }
        if (!IsHost)
        {
            foreach (var pair in syncTargets)
            {
                var sword = swords[pair.Key]; var state = pair.Value;
                sword.transform.position = Vector3.Lerp(sword.transform.position, new Vector3(state.x, state.y, 0), 1 - Mathf.Exp(-25 * Time.unscaledDeltaTime));
                sword.transform.rotation = Quaternion.Slerp(sword.transform.rotation, Quaternion.Euler(0, 0, state.rotation), 1 - Mathf.Exp(-25 * Time.unscaledDeltaTime));
            }
        }
    }

    void FixedUpdate()
    {
        if (!IsHost || !IsPlaying) return;
        UpdateTargets(false);
        foreach (var p in swords) invulnerable[p.Key] = p.Value.isDashing;
        Physics2D.Simulate(Time.fixedDeltaTime);
        rules.ResolveStep(++physicsTick, hits, forfeits);
        hits.Clear(); forfeits.Clear();
        foreach (var score in rules.Players) swords[score.PlayerId].ApplyMultiplayerHealth(score.Hp);
        if (rules.Ended)
        {
            // Deliver final HP/death state before the separately acknowledged result.
            Emit("SYNC", Snapshot());
            var result = new MultiplayerResult { matchId = config.matchId, winnerId = rules.WinnerId, draw = rules.Draw,
                standings = rules.Players.Select(p => new MultiplayerScore { playerId = p.PlayerId, rank = p.Rank,
                    damageDealt = p.DamageDealt, damageTaken = p.DamageTaken, kills = p.Kills,
                    eliminationTick = p.EliminationTick, eliminationReason = p.EliminationReason }).ToArray() };
            Emit("RESULT", result);
            FinishMultiplayer(JsonUtility.ToJson(result));
        }
    }

    public void QueueHit(SwordBattle attacker, SwordBattle target, int damage)
    {
        if (!IsHost || !IsPlaying || target.MultiplayerOwner != this || !rules.Get(target.PlayerId).Alive) return;
        // Two dashes clashing in koma mode break each other's guard.
        bool blocked = invulnerable.ContainsKey(target.PlayerId) && invulnerable[target.PlayerId];
        bool clash = SwordController.isKomaMode && invulnerable.ContainsKey(attacker.PlayerId) && invulnerable[attacker.PlayerId];
        if (!blocked || clash) hits.Add(new Hit(attacker.PlayerId, target.PlayerId, damage));
    }

    public void SubmitLocalInput(bool right)
    {
        if (!IsPlaying || !swords[config.localPlayerId].IsAlive) return;
        Emit("INPUT", new MultiplayerCommand { matchId = config.matchId, action = "PRIMARY", direction = right ? "RIGHT" : "LEFT" });
    }
    [UnityEngine.Scripting.Preserve]
    public void ReceiveMultiplayerInput(string json)
    {
        var msg = JsonUtility.FromJson<MultiplayerCommand>(json);
        if (!Matches(msg) || !IsHost || !IsPlaying || msg.playerId == null || !swords.ContainsKey(msg.playerId) ||
            msg.action != "PRIMARY" || (msg.direction != "LEFT" && msg.direction != "RIGHT") ||
            msg.seq <= sequences[msg.playerId] || Time.unscaledTime - inputTimes[msg.playerId] < .04f) return;
        sequences[msg.playerId] = msg.seq; inputTimes[msg.playerId] = Time.unscaledTime;
        swords[msg.playerId].ExecuteMultiplayerAction(msg.direction == "RIGHT");
    }
    [UnityEngine.Scripting.Preserve]
    public void ForfeitMultiplayer(string json)
    {
        var msg = JsonUtility.FromJson<MultiplayerCommand>(json);
        if (Matches(msg) && IsHost && IsPlaying && msg.playerId != null && swords.ContainsKey(msg.playerId)) forfeits.Add(msg.playerId);
    }

    void UpdateTargets(bool force)
    {
        var candidates = config.players.Select(p => new TargetCandidate(p.playerId, p.slotIndex,
            swords[p.playerId].transform.position.x, swords[p.playerId].transform.position.y, swords[p.playerId].IsAlive)).ToArray();
        bool interval = Time.unscaledTime >= nextTargetUpdate;
        foreach (var p in swords)
        {
            string current = targets[p.Key];
            if (!force && !interval && current != null && swords[current].IsAlive) continue;
            targets[p.Key] = BattlePolicies.Target(p.Key, p.Value.transform.position.x, p.Value.transform.position.y, candidates, current, p.Value.isDashing);
            p.Value.GetComponent<SwordController>().enemyTarget = targets[p.Key] == null ? null : swords[targets[p.Key]].transform;
        }
        if (interval) nextTargetUpdate = Time.unscaledTime + .25f;
    }

    MultiplayerSync Snapshot()
    {
        return new MultiplayerSync { matchId = config.matchId, tick = ++syncTick, phase = phase,
            countdownRemaining = phase == "COUNTDOWN" ? Mathf.Max(0, countdownEnd - Time.unscaledTime) : 0,
            players = config.players.Select(p => {
                var sword = swords[p.playerId]; var pos = sword.transform.position;
                var center = SwordController.isKomaMode ? (Vector3)bodies[p.playerId].worldCenterOfMass : pos;
                sword.currentCenterPosition = center;
                return new MultiplayerPlayerState { playerId = p.playerId, x = pos.x, y = pos.y,
                    rotation = sword.transform.eulerAngles.z, centerX = center.x, centerY = center.y,
                    hp = sword.hp, sp = sword.currentSp, isDashing = sword.isDashing, dashType = sword.currentDashType,
                    targetPlayerId = targets[p.playerId] };
            }).ToArray() };
    }

    [UnityEngine.Scripting.Preserve]
    public void SyncMultiplayer(string json)
    {
        var sync = JsonUtility.FromJson<MultiplayerSync>(json);
        if (config == null || IsHost || sync == null || sync.matchId != config.matchId || sync.tick <= receivedTick || phase == "RESULT") return;
        if (sync.players == null || sync.players.Length != config.players.Length ||
            sync.players.Any(p => p == null || p.playerId == null || !swords.ContainsKey(p.playerId)) ||
            sync.players.Select(p => p.playerId).Distinct().Count() != config.players.Length) return;
        phase = sync.phase; SwordBattle.isRoundStarted = IsPlaying;
        foreach (var data in sync.players)
        {
            var sword = swords[data.playerId];
            if (receivedTick < 0) { sword.transform.position = new Vector3(data.x, data.y, 0); sword.transform.rotation = Quaternion.Euler(0, 0, data.rotation); }
            syncTargets[data.playerId] = data;
            sword.currentCenterPosition = new Vector3(data.centerX, data.centerY, 0);
            sword.ApplyMultiplayerHealth(data.hp);
            sword.ApplyMultiplayerVisuals(data.sp, data.isDashing, data.dashType);
            targets[data.playerId] = data.targetPlayerId;
        }
        receivedTick = sync.tick;
    }

    void LateUpdate()
    {
        if (config == null || Camera.main == null || phase == "RESULT") return;
        var alive = swords.Values.Where(s => s.IsAlive).ToArray();
        if (alive.Length == 0) return;
        Bounds bounds = new Bounds(alive[0].transform.position, Vector3.zero);
        foreach (var sword in alive)
            foreach (var renderer in sword.GetComponentsInChildren<SpriteRenderer>()) bounds.Encapsulate(renderer.bounds);
        var camera = Camera.main;
        float size = Mathf.Max(8, bounds.extents.y + 4, (bounds.extents.x + 4) / camera.aspect);
        // Reserve the upper part of the viewport for React's four health panels.
        var position = new Vector3(bounds.center.x, bounds.center.y + size * .16f, -10);
        float t = 1 - Mathf.Exp(-5 * Time.unscaledDeltaTime);
        camera.transform.position = Vector3.Lerp(camera.transform.position, position, t);
        camera.orthographicSize = Mathf.Lerp(camera.orthographicSize, size * 1.2f, t);
    }

    [UnityEngine.Scripting.Preserve]
    public void FinishMultiplayer(string json)
    {
        var result = JsonUtility.FromJson<MultiplayerResult>(json);
        if (config == null || result == null || result.matchId != config.matchId || phase == "RESULT") return;
        phase = "RESULT"; SwordBattle.matchEnded = true; SwordBattle.isRoundStarted = false;
        foreach (var sword in swords.Values) { sword.StopAllCoroutines(); bodies[sword.PlayerId].simulated = false; }
        Time.timeScale = 1;
    }
    [UnityEngine.Scripting.Preserve]
    public void StopMultiplayer(string json)
    {
        var msg = JsonUtility.FromJson<MultiplayerCommand>(json);
        if (Matches(msg)) StopCurrent();
    }
    bool Matches(MultiplayerCommand msg) { return config != null && msg != null && msg.matchId == config.matchId; }
    void Emit(string type, object data) { if (NetworkManager.Instance != null) NetworkManager.Instance.SendData("MP_" + type, JsonUtility.ToJson(data)); }
    void StopCurrent()
    {
        StopAllCoroutines();
        foreach (var sword in swords.Values) if (sword != null) { sword.StopAllCoroutines(); sword.gameObject.SetActive(false); }
        if (arena != null) { arena.SetActive(false); Destroy(arena); }
        if (wallSprite != null) Destroy(wallSprite);
        if (ownsSimulation) { Physics2D.simulationMode = previousSimulationMode; ownsSimulation = false; }
        swords.Clear(); bodies.Clear(); targets.Clear(); sequences.Clear(); inputTimes.Clear();
        hits.Clear(); forfeits.Clear(); invulnerable.Clear(); syncTargets.Clear();
        config = null; rules = null; phase = "IDLE";
        Time.timeScale = 1; SwordBattle.isRoundStarted = false; SwordBattle.matchEnded = false;
        if (AudioManager.Instance != null) AudioManager.Instance.ResetSoundEffects();
    }
    void OnDestroy() { if (Active) StopCurrent(); }
}
