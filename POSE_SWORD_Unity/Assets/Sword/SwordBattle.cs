using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using TMPro;

public class SwordBattle : MonoBehaviour
{
    public MultiplayerManager MultiplayerOwner { get; private set; }
    public string PlayerId { get; private set; }
    public bool IsAlive { get { return !isDead && hp > 0; } }
    // ▼【新規追加】このテンプレートが最初から持っているtransform.localScale(1でない場合がある)。
    // ConfigureMultiplayerで一度だけ記録し、ReviveFromDefeatで(Vector3.oneではなく)これへ戻す
    private Vector3 baselineScale = Vector3.one;
    [Header("ステータス")]
    public string swordName = "ダミー剣";
    public int hp = 100;
    
    private int maxHp;
    public int attack = 10;
    public string hiltType = "0"; // 柄の種類＝必殺技の種類（JSONのhiltTypeが未指定なら"0"＝デフォルトの必殺技）

    [Header("プレイヤー識別（P1赤 / P2青 / P3黄 / P4緑）")]
    public int playerNumber = 1; // 1〜4。マルチプレイでは自動的にスロット番号(slotIndex+1)がセットされる
    // 枠の色付けやオーラの色付けに使う共通カラーパレット。他スクリプト（MultiplayerManagerのHUD等）からも参照する。
    public static readonly Color[] PlayerColors =
    {
        new Color(0.2f, 0.45f, 1f),     // P1 青
        new Color(0.92f, 0.18f, 0.18f), // P2 赤
        new Color(1f, 0.82f, 0.1f),     // P3 黄
        new Color(0.25f, 0.85f, 0.35f), // P4 緑
    };
    public Color PlayerMainColor => PlayerColors[Mathf.Clamp(playerNumber - 1, 0, PlayerColors.Length - 1)];
    // 必殺技が撃てるようになるSPのライン（独楽モードは竜巻の70、剣モードは満タン）
    public float UltimateThreshold => SwordController.isKomaMode ? 70f : maxSp;

    [Header("UI設定")]
    public Slider hpBar;        // 手前の緑ゲージ
    public Slider delayHpBar;   // ▼【追加】奥の赤ゲージ
    public TextMeshProUGUI nameText; // ▼ 【変更】Text から TextMeshProUGUI に変更
    public TextMeshProUGUI hpText;   // ▼ 【変更】Text から TextMeshProUGUI に変更
    public Image frameImage;    // プレイヤーのメインカラーで塗る枠（HPパネルの縁など。Editor側で用意して割り当てる）
    public Button specialAttackButton; // 必殺技専用ボタン。自キャラのSPが条件を満たした時だけ表示する（Editor側で用意して割り当てる）
    // ▼【新規追加】残機モード：HPパネル内に表示する「あと何本あるか」の小さいアイコン列。
    // 今戦っている剣は含めない(残りの手持ちの剣だけ)。MultiplayerManager.WireHudBarが実行時に生成して渡す
    [HideInInspector] public Image[] reserveSwordIcons;

    [Header("物理・ダメージ調整")]
    public float bounceForce = 500f;
    public float impactMultiplier = 0.05f;
    public float maxImpactValue = 40f;
    public float pointyAngleThreshold = 80f;
    public float critThreshold = 3000f; 
    [Header("ダメージポップアップ")]
    public GameObject damagePopupPrefab;

[Header("演出（エフェクト）")]
public ParticleSystem normalEffectPrehub;
public ParticleSystem critEffectPrefab;
public ParticleSystem guardEffectPrefab;
public ParticleSystem ultimateAuraPrefab; // SPが必殺技分たまった時に体から出す炎エフェクト（メインカラーで色付けする。Editor側で用意して割り当てる）
private ParticleSystem ultimateAuraInstance;
private bool ultimateAuraActive;
private SpriteRenderer spriteRenderer;
private SwordController controller;

[Header("サウンド")]
    public AudioClip normalHitSound; // 通常ヒット音
    public AudioClip critHitSound;   // クリティカル・弱点音
    public AudioClip defeatSound;    // 👈【新規追加】決着専用の音！
private AudioSource audioSource; // 音を鳴らすスピーカー    
public static bool matchEnded = false;

[Header("必殺技（SPゲージ）設定")]
    public Slider spGaugeBar;          // SPゲージのUI（Slider）
    public TextMeshProUGUI spText;
    public float currentSp = 0f;       // 現在のSP
    public float maxSp = 100f;         // SPの最大値
    public float passiveSpFill = 5f;   // 1秒間に自動で溜まる量
    public float damageSpMultiplier = 0.5f; // 受けたダメージの何倍をSPに変換するか
    public float giantSpinScale = 6f; // 巨大化一回転（hiltType:"1"）の拡大率
    public float giantSpinDamageMultiplier = 8f; // 巨大化一回転が命中した時のダメージ倍率（attackへの倍率）
    public float giantSpinRotationSpeed = 1080f; // 巨大化一回転の回転速度（度/秒）。小さくするとゆっくり回るようになる（一回転にかかる時間もこれに応じて伸びる）
    public int cloneCount = 3; // 分身突進（hiltType:"2"）の分身数
    public float cloneSpawnMinRadius = 2f; // 分身の出現位置：自分からの最小距離
    public float cloneSpawnMaxRadius = 4f; // 分身の出現位置：自分からの最大距離
    public float cloneDashSpeed = 35f; // 分身の突進速度
    public float cloneDamageMultiplier = 3f; // 分身1体が命中した時のダメージ倍率（attackへの倍率）
    public float cloneLifeTime = 3f; // 何にも当たらなかった場合に分身が自動的に消えるまでの秒数
    public float cloneSpawnStagger = 0.08f; // 分身が1体ずつ出現する間隔（秒）
    public int leafShieldCount = 3; // リーフシールド（hiltType:"3"）の展開枚数
    public float leafShieldDuration = 15f; // リーフシールドの持続時間（秒）
    public float leafShieldOrbitRadius = 3.5f; // 本体からの周回半径（＝防御圏の広さ）
    public float leafShieldRadius = 0.7f; // シールド1枚あたりの当たり判定の大きさ
    public float leafShieldOrbitSpeed = 150f; // 周回速度（度/秒）
    public float leafShieldHpRatio = 1f / 3f; // 各シールドのHP（本体の最大HPに対する割合）
    public float leafShieldReflectMultiplier = 2f; // シールドが被弾した時、受けたダメージの何倍を相手に返すか

    // 突進状態の管理用
// 突進状態の管理用
    [HideInInspector] public bool isDashing = false;
    [HideInInspector] public bool isDashShooting = false;
    private float dashDamageBonus = 1.0f;
    // ▼【修正】単一のboolだと「最初に当てた1人」以降、他の誰にも一切ヒットしなくなってしまい、
    // 3〜4人戦で2人目・3人目に必殺技が当たらない不具合の原因になっていた。回転中に同じ相手へ
    // 何度も連続ヒットするのを防ぎたいだけなので、「すでに当てた相手の集合」で管理し、
    // 別の相手には引き続き当たるようにする
    private readonly HashSet<SwordBattle> giantSpinHitTargets = new HashSet<SwordBattle>();

    // ▼【新規追加】オレ達シールド(旧リーフシールド)が展開されている間の本体無敵管理。
    // シールドのCollider2DはisTrigger(=当たっても攻撃側の移動を止めない)なので、これが無いと
    // シールドで反射ダメージを受けた直後にそのまま本体まで刺さって、本体側もダメージを受けてしまう。
    // 展開中の枚(GameObject)の数をここで数え、1枚でも残っていれば本体への通常ダメージを無効化する
    [HideInInspector] public int activeLeafShieldCount = 0;
    public bool HasActiveLeafShield => activeLeafShieldCount > 0;

    // ▼【新規追加】残機モードでリスポーン(次の剣に持ち替え)した直後の無敵時間。
    // 無敵中はスプライトを点滅させて、見た目でも無敵中だと分かるようにする
    [Header("残機モード：リスポーン時の無敵")]
    public float respawnInvincibleDuration = 3f; // リスポーン直後、何秒間ダメージを無効化するか
    public float respawnBlinkInterval = 0.12f;   // 点滅の切り替え間隔（秒）
    private float respawnInvincibleTimer = 0f;
    public bool IsRespawnInvincible => respawnInvincibleTimer > 0f;

    // ▼【新規追加】現在のダッシュ技の種類 (0:なし, 1:小ダッシュ, 2:竜巻, 3:大回転, 4:巨大化一回転, 5:分身突進)
    [HideInInspector] public int currentDashType = 0;

    // SwordBattle.cs の変数宣言エリアに追加
    // SwordBattle.cs の変数宣言エリアに追加
    [HideInInspector] public Vector2 trueVelocity; // 独自の真の速度
    private Vector2 lastPosition;
    private Rigidbody2D rb;
    private bool isDead = false;
    public bool IsDead => isDead;

    // ▼【新規追加】カメラが追従するための、画像サイズに左右されない本物の中心座標
    [HideInInspector] public Vector3 currentCenterPosition;

    // ▼【修正】false→trueに切り替わった瞬間のTime.unscaledTimeを自動記録するプロパティにした。
    // Host/Clientそれぞれが「自分がisRoundStartedをtrueだと認識した瞬間」を記録するため、
    // Time.time(アプリ起動からの経過時間、端末ごとにバラバラ)より遥かに揃った「試合開始からの経過時間」の
    // 基準点として使える(動く足場などの演出をHost/ゲスト間で位相を揃えるために利用する)
    private static bool _isRoundStarted = false;
    public static bool isRoundStarted
    {
        get => _isRoundStarted;
        set
        {
            if (value && !_isRoundStarted) roundStartUnscaledTime = Time.unscaledTime;
            _isRoundStarted = value;
        }
    }
    // ▼ isRoundStartedが最後にtrueになった時刻(Time.unscaledTime基準)。まだ一度もtrueになっていなければ-1
    public static float roundStartUnscaledTime = -1f;

    // ▼【N人対応】生存者数の管理。試合開始時にSceneControllerからプレイヤー人数がセットされ、
    // 誰かが撃破されるたびにReportElimination()で減算。残り1人になった時だけ試合終了とする。
    public static int alivePlayerCount = 0;

    public static void ReportElimination()
    {
        alivePlayerCount = Mathf.Max(0, alivePlayerCount - 1);
        if (alivePlayerCount <= 1) matchEnded = true;
    }

