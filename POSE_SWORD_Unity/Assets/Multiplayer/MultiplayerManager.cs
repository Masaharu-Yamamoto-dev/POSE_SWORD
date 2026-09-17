using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using PoseSword.Multiplayer;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
    private bool stageVisible;   // 元のステージを出しているか（出すなら囲いの壁は描かない）
    private SceneController sceneController;   // シーンに作り込まれたHUDを使うために保持する
    private NetworkManager networkManager;
    private bool useSceneHud;    // シーンのHUDを使えるか（使えないときだけ簡易HUDを生成する）
    private Sprite wallSprite;
    // ▼【新規追加】分身突進・リーフシールドなど、本体以外の"付随体"をSYNCでクライアントへ配信するための登録簿（Host側で使用）
    private readonly Dictionary<string, (GameObject obj, string ownerId, Color color)> activeClones = new Dictionary<string, (GameObject, string, Color)>();
    private int cloneIdSeq;
    // ▼【新規追加】クライアント側で、SYNCで届いた分身の位置を再現するための見た目専用オブジェクト
    private readonly Dictionary<string, GameObject> cloneVisuals = new Dictionary<string, GameObject>();
    // Unity側のゲーム画面にも(Reactの4パネルとは別に)簡易HPバーを重ねて表示するためのHUD
    private GameObject hudRoot;
    private TextMeshProUGUI hudStatusText;   // countdownText が無い場合の予備表示
    private TextMeshProUGUI countdownLabel;  // シーン既存のカウントダウン表示（元の見た目）
    private float syncedCountdown;           // ゲストがホストから受け取る残り秒数
    private float goUntil;                   // 「GO!」を出しておく時刻
    private readonly Dictionary<string, Slider> hudHpBars = new Dictionary<string, Slider>();
    private readonly Dictionary<string, TextMeshProUGUI> hudHpTexts = new Dictionary<string, TextMeshProUGUI>();
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
            if (scene == null || network == null || network.playerSwords.Length < 2 || network.playerSwords[0] == null || scene.hostGenerator == null)
                throw new InvalidOperationException("Missing sword template in scene.");
            scene.StopAllCoroutines();
            scene.autoTestOnStart = false;
            network.enabled = false;
            network.isHost = IsHost;
            scene.hostGenerator.generateOnStart = false;
            scene.clientGenerator.generateOnStart = false;
            // 起動時の自動テスト(autoTestOnStart)が3・4人目の剣を作っていることがあるので消す。
            // これを残すと2人対戦でも剣が4本出て、描画も物理も無駄に重くなる。
            scene.ClearDynamicSpawns();
            foreach (var sword in network.playerSwords) if (sword != null) sword.SetActive(false);
            // 元のステージをモードに合わせて出す（NetworkManager と同じ規則）
            bool koma = config.gameMode == "1";
            if (network.swordStage != null) network.swordStage.SetActive(!koma);
            if (network.komaStage != null) network.komaStage.SetActive(koma);
            stageVisible = (network.swordStage != null && !koma) || (network.komaStage != null && koma);
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None)) canvas.enabled = false;
            foreach (var tutorial in FindObjectsByType<TutorialManager>(FindObjectsSortMode.None)) tutorial.enabled = false;
            if (BackgroundManager.Instance != null) BackgroundManager.Instance.enabled = false;
            // 元からあるカウントダウン表示をそのまま使う（進行の管理はこちらで持つ）。
            sceneController = scene;
            networkManager = network;
            // シーンに作り込まれたHPバーがあればそれを使う。無いときだけ簡易HUDを生成する。
            var firstHud = network.playerSwords[0] != null ? network.playerSwords[0].GetComponent<SwordBattle>() : null;
            useSceneHud = firstHud != null && firstHud.hpBar != null;

            countdownLabel = scene.countdownText;
            if (countdownLabel != null)
            {
                countdownLabel.gameObject.SetActive(true);
                ShowCanvasOf(countdownLabel);
            }

            // カットインは出す。ただし時間は止めない（ホストが FixedUpdate で物理を進めているため）。
            CutinManager.scaleTimeDuringCutin = false;
            if (CutinManager.Instance != null)
            {
                CutinManager.Instance.StopAllCoroutines();
                CutinManager.Instance.enabled = true;
                ShowCanvasOf(CutinManager.Instance.cutinCanvasGroup);
            }
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
            foreach (var player in config.players) CreateSword(player, network.playerSwords[0], scene.hostGenerator);
            HideUnusedHud(config.players.Length);
            if (!useSceneHud) BuildHud();
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
        if (config.players == null || config.players.Length < 2 || config.players.Length > 4 || !new[] { "0", "1" }.Contains(config.gameMode) ||
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
        AssignHud(battle, player.slotIndex);
        battle.playerNumber = player.slotIndex + 1;
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
        marker.color = battle.PlayerMainColor;
        obj.SetActive(true);
    }

    // ReactのHUDとは別に、Unityのゲーム画面自体にも人数分(2〜4枚)のHPバーパネルを重ねて表示する
    // シーンに作り込まれたHPバーへスロットごとに配線する。SceneController の単体プレイ時と同じ規則。
    // 0・1番は1対1用のUI、2・3番は Editor で配置した HudTemplate を使う。
    void AssignHud(SwordBattle battle, int slot)
    {
        battle.hpBar = null; battle.delayHpBar = null; battle.nameText = null; battle.hpText = null;
        battle.spGaugeBar = null; battle.spText = null; battle.frameImage = null;
        if (!useSceneHud) return;

        if (slot <= 1)
        {
            var swords = networkManager != null ? networkManager.playerSwords : null;
            var source = swords != null && slot < swords.Length && swords[slot] != null
                ? swords[slot].GetComponent<SwordBattle>() : null;
            if (source == null) return;
            battle.hpBar = source.hpBar; battle.delayHpBar = source.delayHpBar;
            battle.nameText = source.nameText; battle.hpText = source.hpText;
            battle.spGaugeBar = source.spGaugeBar; battle.spText = source.spText;
            battle.frameImage = source.frameImage;
        }
        else
        {
            var template = HudTemplateFor(slot);
            if (template == null || template.hpBar == null) return;
            battle.hpBar = template.hpBar; battle.delayHpBar = template.delayHpBar;
            battle.nameText = template.nameText; battle.hpText = template.hpText;
            battle.spGaugeBar = template.spGaugeBar; battle.spText = template.spText;
            SceneController.SetHudTemplateVisible(template, true);
        }

        ShowCanvasOf(battle.hpBar);
        ShowCanvasOf(battle.spGaugeBar);
    }

    HudTemplate HudTemplateFor(int slot)
    {
        if (sceneController == null) return null;
        return slot == 2 ? sceneController.p3HudTemplate : slot == 3 ? sceneController.p4HudTemplate : null;
    }

    // 参加していないスロットのHPバーは消す（古い数値が残って見えるため）。
    void HideUnusedHud(int playerCount)
    {
        if (!useSceneHud) return;
        for (int slot = playerCount; slot < 4; slot++)
        {
            if (slot <= 1)
            {
                var swords = networkManager != null ? networkManager.playerSwords : null;
                var source = swords != null && slot < swords.Length && swords[slot] != null
                    ? swords[slot].GetComponent<SwordBattle>() : null;
                if (source == null) continue;
                if (source.hpBar != null) source.hpBar.gameObject.SetActive(false);
                if (source.delayHpBar != null) source.delayHpBar.gameObject.SetActive(false);
                if (source.spGaugeBar != null) source.spGaugeBar.gameObject.SetActive(false);
            }
            else SceneController.SetHudTemplateVisible(HudTemplateFor(slot), false);
        }
    }

    // 一度全部消したCanvasのうち、対戦中も使うものだけ表示に戻す。
    void ShowCanvasOf(Component element)
    {
        if (element == null) return;
        var canvas = element.GetComponentInParent<Canvas>(true);
        if (canvas != null) canvas.enabled = true;
    }

    void BuildHud()
    {
        battle.hpBar = null; battle.delayHpBar = null; battle.nameText = null; battle.hpText = null;
        battle.spGaugeBar = null; battle.spText = null;
        Transform bar = hudCanvas != null ? hudCanvas.transform.Find("PL" + (slotIndex + 1) + "Bar") : null;
        if (bar == null) return;

        Transform green = FindChildEndingWith(bar, "HPBarGreen");
        Transform red = FindChildEndingWith(bar, "HPBarRed");
        Transform sp = FindChildEndingWith(bar, "SPBar");
        if (green != null)
        {
            battle.hpBar = green.GetComponent<Slider>();
            battle.hpText = green.Find("HPText (TMP)")?.GetComponent<TextMeshProUGUI>();
            battle.nameText = green.Find("NameText (TMP)")?.GetComponent<TextMeshProUGUI>();
        }
        if (red != null) battle.delayHpBar = red.GetComponent<Slider>();
        if (sp != null)
        {
            battle.spGaugeBar = sp.GetComponent<Slider>();
            battle.spText = sp.Find("SPText (TMP)")?.GetComponent<TextMeshProUGUI>();
        }

        // 中央の状況表示。シーンに既存のカウントダウン表示があればそちらを使うので作らない。
        if (countdownLabel != null) return;
        var statusObj = new GameObject("StatusText", typeof(RectTransform));
        statusObj.transform.SetParent(canvasObj.transform, false);
        var statusRt = statusObj.GetComponent<RectTransform>();
        statusRt.anchorMin = new Vector2(0.1f, 0.4f);
        statusRt.anchorMax = new Vector2(0.9f, 0.6f);
        statusRt.offsetMin = Vector2.zero; statusRt.offsetMax = Vector2.zero;
        hudStatusText = statusObj.AddComponent<TextMeshProUGUI>();
        hudStatusText.alignment = TextAlignmentOptions.Center;
        hudStatusText.enableAutoSizing = true;
        hudStatusText.fontSizeMin = 20; hudStatusText.fontSizeMax = 160;
        hudStatusText.color = Color.white;
        hudStatusText.text = string.Empty;
    }

    // 開始前の待機とカウントダウン。元からある表示があればそれを使い、無ければ生成した予備に出す。
    void UpdateStatusText()
    {
        var label = countdownLabel != null ? countdownLabel : hudStatusText;
        if (label == null) return;
        if (phase == "LOADING")
        {
            label.text = "他のプレイヤーを待っています…";
            goUntil = 0f;
        }
        else if (phase == "COUNTDOWN")
        {
            float remaining = IsHost ? countdownEnd - Time.unscaledTime : syncedCountdown;
            label.text = Mathf.Max(1, Mathf.CeilToInt(remaining)).ToString();
            goUntil = Time.unscaledTime + 0.8f;   // 開始直後に「GO!」を出すための猶予
        }
        else if (IsPlaying && Time.unscaledTime < goUntil)
        {
            label.text = "GO!";
        }
        else
        {
            label.text = string.Empty;
        }
    }

    static Transform FindChildEndingWith(Transform parent, string suffix)
    {
        foreach (Transform child in parent)
            if (child.name.EndsWith(suffix)) return child;
        return null;
    }

    // Two to four players share one arena: spread them evenly instead of assuming four seats.
    Vector3 SpawnPosition(int slot)
    {
        int count = config.players.Length;
        if (count == 2) return new Vector3(slot == 0 ? -8 : 8, 0, 0);
        if (config.gameMode == "0") return new Vector3(-15 + slot * (30f / (count - 1)), 0, 0);
        if (count == 3) return new Vector3(slot == 0 ? 0 : slot == 1 ? -9 : 9, slot == 0 ? 7 : -5, 0);
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
    // 壁は場外へ飛び出さないための当たり判定。元のステージを出しているときは、
    // 見た目がぶつかるので描画しない（判定だけ残す）。
    void Wall(Vector2 position, Vector2 size)
    {
        var wall = new GameObject("ArenaWall"); wall.transform.SetParent(arena.transform);
        wall.transform.position = position; wall.transform.localScale = size;
        wall.AddComponent<BoxCollider2D>();
        if (stageVisible) return;
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
        UpdateStatusText();
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
            // ▼【修正】位置と回転で追従速度を分ける。巨大化一回転(1080°/秒)のような速い回転は、
            // 位置と同じ補間レート(25)だと定常的な追従遅れ(角速度/レート ≈ 1080/25 ≈ 43°)が生じ、
            // クライアント側では「あまり回っていない」ように見えていた。回転だけ大幅に追従を速くして
            // (1080/150 ≈ 7°まで遅れを圧縮)、実際の回転速度に近い見た目にする
            float posT = 1 - Mathf.Exp(-25 * Time.unscaledDeltaTime);
            float rotT = 1 - Mathf.Exp(-150 * Time.unscaledDeltaTime);
            foreach (var pair in syncTargets)
            {
                var sword = swords[pair.Key]; var state = pair.Value;
                sword.transform.position = Vector3.Lerp(sword.transform.position, new Vector3(state.x, state.y, 0), posT);
                sword.transform.rotation = Quaternion.Slerp(sword.transform.rotation, Quaternion.Euler(0, 0, state.rotation), rotT);
            }
            // ▼ 分身・リーフシールドの見た目も、剣本体と同じ補間で滑らかに追従させる
            foreach (var pair in cloneSyncTargets)
            {
                if (!cloneVisuals.TryGetValue(pair.Key, out var obj) || obj == null) continue;
                var state = pair.Value;
                obj.transform.position = Vector3.Lerp(obj.transform.position, new Vector3(state.x, state.y, 0), posT);
                obj.transform.rotation = Quaternion.Slerp(obj.transform.rotation, Quaternion.Euler(0, 0, state.rotation), rotT);
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

    // ▼【新規追加】分身突進・リーフシールドなどの付随体1体をSYNC配信対象として登録し、識別用IDを返す
    public string RegisterClone(GameObject clone, string ownerId, Color color)
    {
        string id = "clone" + (++cloneIdSeq);
        activeClones[id] = (clone, ownerId, color);
        return id;
    }

    // ▼【新規追加】分身が消えた（命中・壁ヒット・寿命切れ）ときに登録を解除する。これでSYNCから外れ、
    // クライアント側のSyncClones()もこのIDを見なくなって見た目の分身も消える
    public void UnregisterClone(string id)
    {
        if (id != null) activeClones.Remove(id);
    }

    public void SubmitLocalInput(bool right)
    {
        if (!IsPlaying || !swords[config.localPlayerId].IsAlive) return;
        Emit("INPUT", new MultiplayerCommand { matchId = config.matchId, action = "PRIMARY", direction = right ? "RIGHT" : "LEFT" });
    }
    // ▼【新規追加】必殺技専用ボタン/スペースキー用の送信。方向は不要
    public void SubmitLocalUltimate()
    {
        if (!IsPlaying || !swords[config.localPlayerId].IsAlive) return;
        Emit("INPUT", new MultiplayerCommand { matchId = config.matchId, action = "ULTIMATE" });
    }
    [UnityEngine.Scripting.Preserve]
    public void ReceiveMultiplayerInput(string json)
    {
        var msg = JsonUtility.FromJson<MultiplayerCommand>(json);
        if (!Matches(msg) || !IsHost || !IsPlaying || msg.playerId == null || !swords.ContainsKey(msg.playerId) ||
            msg.seq <= sequences[msg.playerId] || Time.unscaledTime - inputTimes[msg.playerId] < .04f) return;
        if (msg.action == "PRIMARY" && (msg.direction == "LEFT" || msg.direction == "RIGHT"))
        {
            sequences[msg.playerId] = msg.seq; inputTimes[msg.playerId] = Time.unscaledTime;
            swords[msg.playerId].ExecuteMultiplayerAction(msg.direction == "RIGHT");
        }
        else if (msg.action == "ULTIMATE")
        {
            sequences[msg.playerId] = msg.seq; inputTimes[msg.playerId] = Time.unscaledTime;
            swords[msg.playerId].ExecuteMultiplayerUltimate();
        }
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
                    // ▼ 巨大化一回転(hiltType:"1")の拡大がクライアント側にも見えるよう、剣本体のスケールも同期する
                    scale = sword.transform.localScale.x,
                    targetPlayerId = targets[p.playerId] };
            }).ToArray(),
            // ▼【新規追加】分身突進・リーフシールドなどの付随体の位置をクライアントへ配信する
            clones = activeClones.Where(kv => kv.Value.obj != null).Select(kv => new MultiplayerCloneState {
                id = kv.Key, ownerId = kv.Value.ownerId,
                x = kv.Value.obj.transform.position.x, y = kv.Value.obj.transform.position.y,
                rotation = kv.Value.obj.transform.eulerAngles.z, color = kv.Value.color
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
        syncedCountdown = sync.countdownRemaining;
        foreach (var data in sync.players)
        {
            var sword = swords[data.playerId];
            if (receivedTick < 0) { sword.transform.position = new Vector3(data.x, data.y, 0); sword.transform.rotation = Quaternion.Euler(0, 0, data.rotation); }
            syncTargets[data.playerId] = data;
            sword.currentCenterPosition = new Vector3(data.centerX, data.centerY, 0);
            sword.ApplyMultiplayerHealth(data.hp);
            sword.ApplyMultiplayerVisuals(data.sp, data.isDashing, data.dashType, data.scale);
            targets[data.playerId] = data.targetPlayerId;
        }
        SyncClones(sync.clones);
        receivedTick = sync.tick;
    }

    // ▼【新規追加】Hostから届いた分身の一覧に合わせて、クライアント側の見た目専用オブジェクトを
    // 生成・削除する。位置・回転は即座にスナップさせず、Update()側で剣本体と同じ補間で滑らかに追従させる
    // （新規出現時だけは目標位置にスナップして、原点から一瞬で飛んでくるのを防ぐ）
    void SyncClones(MultiplayerCloneState[] cloneStates)
    {
        var incomingIds = new HashSet<string>();
        if (cloneStates != null)
        {
            foreach (var c in cloneStates)
            {
                if (c == null || c.id == null) continue;
                incomingIds.Add(c.id);
                if (!cloneVisuals.TryGetValue(c.id, out var obj) || obj == null)
                {
                    obj = CreateCloneVisual(c.ownerId, c.color);
                    if (obj == null) continue;
                    cloneVisuals[c.id] = obj;
                    obj.transform.position = new Vector3(c.x, c.y, 0);
                    obj.transform.rotation = Quaternion.Euler(0, 0, c.rotation);
                }
                cloneSyncTargets[c.id] = c;
            }
        }
        foreach (var id in cloneVisuals.Keys.Where(id => !incomingIds.Contains(id)).ToList())
        {
            if (cloneVisuals[id] != null) Destroy(cloneVisuals[id]);
            cloneVisuals.Remove(id);
            cloneSyncTargets.Remove(id);
        }
    }

    // ▼ 付随体の見た目は、持ち主(ownerId)のBladeスプライトをそのまま複製して作る（imageStrは全員が同じmatch設定から
    // ローカルで生成済みなので、スプライト自体をネットワーク越しに送る必要はない）。色は技ごとに異なるためSYNCで受け取る
    GameObject CreateCloneVisual(string ownerId, Color color)
    {
        if (ownerId == null || !swords.TryGetValue(ownerId, out var ownerBattle) || ownerBattle == null) return null;
        var ownerBlade = ownerBattle.transform.Find("Blade");
        var ownerSr = ownerBlade != null ? ownerBlade.GetComponent<SpriteRenderer>() : null;
        if (ownerSr == null || ownerSr.sprite == null) return null;

        var obj = new GameObject("SwordCloneVisual");
        var sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = ownerSr.sprite;
        sr.color = color;
        sr.sortingLayerID = ownerSr.sortingLayerID;
        sr.sortingOrder = ownerSr.sortingOrder;
        return obj;
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
        CutinManager.scaleTimeDuringCutin = true;
        stageVisible = false; useSceneHud = false; sceneController = null; networkManager = null;
        if (countdownLabel != null) countdownLabel.text = string.Empty;
        hudStatusText = null; countdownLabel = null; syncedCountdown = 0; goUntil = 0;
        if (hudRoot != null) { hudRoot.SetActive(false); Destroy(hudRoot); }
        if (ownsSimulation) { Physics2D.simulationMode = previousSimulationMode; ownsSimulation = false; }
        swords.Clear(); bodies.Clear(); targets.Clear(); sequences.Clear(); inputTimes.Clear();
        hits.Clear(); forfeits.Clear(); invulnerable.Clear(); syncTargets.Clear();
        cloneSyncTargets.Clear();
        activeClones.Clear();
        foreach (var visual in cloneVisuals.Values) if (visual != null) Destroy(visual);
        cloneVisuals.Clear();
        config = null; rules = null; phase = "IDLE";
        Time.timeScale = 1; SwordBattle.isRoundStarted = false; SwordBattle.matchEnded = false;
        if (AudioManager.Instance != null) AudioManager.Instance.ResetSoundEffects();
    }
    void OnDestroy() { if (Active) StopCurrent(); }
}