    void Start()
    {   
        if (MultiplayerOwner != null) return;
        matchEnded = false;
        isRoundStarted = false; // ★開始時は一回 false にする
        InitializeComponents();

        maxHp = hp;
        UpdateUI();
        ApplyPlayerColor();
        lastPosition = transform.position;
        currentCenterPosition = transform.position;
    }

    // ▼【新規追加】playerNumber(P1〜P4)に応じたメインカラーを枠画像に反映する
    void ApplyPlayerColor()
    {
        if (frameImage == null) return;
        Color c = PlayerMainColor;
        c.a = frameImage.color.a;
        frameImage.color = c;
    }

    void InitializeComponents()
    {
        rb = GetComponent<Rigidbody2D>();
        controller = GetComponent<SwordController>();
        Transform blade = transform.Find("Blade");
        if (blade != null) spriteRenderer = blade.GetComponent<SpriteRenderer>();
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();

        if (specialAttackButton != null)
        {
            specialAttackButton.onClick.RemoveListener(TryUltimate);
            specialAttackButton.onClick.AddListener(TryUltimate);
            specialAttackButton.gameObject.SetActive(false);
        }

    }

    public void ConfigureMultiplayer(MultiplayerManager owner, string playerId)
    {
        MultiplayerOwner = owner; PlayerId = playerId;
        InitializeComponents(); StopAllCoroutines();
        enabled = true; isDead = false; isDashing = false; isDashShooting = false;
        currentDashType = 0; currentSp = 0;
        ApplyPlayerColor();
        controller.enabled = true;
        foreach (var collider in GetComponentsInChildren<Collider2D>(true)) collider.enabled = true;
        rb.linearVelocity = Vector2.zero; rb.angularVelocity = 0;
        lastPosition = transform.position; currentCenterPosition = transform.position;
        // ▼【新規追加】このテンプレート本来のスケール(1でない場合がある)を、まだ何にも書き換えられていない
        // このタイミングで一度だけ記録する。残機モードの持ち替え時(ReviveFromDefeat)に、Vector3.oneへ
        // 決め打ちで戻すのではなくこの値へ戻すために使う
        baselineScale = transform.localScale;
    }

    // ▼ 通常クリック/タップ由来の入力：必殺技はここでは発動しない（ジャンプ・小ダッシュのみ）
    public void ExecuteMultiplayerAction(bool right)
    {
        if (MultiplayerOwner == null || !MultiplayerOwner.IsHost) return;
        string action = PoseSword.Multiplayer.BattlePolicies.PrimaryAction(SwordController.isKomaMode, currentSp,
            MultiplayerOwner.IsPlaying, IsAlive, isDashing, right);
        switch (action)
        {
            case "KomaDash": StartCoroutine(DashRoutine()); break;
            case "JumpRight": controller.NetworkJump(true); break;
            case "JumpLeft": controller.NetworkJump(false); break;
        }
    }

    // ▼【新規追加】必殺技専用ボタン/スペースキー由来の入力：SPが条件を満たしている時だけ発動する
    // ▼【修正】SPが足りているかどうかの判定だけBattlePoliciesで行い、実際にどの技が出るかはUltimateRoutine()の
    // hiltType分岐に任せる（以前はTornado/SwordDash固定で、柄違いの必殺技がオンラインでは一切出せなかった）
    public void ExecuteMultiplayerUltimate()
    {
        if (MultiplayerOwner == null || !MultiplayerOwner.IsHost) return;
        string action = PoseSword.Multiplayer.BattlePolicies.UltimateAction(SwordController.isKomaMode, currentSp,
            MultiplayerOwner.IsPlaying, IsAlive, isDashing);
        if (action == null) return;
        StartCoroutine(UltimateRoutine());
    }

    // ▼【新規追加】剣本体以外（分身・リーフシールドなど）からも、TakeDamage/QueueHitへの正しい経路で
    // ダメージを与えられるようにする共通口。ローカルではTakeDamageへ、オンラインではHost権威のQueueHitへ回す
    public void DealDamageTo(SwordBattle target, int damage, Vector2 hitPoint, bool isCrit = false, bool isWeakPoint = false)
    {
        if (target == null || target == this || !target.IsAlive) return;
        if (MultiplayerOwner != null) MultiplayerOwner.QueueHit(this, target, damage, isCrit || isWeakPoint);
        else target.TakeDamage(damage, hitPoint, isCrit, isWeakPoint);
    }

    // ▼【新規追加】残機モード：現在の剣が破壊された際、「倒された」状態だけを解除して次の剣に持ち替えさせる。
    // ステータス(hp/maxHp/attack/hiltType)と見た目の刀身自体はMultiplayerManager側がこの直後に
    // SwordGenerator.GenerateSwordFromJsonを呼び直すことで更新するので、ここでは死亡時に無効化した
    // 見た目・当たり判定・操作を元に戻すだけでよい(ConfigureMultiplayerの死亡状態リセット部分と同等)
    public void ReviveFromDefeat()
    {
        if (MultiplayerOwner == null) return;
        isDead = false; isDashing = false; isDashShooting = false;
        currentDashType = 0; currentSp = 0;
        StopUltimateAura();
        StopAllCoroutines();
        controller.enabled = true;
        foreach (var collider in GetComponentsInChildren<Collider2D>(true)) collider.enabled = true;
        if (spriteRenderer != null) spriteRenderer.color = Color.white;
        // ▼【修正】Vector3.oneへ決め打ちで戻すと、テンプレート自体が1以外のスケールで作られている場合に
        // 本来の見た目より小さく(または大きく)なってしまっていた。ConfigureMultiplayerで記録した
        // このテンプレート本来のスケールへ戻す
        transform.localScale = baselineScale;
        // ▼【新規追加】リスポーン直後は一定時間ダメージを受けない(無敵)。Update()側で点滅させつつ
        // カウントダウンし、MultiplayerManager.QueueHit側のinvulnerable判定もこれを見て無効化する
        respawnInvincibleTimer = respawnInvincibleDuration;
    }

    public void ApplyMultiplayerHealth(int health, bool wasCrit = false)
    {
        if (MultiplayerOwner == null || isDead) return;
        int damage = Mathf.Max(0, hp - health);
        if (damage > 0)
        {
            PlayClientDamageEffect(damage, wasCrit);
            if (MultiplayerOwner.IsHost) currentSp = Mathf.Min(maxSp, currentSp + 100f * damage / maxHp * damageSpMultiplier);
        }
        hp = Mathf.Clamp(health, 0, maxHp); UpdateUI();
        if (hp > 0) return;
        isDead = true; isDashing = false; currentDashType = 0;
        StopUltimateAura();
        // ▼【修正】撃破に値する一撃(damage>=20等)は直前のPlayClientDamageEffectでHitStopRoutineを
        // 開始しており、Time.timeScaleを0.05にした直後にここのStopAllCoroutines()が
        // それを巻き込んで強制停止させてしまう。HitStopRoutine側の後処理(等速へ戻す)が
        // 一度も実行されず、3〜4人戦でまだ試合が終わっていない撃破でもスローのまま固まっていた。
        // 試合自体が終わった場合はMatchEndCinematicが改めてtimeScaleを制御するのでmatchEndedの時は触らない
        StopAllCoroutines();
        if (!matchEnded) Time.timeScale = 1f;
        controller.enabled = false; controller.isLocalControlled = false;
        foreach (var collider in GetComponentsInChildren<Collider2D>(true)) collider.enabled = false;
        if (spriteRenderer != null) spriteRenderer.color = new Color(.2f, .2f, .2f);
        if (defeatSound != null) audioSource.PlayOneShot(defeatSound);
        // ▼【修正】ローカルのDefeatRoutineと同じく、その場で静止させず天高く吹き飛ばして回転させる
        // (以前はrb.simulated=falseで即座に静止させていたため、脱落の派手さがマルチプレイだけ失われていた)。
        // またローカルは脱落後も剣を消さず、倒れたままの姿を残して他の生存者の試合を続けるため、
        // 以前あった0.8秒後に非表示にする処理(HideEliminatedSword)も削除した
        if (rb != null)
        {
            rb.gravityScale = 2f;
            rb.linearVelocity = Vector2.zero;
            rb.AddForce(new Vector2(Random.Range(-500f, 500f), 2000f), ForceMode2D.Impulse);
            rb.AddTorque(5000f, ForceMode2D.Impulse);
        }
    }

    // ▼【修正】dashType 4(巨大化一回転)・5(分身突進)・6(リーフシールド発動)の色分けと、
    // 巨大化のスケールをクライアント側にも反映する
    public void ApplyMultiplayerVisuals(float sp, bool dashing, int dashType, float scale)
    {
        if (!IsAlive) return;
        // ▼【新規追加】Client側はUltimateRoutine()自体をローカル実行しない（Host権威でのみ実行される）ため、
        // カットインもここで再生する。dashTypeが必殺技の値に変わった瞬間（0/1→2〜6）だけ発火させる
        int previousDashType = currentDashType;
        currentSp = Mathf.Clamp(sp, 0, maxSp); isDashing = dashing; currentDashType = dashType;
        if (dashType != previousDashType) TryPlayUltimateCutin(dashType);
        if (spriteRenderer != null)
        {
            Color tint = dashType == 1 ? new Color(1, .5f, .5f) :
                dashType == 2 ? new Color(1, .8f, .2f) : dashType == 3 ? new Color(.5f, 1, 1) :
                dashType == 4 ? new Color(1f, .3f, .3f) : dashType == 5 ? new Color(.3f, .6f, 1f) :
                dashType == 6 ? new Color(.4f, .95f, .5f) : Color.white;
            // ▼【新規追加】リスポーン無敵中の点滅(Update()側でアルファだけ操作)を上書きしてしまわないよう、
            // 現在のアルファ値は維持したまま色(RGB)だけ差し替える
            tint.a = spriteRenderer.color.a;
            spriteRenderer.color = tint;
        }
        // ▼【修正】Host側は自分の剣のlocalScale.xだけをSYNCで送っており(MultiplayerManager.Snapshot)、
        // これをVector3.one * scaleでそのまま復元するとZ軸まで巻き込んで上書きしてしまっていた。
        // このテンプレートは元々X/Y=2・Z=1という非対称なスケールで作られており(柄(Handle-A)は
        // ローカルZ座標を-5に固定しているため)、親のZスケールが1→2に化けると柄のワールドZ座標が
        // -5→-10まで移動してカメラ(Z=-10)の目の前・ニアクリップ面のすぐ手前まで来てしまい、
        // 柄がカリングされて画面に描画されなくなる不具合の原因になっていた。
        // Z軸はConfigureMultiplayerで記録した本来のbaselineScaleのまま維持し、X/Yだけ同期する
        if (scale > 0f) transform.localScale = new Vector3(scale, scale, baselineScale.z);
    }

    // ▼【新規追加】dashType(2〜6)に対応する必殺技のカットインを、スローモーションなしで再生する
    // （Host自身の画面は各Routine内で直接PlayCutinを呼んでいるので、ここはClient専用の経路）
    // ▼【修正】ここは常にfalseのままでよい。実際の「止まる」演出はHost側のPhysics2D.Simulate停止
    // (各Routine側でuseSlowMotion=trueを渡している)によってSYNCデータ自体が止まることで実現しており、
    // Client側の位置補間はTime.unscaledDeltaTimeで動いているためローカルでtimeScaleを変えても
    // 見た目上は止まらない(むしろ他の演出のタイミングだけズレる)。ここではカットインUI/SE専用に留める
    void TryPlayUltimateCutin(int dashType)
    {
        if (CutinManager.Instance == null || spriteRenderer == null) return;
        string skillName;
        Color themeColor;
        switch (dashType)
        {
            case 2: skillName = "竜巻猛突!!"; themeColor = new Color(1f, 0.8f, 0.2f); break;
            case 3: skillName = "大回転斬!!"; themeColor = new Color(0.5f, 1f, 1f); break;
            case 4: skillName = "巨大回転斬!!"; themeColor = new Color(1f, 0.3f, 0.3f); break;
            
            case 5: skillName = "オレ達アタック!!"; themeColor = new Color(0.3f, 0.6f, 1f); break;
            case 6: skillName = "オレ達シールド!!"; themeColor = new Color(0.4f, 0.95f, 0.5f); break;
            default: return;
        }
        CutinManager.Instance.PlayCutin(spriteRenderer.sprite, swordName, skillName, themeColor, PlayerMainColor, false);
    }

    void FixedUpdate()
    {
        trueVelocity = ((Vector2)transform.position - lastPosition) / Time.fixedDeltaTime;
        lastPosition = transform.position;

        // ▼【新規追加】Host側（物理演算が動いている側）は、自分の本物の回転中心を毎フレーム記録する
        if (rb != null && rb.bodyType == RigidbodyType2D.Dynamic)
        {
            if (SwordController.isKomaMode)
            {
                // 物理エンジンが回している中心（worldCenterOfMass）を正確に捉える！
                currentCenterPosition = new Vector3(rb.worldCenterOfMass.x, rb.worldCenterOfMass.y, transform.position.z);
            }
            else
            {
                currentCenterPosition = transform.position;
            }
        }
    }

    // ▼【追加】毎フレーム呼ばれる関数
    void Update()
    {
        if (delayHpBar != null && hpBar != null && delayHpBar.value > hpBar.value)
        {
            delayHpBar.value = Mathf.Lerp(delayHpBar.value, hpBar.value, 5f * Time.unscaledDeltaTime);
            if (delayHpBar.value - hpBar.value < 0.5f) delayHpBar.value = hpBar.value;
        }

        if (MultiplayerOwner == null && Input.GetMouseButtonDown(0))
    {
        Debug.Log($"クリック検出 / Round={isRoundStarted}, Dead={isDead}, Ended={matchEnded}, Local={(controller != null && controller.isLocalControlled)}");
    }

        // ▼【新規追加】リスポーン直後の無敵時間をカウントダウンしつつ、スプライトを点滅させる。
        // isDead/matchEnded中でも(死亡演出等の色と衝突しないよう)無敵タイマー自体は進めておく
        if (respawnInvincibleTimer > 0f)
        {
            respawnInvincibleTimer -= Time.deltaTime;
            if (spriteRenderer != null)
            {
                if (respawnInvincibleTimer <= 0f)
                {
                    respawnInvincibleTimer = 0f;
                    var c = spriteRenderer.color; c.a = 1f; spriteRenderer.color = c;
                }
                else
                {
                    bool visible = Mathf.FloorToInt(respawnInvincibleTimer / respawnBlinkInterval) % 2 == 0;
                    var c = spriteRenderer.color; c.a = visible ? 1f : 0.25f; spriteRenderer.color = c;
                }
            }
        }

        if (!isRoundStarted || isDead || matchEnded) return;

        if (MultiplayerOwner == null || MultiplayerOwner.IsHost) currentSp += passiveSpFill * Time.deltaTime;
        currentSp = Mathf.Clamp(currentSp, 0f, maxSp);

        if (spGaugeBar != null)
        {
            spGaugeBar.maxValue = maxSp;
            spGaugeBar.value = currentSp;
        }

        if (spText != null)
        {
            spText.text = $"SP: {Mathf.FloorToInt(currentSp)} / {maxSp}";
        }

        UpdateUltimateAura();
        UpdateSpecialAttackButton();

        if (controller != null && controller.isLocalControlled)
        {
            // ▼【変更】画面の「右半分」か「左半分」かを判定してジャンプ！（必殺技はここでは発動しない）
            // ▼【修正】必殺技ボタンをクリックした時にもGetMouseButtonDown(0)は真になってしまい、
            // 「画面タップ」としての通常アクション(独楽モードなら小ダッシュ)も同時に暴発していた。
            // これが必殺技ボタンを押した直後にSPを食い合ってしまい、SPが70超あっても直前の小ダッシュで
            // 0まで消費された状態でULTIMATE入力が処理される→しきい値未満で不発、という不具合の原因だった。
            // ▼【修正】最初はEventSystem.IsPointerOverGameObject()でUI全般を除外していたが、これだと
            // HPバー・名前表示・チュートリアルの帯など、必殺技ボタンと無関係なUI要素の上をタップしただけでも
            // 通常アクションが握りつぶされ、「画面を押してもジャンプできないことがある」不具合になっていた。
            // 必殺技ボタン自身の範囲と重なっている時だけ除外するよう、判定を絞り込む
            bool overSpecialButton = specialAttackButton != null && specialAttackButton.gameObject.activeInHierarchy &&
                RectTransformUtility.RectangleContainsScreenPoint(
                    specialAttackButton.GetComponent<RectTransform>(), Input.mousePosition, null);
            if (Input.GetMouseButtonDown(0) && !overSpecialButton)
            {
                // クリックしたX座標が、画面幅の半分より大きければ「右（true）」、小さければ「左（false）」
                bool clickedRight = Input.mousePosition.x > (Screen.width / 2f);
                TryAction(clickedRight);
            }

            // ▼【新規追加】PC操作時のみ：スペースキーでも必殺技専用ボタンと同じ発動ができるようにする
            if (Input.GetKeyDown(KeyCode.Space)) TryUltimate();
        }
    }

    // ▼【新規追加】必殺技専用ボタンの表示切り替え：自キャラのSPが必殺技分たまっている時だけ表示する
    // オンラインN人戦では全員のSwordBattleが同じ1つのボタンを参照しているため、
    // 自分が操作しているキャラ以外は絶対にこのボタンへ触れないようにする（他人の生死やSPで消えてしまうのを防ぐ）
    void UpdateSpecialAttackButton()
    {
        if (specialAttackButton == null || controller == null || !controller.isLocalControlled) return;
        bool shouldShow = !isDashing && currentSp >= UltimateThreshold;
        if (specialAttackButton.gameObject.activeSelf != shouldShow)
            specialAttackButton.gameObject.SetActive(shouldShow);
    }

    // ▼【新規追加】残機モード：HPパネル内の「あと何本あるか」アイコン列を、渡された残りの剣の
    // 画像で更新する。今戦っている剣は含まない配列を渡す想定。本数が足りない枠は非表示にする
    public void UpdateReserveSwordIcons(Sprite[] sprites)
    {
        if (reserveSwordIcons == null) return;
        for (int i = 0; i < reserveSwordIcons.Length; i++)
        {
            var icon = reserveSwordIcons[i];
            if (icon == null) continue;
            Sprite sprite = sprites != null && i < sprites.Length ? sprites[i] : null;
            icon.gameObject.SetActive(sprite != null);
            if (sprite != null) icon.sprite = sprite;
        }
    }

    // ▼追加：SwordGeneratorから正しいタイミングで呼ばれる初期化関数
    public void SetupStatus(string newName, int newHp, int newAttack, string newHiltType = "0")
    {
        swordName = newName;
        hp = newHp;
        maxHp = newHp;
        attack = newAttack;
        hiltType = string.IsNullOrEmpty(newHiltType) ? "0" : newHiltType;
        UpdateUI();
    }

    public void UpdateUI()
    {
        if (hpBar != null)
        {
            hpBar.maxValue = maxHp;
            hpBar.value = hp;
        }

        // ▼【追加】初期化時（HP満タン時）は赤ゲージも瞬時に合わせる
        if (delayHpBar != null && hp == maxHp)
        {
            delayHpBar.maxValue = maxHp;
            delayHpBar.value = hp;
        }

        if (nameText != null) nameText.text = swordName;
        if (hpText != null) hpText.text = $"{hp} / {maxHp}";
    }

    // ▼【新規追加】SPが必殺技を撃てる量まで溜まっている間、メインカラーの炎エフェクトを体から出し続ける
    void UpdateUltimateAura()
    {
        bool shouldBeActive = IsAlive && currentSp >= UltimateThreshold;
        if (shouldBeActive == ultimateAuraActive) return;
        ultimateAuraActive = shouldBeActive;

        if (shouldBeActive)
        {
            if (ultimateAuraInstance == null && ultimateAuraPrefab != null)
            {
                ultimateAuraInstance = Instantiate(ultimateAuraPrefab, transform);
                ultimateAuraInstance.transform.localPosition = new Vector3(0f, 0f, 5f);
                var main = ultimateAuraInstance.main;
                main.startColor = PlayerMainColor;
            }
            if (ultimateAuraInstance != null) ultimateAuraInstance.Play();
        }
        else if (ultimateAuraInstance != null)
        {
            ultimateAuraInstance.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    void StopUltimateAura()
    {
        ultimateAuraActive = false;
        if (ultimateAuraInstance != null) ultimateAuraInstance.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        // ▼ ボタンは全員で共有されているため、自分が操作しているキャラの死亡時だけ隠す（他人の脱落で消してしまわないように）
        if (specialAttackButton != null && controller != null && controller.isLocalControlled)
            specialAttackButton.gameObject.SetActive(false);
    }

    // ▼【修正】ローカルはBattleCameraが常に有効なのでcam.TriggerShakeがそのまま効くが、
    // マルチプレイ中はBattleCameraを無効化してMultiplayerManager自身がカメラを制御しているため、
    // 呼び先をそちらに振り分ける共通口（呼び出し側の見た目は変えなくていいようにする）
    void ShakeCamera(float duration, float magnitude)
    {
        if (MultiplayerOwner != null) { MultiplayerOwner.TriggerShake(duration, magnitude); return; }
        BattleCamera cam = Camera.main != null ? Camera.main.GetComponent<BattleCamera>() : null;
        if (cam != null) cam.TriggerShake(duration, magnitude);
    }

    // ▼【新規追加】柄迫り合い・壁バウンドなどダメージを伴わない衝突演出は、Host側のOnCollisionEnter2Dでしか
    // 発生しないためゲスト側の画面には何も表示されなかった。MultiplayerManagerのSYNCに乗せて届いた合図で、
    // ゲスト側でも同じ火花・効果音だけを一度だけ再生する
    public void PlayClashEffect()
    {
        if (guardEffectPrefab != null) Instantiate(guardEffectPrefab, transform.position, Quaternion.identity);
        if (normalHitSound != null) audioSource.PlayOneShot(normalHitSound);
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        // ▼【修正】ローカルはカウントダウン中も衝突判定(弾き・鍔迫り合いの火花)が普通に働くため、
        // マルチプレイのカウントダウン中も同様に動かす（ダメージ確定はQueueHit側がIsPlayingで別途ガード済み）
        if (MultiplayerOwner != null && (!MultiplayerOwner.IsHost || !MultiplayerOwner.IsSimulating)) return;
        if (isDead) return;
        bool wasDashing = isDashing;
        
        SwordBattle target = collision.gameObject.GetComponent<SwordBattle>();

        // ==========================================
        // ▼【修正1】剣モード：必殺技ダッシュ中の「地形バウンド」
        // ==========================================
        if (!SwordController.isKomaMode && isDashing)
        {
            if (!isDashShooting) return;
            if (target == null)
            {
                // 相手の剣ではなく「壁」や「床」にぶつかった場合はピンボールのように跳ね返る！
                if (rb != null && collision.contacts.Length > 0)
                {
                    Vector2 inDirection = rb.linearVelocity.normalized;
                    Vector2 normal = collision.contacts[0].normal;
                    Vector2 bounceDir = Vector2.Reflect(inDirection, normal);
                    
                    // スピードを維持して反射（最低40fは担保して超高速をキープ）
                    float currentSpeed = Mathf.Max(rb.linearVelocity.magnitude, 40f);
                    rb.linearVelocity = bounceDir * currentSpeed; 
                    
                    // 回転も進行方向に合わせる
                    rb.angularVelocity = Mathf.Sign(bounceDir.x) * -2500f;

                    // 壁に当たった音とエフェクト
                    if (normalHitSound != null) audioSource.PlayOneShot(normalHitSound);
                    if (guardEffectPrefab != null) Instantiate(guardEffectPrefab, collision.contacts[0].point, Quaternion.identity);

                    // 画面も軽く揺らす
                    ShakeCamera(0.1f, 0.2f);
                    // ▼ オンライン対戦ではこの演出はHost側でしか起きないため、ゲスト側にも一度きりのVFX/SEとして知らせる
                    if (MultiplayerOwner != null) MultiplayerOwner.NotifyClash(PlayerId);
                }
                // ダッシュは終わらせず、ここで処理を抜ける（反射し続ける）
                return;
            }
            else
            {
                // 相手の剣に当たった時は突き刺さってダッシュ終了
                EndSwordDash();
            }
        }

        // 相手の剣との衝突
        if (target != null && target != this)
        {
            Collider2D myCollider = collision.otherCollider;
            Collider2D targetCollider = collision.collider;

            // ==========================================
            // ▼【修正2】柄同士の鍔迫り合い（ガキン！と大きく弾く）
            // ==========================================
            bool isHandleClash = myCollider.CompareTag("Handle") && targetCollider.CompareTag("Handle");
            if (isHandleClash && !wasDashing)
            {
                // 火花を出して音を鳴らす
                if (guardEffectPrefab != null) Instantiate(guardEffectPrefab, collision.contacts[0].point, Quaternion.identity);
                if (normalHitSound != null) audioSource.PlayOneShot(normalHitSound);
                if (MultiplayerOwner != null) MultiplayerOwner.NotifyClash(PlayerId);

                if (rb != null)
                {
                    Vector2 clashBounce = (transform.position - target.transform.position).normalized;
                    clashBounce.y += 1.0f; // やや上方向に激しく弾く
                    rb.AddForce(clashBounce * (bounceForce * rb.mass * 1.5f), ForceMode2D.Impulse);
                }
                return; // 鍔迫り合いなのでダメージ計算はせず終了
            }

            if (rb != null)
            {
                if (!wasDashing)
                {
                    Vector2 bounceDir = (transform.position - collision.transform.position).normalized;
                    if (!SwordController.isKomaMode) {
                        bounceDir.y += 0.5f; 
                    }
                    rb.AddForce(bounceDir.normalized * (bounceForce * rb.mass), ForceMode2D.Impulse);
                }
            }

            // ==========================================
            // ▼【修正3】柄ガードのブレイク（必殺技中は柄で当たっても攻撃判定！）
            // ==========================================
            // 自分が「ダッシュ中ではない通常時」だけ、自分の柄が当たった時の攻撃をキャンセルする。
            // つまり、必殺技中は柄だろうが何だろうが問答無用で相手を叩き斬る！
            if (!wasDashing && myCollider.CompareTag("Handle")) return;

            float impact = collision.relativeVelocity.magnitude;
            
            // ===== 以降は既存のコードがそのまま続きます =====
            if (impact > 2.0f)
            {
                float clampedImpact = Mathf.Min(impact, maxImpactValue);
                int damage = Mathf.RoundToInt(clampedImpact * this.attack * impactMultiplier);
                damage = Mathf.Max(damage, 1);

                bool isCrit = false;
                bool isWeakPoint = false;
                Vector2 hitPoint = collision.GetContact(0).point;

                // 相手の剣との衝突

            // ==========================================
            // ▼【新規追加】独楽モード：カウンター（必殺技ブレイク）！
            // ==========================================
            // 自分が竜巻（ダッシュ）中で、相手も小ダッシュ（または竜巻）で突っ込んできた場合
            if (SwordController.isKomaMode && wasDashing && target.isDashing)
            {
                isDashing = false; // 竜巻を強制終了！
                isDashShooting = false;
                
                Debug.Log("💥 カウンター炸裂！相手の突進によって竜巻がブレイクされた！");
                
                // 竜巻を破られたペナルティとして、大きく後方に弾き飛ばされる
                if (rb != null)
                {
                    Vector2 breakBounce = (transform.position - target.transform.position).normalized;
                    rb.AddForce(breakBounce * (bounceForce * rb.mass * 2.0f), ForceMode2D.Impulse);
                }
                
                // 画面を激しく揺らしてブレイク成功を演出
                ShakeCamera(0.2f, 0.4f);
                if (MultiplayerOwner != null) MultiplayerOwner.NotifyClash(PlayerId);
            }


                if (SwordController.isKomaMode)
                {
                    // 【独楽モード：ダメージ調整】
                    // 独楽は回転による相対速度(impact)が常に高くなりやすく、連続ヒットもするため、
                    // 基礎ダメージの時点で少しデバフ（0.6倍）をかけて剣モードの水準に合わせます。
                    damage = Mathf.Max(Mathf.RoundToInt(damage * 0.6f), 1);

                    Rigidbody2D targetRb = collision.gameObject.GetComponent<Rigidbody2D>();
                    
                    float myMass = rb.mass;
                    float targetMass = (targetRb != null) ? targetRb.mass : 1f;

                    float mySpeed = rb.linearVelocity.magnitude;
                    float targetSpeed = (targetRb != null) ? targetRb.linearVelocity.magnitude : 0f;

                    float myEnergy = myMass * (mySpeed * mySpeed);
                    float targetEnergy = targetMass * (targetSpeed * targetSpeed);

                    bool amIStronger = (myEnergy > targetEnergy);
                    
                    float m = rb.mass;
                    float v = impact; 
                    float kineticEnergy = m * (v * v);

                    float hitRadius = Vector2.Distance(transform.position, hitPoint);
                    float centrifugalBonus = 1.0f + (hitRadius * 0.5f); 

                    float komaPower = kineticEnergy * centrifugalBonus;

                    // ▼【修正】エネルギー倍率の上がり方を緩やかにし、最大でも「1.5倍」までに制限する
                    float powerMultiplier = 1.0f + (komaPower / 20000f); 
                    powerMultiplier = Mathf.Clamp(powerMultiplier, 1.0f, 1.5f); 
                    damage = Mathf.RoundToInt(damage * powerMultiplier);

                    // ▼【修正】ダッシュ時のバグ（/5で減っていた）を修正しつつ、強すぎない1.2倍ボーナスに
                    if (isDashing)
                    {
                        damage = Mathf.RoundToInt(damage * 2f);
                    }

                    bool isSharp = (myCollider is PolygonCollider2D myPoly && IsPointy(hitPoint, myPoly));

                    // ▼【修正】クリティカルの倍率もマイルドに抑え、剣モードに近い削り合いにする
                    if (amIStronger && komaPower > critThreshold && isSharp)
                    {
                        // ①【絶・クリティカル】（約1.8倍）
                        damage = Mathf.RoundToInt(damage * 1.8f);
                        isCrit = true;
                        Debug.Log($"💥 独楽【絶・クリティカル】! 破壊力と鋭利さが合わさった！");
                    }
                    else if (amIStronger && komaPower > critThreshold)
                    {
                        // ②【超破壊】（約1.3倍）
                        damage = Mathf.RoundToInt(damage * 1.3f);
                        isCrit = true;
                        Debug.Log($"💥 独楽【超破壊】! (自エネ: {myEnergy:F0} > 敵エネ: {targetEnergy:F0})");
                    }
                    else if (isSharp)
                    {
                        // ③【鋭利（カウンター）】（約1.3倍）
                        damage = Mathf.RoundToInt(damage * 1.3f);
                        isCrit = true;
                        Debug.Log($"💥 独楽【鋭利】! 一矢報いるカウンター直撃！");
                    }
                    else
                    {
                        // ④ どれも満たさない場合は通常ダメージ
                    }
                }
                else
                {
                    if (wasDashing)
                    {
                        damage = Mathf.RoundToInt(damage * dashDamageBonus);
                        dashDamageBonus = 1.0f; // 使ったらリセットする
                    }
                    if (myCollider is PolygonCollider2D myPoly && IsPointy(hitPoint, myPoly))
                    {
                        damage *= 3;
                        isCrit = true;
                    }

                // 相手の柄（弱点）判定
                    if (targetCollider.CompareTag("Handle"))
                    {
                        damage *= 2;
                        isWeakPoint = true;
                    }
                }
                    // ▼変更：TakeDamageの結果（倒したかどうか）を受け取る
                    bool killedTarget = false;
                    if (MultiplayerOwner != null) MultiplayerOwner.QueueHit(this, target, damage, isCrit || isWeakPoint);
                    else killedTarget = target.TakeDamage(damage, hitPoint, isCrit, isWeakPoint);

                    // ▼追加：もし自分が勝者になったなら、弾き飛ぶのをキャンセル！
                    if (killedTarget)
                    {
                        // 弾き飛ばされた勢い（横方向の速度）と回転をピタッと止める
                        rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y); 
                        rb.angularVelocity = 0f;
                        
                        // 勝負がついたので、クリックしても動かないように操作を無効化する
                        if (controller != null) controller.enabled = false;
                        
                        Debug.Log("🏆 勝者決定！その場にカッコよく着地します。");
                    }
                }
            }
        }
    
    bool IsPointy(Vector2 hitPointWorld, PolygonCollider2D poly)
    {
        Vector2[] points = poly.points;
        if (points.Length < 3) return false;
        Vector2 hitPointLocal = poly.transform.InverseTransformPoint(hitPointWorld);
        int closestIndex = 0;
        float minDistance = float.MaxValue;
        for (int i = 0; i < points.Length; i++)
        {
            float dist = Vector2.Distance(hitPointLocal, points[i]);
            if (dist < minDistance)
            {
                minDistance = dist;
                closestIndex = i;
            }
        }
        Vector2 prevPoint = points[(closestIndex - 1 + points.Length) % points.Length];
        Vector2 currentPoint = points[closestIndex];
        Vector2 nextPoint = points[(closestIndex + 1) % points.Length];
        Vector2 dir1 = prevPoint - currentPoint;
        Vector2 dir2 = nextPoint - currentPoint;
        return Vector2.Angle(dir1, dir2) < pointyAngleThreshold;
    }

    // ▼変更：void から bool に変更
    public bool TakeDamage(int damage, Vector2 hitPos, bool isCrit, bool isWeakPoint)
    {
        if (MultiplayerOwner != null) return false; // HP is committed once per physics step by MatchRules.
        if (isDead) return false; // 変更
        if (isDashing)
        {
            Debug.Log("🛡️ 突進中につき無敵！攻撃を弾いた！");
            // ※もし「キンッ！」という弾き音（パリィ音）があればここで鳴らすと最高です
            // if (parrySound != null) audioSource.PlayOneShot(parrySound);
            return false;
        }
        if (HasActiveLeafShield)
        {
            Debug.Log("🍃 オレ達シールド展開中につき本体無敵！攻撃はシールドの反射に任せる！");
            return false;
        }
        if (IsRespawnInvincible)
        {
            Debug.Log("✨ リスポーン直後につき無敵！攻撃を弾いた！");
            return false;
        }

        float damagePercentage = ((float)damage / maxHp) * 100f; 
        currentSp = Mathf.Clamp(currentSp + (damagePercentage * damageSpMultiplier), 0f, maxSp);

        hp -= damage;
        if (hp < 0) hp = 0;
        UpdateUI();

        // ▼【新規追加】ダメージ数値をポップアップさせる！
        if (damagePopupPrefab != null && damage > 0)
        {
            // 剣がぶつかった位置(hitPos)に数字を出す
            GameObject popup = Instantiate(damagePopupPrefab, hitPos, Quaternion.identity);
            DamagePopup popupScript = popup.GetComponent<DamagePopup>();
            if (popupScript != null) popupScript.Setup(damage, isCrit || isWeakPoint);
        }

        if (hp == 0)
        {
            ReportElimination();
            // 時間の奪い合いを防ぐため、進行中のヒットストップなどを全て強制キャンセル
            StopAllCoroutines();
            
            // 決着時は確定で派手なクリティカル音を鳴らす
            if (defeatSound != null) audioSource.PlayOneShot(defeatSound);
            
            // ド派手な決着演出コルーチンを開始
            StartCoroutine(DefeatRoutine());
            
            return true; // 相手を倒したことを教える
        }

        if (isCrit || isWeakPoint)
        {
            if (critHitSound != null) audioSource.PlayOneShot(critHitSound);
        }
        else
        {
            if (normalHitSound != null) audioSource.PlayOneShot(normalHitSound);
        }
        if (damage >= 20 || isCrit || isWeakPoint)
        {
            StartCoroutine(HitStopRoutine(0.1f));
            BattleCamera cam = Camera.main.GetComponent<BattleCamera>();
            if (cam != null) cam.TriggerShake(0.1f, 0.3f);

            // ▼【新規追加】ダメージに応じて背景のオーラ境界線を激しく揺らす！
            if (BackgroundManager.Instance != null)
            {
                // ダメージの量に応じて揺れの強さを変える
                BackgroundManager.Instance.TriggerImpact(damage * 0.1f);
            }
        }

        if (isCrit && critEffectPrefab != null) Instantiate(critEffectPrefab, hitPos, Quaternion.identity);
        else if (isWeakPoint && guardEffectPrefab != null) Instantiate(guardEffectPrefab, hitPos, Quaternion.identity);
        else Instantiate(normalEffectPrehub, hitPos, Quaternion.identity);

        if (hp <= 0)
        {
            StartCoroutine(DefeatRoutine());
            return true; // ▼追加：相手を倒したことを教える！
        }

        return false; // ▼追加：まだ倒していない
    }

    IEnumerator HitStopRoutine(float duration)
    {
        if (matchEnded) yield break;
        Time.timeScale = 0.05f;
        yield return new WaitForSecondsRealtime(duration);
        // ▼【修正】ここのisDeadは「このヒットストップを始めた本人が死んだか」でしかなく、「試合全体が
        // 終わったか」ではない。マルチプレイのApplyMultiplayerHealthは致命打でも先にHitStopRoutineを
        // 開始してからisDead=trueにするため、3〜4人戦で決着がついていない撃破でもここがfalseのままに
        // なり、Time.timeScaleが0.05に固まって戻らなくなっていた。本当に戻すべきでないのはmatchEnded
        // (＝試合全体の決着)の時だけなので、isDeadでの判定はやめる
        if (!matchEnded)
        {
            Time.timeScale = 1f;
        }
    }

    // ▼変更：ド派手な決着演出
    IEnumerator DefeatRoutine()
    {
        isDead = true;
        StopUltimateAura();
        if (controller != null) controller.enabled = false;

        // 剣が黒くなり、コライダーを消してすり抜けるようにする（めり込み防止）
        // ▼【N人対応】ここまでは「自分が脱落した」演出。試合が終わったかに関わらず毎回実行する
        if (spriteRenderer != null) spriteRenderer.color = new Color(0.2f, 0.2f, 0.2f);
        Collider2D[] colliders = GetComponentsInChildren<Collider2D>();
        foreach (var col in colliders) col.enabled = false;

        // 天高く吹き飛ばし、超高速で回転させる！
        rb.gravityScale = 2f;
        rb.linearVelocity = Vector2.zero; // 今の勢いをリセット
        rb.AddForce(new Vector2(Random.Range(-500f, 500f), 2000f), ForceMode2D.Impulse);
        rb.AddTorque(5000f, ForceMode2D.Impulse);

        // ▼【N人対応】ここから先（カメラ固定・スローモーション・完全停止）は
        // 「本当に試合が終わった撃破」の時だけ行う。3〜4人戦で他の生存者がまだ残っている間は
        // 試合を止めず、脱落者は上の演出だけ見せて続行させる。
        if (!matchEnded) yield break;

        // カメラを激しく揺らして固定！
        BattleCamera cam = Camera.main.GetComponent<BattleCamera>();
        if (cam != null)
        {
            cam.StopTracking(); // 追従をストップして位置をロック！
            cam.TriggerShake(1.5f, 1.2f);
        }

        // スローモーション発動
        // ▼【修正】値を一度セットするだけだと、この直後に他の剣（無関係な必殺技カットインなど）が
        // Time.timeScaleを上書きした場合、そのまま中途半端な速度で固まってしまう。
        // 2.5秒間、毎フレーム押し戻すことで、決着後の演出中はこの値を確実に保つ
        float holdTimer = 0f;
        while (holdTimer < 2.5f)
        {
            Time.timeScale = 0.15f;
            holdTimer += Time.unscaledDeltaTime;
            yield return null;
        }

        // ゲーム完全停止
        Time.timeScale = 0f;
    }

    public void PlayClientDamageEffect(int damage, bool isCrit = false)
    {
        if (isDead || matchEnded) return;

        // ❌ 修正前：一律で通常音が鳴っていた
        // if (normalHitSound != null) audioSource.PlayOneShot(normalHitSound);

        // ⭕ 修正後：通信に頼らず、届いたダメージの大きさで通常音とクリティカル音をスマートに分岐！
        // ▼【新規追加】TakeDamage()はマルチプレイ中は呼ばれない（HPはMatchRules経由で直接反映される）ため、
        // ここでヒットエフェクトも出さないとマルチプレイでは誰の画面にも斬撃エフェクトが表示されなかった
        // ▼【修正】isCritはQueueHit経由でHostが判定した本物のクリティカル/弱点ヒット情報。
        // 以前はダメージ量(100以上)だけで代用しており、実際のクリティカル判定とズレていた
        if (isCrit || damage >= 100)
        {
            if (critHitSound != null) audioSource.PlayOneShot(critHitSound);
            if (critEffectPrefab != null) Instantiate(critEffectPrefab, transform.position, Quaternion.identity);
        }
        else
        {
            if (normalHitSound != null) audioSource.PlayOneShot(normalHitSound);
            if (normalEffectPrehub != null) Instantiate(normalEffectPrehub, transform.position, Quaternion.identity);
        }

        // ▼ 通信相手の画面にもダメージ数値を出す（ここはそのまま）
        if (damagePopupPrefab != null && damage > 0)
        {
            Vector3 spawnPos = transform.position + (Vector3)Random.insideUnitCircle * 1.5f;
            GameObject popup = Instantiate(damagePopupPrefab, spawnPos, Quaternion.identity);
            DamagePopup popupScript = popup.GetComponent<DamagePopup>();
            if (popupScript != null) popupScript.Setup(damage, damage >= 20);
        }

        // ---（以下、カメラシェイクや決着音の既存コードがそのまま続きます）---
        if (damage >= 20 || isCrit)
        {
            // ▼【修正】ローカルのTakeDamage()と同じく、大ダメージ・クリティカル・弱点ヒットで一瞬止める
            // ヒットストップ演出を追加(以前はTakeDamage()経由でしか発生せず、マルチプレイでは常に無音で通過していた)
            StartCoroutine(HitStopRoutine(0.1f));
            ShakeCamera(0.1f, 0.3f);

            if (BackgroundManager.Instance != null)
            {
                BackgroundManager.Instance.TriggerImpact(damage * 0.1f);
            }
        }

        if (MultiplayerOwner == null && hp - damage <= 0)
        {
            ReportElimination();
            StopAllCoroutines();
            if (defeatSound != null) audioSource.PlayOneShot(defeatSound);
            StartCoroutine(DefeatRoutine());
            Debug.Log("🏆 クライアント側でも決着を検知！DefeatRoutineを開始します。");
        }
    }
    // ▼【新規追加】突進アクション
    // ▼【変更】SPの量によって技を分岐させる
    // ▼【変更】自分のアクションを実行しつつ、その名前をWebに送る！
    // ▼【変更】通常クリック/タップ用：必殺技はここでは発動せず、ジャンプ・小ダッシュのみ行う
    public void TryAction(bool clickedRight = true)
    {
        if (MultiplayerOwner != null) { MultiplayerOwner.SubmitLocalInput(clickedRight); return; }
        if (isDead || matchEnded || isDashing) return;

        string actionName = ""; // ★Webに送る用のアクション名

        if (SwordController.isKomaMode)
        {
            if (currentSp >= 20f)
            {
                StartCoroutine(DashRoutine());
                actionName = "KomaDash";
            }
        }
        else if (controller != null)
        {
            controller.NetworkJump(clickedRight);
            actionName = clickedRight ? "JumpRight" : "JumpLeft";
        }

        // ▼【新規追加】自分が操作した時だけ、Web（React）側にアクションを伝える！
        if (controller != null && controller.isLocalControlled && !string.IsNullOrEmpty(actionName))
        {
            InputMessage msg = new InputMessage();
            msg.action = actionName;
            NetworkManager.Instance.SendData("INPUT", JsonUtility.ToJson(msg));
        }
    }

    // ▼【新規追加】必殺技専用ボタン/スペースキー用：SPが必殺技分たまっている時だけ発動する
    public void TryUltimate()
    {
        // ▼【修正】specialAttackButtonはP1〜P4全員のSwordBattleが同じ1つのUIボタンを共有しており、
        // 各インスタンスがInitializeComponents()で自分のTryUltimateをonClickに登録するため、
        // ボタンを1回押すと全員分のTryUltimate()が呼ばれてしまう。
        // 自分が操作しているキャラでなければ即座に何もしないようにして、他人の必殺技が暴発しないようにする
        if (controller == null || !controller.isLocalControlled) return;
        if (MultiplayerOwner != null) { MultiplayerOwner.SubmitLocalUltimate(); return; }
        if (isDead || matchEnded || isDashing || !isRoundStarted) return;
        if (currentSp < UltimateThreshold) return;

        StartCoroutine(UltimateRoutine());
        string actionName = SwordController.isKomaMode ? "Tornado" : "SwordDash";

        // ▼ 自分が操作した時だけ、Web（React）側にアクションを伝える！
        if (controller != null && controller.isLocalControlled)
        {
            InputMessage msg = new InputMessage();
            msg.action = actionName;
            NetworkManager.Instance.SendData("INPUT", JsonUtility.ToJson(msg));
        }
    }

    // ▼【新規追加】通信相手のアクションを「強制発動」させる受信専用関数！
    public void ExecuteRemoteAction(string actionName)
    {
        if (isDead || matchEnded || isDashing) return;

        Debug.Log($"📡 相手からの通信を受信！ 強制発動: {actionName}");

        if (actionName == "Tornado")
        {
            currentSp = 0f; // 強制消費
            StartCoroutine(UltimateRoutine());
        }
        else if (actionName == "KomaDash")
        {
            currentSp = 0f;
            StartCoroutine(DashRoutine());
        }
        else if (actionName == "SwordDash")
        {
            currentSp = 0f;
            StartCoroutine(UltimateRoutine());
        }
        else if (actionName == "JumpRight")
        {
            if (controller != null) controller.NetworkJump(true);
        }
        else if (actionName == "JumpLeft")
        {
            if (controller != null) controller.NetworkJump(false);
        }
    }

    // ▼【修正】独楽モード：牽制の小ダッシュ（SP20〜69）
    // ▼【修正】独楽モード：牽制の小ダッシュ
    IEnumerator DashRoutine()
    {
        isDashing = true;
        currentDashType = 1;
        if (spriteRenderer != null) spriteRenderer.color = new Color(1f, 0.5f, 0.5f);
        
        float consumedSp = currentSp;
        currentSp = 0f; 

        // ▼【修正】rb.bodyType == RigidbodyType2D.Dynamic を追加（Hostのみ動く）
        if (controller != null && controller.enemyTarget != null && rb != null && rb.bodyType == RigidbodyType2D.Dynamic)
        {
            Vector2 dirToEnemy = (controller.enemyTarget.position - transform.position).normalized;
            rb.AddForce(dirToEnemy * (consumedSp * 1.5f), ForceMode2D.Impulse);
            rb.AddTorque(-consumedSp * 50f, ForceMode2D.Impulse); 
        }

        yield return new WaitForSeconds(0.2f);

        isDashing = false;
        currentDashType = 0;
        if (spriteRenderer != null) spriteRenderer.color = Color.white;
    }

    // ▼【新規追加】剣ごとのhiltType（JSON由来）に応じて必殺技の中身を振り分ける
    // 未対応のhiltType（未指定＝"0"を含む）は既存のデフォルト必殺技にフォールバック
    IEnumerator UltimateRoutine()
    {
        // ▼ 独楽モードと剣モードで必殺技を完全に分けて管理する。
        // hiltType(柄の種類)ごとの技は、モードごとに別々のswitchで振り分ける。
        if (SwordController.isKomaMode)
        {
            switch (hiltType)
            {
                // ▼ 独楽モード専用の必殺技を増やす場合はここにcaseを足す（今のところhiltTypeによらず竜巻に統一）
                default:
                    yield return StartCoroutine(TornadoDashRoutine());
                    break;
            }
        }
        else
        {
            switch (hiltType)
            {
                // ▼ 剣モード専用の必殺技を増やす場合はここにcaseを足す
                case "1": // 巨大化して一回転。当たると大ダメージ
                    yield return StartCoroutine(GiantSpinRoutine());
                    break;
                case "2": // 分身を3体出現させ、相手へホーミング突進させる
                    yield return StartCoroutine(CloneRushRoutine());
                    break;
                case "3": // 本体の周りをシールドが公転する「リーフシールド」
                    yield return StartCoroutine(LeafShieldRoutine());
                    break;
                default:
                    yield return StartCoroutine(SwordDashRoutine());
                    break;
            }
        }
    }

    // ▼【修正】独楽モード：超必殺「竜巻」
    IEnumerator TornadoDashRoutine()
    {
        isDashing = true;
        currentDashType = 2;
        float consumedSp = currentSp;
        currentSp = 0f; 

        Debug.Log($"🌪️ 独楽モード：超必殺【竜巻】発動！ (消費SP: {consumedSp:F0})");

        if (CutinManager.Instance != null && spriteRenderer != null)
        {
            // ▼【修正】マルチプレイでも必殺技カットイン中は全員の動きを止める演出にする(useSlowMotion常にtrue)。
            // Host権威のPhysics2D.Simulateが実質停止するため、この間だけは意図的に両プレイヤーとも止まる
            // (Client側は停止したSYNCデータをそのまま補間するので、こちらでも自然に止まって見える)
            CutinManager.Instance.PlayCutin(spriteRenderer.sprite, swordName, "竜巻猛突!!", new Color(1f, 0.8f, 0.2f), PlayerMainColor, true);
        }

        if (spriteRenderer != null) spriteRenderer.color = new Color(1f, 0.8f, 0.2f);

        float duration = 0.8f; 
        float timer = 0f;

        while (timer < duration)
        {
            if (!isDashing || isDead || matchEnded) break;

            // ▼【修正】rb.bodyType == RigidbodyType2D.Dynamic を追加（Hostのみ動く）
            if (controller != null && controller.enemyTarget != null && rb != null && rb.bodyType == RigidbodyType2D.Dynamic)
            {
                Vector2 dirToEnemy = (controller.enemyTarget.position - transform.position).normalized;
                rb.AddForce(dirToEnemy * (rb.mass * 30f), ForceMode2D.Force);
                rb.AddTorque(2000f * rb.mass, ForceMode2D.Force);
            }

            if (Random.value > 0.7f && guardEffectPrefab != null)
            {
                Instantiate(guardEffectPrefab, transform.position + (Vector3)Random.insideUnitCircle * 1.5f, Quaternion.identity);
            }

            timer += Time.deltaTime;
            yield return null; 
        }

        // ▼【修正】ブレーキもHostのみ
        if (rb != null && rb.bodyType == RigidbodyType2D.Dynamic)
        {
            rb.linearVelocity *= 0.5f; 
        }

        isDashing = false;
        currentDashType = 0;
        if (spriteRenderer != null) spriteRenderer.color = Color.white;
    }

    // ▼【新規追加】剣モード必殺技（hiltType:"1"）：巨大化して一回転。当たると大ダメージ
    IEnumerator GiantSpinRoutine()
    {
        isDashing = true;
        currentDashType = 4;
        currentSp = 0f;
        giantSpinHitTargets.Clear();

        // ▼ マルチプレイ中は演出のスローモーション(Time.timeScale変更)が全員の画面をブロックしてしまうため、
        // Tornado/SwordDashと同様にローカル/テストモード時だけ再生する
        if (CutinManager.Instance != null && spriteRenderer != null)
        {
            CutinManager.Instance.PlayCutin(spriteRenderer.sprite, swordName, "巨大回転斬!!", new Color(1f, 0.3f, 0.3f), PlayerMainColor, true);
        }

        if (spriteRenderer != null) spriteRenderer.color = new Color(1f, 0.3f, 0.3f);

        Vector3 originalScale = transform.localScale;
        transform.localScale = originalScale * giantSpinScale;

        // 壁にも相手の剣にも物理的に引っかからず回転できるよう、回転中は当たり判定をトリガー化する
        // （ダメージはOnTriggerEnter2D側で判定する）
        Collider2D[] myColliders = GetComponentsInChildren<Collider2D>();
        bool[] originalTriggerStates = new bool[myColliders.Length];
        for (int i = 0; i < myColliders.Length; i++)
        {
            originalTriggerStates[i] = myColliders[i].isTrigger;
            myColliders[i].isTrigger = true;
        }

        bool controlsPhysics = rb != null && rb.bodyType == RigidbodyType2D.Dynamic;
        float originalGravityScale = 0f;

        if (controlsPhysics)
        {
            // 回転中は床に落ちていかないよう、重力も一時的に切る
            originalGravityScale = rb.gravityScale;
            rb.gravityScale = 0f;
            rb.linearVelocity = Vector2.zero;
        }

        // 敵がいる方向に振りかぶるように回転方向を決める
        float spinDir = 1f;
        if (controller != null && controller.enemyTarget != null)
        {
            float dx = controller.enemyTarget.position.x - transform.position.x;
            if (Mathf.Abs(dx) > 0.01f)
            {
                spinDir = Mathf.Sign(dx) * -1f;
            }
        }

        float angularSpeed = giantSpinRotationSpeed * spinDir; // 度/秒
        float duration = 360f / Mathf.Abs(angularSpeed); // ちょうど一回転分の時間

        if (controlsPhysics)
        {
            rb.angularVelocity = angularSpeed;
        }

        yield return new WaitForSeconds(duration);

        if (controlsPhysics)
        {
            rb.angularVelocity = 0f;
            rb.gravityScale = originalGravityScale;
        }

        for (int i = 0; i < myColliders.Length; i++)
        {
            if (myColliders[i] != null) myColliders[i].isTrigger = originalTriggerStates[i];
        }

        transform.localScale = originalScale;
        isDashing = false;
        currentDashType = 0;
        if (spriteRenderer != null) spriteRenderer.color = Color.white;
    }

    // ▼【新規追加】剣モード必殺技（hiltType:"2"）：分身を3体出現させ、相手へホーミング突進させる
    IEnumerator CloneRushRoutine()
    {
        isDashing = true;
        currentDashType = 5;
        currentSp = 0f;

        Color cloneColor = new Color(0.3f, 0.6f, 1f);

        // ▼ マルチプレイ中は演出のスローモーション(Time.timeScale変更)が全員の画面をブロックしてしまうため、
        // Tornado/SwordDashと同様にローカル/テストモード時だけ再生する
        if (CutinManager.Instance != null && spriteRenderer != null)
        {
            CutinManager.Instance.PlayCutin(spriteRenderer.sprite, swordName, "オレ達アタック!!", cloneColor, PlayerMainColor, true);
        }

        if (spriteRenderer != null) spriteRenderer.color = cloneColor;

        // ▼【重要】実際のダメージ計算はDynamic（＝物理演算の権威を持つHost側）でのみ行う。
        // Client側の見た目再生（Kinematicコピー）では分身は出すが、ダメージは与えない。
        bool isAuthoritative = rb != null && rb.bodyType == RigidbodyType2D.Dynamic;
        int cloneDamage = Mathf.Max(Mathf.RoundToInt(attack * cloneDamageMultiplier), 1);

        Vector3 enemyPos = (controller != null && controller.enemyTarget != null)
            ? controller.enemyTarget.position
            : transform.position + transform.right;

        Collider2D[] myColliders = GetComponentsInChildren<Collider2D>();
        List<Collider2D> spawnedColliders = new List<Collider2D>();
        const float cloneColliderRadius = 0.6f;
        float sectorSize = 360f / cloneCount; // 各分身の出現方向を等間隔に散らすための区画角度

        for (int i = 0; i < cloneCount; i++)
        {
            // ▼【重要】画面端など出現位置の確保が難しい状況で1体の生成に失敗しても、
            // 残りの分身の生成や末尾の状態リセットが必ず実行されるようにtry/catchで保護する
            try
            {
                float baseAngle = i * sectorSize;
                Vector3 spawnPos = FindCloneSpawnPosition(cloneColliderRadius, baseAngle, sectorSize);

                GameObject cloneObj = new GameObject("SwordClone");
                cloneObj.transform.position = spawnPos;
                cloneObj.transform.rotation = transform.rotation;

                // ▼【新規追加】分身たちに「今使っている自分の剣 + まだ使っていない手持ちの剣」の形を持たせる
                // (i=0が現在の自分、以降は手持ちの他の剣)。既に使い終えて手放した剣は含めない。
                // 残機モードのON/OFFに関わらず、手持ちの剣が1本・2本だけでも安全に動作する
                // (GetLifeSpriteが範囲を丸めるので、その場合は同じ形が繰り返されるだけ)。
                // 画像が取れない場合は従来通り現在の刀身と同じ見た目にフォールバックする
                int lifeSpriteIndex = -1;
                Sprite cloneSprite = spriteRenderer != null ? spriteRenderer.sprite : null;
                if (MultiplayerOwner != null)
                {
                    int wantedIndex = MultiplayerOwner.GetCurrentLifeIndex(PlayerId) + i;
                    var lifeSprite = MultiplayerOwner.GetLifeSprite(PlayerId, wantedIndex);
                    if (lifeSprite != null) { cloneSprite = lifeSprite; lifeSpriteIndex = wantedIndex; }
                }

                if (spriteRenderer != null)
                {
                    SpriteRenderer sr = cloneObj.AddComponent<SpriteRenderer>();
                    sr.sprite = cloneSprite;
                    sr.color = cloneColor;
                    sr.sortingLayerID = spriteRenderer.sortingLayerID;
                    sr.sortingOrder = spriteRenderer.sortingOrder;
                    // ▼【新規追加】手持ちの剣は元画像のピクセルサイズがバラバラなため、現在の刀身と
                    // 見た目の横幅が揃うようスケールを正規化する。当たり判定(cloneColliderRadius)は
                    // 形状に関わらず固定なので、これをしないと見た目と判定がズレて「当たらない」ように見えていた
                    float refWidth = spriteRenderer.sprite != null ? spriteRenderer.sprite.bounds.size.x : 0f;
                    float cloneWidth = cloneSprite != null ? cloneSprite.bounds.size.x : 0f;
                    if (refWidth > 0f && cloneWidth > 0f) cloneObj.transform.localScale = Vector3.one * (refWidth / cloneWidth);
                }

                CircleCollider2D cloneCollider = cloneObj.AddComponent<CircleCollider2D>();
                cloneCollider.radius = cloneColliderRadius;

                Rigidbody2D cloneRb = cloneObj.AddComponent<Rigidbody2D>();
                cloneRb.gravityScale = 0f;

                Vector2 dirToEnemy = ((Vector2)enemyPos - (Vector2)spawnPos).normalized;
                cloneRb.linearVelocity = dirToEnemy * cloneDashSpeed;

                // ▼【新規追加】オンライン対戦では、Host権威の分身だけをMultiplayerManagerに登録し、
                // その位置をSYNCでクライアントへ配信して見た目を再現できるようにする
                string cloneId = (isAuthoritative && MultiplayerOwner != null) ? MultiplayerOwner.RegisterClone(cloneObj, PlayerId, cloneColor, lifeSpriteIndex) : null;

                SwordCloneProjectile clone = cloneObj.AddComponent<SwordCloneProjectile>();
                clone.Setup(this, isAuthoritative, cloneDamage, cloneLifeTime, MultiplayerOwner, cloneId);

                // 自分自身や他の分身とは当たらないようにする
                foreach (Collider2D myCol in myColliders)
                {
                    Physics2D.IgnoreCollision(cloneCollider, myCol, true);
                }
                foreach (Collider2D otherCloneCollider in spawnedColliders)
                {
                    Physics2D.IgnoreCollision(cloneCollider, otherCloneCollider, true);
                }

                // ▼【保険】それでも壁と重なった状態で出現してしまった場合、出現直後に消えないよう
                // 重なっている壁（相手の剣ではないコライダー）とは衝突しないようにする
                foreach (Collider2D overlapped in Physics2D.OverlapCircleAll(spawnPos, cloneColliderRadius))
                {
                    if (overlapped.GetComponentInParent<SwordBattle>() == null)
                    {
                        Physics2D.IgnoreCollision(cloneCollider, overlapped, true);
                    }
                }
                spawnedColliders.Add(cloneCollider);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"❌ 分身の生成に失敗しました（{i + 1}体目）: {e}");
            }

            // 出現タイミングを1体ずつずらす（最後の1体の後は待たない）
            if (i < cloneCount - 1 && cloneSpawnStagger > 0f)
            {
                yield return new WaitForSeconds(cloneSpawnStagger);
            }
        }

        yield return new WaitForSeconds(0.3f);

        isDashing = false;
        currentDashType = 0;
        if (spriteRenderer != null) spriteRenderer.color = Color.white;
    }

    // ▼【新規追加】剣モード必殺技（hiltType:"3"）：本体の周りをシールドが公転する「リーフシールド」。
    // 発動直後（無敵の一瞬）を除けば通常通り動けるようになり、シールドはバックグラウンドで持続時間いっぱい残り続ける。
    // 各シールドは本体の攻撃力ぶんのダメージを敵に与えつつ、敵の攻撃力ぶんのダメージを受けて壊れる。
    IEnumerator LeafShieldRoutine()
    {
        isDashing = true;
        currentDashType = 6;
        currentSp = 0f;

        Color shieldColor = new Color(0.4f, 0.95f, 0.5f);

        // ▼ マルチプレイ中は演出のスローモーション(Time.timeScale変更)が全員の画面をブロックしてしまうため、
        // 他の剣モード必殺技と同様にローカル/テストモード時だけ再生する
        if (CutinManager.Instance != null && spriteRenderer != null)
        {
            CutinManager.Instance.PlayCutin(spriteRenderer.sprite, swordName, "オレ達シールド!!", shieldColor, PlayerMainColor, true);
        }

        if (spriteRenderer != null) spriteRenderer.color = shieldColor;

        // ▼【重要】実際にシールドを展開・判定するのはHost権威（またはローカル/テストモード）側のみ。
        // Client側の見た目再生（Kinematicコピー）では展開しない（見た目はSYNCで複製される）
        bool isAuthoritative = rb == null || rb.bodyType == RigidbodyType2D.Dynamic;
        if (isAuthoritative && spriteRenderer != null)
        {
            float shieldHp = Mathf.Max(1f, maxHp * leafShieldHpRatio);
            float sectorSize = 360f / Mathf.Max(1, leafShieldCount);

            for (int i = 0; i < leafShieldCount; i++)
            {
                GameObject shieldObj = new GameObject("LeafShield_" + i);

                // ▼【新規追加】シールドたちに「今使っている自分の剣 + まだ使っていない手持ちの剣」の形を
                // 持たせる(CloneRushRoutineと同じ考え方。残機モードのON/OFFに関わらず、手持ちが1・2本でも安全)
                int lifeSpriteIndex = -1;
                Sprite shieldSprite = spriteRenderer.sprite;
                if (MultiplayerOwner != null)
                {
                    int wantedIndex = MultiplayerOwner.GetCurrentLifeIndex(PlayerId) + i;
                    var lifeSprite = MultiplayerOwner.GetLifeSprite(PlayerId, wantedIndex);
                    if (lifeSprite != null) { shieldSprite = lifeSprite; lifeSpriteIndex = wantedIndex; }
                }

                SpriteRenderer sr = shieldObj.AddComponent<SpriteRenderer>();
                sr.sprite = shieldSprite;
                sr.color = shieldColor;
                sr.sortingLayerID = spriteRenderer.sortingLayerID;
                sr.sortingOrder = spriteRenderer.sortingOrder;
                // ▼【新規追加】手持ちの剣は元画像のピクセルサイズがバラバラなため、現在の刀身と見た目の
                // 横幅が揃うようスケールを正規化する。当たり判定(leafShieldRadius)は形状に関わらず固定なので、
                // これをしないと見た目が大きい個体ほど「当たらない」ように見えていた
                float shieldRefWidth = spriteRenderer.sprite != null ? spriteRenderer.sprite.bounds.size.x : 0f;
                float shieldSpriteWidth = shieldSprite != null ? shieldSprite.bounds.size.x : 0f;
                if (shieldRefWidth > 0f && shieldSpriteWidth > 0f) shieldObj.transform.localScale = Vector3.one * (shieldRefWidth / shieldSpriteWidth);

                CircleCollider2D col = shieldObj.AddComponent<CircleCollider2D>();
                col.isTrigger = true;
                col.radius = leafShieldRadius;

                LeafShieldOrb orb = shieldObj.AddComponent<LeafShieldOrb>();
                orb.Setup(this, i * sectorSize, leafShieldOrbitRadius, leafShieldOrbitSpeed, shieldHp, leafShieldReflectMultiplier, leafShieldDuration);

                // ▼ オンライン対戦では、この分身をMultiplayerManagerに登録し、位置をSYNCでクライアントへ配信する
                if (MultiplayerOwner != null)
                {
                    string id = MultiplayerOwner.RegisterClone(shieldObj, PlayerId, shieldColor, lifeSpriteIndex);
                    orb.SetNetworkId(MultiplayerOwner, id);
                }
            }
        }

        yield return new WaitForSeconds(0.2f);

        isDashing = false;
        currentDashType = 0;
        if (spriteRenderer != null) spriteRenderer.color = Color.white;
    }

    // ▼【新規追加】分身の出現位置を決める。baseAngleを中心とした区画(sectorSize)内でランダムな方向・距離を選び、
    // 壁・床と重なる場合は区画内で角度をずらしながら数回試行する。分身ごとに区画をずらすことで位置が偏らないようにする。
    // 見つからなければ最後に試した場所を返す（呼び出し側でIgnoreCollisionの保険をかける）
    // 画面端など、割り当てられた方向(baseAngle)がずっと壁で塞がっている場合に備え、
    // 後半の試行では区画に関係なく全方位からも探す
    private Vector3 FindCloneSpawnPosition(float colliderRadius, float baseAngle, float sectorSize)
    {
        const int attemptCount = 8;
        const int sectorAttempts = 3; // 最初の数回は割り当てられた方向を優先して探す

        Vector3 candidate = transform.position;
        for (int attempt = 0; attempt < attemptCount; attempt++)
        {
            float angle = attempt < sectorAttempts
                ? baseAngle + Random.Range(-sectorSize * 0.5f, sectorSize * 0.5f)
                : Random.Range(0f, 360f); // 割り当てられた区画が塞がっている場合は全方位から探す

            // 半径も塞がっている場合に備え、徐々に自分に近い側も試す
            float minRadius = Mathf.Lerp(cloneSpawnMinRadius, 0.5f, (float)attempt / (attemptCount - 1));
            float radius = Random.Range(Mathf.Min(minRadius, cloneSpawnMaxRadius), cloneSpawnMaxRadius);

            Vector2 dir = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
            candidate = transform.position + (Vector3)(dir * radius);

            bool blockedByWall = false;
            foreach (Collider2D overlapped in Physics2D.OverlapCircleAll(candidate, colliderRadius))
            {
                if (overlapped.GetComponentInParent<SwordBattle>() == null)
                {
                    blockedByWall = true;
                    break;
                }
            }

            if (!blockedByWall) return candidate;
        }
        return candidate;
    }

    // ▼【新規追加】巨大化一回転（currentDashType 4）専用のダメージ判定
    // （回転中は当たり判定をトリガー化しているためOnCollisionEnter2Dではなくこちらで処理する）
    void OnTriggerEnter2D(Collider2D other)
    {
        if (isDead || matchEnded) return;
        if (currentDashType != 4) return;

        // ▼【重要】トリガー判定はKinematic同士でも発火してしまう。
        // 他の必殺技と同様、実際のダメージ計算はDynamic（＝物理演算の権威を持つHost側）でのみ行い、
        // Client側の見た目再生（Kinematicコピー）では二重計算しない。HP等はHostからのSYNCで同期される。
        if (rb == null || rb.bodyType != RigidbodyType2D.Dynamic) return;

        SwordBattle target = other.GetComponentInParent<SwordBattle>();
        if (target == null || target == this) return;
        // ▼【修正】同じ相手への連続ヒット(回転中に何度もすれ違う)だけを防ぐ。Add()はこの相手が
        // 初めてなら追加してtrueを返すので、既にヒット済みの相手ならここでreturnして二重ヒットを防止しつつ、
        // 別の相手(初めて触れた相手)には引き続き当たるようにする
        if (!giantSpinHitTargets.Add(target)) return;

        bool isWeakPoint = other.CompareTag("Handle");
        int damage = Mathf.RoundToInt(attack * giantSpinDamageMultiplier);
        if (isWeakPoint) damage *= 2;
        damage = Mathf.Max(damage, 1);

        Vector2 hitPoint = other.ClosestPoint(transform.position);
        // ▼【修正】target.TakeDamage()を直接呼ぶと、target側がマルチプレイ中(MultiplayerOwner != null)の時に
        // 何もせず握りつぶしてしまう（HPはQueueHit経由のMatchRulesでのみ確定するため）。
        // DealDamageTo()を通すことで、オンラインでも正しくダメージが反映されるようにする
        DealDamageTo(target, damage, hitPoint, true, isWeakPoint);
    }

    // ▼【修正】剣モード専用「一直線ジャンプダッシュ」
    IEnumerator SwordDashRoutine()
    {
        isDashing = true;
        currentDashType = 3;
        isDashShooting = false; 
        
        if (spriteRenderer != null) spriteRenderer.color = new Color(0.5f, 1f, 1f); 

        currentSp = 0f;
        dashDamageBonus = 5.0f; 

        if (CutinManager.Instance != null && spriteRenderer != null)
        {
            CutinManager.Instance.PlayCutin(spriteRenderer.sprite, swordName, "大回転斬!!", new Color(0.5f, 1f, 1f), PlayerMainColor, true);
        }

        // ▼【修正】1. 小ジャンプの予備動作（Hostのみ）
        if (rb != null && rb.bodyType == RigidbodyType2D.Dynamic)
        {
            rb.linearVelocity = new Vector2(0f, 15f); 
            rb.angularVelocity = 720f; 
        }
        
        yield return new WaitForSeconds(0.2f);

        // ▼【修正】2. 空中で一瞬静止（Hostのみ）
        if (rb != null && rb.bodyType == RigidbodyType2D.Dynamic)
        {
            rb.linearVelocity = Vector2.zero; 
            rb.gravityScale = 0f;            
            rb.angularVelocity = 1440f;
        }

        yield return new WaitForSeconds(0.1f);

        isDashShooting = true; 

        // ▼【修正】3. 敵に向かって一直線に発射！（Hostのみ）
        if (controller != null && controller.enemyTarget != null && rb != null && rb.bodyType == RigidbodyType2D.Dynamic)
        {
            Vector2 dirToEnemy = (controller.enemyTarget.position - transform.position).normalized;
            rb.linearVelocity = dirToEnemy * 50f; 
            
            float spinDir = Mathf.Sign(dirToEnemy.x) * -1f; 
            rb.angularVelocity = spinDir * 2500f; 

            Debug.Log($"⚔️ 剣モード：一直線ダッシュ発動！");
        }

        yield return new WaitForSeconds(2.0f);
        if (isDashing && !SwordController.isKomaMode)
        {
            EndSwordDash();
            dashDamageBonus = 1.0f; 
        }
    }

    // ▼【修正】ダッシュ終了時
    public void EndSwordDash()
    {
        if (!isDashing) return;

        isDashing = false;
        currentDashType = 0;
        isDashShooting = false; 
        
        if (spriteRenderer != null) spriteRenderer.color = Color.white;
        
        // ▼【修正】停止処理もHostのみ
        if (rb != null && rb.bodyType == RigidbodyType2D.Dynamic)
        {
            rb.linearVelocity = Vector2.zero; 
            rb.angularVelocity = 0f;
            if (controller != null) controller.ApplyPhysicsMode();
        }
        StartCoroutine(DelayEndDashFlag());
    }
    IEnumerator DelayEndDashFlag()
    {
        // Unityの物理演算がこのフレームの処理を終えるまで待機する
        yield return new WaitForFixedUpdate();
        
        // 計算が終わって安全になってから、無敵フラグを解除！
        isDashing = false;
    }
}
