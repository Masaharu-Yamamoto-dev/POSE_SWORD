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
    // ▼【新規追加】オレ達アタック・オレ達シールドの分身を染める色。分身は剣の絵に色を掛け合わせて染めるので、
    // プレイヤーの色そのままだと暗くなりすぎる。白に寄せて明るくする（cloneTintLighten：0でプレイヤーの色そのまま、1で白）
    public Color CloneTint => Color.Lerp(PlayerMainColor, Color.white, cloneTintLighten);
    // 必殺技が撃てるようになるSPのライン（独楽モードは竜巻の70、剣モードは満タン）
    // 通常必殺技の発動ライン。既定の100は maxSp の既定値と同じなので、従来の挙動は変わらない。
    // 1vs3のボスだけ maxSp が200になるが、通常必殺技は100のままにしたいので参照先を分けてある。
    public float ultimateSp = 100f;
    public float UltimateThreshold => SwordController.isKomaMode ? 70f : ultimateSp;

    [Header("UI設定")]
    public Slider hpBar;        // 手前の緑ゲージ
    public Slider delayHpBar;   // ▼【追加】奥の赤ゲージ
    public TextMeshProUGUI nameText; // ▼ 【変更】Text から TextMeshProUGUI に変更
    public TextMeshProUGUI hpText;   // ▼ 【変更】Text から TextMeshProUGUI に変更
    public Image frameImage;    // プレイヤーのメインカラーで塗る枠（HPパネルの縁など。Editor側で用意して割り当てる）
    public Button specialAttackButton; // 必殺技専用ボタン。自キャラのSPが条件を満たした時だけ表示する（Editor側で用意して割り当てる）
    // 1vs3：掌握に切り替わっている間だけ差し替える、共有ボタンの元の見た目
    TextMeshProUGUI ultimateButtonLabel;
    Image ultimateButtonImage;
    string ultimateButtonLabelText;
    Color ultimateButtonColor;
    bool suppressSkinCaptured;
    bool suppressSkinApplied;
    // 制圧されている間の減光率（RGBに掛ける。アルファは保つ）と、今それを掛けているかどうか
    const float SuppressDim = 0.4f;
    bool suppressTinted;
    // 制圧の技番号。既存の1〜6と衝突しない値にして、色分けとカットインの同期にそのまま乗せる
    public const int SuppressDashType = 7;
    public const string SuppressSkillName = "オレ掌握斬!!";
    // 巨大回転斬の後の「ふらつき」の技番号。技番号の同期に乗せて、ゲストの画面でも点滅させる。
    // カットインは鳴らさない（TryPlayUltimateCutinの対象外）
    public const int StaggerDashType = 8;
    public bool IsStaggered => currentDashType == StaggerDashType;
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
// 刀身の絵。必殺技の演出（SkillEffects）が描画順や残像の元に使う
public SpriteRenderer BodyRenderer => spriteRenderer;

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
    public float spinDashDamageMultiplier = 4f; // 大回転斬（hiltType:"0"）が相手の剣に当たった時の、通常の衝突ダメージへの倍率（以前は5）
    // ▼【新規追加】大回転斬の終わり方。以前はその場で速さも回転も0になって落ちていた。
    // 外れた（2秒たった）時は減速しながら回転がゆるみ、敵に当たった時は反動で少し弾かれてから、どちらもゆっくり降りる。
    // 減速・弾かれている間は操作を受け付けず、ゆっくり降りる間は操作でき、ジャンプするとすぐ普通の動きに戻る
    public float spinMissSlowTime = 0.45f;      // 外れた時：減速して回転がゆるむまで
    public float spinHitRecoilTime = 0.25f;     // 当たった時：弾かれて回転がゆるむまで
    public float spinHitRecoilDistance = 2f;    // 当たった時に弾かれる距離（突進と反対向き）
    public float spinDescendTime = 0.5f;        // ゆっくり降りる：重力をこの秒数かけて元に戻す
    // 突進中に床や壁へこの角度（度）より浅く触れた時は、跳ね返さずにその面に沿って滑る。
    // 地上どうしで撃つと、回る刀身が床に触れるたびに跳ね返って、凸凹と這うように見えていた
    public float spinGrazeAngle = 30f;
    public float giantSpinScale = 5f; // 巨大化一回転（hiltType:"1"）の拡大率（以前は6）
    public float giantSpinDamageMultiplier = 8f; // 巨大化一回転が命中した時のダメージ倍率（attackへの倍率）
    public float giantSpinRotationSpeed = 1080f; // 巨大化一回転の回転速度（度/秒）。小さくするとゆっくり回るようになる（一回転にかかる時間もこれに応じて伸びる）
    // ▼【新規追加】巨大回転斬の流れ（カットインの後）：柄が光る → 3段階で巨大化 → 振りかぶり → 一回転（攻撃判定はここだけ）
    // → 余韻 → 縮む → ふらつき。秒数はゲーム内の時間で、撃破で止められている間は進まない（ふらつきを除く）
    public float giantSpinHiltGlowTime = 0.2f;   // 柄が光る（技の出どころを見せる）
    public float giantSpinGrowTime = 0.3f;       // 3段階で巨大化しきるまで
    public float giantSpinWindupTime = 0.15f;    // 振りかぶり
    public float giantSpinWindupAngle = 30f;     // 振りかぶりで回転と逆向きに引く角度（度）
    public float giantSpinLingerTime = 0.2f;     // 一回転した後、大きいまま止まる余韻
    public float giantSpinShrinkTime = 0.3f;     // 元の大きさに縮むまで
    public float giantSpinSecondHitRate = 0.7f;  // 2人目に当たった時の威力の割合
    public float giantSpinLaterHitRate = 0.5f;   // 3人目以降に当たった時の威力の割合
    public float giantSpinStaggerOnHit = 0.3f;   // 誰かに当てた後のふらつき（操作できない・無敵ではない）
    public float giantSpinStaggerOnMiss = 1.0f;  // 誰にも当たらなかった後のふらつき
    public int cloneCount = 3; // 分身突進（hiltType:"2"）の分身数
    public float cloneDashSpeed = 43f; // 分身の突進速度（以前は35）
    public float cloneDamageMultiplier = 3f; // 分身1体が命中した時のダメージ倍率（attackへの倍率）
    public float cloneLifeTime = 3f; // 何にも当たらなかった場合に分身が自動的に消えるまでの秒数
    // ▼【変更】本体が溜めてから、本体の位置から1体ずつ撃ち出す。撃つ瞬間にその時の相手を狙い、撃った後しばらくは弱く追尾する。
    // 以前は分身を持ち場へ出して漂わせ、構えて身を引いてから撃っていたが、ごちゃつくうえ、頭上にリフトがあると撃った直後に当たって消えていた
    public float cloneLaunchInterval = 0.35f;  // 1体ずつ撃つ間隔（溜め終わってから、本体の位置から1体ずつ撃ち出す）
    public float cloneChargeTime = 0.45f;      // 撃ち始める前に本体が溜める時間
    public float cloneHomingTurnRate = 120f;   // 追尾で曲がれる速さの上限（度/秒）
    public float cloneHomingTime = 0.8f;       // 撃ってから追尾する時間。過ぎたらまっすぐ飛ぶ（以前は0.6）
    // 撃った後に狙った相手が倒れた時：次の相手が進む向きのこの角度以内にいれば、そちらへ向きを変える（それ以外は真っすぐ）
    public float cloneRetargetCone = 60f;
    public float cloneRetargetTurnRate = 300f; // その時だけ曲がれる速さ（度/秒）
    public float cloneRetargetTime = 0.6f;     // その時だけ追尾する時間
    [Range(0f, 1f)] public float cloneTintLighten = 0.5f; // 分身を染める色を、プレイヤーの色からどれだけ白に寄せるか
    public float cloneLifestealRate = 0.2f; // 分身が相手のHPを実際に減らした量のうち、本体が回復する割合（ライフスティール）
    public int leafShieldCount = 3; // リーフシールド（hiltType:"3"）の展開枚数
    public float leafShieldDuration = 15f; // リーフシールドの持続時間（秒）
    public float leafShieldOrbitRadius = 3.5f; // 本体からの周回半径（＝防御圏の広さ）
    public float leafShieldRadius = 0.7f; // シールド1枚あたりの当たり判定の大きさ
    public float leafShieldOrbitSpeed = 150f; // 周回速度（度/秒）
    public float leafShieldReflectMultiplier = 1f; // 返すダメージの倍率（分身に触れた敵には自分の攻撃力、本体を殴ってきた敵にはその敵の攻撃力に掛ける）
    // ▼【変更】オレ達シールドは防御寄りに：本体は無敵ではなく受けるダメージが減り、分身は回数で割れる
    public int leafShieldDurability = 2;            // 分身1枚の耐久（触れられる・本体が被弾するたびに1減り、0で割れる）
    // ▼ 輪の広がり方：普段は leafShieldOrbitRadius。一番近い敵が「感知射程」に入ったら広げ始め、広げた後は「実射程」まで追いかける。
    // 始める距離（感知射程）とやめる距離（実射程）に差があるので、境目を敵が通っても広がったり戻ったりを繰り返さない
    // ▼【変更】感知射程を最大の届く距離より長くした。遠くの敵には輪を最大まで広げて構え、届くのは最大より近い敵だけ。
    // 感知射程の外へ出たら、少し待ってゆっくり戻す（境目で出入りしても輪がゆっくり動くだけなので、揺れが目立たない）
    public float leafShieldSenseRadius = 10f;       // 感知射程：一番近い敵がここまで入ったら広げ始め、ここから出たら戻す
    public float leafShieldMaxOrbitRadius = 8f;     // 最大の届く距離（輪の半径の上限）
    public float leafShieldReachDepth = .5f;        // 輪を「敵の一番近い点」よりこれだけ外に合わせ、分身が敵の体に食い込む位置を通るようにする
    public float leafShieldChaseOrbitSpeed = 150f;  // 敵を追っている間の回る速さ（度/秒）。今は普段（leafShieldOrbitSpeed）と同じにしている（以前は180）
    public float leafShieldReachTime = .15f;        // 広げる時に、どこまで広げてもほぼ届くまでの時間（着く直前はなめらかに減速）
    public float leafShieldRadiusReturnSpeed = 1.5f; // 輪を普段の半径へ戻す速さ（毎秒）。広げる時よりゆっくり戻して落ち着いて見せる（以前は3）
    public float leafShieldReachStartDelay = 0f;    // 敵が感知射程にこの秒数いたら広げ始める（0ならすぐ。一瞬かすめただけで広げたくない時に上げる）
    public float leafShieldReachHoldTime = .4f;     // 実射程から出ても、この秒数は広げたまま待ってから戻す
    public float leafShieldDamageTakenRate = .5f;   // シールドが出ている間、本体が受けるダメージの割合
    public float shieldRetaliateInterval = .3f;     // 同じ敵に返すのは、この秒数に1回まで
    public float leafShieldKnockbackRate = .3f;     // シールドが出ている間、剣同士がぶつかった時に自分が弾かれる強さの割合（鎧で踏みとどまる）

    // 突進状態の管理用
// 突進状態の管理用
    [HideInInspector] public bool isDashing = false;
    [HideInInspector] public bool isDashShooting = false;
    // ▼【新規追加】大回転斬の段階（SpinDashStage の値）。演出の切り替えと、終わり際の操作の受け付けに使う。
    // オンライン対戦ではホストが決めてゲストへ同期する
    [HideInInspector] public int spinDashPhase = 0;
    // ▼【新規追加】巨大回転斬の段階（GiantSpinStage の値。ふらつきは含まない）。演出の切り替えに使い、ホストからゲストへ同期する
    [HideInInspector] public int giantSpinPhase = 0;
    private float dashDamageBonus = 1.0f;
    // ▼【修正】単一のboolだと「最初に当てた1人」以降、他の誰にも一切ヒットしなくなってしまい、
    // 3〜4人戦で2人目・3人目に必殺技が当たらない不具合の原因になっていた。回転中に同じ相手へ
    // 何度も連続ヒットするのを防ぎたいだけなので、「すでに当てた相手の集合」で管理し、
    // 別の相手には引き続き当たるようにする
    private readonly HashSet<SwordBattle> giantSpinHitTargets = new HashSet<SwordBattle>();
    // ▼【新規追加】巨大回転斬の攻撃判定が有効なのは一回転の間だけ（巨大化・振りかぶり・余韻・縮む間は当たらない）
    private bool giantSpinHitActive;
    // 1つの物理ステップで触れた相手をここに貯め、次のFixedUpdateで近い順に当てる（2人目以降の減衰の順番を決めるため）
    private readonly List<(SwordBattle target, Collider2D part)> pendingGiantSpinHits = new List<(SwordBattle target, Collider2D part)>();
    // ▼【調査用】必殺技ボタンがでっぱなしになる不具合調査用の、一度きりログ発火フラグ
    private bool loggedUpdateStall = false;
    private bool loggedButtonGuardStall = false;
    private bool wasLocalControlledForButton = false;

    // ▼【変更】オレ達シールドが展開されている間の本体の守り。以前は1枚でも残っていれば本体が完全無敵だったが、
    // 受けるダメージを減らす（leafShieldDamageTakenRate）だけにし、柄も弱点にならないようにした
    readonly List<LeafShieldOrb> activeShieldOrbs = new List<LeafShieldOrb>();
    public bool HasActiveLeafShield => activeShieldOrbs.Count > 0;
    public void RegisterShieldOrb(LeafShieldOrb orb) { if (orb != null && !activeShieldOrbs.Contains(orb)) activeShieldOrbs.Add(orb); }
    public void UnregisterShieldOrb(LeafShieldOrb orb) { activeShieldOrbs.Remove(orb); }
    // 今の輪の半径。分身(LeafShieldOrb)はこれを見て回る
    float leafShieldRadiusNow = -1f;
    public float LeafShieldOrbitRadiusNow => leafShieldRadiusNow > 0f ? leafShieldRadiusNow : leafShieldOrbitRadius;
    // 今の回る速さ（度/秒）。敵を追って輪を広げている間は速く回し、敵の方向を通る回数を増やす
    public float LeafShieldOrbitSpeedNow => shieldReaching ? leafShieldChaseOrbitSpeed : leafShieldOrbitSpeed;

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
        currentDashType = 0; currentSp = 0; spinDashPhase = 0; giantSpinPhase = 0; cloneRushPhase = 0; cloneRushShotsLeft = 0;
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
            MultiplayerOwner.IsPlaying, IsAlive, InputBlockedBySkill, right, MultiplayerOwner.IsSuppressed(PlayerId) || MultiplayerOwner.IsRooted(PlayerId) || IsStaggered);
        switch (action)
        {
            case "KomaDash": StartCoroutine(DashRoutine()); break;
            case "JumpRight": Jump(true); break;
            case "JumpLeft": Jump(false); break;
        }
    }

    // ▼【新規追加】必殺技専用ボタン/スペースキー由来の入力：SPが条件を満たしている時だけ発動する
    // ▼【修正】SPが足りているかどうかの判定だけBattlePoliciesで行い、実際にどの技が出るかはUltimateRoutine()の
    // hiltType分岐に任せる（以前はTornado/SwordDash固定で、柄違いの必殺技がオンラインでは一切出せなかった）
    public void ExecuteMultiplayerUltimate()
    {
        if (MultiplayerOwner == null || !MultiplayerOwner.IsHost) return;
        // 閾値はUIと同じ ultimateSp を渡す。ここを固定値にすると、ボス(maxSp=200)で
        // 「ボタンは出ていないのに撃てる」ような食い違いが起きる。
        string action = PoseSword.Multiplayer.BattlePolicies.UltimateAction(SwordController.isKomaMode, currentSp,
            MultiplayerOwner.IsPlaying, IsAlive, isDashing, MultiplayerOwner.IsSuppressed(PlayerId) || MultiplayerOwner.IsRooted(PlayerId) || IsStaggered, ultimateSp);
        if (action == null) return;
        StartCoroutine(UltimateRoutine());
    }

    // ▼【新規追加】1vs3：ボスの制圧。自分の画面から送るだけで、実際に効くかどうかはHostが決める
    // ▼【新規追加】1vs3：SPが満タンまで溜まっていて、掌握に切り替わっている状態かどうか。
    // ボスだけ maxSp が200で、トリオは maxSp が100=通常必殺技のラインなので、
    // 「満タン かつ 通常必殺技のラインより上」を条件にすればボス以外では立たない。
    public bool SuppressReady
    {
        get
        {
            return MultiplayerOwner != null && MultiplayerOwner.SoloModeActive &&
                maxSp > ultimateSp && currentSp >= maxSp &&
                IsAlive && !isDashing && !MultiplayerOwner.IsSuppressed(PlayerId);
        }
    }

    // ▼【新規追加】Host側の実行口。範囲判定と効果の適用はMultiplayerManagerが持つ
    public void ExecuteMultiplayerSuppress()
    {
        if (MultiplayerOwner == null || !MultiplayerOwner.IsHost) return;
        MultiplayerOwner.ApplySuppress(PlayerId);
    }

    // 制圧の発動で消費するSP。ゲージ満タンぶんを使い切る
    public void ConsumeSuppressSp() { currentSp = 0f; }

    // ▼【新規追加】1vs3：ボスを一回り大きくする。
    // baselineScale ごと掛けるのが要点。ここを現在の localScale だけにすると、
    // 残機モードの持ち替え(ReviveFromDefeat)や必殺技演出の後始末で baselineScale へ
    // 戻された瞬間にボスが元の大きさに縮んでしまう。
    // Host側は localScale.x をSYNCで配っているので、ゲストの画面にもそのまま乗る。
    public void ApplyBaseScale(float multiplier)
    {
        if (multiplier <= 0f || Mathf.Approximately(multiplier, 1f)) return;
        baselineScale = new Vector3(baselineScale.x * multiplier, baselineScale.y * multiplier, baselineScale.z);
        transform.localScale = new Vector3(transform.localScale.x * multiplier,
            transform.localScale.y * multiplier, transform.localScale.z);
    }

    // ▼【新規追加】1vs3：掌握の3段目（薙ぎ払い）の動き。巨大回転斬(GiantSpinRoutine)を参考にした流れで、
    // 振りかぶり → ちょうど一回転 → 余韻。巨大回転斬と違い、剣の大きさは変えない。
    // 相手は「刀身が通り過ぎた瞬間」に1人ずつ斬る（onPass）。ダメージと吹き飛ばしはMultiplayerManagerが持つ。
    // 位置・回転・大きさはいずれも同期に乗っているので、ゲストの画面にもそのまま見える。Hostだけが呼ぶ。
    public struct JudgmentSweepMotion
    {
        public float windupSeconds;  // 振りかぶり
        public float windupAngle;    // 振りかぶりで回転と逆向きに引く角度（度）
        public float spinSpeed;      // 一回転の速さ（度/秒）。一回転にかかる時間は 360 / spinSpeed
        public float lingerSeconds;  // 一回転した後、その場に止まる余韻
    }

    public void BeginJudgmentSweep(JudgmentSweepMotion motion, IList<SwordBattle> targets, System.Action<SwordBattle> onPass, System.Action onFinished)
    {
        if (!IsAlive) { onFinished?.Invoke(); return; }
        StartCoroutine(JudgmentSweepRoutine(motion, targets, onPass, onFinished));
    }

    IEnumerator JudgmentSweepRoutine(JudgmentSweepMotion motion, IList<SwordBattle> targets, System.Action<SwordBattle> onPass, System.Action onFinished)
    {
        bool controlsPhysics = rb != null && rb.bodyType == RigidbodyType2D.Dynamic;
        float originalGravityScale = 0f;
        if (controlsPhysics)
        {
            // 技の間はその場に留める（巨大回転斬と同じ）
            originalGravityScale = rb.gravityScale;
            rb.gravityScale = 0f;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
        // 集めた相手は中心に固まっているので、当たり判定をトリガーにして引っかからずに回り切れるようにする
        Collider2D[] myColliders = GetComponentsInChildren<Collider2D>();
        bool[] originalTriggerStates = new bool[myColliders.Length];
        for (int i = 0; i < myColliders.Length; i++)
        {
            originalTriggerStates[i] = myColliders[i].isTrigger;
            myColliders[i].isTrigger = true;
        }

        // 回る向き：巨大回転斬と同じく、狙っている相手の方へ振り下ろす向き
        float spinDir = 1f;
        if (controller != null && controller.enemyTarget != null)
        {
            float dx = controller.enemyTarget.position.x - transform.position.x;
            if (Mathf.Abs(dx) > 0.01f) spinDir = Mathf.Sign(dx) * -1f;
        }

        // 1. 振りかぶり：回る向きと逆に少し引く
        if (controlsPhysics && motion.windupSeconds > 0f) rb.angularVelocity = -spinDir * motion.windupAngle / motion.windupSeconds;
        yield return StartCoroutine(WaitSkillTime(motion.windupSeconds));

        // 2. ちょうど一回転。刀身の向き(transform.up)から回る向きに測って、相手のいる角度を通り過ぎた瞬間に斬る
        float speed = Mathf.Max(1f, Mathf.Abs(motion.spinSpeed));
        float duration = 360f / speed;
        var remaining = new List<(SwordBattle target, float angle)>();
        if (targets != null)
        {
            Vector2 centre = transform.position;
            float bladeAngle = Mathf.Atan2(transform.up.y, transform.up.x) * Mathf.Rad2Deg;
            foreach (var target in targets)
            {
                if (target == null) continue;
                Vector2 toTarget = (Vector2)target.transform.position - centre;
                float targetAngle = Mathf.Atan2(toTarget.y, toTarget.x) * Mathf.Rad2Deg;
                remaining.Add((target, Mathf.Repeat((targetAngle - bladeAngle) * spinDir, 360f)));
            }
            remaining.Sort((a, b) => a.angle.CompareTo(b.angle));
        }
        float swept = 0f;
        while (swept < 360f)
        {
            // 摩擦で減速しないよう、回っている間は毎フレーム速さを掛け直す
            if (controlsPhysics) rb.angularVelocity = speed * spinDir;
            yield return null;
            if (!SkillClockStopped) swept += speed * Time.deltaTime;
            while (remaining.Count > 0 && remaining[0].angle <= swept)
            {
                onPass?.Invoke(remaining[0].target);
                remaining.RemoveAt(0);
            }
        }
        // 取りこぼし（中心に重なっていた相手など）は回り切った時点で斬る
        foreach (var rest in remaining) onPass?.Invoke(rest.target);
        if (controlsPhysics) rb.angularVelocity = 0f;

        // 3. 余韻
        yield return StartCoroutine(WaitSkillTime(motion.lingerSeconds));

        for (int i = 0; i < myColliders.Length; i++)
            if (myColliders[i] != null) myColliders[i].isTrigger = originalTriggerStates[i];
        if (controlsPhysics) rb.gravityScale = originalGravityScale;
        onFinished?.Invoke();
    }

    // ▼【新規追加】剣本体以外（分身・リーフシールドなど）からも、TakeDamage/QueueHitへの正しい経路で
    // ダメージを与えられるようにする共通口。ローカルではTakeDamageへ、オンラインではHost権威のQueueHitへ回す。
    // byBody：剣の本体で当てたか（巨大回転斬は本体、分身・リーフシールドは本体ではない）
    // lifestealRate：相手のHPを実際に減らした量のうち、自分が回復する割合（0〜1。オレ達アタックの分身が渡す）。
    // オンラインではMatchRulesがダメージを確定した後に回復させ、ローカルではここで減った量を見て回復する
    public void DealDamageTo(SwordBattle target, int damage, Vector2 hitPoint, bool isCrit = false, bool isWeakPoint = false, bool byBody = false,
        float lifestealRate = 0f)
    {
        if (target == null || target == this || !target.IsAlive) return;
        if (MultiplayerOwner != null)
        {
            MultiplayerOwner.QueueHit(this, target, damage, hitPoint, isCrit || isWeakPoint, byBody, lifestealRate);
            return;
        }
        int hpBefore = target.hp;
        target.TakeDamage(damage, hitPoint, isCrit, isWeakPoint, this, byBody);
        int dealt = Mathf.Max(0, hpBefore - target.hp);
        // 端数はMatchRulesと同じく四捨五入（0.5は切り上げ）
        if (lifestealRate > 0f && dealt > 0) HealLocally((int)System.Math.Round(dealt * (double)Mathf.Clamp01(lifestealRate), System.MidpointRounding.AwayFromZero));
    }

    // ▼【新規追加】ローカル対戦のライフスティール。今の剣の最大HPを超えない
    void HealLocally(int amount)
    {
        if (amount <= 0 || isDead) return;
        int healed = Mathf.Min(amount, maxHp - hp);
        if (healed <= 0) return;
        hp += healed;
        UpdateUI();
        ShowHealPopup(healed);
    }

    void ShowHealPopup(int amount)
    {
        if (damagePopupPrefab != null) DamagePopup.SpawnHeal(damagePopupPrefab, this, amount, PlayerId);
    }

    // ▼【新規追加】オンライン対戦のダメージ1件ぶんの見た目（当たった場所の火花とダメージ数字）。
    // HostとゲストのどちらもMultiplayerManagerのダメージ記録から呼ぶ。音・ヒットストップ・揺れは
    // これまでどおりPlayClientDamageEffect（HPの減り）側で鳴らす
    // persist：決着の一撃。数字と撃破演出の印を消さずに残す。出した数字を返す（決着のカメラが寄る先に使う）
    public DamagePopup PlayHitVisual(Vector2 point, int damage, bool isCrit, bool lethal, bool persist, DamagePopup.Relation relation, Color attackerColor)
    {
        // 撃破の一撃は専用のヒットエフェクト（DefeatEffects）を出す。用意できなかった時は、
        // 通常かクリティカルかに関わらず一番大きいヒットエフェクト（クリティカルの物）を出す
        // ほかのヒットエフェクトと同じく、やられた剣より手前・撃破の数字と墨の印より後ろに出す
        bool killHitPlayed = lethal && DefeatEffects.Current.PlayKillHit(point, DefeatGhost.HitEffectSortingOrder);
        ParticleSystem effect = isCrit || lethal ? critEffectPrefab : normalEffectPrehub;
        if (effect != null && !killHitPlayed)
        {
            // ダメージのヒットエフェクトは、手前に出したやられた剣より、さらに手前に出す
            var spawned = Instantiate(effect, point, Quaternion.identity);
            foreach (var r in spawned.GetComponentsInChildren<ParticleSystemRenderer>(true))
                r.sortingOrder = DefeatGhost.HitEffectSortingOrder + r.sortingOrder;
        }
        if (damagePopupPrefab == null) return null;
        return DamagePopup.Spawn(damagePopupPrefab, point, this, damage, isCrit, lethal, relation, attackerColor, PlayerId, persist);
    }

    // ▼【新規追加】撃破の見た目（オンライン対戦）。剣（刀身と柄）を撃破の見た目に変え、吹き飛んでから少しして
    // 薄れながら消す。見た目そのもの（標準は墨）は DefeatEffects が担当し、ここはタイミングと後始末だけを持つ。
    // 消え切るまではカメラの画角に含める。決着の時は FinishMultiplayer が StopAllCoroutines するので、消えずに残る
    // 流れ：やられた瞬間に震え（デュクシ）→ とどめの印（墨の帯→一閃→飛沫）を書き終えて余韻が過ぎるまで、元の見た目のまま
    // その場で止まる → 色が抜けて跳ね上がる → 少しして薄れながら消える。当たり判定はやられた時点で消えている。
    // 止める・跳ね上げるのは物理を持つホストだけで、ゲストは位置の同期で同じ動きになる
    bool defeatedSortingRaised;

    void BeginDefeatVisual()
    {
        var renderers = GetComponentsInChildren<SpriteRenderer>();
        // やられた剣は、撃破した側の剣に隠れないよう手前に出す（ヒットエフェクト・数字・墨よりは奥）
        if (!defeatedSortingRaised)
        {
            defeatedSortingRaised = true;
            foreach (var r in renderers) r.sortingOrder += DefeatGhost.DefeatedSortingOffset;
        }
        DefeatJitter.Play(gameObject, renderers);
        if (MultiplayerOwner != null) MultiplayerOwner.AddCameraTarget(transform);
        if (rb != null) { rb.linearVelocity = Vector2.zero; rb.angularVelocity = 0f; rb.gravityScale = 0f; }
        StartCoroutine(VanishRoutine(renderers));
    }

    IEnumerator VanishRoutine(SpriteRenderer[] renderers)
    {
        yield return new WaitForSeconds(DefeatEffects.Current.KillMarkDuration);
        // 跳ね上がる瞬間に、倒された見た目（色が抜ける）にする
        DefeatEffects.Current.ApplyDefeatedLook(renderers);
        // 吹き飛ばす：質量に関係なく同じ速さで跳ね上げ、回転は控えめに（抜け殻 DefeatGhost も同じ値）
        if (rb != null && (MultiplayerOwner == null || MultiplayerOwner.IsHost))
        {
            rb.gravityScale = DefeatGhost.GravityScale;
            rb.linearVelocity = new Vector2(Random.Range(-DefeatGhost.KnockSide, DefeatGhost.KnockSide), DefeatGhost.KnockUp);
            rb.angularVelocity = Random.value < .5f ? -DefeatGhost.Spin : DefeatGhost.Spin;
        }
        yield return new WaitForSeconds(DefeatGhost.VanishDelay);
        var bounds = new Bounds(transform.position, Vector3.zero);
        foreach (var r in renderers) if (r != null) bounds.Encapsulate(r.bounds);
        DefeatEffects.Current.PlayVanish(bounds.center, 1f);
        Vector3 start = transform.localScale;
        var startAlpha = new float[renderers.Length];
        for (int i = 0; i < renderers.Length; i++) startAlpha[i] = renderers[i] != null ? renderers[i].color.a : 0f;
        float t = 0f;
        while (t < DefeatGhost.VanishTime)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / DefeatGhost.VanishTime);
            transform.localScale = new Vector3(start.x * (1f + .12f * k), start.y * (1f + .12f * k), start.z);
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) { var c = renderers[i].color; c.a = startAlpha[i] * (1f - k); renderers[i].color = c; }
            DefeatEffects.Current.UpdateVanish(renderers, k);
            yield return null;
        }
        foreach (var r in renderers) if (r != null) r.enabled = false;
        DefeatEffects.Current.UpdateVanish(renderers, 1f);
        if (MultiplayerOwner != null) MultiplayerOwner.RemoveCameraTarget(transform);
    }

    // ▼【新規追加】残機制で剣が1本壊れたとき、次に使うはずだった残機アイコンを墨の線で消してから、並びを更新する
    public void CrossOutReserveIcon(System.Action updateIcons)
    {
        var icon = reserveSwordIcons != null && reserveSwordIcons.Length > 0 ? reserveSwordIcons[0] : null;
        InkCrossOut.Play(icon, updateIcons);
    }

    // ▼【新規追加】残機モード：現在の剣が破壊され、待ち時間が明けた時に、「倒された」状態だけを解除して次の剣に持ち替えさせる。
    // 壊れた剣は待っている間に撃破と同じやられ演出（ApplyMultiplayerHealth→BeginDefeatVisual）を済ませている。
    // ステータス(hp/maxHp/attack/hiltType)と見た目の刀身自体はMultiplayerManager側がこの直後に
    // SwordGenerator.GenerateSwordFromJsonを呼び直すことで更新するので、ここでは死亡時に無効化した
    // 見た目・当たり判定・操作を元に戻すだけでよい(ConfigureMultiplayerの死亡状態リセット部分と同等)
    public void ReviveFromDefeat()
    {
        if (MultiplayerOwner == null) return;
        isDead = false; isDashing = false; isDashShooting = false; skillAllowsMovement = false;
        currentDashType = 0; currentSp = 0; spinDashPhase = 0; giantSpinPhase = 0; cloneRushPhase = 0; cloneRushShotsLeft = 0;
        StopUltimateAura();
        StopAllCoroutines();
        controller.enabled = true;
        foreach (var collider in GetComponentsInChildren<Collider2D>(true)) collider.enabled = true;
        // 撃破演出で変えた見た目（色・非表示・描画順）を、刀身と柄の両方とも元に戻す。にじみ用の写しは消す
        foreach (var r in GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (r.name.StartsWith(InkDefeatEffect.BleedName)) { Destroy(r.gameObject); continue; }
            r.enabled = true; r.color = Color.white;
            if (defeatedSortingRaised) r.sortingOrder -= DefeatGhost.DefeatedSortingOffset;
        }
        defeatedSortingRaised = false;
        if (MultiplayerOwner != null) MultiplayerOwner.RemoveCameraTarget(transform);
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
            PlayShieldGuard();
            PlayClientDamageEffect(damage, wasCrit);
            if (MultiplayerOwner.IsHost) currentSp = Mathf.Min(maxSp, currentSp + 100f * damage / maxHp * damageSpMultiplier);
        }
        // ▼【新規追加】ライフスティールでHPが増えた時は、回復した量を出す（ホストもゲストも同じHPの同期から判断する）。
        // 残機制の持ち替えは、新しい剣を作る時に先にHPが入れ直されるので、ここでは増えた扱いにならない
        int healed = hp > 0 ? Mathf.Max(0, Mathf.Min(health, maxHp) - hp) : 0;
        hp = Mathf.Clamp(health, 0, maxHp); UpdateUI();
        if (healed > 0) ShowHealPopup(healed);
        if (hp > 0) return;
        isDead = true; isDashing = false; currentDashType = 0; skillAllowsMovement = false; spinDashPhase = 0; giantSpinPhase = 0; cloneRushPhase = 0; cloneRushShotsLeft = 0;
        RestoreTeammateCollisions();
        SetHiltGlow(false);
        giantSpinHitActive = false;
        pendingGiantSpinHits.Clear();
        StopUltimateAura();
        // ▼【修正】撃破に値する一撃(damage>=20等)は直前のPlayClientDamageEffectでHitStopRoutineを
        // 開始しており、Time.timeScaleを0.05にした直後にここのStopAllCoroutines()が
        // それを巻き込んで強制停止させてしまう。HitStopRoutine側の後処理(等速へ戻す)が
        // 一度も実行されず、3〜4人戦でまだ試合が終わっていない撃破でもスローのまま固まっていた。
        // 試合自体が終わった場合はMatchEndCinematicが改めてtimeScaleを制御するのでmatchEndedの時は触らない
        StopAllCoroutines();
        // 必殺技のカットインがスローをかけている最中なら、等速へ戻すのはカットイン側に任せる
        if (!matchEnded && !CutinSlowingTime) Time.timeScale = 1f;
        // ▼【新規追加】倒された後はUpdate()が止まり、必殺技ボタンが更新されなくなる。SPが溜まったまま倒されると
        // 撃てる見た目で固まるので、操作を外す前に一度だけ「撃てない」状態にしておく
        // （残機制では次の剣が出るまで数秒あるので特に目立つ。次の剣が出ればUpdate()がまた更新する）
        UpdateSpecialAttackButton();
        controller.enabled = false; controller.isLocalControlled = false;
        foreach (var collider in GetComponentsInChildren<Collider2D>(true)) collider.enabled = false;
        // ▼【変更】灰色にする代わりに撃破演出の見た目にし、少しして消す（刀身だけでなく柄も）
        BeginDefeatVisual();
        if (defeatSound != null) audioSource.PlayOneShot(defeatSound);
        // ▼【修正】ローカルのDefeatRoutineと同じく、その場で静止させず天高く吹き飛ばして回転させる
        // (以前はrb.simulated=falseで即座に静止させていたため、脱落の派手さがマルチプレイだけ失われていた)。
        // またローカルは脱落後も剣を消さず、倒れたままの姿を残して他の生存者の試合を続けるため、
        // 以前あった0.8秒後に非表示にする処理(HideEliminatedSword)も削除した
        // ▼【変更】以前はここで質量に関係なく同じ力で弾いていたため、軽い剣ほど天高く飛び、長い距離を落ちてきて、
        // 生きている剣と見分けにくかった。今は BeginDefeatVisual がいったんその場に止め、とどめの印を書き終えてから
        // 質量に関係なく同じ速さで跳ね上げる（VanishRoutine）
    }

    // ▼【修正】dashType 4(巨大化一回転)・5(分身突進)・6(リーフシールド発動)の色分けと、
    // 巨大化のスケールをクライアント側にも反映する
    public void ApplyMultiplayerVisuals(float sp, bool dashing, int dashType, float scale, int spinPhase = 0, int giantPhase = 0,
        int clonePhase = 0, int cloneShots = 0)
    {
        if (!IsAlive) return;
        spinDashPhase = spinPhase;
        giantSpinPhase = giantPhase;
        cloneRushPhase = clonePhase;
        cloneRushShotsLeft = cloneShots;
        // ▼【新規追加】Client側はUltimateRoutine()自体をローカル実行しない（Host権威でのみ実行される）ため、
        // カットインもここで再生する。dashTypeが必殺技の値に変わった瞬間（0/1→2〜6）だけ発火させる
        int previousDashType = currentDashType;
        currentSp = Mathf.Clamp(sp, 0, maxSp); isDashing = dashing; currentDashType = dashType;
        if (dashType != previousDashType) TryPlayUltimateCutin(dashType);
        if (spriteRenderer != null)
        {
            Color tint = ColorForDashType(dashType);
            // ▼ リスポーン無敵中の点滅(Update()側でアルファだけ操作)を上書きしないよう、
            // 現在のアルファ値は維持したまま色(RGB)だけ差し替える
            tint.a = spriteRenderer.color.a;
            spriteRenderer.color = tint;
        }
        // ▼ Host側は localScale.x だけをSYNCで送っている。Vector3.one * scale で戻すとZ軸まで
        // 上書きしてしまい、柄(Handle-A)のワールドZがカメラのニアクリップ面まで来て描画されなくなる。
        // Z軸は ConfigureMultiplayer で記録した baselineScale のまま維持し、X/Yだけ同期する
        if (scale > 0f) transform.localScale = new Vector3(scale, scale, baselineScale.z);
    }

    // 技の種別に対応する刀身の色。制圧の減光でも「本来の色」を引くのに使う。
    // ▼【新規追加】巨大回転斬の「届く範囲の円」と、その後の「ふらつき」の点滅（どちらも仮の見た目）。
    // 技番号と大きさはゲストにも同期されているので、ホストもゲストもそれぞれの画面でこれを見て描く
    void LateUpdate()
    {
        UpdateGiantSpinLook();
        UpdateCloneRushChargeLook();
        UpdateShieldAura();
        UpdateSpinDashLook();
    }

    // ▼【新規追加】オレ達シールドの、守っている間のふちどりと、殴られた時の盾の光（SkillEffects.LeafShield）。
    // ゲストには分身がないので、守っているかは同期された分身の見た目があるかで決める
    ILeafShieldAura shieldAura;

    bool ShieldShowing => IsAlive && (HasActiveLeafShield ||
        (MultiplayerOwner != null && !MultiplayerOwner.IsHost && MultiplayerOwner.HasShieldVisuals(PlayerId)));

    void UpdateShieldAura()
    {
        bool show = ShieldShowing && !SwordController.isKomaMode;
        if (shieldAura == null && show) shieldAura = SkillEffects.LeafShield.CreateAura(this);
        if (shieldAura != null) shieldAura.Tick(show, Time.deltaTime);
    }

    // 守っている間に本体が殴られた時：一番近い敵の方に盾の光を出す
    void PlayShieldGuard()
    {
        if (!ShieldShowing || SwordController.isKomaMode) return;
        Vector2 from = Vector2.right;
        float best = float.MaxValue;
        foreach (var enemy in NearbyEnemies())
        {
            if (enemy == null || enemy == this) continue;
            Vector2 to = (Vector2)enemy.transform.position - (Vector2)transform.position;
            if (to.sqrMagnitude < best) { best = to.sqrMagnitude; from = to; }
        }
        if (shieldAura == null) shieldAura = SkillEffects.LeafShield.CreateAura(this);
        shieldAura.PlayGuard(from);
    }

    PolygonCollider2D[] bladeShapes;

    // ▼【変更】巨大回転斬の演出。以前は仮の見た目（赤い円・ふらつきの点滅・ホストだけの柄の金色）だった。
    // 段階（giantSpinPhase、ゲストへ同期）の変わり目を見て、見た目（SkillEffects.GiantSpin）を切り替える。
    // ふらつきは技番号（StaggerDashType）から決める
    IGiantSpinLook giantSpinLook;
    GiantSpinStage giantSpinLookStage;

    void UpdateGiantSpinLook()
    {
        GiantSpinStage stage = !IsAlive || SwordController.isKomaMode ? GiantSpinStage.None
            : giantSpinPhase != 0 ? (GiantSpinStage)giantSpinPhase
            : IsStaggered ? GiantSpinStage.Stagger : GiantSpinStage.None;
        if (stage != giantSpinLookStage)
        {
            // 技を始めた時の刀身の形で届く範囲を測る（持ち替えで刀身が変わることがあるので毎回取り直す）
            if (stage == GiantSpinStage.HiltGlow || bladeShapes == null) bladeShapes = GetComponentsInChildren<PolygonCollider2D>(true);
            if (giantSpinLook == null && stage != GiantSpinStage.None) giantSpinLook = SkillEffects.GiantSpin.Create(this);
            // 柄が光る：柄を金色にする（演出の部品は光のにじみと粒を足す）
            SetHiltGlow(stage == GiantSpinStage.HiltGlow);
            if (giantSpinLook != null) giantSpinLook.SetStage(stage, GiantStageDuration(stage));
            giantSpinLookStage = stage;
        }
        if (giantSpinLook != null && stage != GiantSpinStage.None) giantSpinLook.Tick(Time.deltaTime);
    }

    float GiantStageDuration(GiantSpinStage stage)
    {
        switch (stage)
        {
            case GiantSpinStage.HiltGlow: return giantSpinHiltGlowTime;
            case GiantSpinStage.Grow: return giantSpinGrowTime;
            case GiantSpinStage.Windup: return giantSpinWindupTime;
            case GiantSpinStage.Spin: return 360f / Mathf.Max(1f, Mathf.Abs(giantSpinRotationSpeed));
            case GiantSpinStage.Linger: return giantSpinLingerTime;
            case GiantSpinStage.Shrink: return giantSpinShrinkTime;
            case GiantSpinStage.Stagger: return giantSpinStaggerOnMiss;
            default: return 0f;
        }
    }

    // 今の大きさでの、剣の中心から一番遠い刀身の点までの距離（巨大回転斬の届く範囲。演出が使う）
    public float GiantSpinReachNow
    {
        get
        {
            if (bladeShapes == null) bladeShapes = GetComponentsInChildren<PolygonCollider2D>(true);
            return CurrentReach();
        }
    }

    // 演出が「当たったか」を見るための相手（ホストもゲストも同じ顔ぶれ）
    public IEnumerable<SwordBattle> EffectOpponents() => NearbyEnemies();

    // 剣の中心から一番遠い刀身の点までの距離（回転しても変わらない、今の大きさでの届く範囲）
    float CurrentReach()
    {
        Vector2 centre = transform.position;
        float reach = 0f;
        if (bladeShapes != null)
            foreach (var shape in bladeShapes)
            {
                if (shape == null) continue;
                for (int p = 0; p < shape.pathCount; p++)
                    foreach (var point in shape.GetPath(p))
                        reach = Mathf.Max(reach, ((Vector2)shape.transform.TransformPoint(point + shape.offset) - centre).magnitude);
            }
        if (reach <= 0f && spriteRenderer != null) reach = spriteRenderer.bounds.extents.magnitude;
        return reach;
    }

    void OnDestroy()
    {
        if (spinDashLook != null) { spinDashLook.Dispose(); spinDashLook = null; }
        if (giantSpinLook != null) { giantSpinLook.Dispose(); giantSpinLook = null; }
        if (cloneRushChargeLook != null) { cloneRushChargeLook.Dispose(); cloneRushChargeLook = null; }
        if (shieldAura != null) { shieldAura.Dispose(); shieldAura = null; }
    }

    public static Color ColorForDashType(int dashType)
    {
        return dashType == 1 ? new Color(1, .5f, .5f) :
            dashType == 2 ? new Color(1, .8f, .2f) : dashType == 3 ? new Color(.5f, 1, 1) :
            dashType == 4 ? new Color(1f, .3f, .3f) : dashType == 5 ? new Color(.3f, .6f, 1f) :
            dashType == 6 ? new Color(.4f, .95f, .5f) :
            dashType == SuppressDashType ? new Color(.78f, .45f, 1f) :
            dashType == StaggerDashType ? new Color(.7f, .66f, .66f) : Color.white;
    }

    // ▼【新規追加】1vs3：制圧のカットイン。技番号を一瞬だけ立てることで、同期を受け取った側の
    // ApplyMultiplayerVisuals からも同じ経路でカットインが出る（専用の同期項目を足さずに済む）。
    // isDashing は触らないので、制圧を撃ったボスが無敵になったり突進扱いになったりはしない。
    public void PlaySuppressCutin()
    {
        if (!IsAlive) return;
        StartCoroutine(SuppressCutinRoutine());
    }

    IEnumerator SuppressCutinRoutine()
    {
        currentDashType = SuppressDashType;
        if (spriteRenderer != null)
        {
            spriteRenderer.color = ColorForDashType(SuppressDashType);
            if (CutinManager.Instance != null)
                CutinManager.Instance.PlayCutin(spriteRenderer.sprite, swordName, SuppressSkillName,
                    ColorForDashType(SuppressDashType), PlayerMainColor, MultiplayerOwner == null);
        }
        yield return new WaitForSecondsRealtime(0.8f);
        // 待っている間に別の技が始まっていたら、その技の番号と色を尊重して何もしない
        if (currentDashType != SuppressDashType) yield break;
        currentDashType = 0;
        if (spriteRenderer != null && IsAlive && !isDashing) spriteRenderer.color = Color.white;
    }

    // ▼【新規追加】1vs3：制圧されている間は刀身を暗く落として、操作できない相手を見分けやすくする。
    // 色は技ごとの色分けや撃破時のグレーなど16箇所から書き込まれるので、それらには手を触れず
    // 「制圧されている間だけ毎フレーム塗り直す」方式にしている。本来の色は保存せず currentDashType
    // から引き直すので、制圧中に発動済みの技が終わって色が変わっても破綻しない。
    void UpdateSuppressTint()
    {
        if (spriteRenderer == null) return;
        bool suppressed = MultiplayerOwner != null && IsAlive && MultiplayerOwner.IsSuppressed(PlayerId);
        if (suppressed)
        {
            Color baseColor = ColorForDashType(currentDashType);
            spriteRenderer.color = new Color(baseColor.r * SuppressDim, baseColor.g * SuppressDim,
                baseColor.b * SuppressDim, baseColor.a);
            suppressTinted = true;
            return;
        }
        if (!suppressTinted) return;
        suppressTinted = false;
        // 撃破済みならグレーのまま。ここで戻すと ApplyMultiplayerHealth が塗った色を消してしまう。
        if (IsAlive) spriteRenderer.color = ColorForDashType(currentDashType);
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
            // ▼【新規追加】1vs3：制圧。Host側は SuppressCutinRoutine で直接鳴らし、
            // 他プレイヤーの画面には dashType の同期を通じてここから出る
            case SuppressDashType: skillName = SuppressSkillName; themeColor = ColorForDashType(SuppressDashType); break;
            default: return;
        }
        CutinManager.Instance.PlayCutin(spriteRenderer.sprite, swordName, skillName, themeColor, PlayerMainColor, false);
    }

    void FixedUpdate()
    {
        // 巨大回転斬：前の物理ステップで触れた相手に、近い順で当てる
        FlushGiantSpinHits();
        // 大回転斬：当たった時に弾かれる向きを決めるため、突進中の速さと回転を覚えておく
        // （当たった衝突の中では、速さがもう衝突で変わっているため）
        if (spinDashPhase == (int)SpinDashStage.Dash && rb != null && rb.bodyType == RigidbodyType2D.Dynamic)
        {
            // ▼【新規追加】突進中は、撃ち出した（跳ね返った）時の速さと回転を保つ。回っている刀身が天井や壁をかすると、
            // まさつで車輪のように天井をつかんで蹴り出され、進む向きと速さが変わっていた。
            // 向きが変わるのは、壁での跳ね返り・シールドでの跳ね返りの時だけにする。撃破で止められている間は触らない
            if (isDashShooting && !SkillClockStopped) ReaimAfterSkimmingOffAnEdge();
            if (isDashShooting && !SkillClockStopped && spinDashVelocity.sqrMagnitude > .01f)
            {
                rb.linearVelocity = spinDashVelocity;
                rb.angularVelocity = spinDashAngular;
            }
            spinTravelVelocity = rb.linearVelocity;
            spinTravelAngular = rb.angularVelocity;
        }
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

        // ▼【調査用ログ】必殺技ボタンがでっぱなし/使えなくなる不具合の調査用。
        // このreturnを抜けると、この剣のUpdateSpecialAttackButton()等が二度と呼ばれなくなる
        // (=共有ボタンが今の状態のまま固まる)ため、自分の操作キャラでこれが起きた瞬間を記録しておく
        if ((!isRoundStarted || isDead || matchEnded) && controller != null && controller.isLocalControlled && !loggedUpdateStall)
        {
            loggedUpdateStall = true;
            Debug.Log($"⏸️ [調査用] Update()が早期returnするようになりました(以後この剣のUpdateSpecialAttackButton等は呼ばれません): " +
                $"sword={gameObject.name}, isRoundStarted={isRoundStarted}, isDead={isDead}, matchEnded={matchEnded}, " +
                $"currentSp={currentSp}, buttonActive={(specialAttackButton != null ? specialAttackButton.gameObject.activeSelf.ToString() : "no-button")}");
        }
        if (!isRoundStarted || isDead || matchEnded) return;
        UpdateLeafShieldRadius();

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
        UpdateSuppressTint();

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
            // ▼ 1vs3ではSPが満タンになると、このキーで出るものが掌握へ切り替わる（TryUltimate内で振り分ける）
            if (Input.GetKeyDown(KeyCode.Space)) TryUltimate();
        }
    }

    // ▼【新規追加】必殺技専用ボタンの表示切り替え：自キャラのSPが必殺技分たまっている時だけ表示する
    // オンラインN人戦では全員のSwordBattleが同じ1つのボタンを参照しているため、
    // 自分が操作しているキャラ以外は絶対にこのボタンへ触れないようにする（他人の生死やSPで消えてしまうのを防ぐ）
    void UpdateSpecialAttackButton()
    {
        // ▼【調査用ログ】自分の操作キャラのはずなのに、途中からisLocalControlled/controller/ボタン参照が
        // 欠けてここでスキップされるようになった場合を捕捉する(一度だけログを出す)
        if ((specialAttackButton == null || controller == null || !controller.isLocalControlled) &&
            wasLocalControlledForButton && !loggedButtonGuardStall)
        {
            loggedButtonGuardStall = true;
            Debug.Log($"⏸️ [調査用] UpdateSpecialAttackButton()が以後スキップされるようになりました: " +
                $"sword={gameObject.name}, specialAttackButtonNull={specialAttackButton == null}, " +
                $"controllerNull={controller == null}, isLocalControlled={(controller != null ? controller.isLocalControlled.ToString() : "n/a")}");
        }
        if (specialAttackButton == null || controller == null || !controller.isLocalControlled) return;
        wasLocalControlledForButton = true;
        // 制圧を受けている間はどの入力も通らないので、押せるように見せない
        bool suppressed = MultiplayerOwner != null && MultiplayerOwner.IsSuppressed(PlayerId);
        // 倒された剣は撃てない（残機制で次の剣を待っている間も、ボタンを撃てる見た目のまま残さない）
        bool shouldShow = IsAlive && !isDashing && !suppressed && currentSp >= UltimateThreshold;
        // ▼【新規追加】アイコン付きのボタン(SkillIconView)は、溜めている途中も出したままにして丸ゲージで
        // 溜まり具合を見せる。押せるのは撃てる時だけ。アイコンが無いボタンは従来どおり撃てる時だけ出す
        var iconView = SkillIconViewOfButton();
        bool visible = iconView != null || shouldShow;
        if (specialAttackButton.gameObject.activeSelf != visible)
        {
            // ▼【調査用ログ】ボタンの表示/非表示が切り替わるたびに、判断材料を全部出しておく。
            // 「でっぱなし」で困っている時は、最後にこのログが出た時の値と、それ以降ログが
            // 一切出ていない(=このメソッド自体が呼ばれなくなった)かどうかを見れば原因を絞り込める
            Debug.Log($"🔘 [調査用] 必殺技ボタンの表示切り替え: sword={gameObject.name}, " +
                $"{specialAttackButton.gameObject.activeSelf}→{visible}, currentSp={currentSp}, " +
                $"UltimateThreshold={UltimateThreshold}, isDashing={isDashing}, suppressed={suppressed}, " +
                $"isKomaMode={SwordController.isKomaMode}, isRoundStarted={isRoundStarted}");
            specialAttackButton.gameObject.SetActive(visible);
        }
        if (iconView != null)
        {
            if (specialAttackButton.interactable != shouldShow) specialAttackButton.interactable = shouldShow;
            // 1vs3のボスだけ、通常必殺技のラインを越えた分を掌握斬までの2周目として重ねて見せる。
            // 満タンで掌握に切り替わったらアイコン自体が掌握斬になるので、2周目は消す
            bool overcharge = MultiplayerOwner != null && MultiplayerOwner.SoloModeActive &&
                maxSp > ultimateSp && !SuppressReady;
            float overFill = overcharge ? Mathf.Clamp01((currentSp - ultimateSp) / (maxSp - ultimateSp)) : 0f;
            iconView.Show(SkillIconKey, Mathf.Clamp01(currentSp / UltimateThreshold), shouldShow,
                overcharge ? "grasp" : null, overFill);
        }
        // ▼【新規追加】1vs3：ボタンは1つのまま、SPが満タンになったら掌握へ切り替わる。
        // どちらが出るのか分かるよう、表示も切り替える
        ApplySuppressButtonSkin(SuppressReady);
    }

    // ▼【新規追加】必殺技アイコンの種類(Assets/UI/SkillIcons のファイル名の頭)。
    // 1vs3のボスがSP満タンで掌握に切り替わっている間は掌握斬、独楽モードは竜巻、それ以外は柄の種類で決まる
    public string SkillIconKey
    {
        get
        {
            if (SuppressReady) return "grasp";
            if (SwordController.isKomaMode) return "tornado";
            switch (hiltType)
            {
                case "1": return "giant";
                case "2": return "attack";
                case "3": return "shield";
                default: return "spin";
            }
        }
    }

    // 共有ボタンはMultiplayerManagerが試合ごとに差し替えるので、ボタンが変わった時だけ探し直す
    Button iconViewButton;
    SkillIconView iconViewCache;
    SkillIconView SkillIconViewOfButton()
    {
        if (iconViewButton != specialAttackButton)
        {
            iconViewButton = specialAttackButton;
            iconViewCache = specialAttackButton != null ? specialAttackButton.GetComponent<SkillIconView>() : null;
        }
        return iconViewCache;
    }

    // 掌握に切り替わっている間だけ、共有の必殺技ボタンの文字と色を差し替える。
    // 元の値は最初に覚えておき、戻す時と試合終了時(OnDisable)に必ず復帰させる。
    void ApplySuppressButtonSkin(bool suppressMode)
    {
        if (specialAttackButton == null || suppressMode == suppressSkinApplied) return;
        if (!suppressSkinCaptured)
        {
            ultimateButtonLabel = specialAttackButton.GetComponentInChildren<TextMeshProUGUI>(true);
            ultimateButtonImage = specialAttackButton.GetComponent<Image>();
            if (ultimateButtonLabel != null) ultimateButtonLabelText = ultimateButtonLabel.text;
            if (ultimateButtonImage != null) ultimateButtonColor = ultimateButtonImage.color;
            suppressSkinCaptured = true;
        }
        suppressSkinApplied = suppressMode;
        if (ultimateButtonLabel != null)
            ultimateButtonLabel.text = suppressMode ? "掌握斬" : ultimateButtonLabelText;
        if (ultimateButtonImage != null)
            ultimateButtonImage.color = suppressMode ? new Color(.42f, .11f, .60f, ultimateButtonColor.a) : ultimateButtonColor;
    }

    // 試合が終わる・撃破される・剣が消される時に、共有ボタンの見た目を元へ戻す。
    // このボタンはシーンに1つしか無く、次の試合へ持ち越されるため。
    void OnDisable()
    {
        ApplySuppressButtonSkin(false);
        // 突進の途中で剣が片付けられても、味方との当たり判定を切ったままにしない
        RestoreTeammateCollisions();
        SetHiltGlow(false);
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

        // ▼【新規追加】大回転斬の終わり際（減速・弾かれている間）：壁や床では進む向きだけ跳ね返し、
        // 敵の剣とぶつかったらそこで打ち切って、普通の当たり方（弾かれる力など）に任せる。
        // 当たって弾かれている最中の、その当たった相手の別の部分（刀身と柄）との衝突では打ち切らない
        if (SpinSettleLocked && !SwordController.isKomaMode)
        {
            if (target == null)
            {
                if (collision.contactCount > 0) spinSettleVelocity = Vector2.Reflect(spinSettleVelocity, collision.GetContact(0).normal);
            }
            else if (target != this && target != spinHitTarget) EndSpinSettle();
        }

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

                    // ▼【修正】回っている刀身が、進んでいる向きの後ろ側の壁をかすっただけの時（天井ぎわから真下へ撃った時の天井など）は、
                    // 跳ね返さずにそのまま進む。以前はこれも跳ね返していたため、真下へ進んでいたのに天井の方へ折り返して、突進が乱れていた。
                    // 法線は壁からこの剣の側を向くので、ぶつかる前の進む向き（FixedUpdateで覚えた速さ）と同じ側なら、壁から離れている
                    if (spinDashVelocity.sqrMagnitude > 1f && Vector2.Dot(spinDashVelocity, normal) > 0f)
                    {
                        rb.linearVelocity = spinDashVelocity;
                        rb.angularVelocity = spinDashAngular;
                        return;
                    }

                    // ▼【新規追加】浅い角度で触れた時（地上どうしで撃って床をこすった時など）は、跳ね返さずに面に沿って滑る。
                    // 速さと回転はそのまま。火花・音・揺れも出さない
                    if (spinDashVelocity.sqrMagnitude > 1f)
                    {
                        Vector2 heading = spinDashVelocity.normalized;
                        float into = -Vector2.Dot(heading, normal);
                        if (into < Mathf.Sin(spinGrazeAngle * Mathf.Deg2Rad))
                        {
                            Vector2 along = heading + into * normal;
                            if (along.sqrMagnitude > .0001f)
                            {
                                // 滑っている面を覚える。その面の端を越えたら、狙っていた相手へ向かい直す（FixedUpdate）
                                spinSkimSurface = collision.collider;
                                spinSkimNormal = normal;
                                spinSkimHeight = Mathf.Max(0f, Vector2.Dot(rb.worldCenterOfMass - collision.contacts[0].point, normal));
                                spinDashVelocity = along.normalized * spinDashVelocity.magnitude;
                                rb.linearVelocity = spinDashVelocity;
                                rb.angularVelocity = spinDashAngular;
                                return;
                            }
                        }
                    }
                    Vector2 bounceDir = Vector2.Reflect(inDirection, normal);
                    
                    // スピードを維持して反射（最低40fは担保して超高速をキープ）
                    float currentSpeed = Mathf.Max(rb.linearVelocity.magnitude, 40f);
                    rb.linearVelocity = bounceDir * currentSpeed; 
                    
                    // 回転も進行方向に合わせる
                    rb.angularVelocity = Mathf.Sign(bounceDir.x) * -2500f;
                    spinSkimSurface = null;
                    spinDashVelocity = rb.linearVelocity;
                    spinDashAngular = rb.angularVelocity;

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
                // 相手の剣に当たった時はダッシュ終了（反動で少し弾かれる）
                spinHitTarget = target;
                EndSwordDash(true);
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
                    rb.AddForce(clashBounce * (bounceForce * rb.mass * 1.5f * KnockbackScale), ForceMode2D.Impulse);
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
                    rb.AddForce(bounceDir.normalized * (bounceForce * rb.mass * KnockbackScale), ForceMode2D.Impulse);
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

                // 相手の柄（弱点）判定。オレ達シールドが出ている相手は、柄が弱点にならない
                    if (targetCollider.CompareTag("Handle") && !target.HasActiveLeafShield)
                    {
                        damage *= 2;
                        isWeakPoint = true;
                    }
                }
                    // ▼変更：TakeDamageの結果（倒したかどうか）を受け取る
                    bool killedTarget = false;
                    if (MultiplayerOwner != null) MultiplayerOwner.QueueHit(this, target, damage, hitPoint, isCrit || isWeakPoint);
                    else killedTarget = target.TakeDamage(damage, hitPoint, isCrit, isWeakPoint, this, true);

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
    // attacker：攻撃した剣（分かる時だけ）。byBody：剣の本体で当てたか。オレ達シールドの返しに使う
    public bool TakeDamage(int damage, Vector2 hitPos, bool isCrit, bool isWeakPoint, SwordBattle attacker = null, bool byBody = true)
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
        if (IsRespawnInvincible)
        {
            Debug.Log("✨ リスポーン直後につき無敵！攻撃を弾いた！");
            return false;
        }

        // ▼【変更】オレ達シールドが出ている間は、受けるダメージを減らす（以前は完全無敵）
        bool shielded = HasActiveLeafShield;
        damage = ShieldedDamage(damage);
        if (shielded) PlayShieldGuard();

        float damagePercentage = ((float)damage / maxHp) * 100f; 
        currentSp = Mathf.Clamp(currentSp + (damagePercentage * damageSpMultiplier), 0f, maxSp);

        hp -= damage;
        if (hp < 0) hp = 0;
        UpdateUI();
        // 本体が被弾したら、分身の耐久を1減らし、本体で殴ってきた相手には返す
        if (shielded && hp > 0) OnShieldedBodyHit(attacker, hitPos, byBody);

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
        // ▼【修正】必殺技のカットイン中に被弾すると、ここで等速に戻してカットインのスローを途中で解いていた。
        // カットインがスローをかけている間は、終わりの等速化はカットイン側に任せる
        if (!matchEnded && !CutinSlowingTime)
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
        // ▼【修正】オンライン対戦では、火花とダメージ数字はMultiplayerManagerのダメージ記録から
        // 当たった場所に出す（PlayHitVisual）。ここでは音だけ鳴らす。旧NetworkManager経由の時だけ従来どおり出す
        bool legacyVisuals = MultiplayerOwner == null;
        if (isCrit || damage >= 100)
        {
            if (critHitSound != null) audioSource.PlayOneShot(critHitSound);
            if (legacyVisuals && critEffectPrefab != null) Instantiate(critEffectPrefab, transform.position, Quaternion.identity);
        }
        else
        {
            if (normalHitSound != null) audioSource.PlayOneShot(normalHitSound);
            if (legacyVisuals && normalEffectPrehub != null) Instantiate(normalEffectPrehub, transform.position, Quaternion.identity);
        }

        // ▼ 通信相手の画面にもダメージ数値を出す（旧NetworkManager経由の時だけ）
        if (legacyVisuals && damagePopupPrefab != null && damage > 0)
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
        // 巨大回転斬の後のふらつき中は操作を受け付けない
        if (isDead || matchEnded || InputBlockedBySkill || IsStaggered) return;

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
            Jump(clickedRight);
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
        // ▼【修正】1vs3：ボタンは1つのまま、SP量で撃つものが変わる。満タン(=maxSp)なら掌握、
        // そこに届いていなければ通常必殺技。実際に撃てるかどうかはHost側が改めて判定する
        if (MultiplayerOwner != null)
        {
            if (SuppressReady) MultiplayerOwner.SubmitLocalSuppress();
            else MultiplayerOwner.SubmitLocalUltimate();
            return;
        }
        if (isDead || matchEnded || isDashing || IsStaggered || !isRoundStarted) return;
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
        // 巨大回転斬の後のふらつき中は操作を受け付けない
        if (isDead || matchEnded || InputBlockedBySkill || IsStaggered) return;

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
            if (controller != null) Jump(true);
        }
        else if (actionName == "JumpLeft")
        {
            if (controller != null) Jump(false);
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

    // ▼【新規追加】必殺技のカットインを出し、抜け切るまで待つ。技の中身はこの後に始める
    // （以前はカットインと同時に始まり、0.05倍のスローの中でカットインの裏で少しずつ進んでいた）。
    // マルチプレイでもカットイン中は全員の動きを止める演出にする(useSlowMotion常にtrue)。
    // Host権威のPhysics2D.Simulateが実質停止するため、この間だけは意図的に全員が止まる
    // (Client側は停止したSYNCデータをそのまま補間するので、こちらでも自然に止まって見える)。
    // 待つのは実時間。別の剣のカットインに置き換えられたら、その時点で待つのをやめる
    IEnumerator PlayUltimateCutinAndWait(string skillName, Color themeColor)
    {
        if (CutinManager.Instance == null || spriteRenderer == null) yield break;
        int serial = CutinManager.Instance.PlayCutin(spriteRenderer.sprite, swordName, skillName, themeColor, PlayerMainColor, true);
        // カットインが何かの理由で終わらなかった時に、技が始まらないまま固まらないための上限
        float giveUpAt = Time.unscaledTime + 3f;
        while (CutinManager.Instance != null && !CutinManager.Instance.IsFinished(serial) && Time.unscaledTime < giveUpAt)
            yield return null;
    }

    // ▼【新規追加】本体で攻撃しない必殺技（オレ達アタック・オレ達シールド）は、カットインが抜けた後なら
    // 無敵(isDashing)の間でも移動（ジャンプ）できる。大回転斬・巨大回転斬は剣そのものが動いて攻撃するので止めたまま
    bool skillAllowsMovement;
    // 大回転斬の終わり際（減速・弾かれている間）も受け付けない
    bool InputBlockedBySkill => (isDashing && !skillAllowsMovement) || SpinSettleLocked;
    bool SpinSettleLocked => spinDashPhase == (int)SpinDashStage.SlowMiss || spinDashPhase == (int)SpinDashStage.SlowHit;

    // ジャンプ。大回転斬の後のゆっくり降りる途中なら、そこで打ち切って普通の動きに戻してから跳ぶ
    void Jump(bool right)
    {
        if (spinDashPhase == (int)SpinDashStage.Descend) EndSpinSettle();
        controller.NetworkJump(right);
    }

    // カットインを待っている間に倒された・試合が終わった時は、技を始めずに状態だけ戻す
    bool UltimateCancelled => isDead || matchEnded;
    void CancelUltimate()
    {
        skillAllowsMovement = false;
        isDashing = false;
        isDashShooting = false;
        currentDashType = 0;
        giantSpinPhase = 0;
        if (spriteRenderer != null) spriteRenderer.color = Color.white;
    }

    // ▼【新規追加】必殺技の待ち時間。WaitForSecondsと同じくゲーム内の時間で数えるが、オンライン対戦で
    // 撃破して短く止められている間（MultiplayerManager.FreezeKiller）は進めない。
    // 以前は止まっている間も時間が進み、その分だけ技が削れていた（巨大回転斬はほぼ一回転分が消えていた）
    bool SkillClockStopped => MultiplayerOwner != null && MultiplayerOwner.IsKillerFrozen(PlayerId);

    IEnumerator WaitSkillTime(float seconds)
    {
        float elapsed = 0f;
        while (elapsed < seconds)
        {
            yield return null;
            if (!SkillClockStopped) elapsed += Time.deltaTime;
        }
    }

    // カットインが時間をゆっくりにしている最中か。ヒットストップなどの後で等速に戻す時は、
    // これがtrueなら戻さない（戻すとカットインのスローが途中で解け、技がカットインの裏で進んでしまう）
    static bool CutinSlowingTime => CutinManager.Instance != null && CutinManager.Instance.IsSlowingTime;

    // ▼【新規追加】剣ごとのhiltType（JSON由来）に応じて必殺技の中身を振り分ける
    // 未対応のhiltType（未指定＝"0"を含む）は既存のデフォルト必殺技にフォールバック
    IEnumerator UltimateRoutine()
    {
        // 前の技の「移動できる」を持ち越さない
        skillAllowsMovement = false;
        if (spinDashPhase != 0) EndSpinSettle();
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

        if (spriteRenderer != null) spriteRenderer.color = new Color(1f, 0.8f, 0.2f);
        yield return StartCoroutine(PlayUltimateCutinAndWait("竜巻猛突!!", new Color(1f, 0.8f, 0.2f)));
        if (UltimateCancelled) { CancelUltimate(); yield break; }

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

            // 撃破で止められている間は、竜巻の残り時間を減らさない
            if (!SkillClockStopped) timer += Time.deltaTime;
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
        pendingGiantSpinHits.Clear();
        giantSpinHitActive = false;
        Color giantColor = ColorForDashType(4);

        if (spriteRenderer != null) spriteRenderer.color = giantColor;
        // カットインが抜けてから巨大化する（以前はカットインの裏で先に6倍になっていた）
        yield return StartCoroutine(PlayUltimateCutinAndWait("巨大回転斬!!", giantColor));
        if (UltimateCancelled) { CancelUltimate(); yield break; }

        bool controlsPhysics = rb != null && rb.bodyType == RigidbodyType2D.Dynamic;
        float originalGravityScale = 0f;
        if (controlsPhysics)
        {
            // 技の間は床に落ちていかないよう、重力も一時的に切ってその場に留める
            originalGravityScale = rb.gravityScale;
            rb.gravityScale = 0f;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }

        // 1. 柄が光る（技の種類を決めているのは柄なので、技の出どころを柄で見せる）。見た目は UpdateGiantSpinLook
        giantSpinPhase = (int)GiantSpinStage.HiltGlow;
        yield return StartCoroutine(WaitSkillTime(giantSpinHiltGlowTime));

        // 壁にも相手の剣にも物理的に引っかからないよう、大きくなる前から当たり判定をトリガー化する。
        // ダメージはOnTriggerEnter2D/Stay2D側で、一回転の間だけ判定する
        Collider2D[] myColliders = GetComponentsInChildren<Collider2D>();
        bool[] originalTriggerStates = new bool[myColliders.Length];
        for (int i = 0; i < myColliders.Length; i++)
        {
            originalTriggerStates[i] = myColliders[i].isTrigger;
            myColliders[i].isTrigger = true;
        }

        // 2. 3段階で巨大化する（各段の最後で少し行き過ぎてから戻る）。届く範囲の円などは UpdateGiantSpinLook が出す
        giantSpinPhase = (int)GiantSpinStage.Grow;
        Vector3 originalScale = transform.localScale;
        float stepTime = giantSpinGrowTime / 3f;
        for (int step = 1; step <= 3; step++)
        {
            Vector3 from = transform.localScale;
            // 奥行き(z)は拡大しない（ScaledFlat参照）
            Vector3 to = ScaledFlat(originalScale, Mathf.Lerp(1f, giantSpinScale, step / 3f));
            float elapsed = 0f;
            while (elapsed < stepTime)
            {
                yield return null;
                if (!SkillClockStopped) elapsed += Time.deltaTime;
                transform.localScale = Vector3.LerpUnclamped(from, to, EaseOutBack(Mathf.Clamp01(elapsed / stepTime)));
            }
            transform.localScale = to;
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

        // 3. 振りかぶり：回る向きと逆に少し引く
        giantSpinPhase = (int)GiantSpinStage.Windup;
        if (controlsPhysics && giantSpinWindupTime > 0f) rb.angularVelocity = -spinDir * giantSpinWindupAngle / giantSpinWindupTime;
        yield return StartCoroutine(WaitSkillTime(giantSpinWindupTime));

        // 4. 一回転。攻撃判定があるのはここだけ
        float angularSpeed = giantSpinRotationSpeed * spinDir; // 度/秒
        float duration = 360f / Mathf.Abs(angularSpeed); // ちょうど一回転分の時間
        if (controlsPhysics) rb.angularVelocity = angularSpeed;
        giantSpinPhase = (int)GiantSpinStage.Spin;
        giantSpinHitActive = true;
        // 回転中に撃破して止められても、止まっている間は数えないので一回転しきる
        yield return StartCoroutine(WaitSkillTime(duration));
        giantSpinHitActive = false;
        if (controlsPhysics) rb.angularVelocity = 0f;

        // 5. 余韻のあと縮む
        giantSpinPhase = (int)GiantSpinStage.Linger;
        yield return StartCoroutine(WaitSkillTime(giantSpinLingerTime));
        giantSpinPhase = (int)GiantSpinStage.Shrink;
        Vector3 bigScale = transform.localScale;
        float shrinkElapsed = 0f;
        while (shrinkElapsed < giantSpinShrinkTime)
        {
            yield return null;
            if (!SkillClockStopped) shrinkElapsed += Time.deltaTime;
            float k = Mathf.Clamp01(shrinkElapsed / giantSpinShrinkTime);
            transform.localScale = Vector3.Lerp(bigScale, originalScale, k * k * (3f - 2f * k));
        }
        transform.localScale = originalScale;

        for (int i = 0; i < myColliders.Length; i++)
        {
            if (myColliders[i] != null) myColliders[i].isTrigger = originalTriggerStates[i];
        }
        if (controlsPhysics) rb.gravityScale = originalGravityScale;

        // 6. ふらつき：操作を受け付けない（無敵ではない）。外した時は長い。見た目は技番号から UpdateGiantSpinLook が出す
        giantSpinPhase = 0;
        bool hitSomeone = giantSpinHitTargets.Count > 0;
        isDashing = false;
        currentDashType = StaggerDashType;
        if (spriteRenderer != null) spriteRenderer.color = ColorForDashType(StaggerDashType);
        yield return new WaitForSeconds(hitSomeone ? giantSpinStaggerOnHit : giantSpinStaggerOnMiss);
        if (currentDashType == StaggerDashType) currentDashType = 0;
        if (spriteRenderer != null && IsAlive) spriteRenderer.color = Color.white;
    }

    // ▼【修正】剣を大きくする時は、x・yだけを拡大して奥行き(z)は元のままにする。
    // 柄(Handle-A)は刀身より手前に描くため z=-5 に置かれており、zまで6倍にすると z=-30 になって
    // z=-10 にあるカメラの後ろへ回り込み、映らなくなっていた（巨大化すると柄が消える不具合の原因）
    static Vector3 ScaledFlat(Vector3 scale, float multiplier)
    {
        return new Vector3(scale.x * multiplier, scale.y * multiplier, scale.z);
    }

    // 少し行き過ぎてから止まる動き（巨大化の各段で使う）
    static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }

    // ▼【新規追加】巨大回転斬の「柄が光る」。柄（Handleタグ）の見た目だけを金色にする。
    // 段階（giantSpinPhase）を見て UpdateGiantSpinLook が呼ぶので、ゲストの画面でも光る
    readonly List<(SpriteRenderer renderer, Color color)> glowingHilts = new List<(SpriteRenderer renderer, Color color)>();

    void SetHiltGlow(bool on)
    {
        foreach (var hilt in glowingHilts) if (hilt.renderer != null) hilt.renderer.color = hilt.color;
        glowingHilts.Clear();
        if (!on) return;
        foreach (var r in GetComponentsInChildren<SpriteRenderer>())
        {
            if (r == spriteRenderer || !r.CompareTag("Handle")) continue;
            glowingHilts.Add((r, r.color));
            r.color = new Color(1f, .85f, .3f, r.color.a);
        }
    }

    // ▼【新規追加】剣モード必殺技（hiltType:"2"）：分身を3体出現させ、相手へホーミング突進させる
    IEnumerator CloneRushRoutine()
    {
        isDashing = true;
        currentDashType = 5;
        currentSp = 0f;

        Color cloneColor = new Color(0.3f, 0.6f, 1f);

        if (spriteRenderer != null) spriteRenderer.color = cloneColor;
        // カットインが抜けてから分身を出す（以前は1体目がカットインの裏で出て、スローの中を進んでいた）
        yield return StartCoroutine(PlayUltimateCutinAndWait("オレ達アタック!!", cloneColor));
        if (UltimateCancelled) { CancelUltimate(); yield break; }
        skillAllowsMovement = true;

        // ▼【重要】実際のダメージ計算はDynamic（＝物理演算の権威を持つHost側）でのみ行う。
        // Client側の見た目再生（Kinematicコピー）では分身は出すが、ダメージは与えない。
        bool isAuthoritative = rb != null && rb.bodyType == RigidbodyType2D.Dynamic;
        int cloneDamage = Mathf.Max(Mathf.RoundToInt(attack * cloneDamageMultiplier), 1);
        List<Collider2D> spawnedColliders = new List<Collider2D>();

        // 狙える相手がいない時に撃つ向き：技を出した時の相手の方
        Vector2 toEnemy = CloneAimPosition() - (Vector2)transform.position;
        cloneForward = toEnemy.sqrMagnitude > .0001f ? toEnemy.normalized : Vector2.right;

        // ▼【変更】分身の予備動作（持ち場へ飛び出す・並ぶ・構える・引く）をやめた。本体が溜めてから、
        // 本体の位置から1体ずつそのまま撃ち出す。以前は3体が同時に動いてごちゃつき、頭上にリフトや天井があると
        // 持ち場が壁際になって、撃った直後に当たって消えていた。本体のいる所は必ず空いているので、そこから撃てば消えない
        cloneRushPhase = (int)CloneRushChargeStage.Charge;
        cloneRushShotsLeft = cloneCount;
        yield return StartCoroutine(WaitSkillTime(cloneChargeTime));
        // 本体の無敵は、溜め終わってから0.3秒で切る。撃つのはその後も続き、本体は自由に動ける
        StartCoroutine(EndCloneRushProtectionAfter(0.3f));
        cloneRushPhase = (int)CloneRushChargeStage.Fire;

        for (int i = 0; i < cloneCount; i++)
        {
            if (i > 0 && cloneLaunchInterval > 0f) yield return StartCoroutine(WaitSkillTime(cloneLaunchInterval));
            if (!IsAlive) break;
            // ▼【重要】1体の生成に失敗しても、残りの分身や末尾の状態リセットが必ず実行されるようにtry/catchで保護する
            try { FireCloneFromMaster(i, isAuthoritative, cloneDamage, spawnedColliders); }
            catch (System.Exception e) { Debug.LogError($"❌ 分身の生成に失敗しました（{i + 1}体目）: {e}"); }
            cloneRushShotsLeft = cloneCount - i - 1;
        }
        cloneRushPhase = 0;
        cloneRushShotsLeft = 0;
    }

    // ▼【新規追加】オレ達アタックの段階（CloneRushChargeStage）と、残りの発射数。演出と同期に使う
    [HideInInspector] public int cloneRushPhase;
    [HideInInspector] public int cloneRushShotsLeft;

    // ▼【新規追加】オレ達アタックの溜めと残りの発射数の演出。段階（cloneRushPhase、ゲストへ同期）を見て切り替える
    ICloneRushChargeLook cloneRushChargeLook;
    int cloneRushLookPhase, cloneRushLookShots;

    void UpdateCloneRushChargeLook()
    {
        int phase = IsAlive && !SwordController.isKomaMode ? cloneRushPhase : 0;
        int shots = phase != 0 ? cloneRushShotsLeft : 0;
        if (phase != cloneRushLookPhase || shots != cloneRushLookShots)
        {
            if (cloneRushChargeLook == null && phase != 0) cloneRushChargeLook = SkillEffects.CloneRush.CreateCharge(this);
            if (cloneRushChargeLook != null) cloneRushChargeLook.SetStage((CloneRushChargeStage)phase, shots, cloneChargeTime);
            cloneRushLookPhase = phase;
            cloneRushLookShots = shots;
        }
        if (cloneRushChargeLook != null && phase != 0) cloneRushChargeLook.Tick(Time.deltaTime);
    }

    // 分身1体を本体の位置で作り、撃つ瞬間の相手（いなければ前）へそのまま撃ち出す
    void FireCloneFromMaster(int order, bool isAuthoritative, int cloneDamage, List<Collider2D> spawnedColliders)
    {
        const float cloneColliderRadius = 0.6f;
        Transform target = PickCloneTarget();
        Vector2 origin = transform.position;
        Vector2 direction = target != null ? (Vector2)target.position - origin : cloneForward;
        if (direction.sqrMagnitude < .0001f) direction = cloneForward;
        direction.Normalize();

        GameObject cloneObj = new GameObject("SwordClone");
        cloneObj.transform.position = origin;
        cloneObj.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f);

        // ▼【新規追加】分身たちに「今使っている自分の剣 + まだ使っていない手持ちの剣」の形を持たせる
        // (order=0が現在の自分、以降は手持ちの他の剣)。画像が取れない場合は現在の刀身と同じ見た目にする
        int lifeSpriteIndex = -1;
        Sprite cloneSprite = spriteRenderer != null ? spriteRenderer.sprite : null;
        if (MultiplayerOwner != null)
        {
            int wantedIndex = MultiplayerOwner.GetCurrentLifeIndex(PlayerId) + order;
            var lifeSprite = MultiplayerOwner.GetLifeSprite(PlayerId, wantedIndex);
            if (lifeSprite != null) { cloneSprite = lifeSprite; lifeSpriteIndex = wantedIndex; }
        }
        // ▼【変更】分身はプレイヤーの色に染める（以前は技の青。誰の分身か分かるように）
        Color cloneColor = CloneTint;
        if (spriteRenderer != null)
        {
            SpriteRenderer sr = cloneObj.AddComponent<SpriteRenderer>();
            sr.sprite = cloneSprite;
            sr.color = cloneColor;
            sr.sortingLayerID = spriteRenderer.sortingLayerID;
            sr.sortingOrder = spriteRenderer.sortingOrder;
            // ▼【修正】大きさは CloneVisualScale で本体の刀身と同じ太さにそろえる（どの剣を装備していても同じ大きさ）
            cloneObj.transform.localScale = Vector3.one * CloneVisualScale(this, cloneSprite);
        }
        // 当たり判定の大きさは見た目の大きさに関わらず固定（cloneColliderRadius）。拡大縮小の分を打ち消しておく
        CircleCollider2D cloneCollider = cloneObj.AddComponent<CircleCollider2D>();
        cloneCollider.radius = cloneColliderRadius / Mathf.Max(.0001f, cloneObj.transform.localScale.x);
        Rigidbody2D cloneRb = cloneObj.AddComponent<Rigidbody2D>();
        cloneRb.gravityScale = 0f;

        // ▼ オンライン対戦では、Host権威の分身だけをMultiplayerManagerに登録し、SYNCでクライアントへ見た目を配信する
        string cloneId = (isAuthoritative && MultiplayerOwner != null) ? MultiplayerOwner.RegisterClone(cloneObj, PlayerId, cloneColor, lifeSpriteIndex) : null;

        // 自分自身や他の分身とは当たらないようにする（本体の位置から撃つので、本体と重なって生まれる）
        foreach (Collider2D myCol in GetComponentsInChildren<Collider2D>()) Physics2D.IgnoreCollision(cloneCollider, myCol, true);
        foreach (Collider2D other in spawnedColliders) if (other != null) Physics2D.IgnoreCollision(cloneCollider, other, true);
        spawnedColliders.Add(cloneCollider);

        SwordCloneProjectile clone = cloneObj.AddComponent<SwordCloneProjectile>();
        clone.Setup(this, isAuthoritative, cloneDamage, cloneLifeTime, MultiplayerOwner, cloneId);
        // 演出（撃った所の閃光と輪・軌跡・飛沫など）。ゲストの画面では MultiplayerManager が同じものを付ける
        clone.Look = SkillEffects.CloneRush.Attach(cloneObj, this);
        clone.FireNow(target, direction, cloneDashSpeed, cloneHomingTurnRate, cloneHomingTime, cloneLifeTime);
    }

    // 狙える相手がいない時に分身を撃つ向き（技を出した時の相手の方）
    Vector2 cloneForward = Vector2.right;

    // ▼【新規追加】分身が狙う相手：本体が今狙っている相手。その相手が倒れていれば、次の生きている相手。いなければ null
    public Transform PickCloneTarget()
    {
        Transform aimed = controller != null ? controller.enemyTarget : null;
        if (aimed != null && IsCloneTargetAlive(aimed)) return aimed;
        foreach (var enemy in NearbyEnemies())
            if (enemy != null && enemy != this && enemy.IsAlive) return enemy.transform;
        return null;
    }

    bool IsCloneTargetAlive(Transform target)
    {
        if (target == null) return false;
        var sword = target.GetComponentInParent<SwordBattle>();
        if (sword == null) return true;
        if (sword == this || !sword.IsAlive) return false;
        return MultiplayerOwner == null || !MultiplayerOwner.AreTeammates(PlayerId, sword.PlayerId);
    }

    // 分身が狙う相手の位置（いなければ自分の正面）
    Vector2 CloneAimPosition()
    {
        return controller != null && controller.enemyTarget != null
            ? (Vector2)controller.enemyTarget.position
            : (Vector2)(transform.position + transform.right);
    }

    IEnumerator EndCloneRushProtectionAfter(float seconds)
    {
        yield return StartCoroutine(WaitSkillTime(seconds));
        if (currentDashType != 5) yield break;
        skillAllowsMovement = false;
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

        if (spriteRenderer != null) spriteRenderer.color = shieldColor;
        // カットインが抜けてからシールドを出す（以前はカットインの開始と同時に出て、もう回っていた）
        yield return StartCoroutine(PlayUltimateCutinAndWait("オレ達シールド!!", shieldColor));
        if (UltimateCancelled) { CancelUltimate(); yield break; }
        skillAllowsMovement = true;

        // ▼【重要】実際にシールドを展開・判定するのはHost権威（またはローカル/テストモード）側のみ。
        // Client側の見た目再生（Kinematicコピー）では展開しない（見た目はSYNCで複製される）
        bool isAuthoritative = rb == null || rb.bodyType == RigidbodyType2D.Dynamic;
        if (isAuthoritative && spriteRenderer != null)
        {
            float sectorSize = 360f / Mathf.Max(1, leafShieldCount);
            leafShieldRadiusNow = leafShieldOrbitRadius;

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
                // ▼【変更】シールドの分身はプレイヤーの色に染める（以前は技の緑。誰の分身か分かるように）
                sr.color = CloneTint;
                sr.sortingLayerID = spriteRenderer.sortingLayerID;
                sr.sortingOrder = spriteRenderer.sortingOrder;
                // ▼【修正】大きさは CloneVisualScale で本体の刀身と同じ太さにそろえる（どの剣を装備していても同じ大きさ）
                shieldObj.transform.localScale = Vector3.one * CloneVisualScale(this, shieldSprite);

                // 当たり判定の大きさは見た目の大きさに関わらず固定（leafShieldRadius）。拡大縮小の分を打ち消しておく
                CircleCollider2D col = shieldObj.AddComponent<CircleCollider2D>();
                col.isTrigger = true;
                col.radius = leafShieldRadius / Mathf.Max(.0001f, shieldObj.transform.localScale.x);

                LeafShieldOrb orb = shieldObj.AddComponent<LeafShieldOrb>();
                orb.Setup(this, i * sectorSize, leafShieldDurability, leafShieldDuration, i);
                // 演出（展開して着いた時の輪・広げている間の軌跡・ひび・割れ方）。ゲストの画面では MultiplayerManager が同じものを付ける
                SkillEffects.LeafShield.AttachOrb(shieldObj, this);

                // ▼ オンライン対戦では、この分身をMultiplayerManagerに登録し、位置をSYNCでクライアントへ配信する
                if (MultiplayerOwner != null)
                {
                    string id = MultiplayerOwner.RegisterClone(shieldObj, PlayerId, CloneTint, lifeSpriteIndex);
                    orb.SetNetworkId(MultiplayerOwner, id);
                }
            }
        }

        yield return StartCoroutine(WaitSkillTime(0.2f));

        skillAllowsMovement = false;
        isDashing = false;
        currentDashType = 0;
        if (spriteRenderer != null) spriteRenderer.color = Color.white;
    }

    // ▼【新規追加】オレ達シールドの守りと返し
    // 剣同士がぶつかった時に自分が弾かれる強さの倍率。オレ達シールドが出ている間は弱める（相手側の弾かれ方は変えない）
    public float KnockbackScale => HasActiveLeafShield ? leafShieldKnockbackRate : 1f;

    // 受けるダメージ：シールドが出ていれば leafShieldDamageTakenRate を掛ける（1未満にはしない）
    public int ShieldedDamage(int damage)
    {
        return HasActiveLeafShield ? Mathf.Max(1, Mathf.RoundToInt(damage * leafShieldDamageTakenRate)) : damage;
    }

    // 本体が実際にダメージを受けた時（ローカルはTakeDamage、オンラインはMatchRulesの確定後にMultiplayerManagerから）。
    // 耐久が一番残っている分身を1削り、本体で殴ってきた相手には返す。分身や返しの攻撃（本体ではない）には返さないので、
    // シールド同士で返し合いが続くことはない
    public void OnShieldedBodyHit(SwordBattle attacker, Vector2 point, bool byBody)
    {
        if (!HasActiveLeafShield) return;
        LeafShieldOrb sturdiest = null;
        foreach (var orb in activeShieldOrbs)
            if (orb != null && (sturdiest == null || orb.Durability > sturdiest.Durability)) sturdiest = orb;
        if (sturdiest != null) sturdiest.Wear(1);
        // 本体を殴ってきた相手には、相手自身の攻撃力ぶんを返す（殴るほど痛い）
        if (byBody && attacker != null) TryShieldRetaliate(attacker, point, attacker.attack);
    }

    // 敵に「baseAttack×倍率」を返す。分身に触れた時と本体を殴られた時で共通の間隔（同じ敵には shieldRetaliateInterval 秒に1回まで）。
    // ▼【変更】分身に触れた敵には自分の攻撃力、本体を殴ってきた敵にはその敵の攻撃力を基にする（呼び出し側が決める）。
    // 分身の返しを相手の攻撃力で決めていた時は、攻撃力が低くHPの多い相手をほとんど削れず、相手によって強さが大きく変わっていた。
    // 本体で当てた攻撃ではない扱いなので、倒しても撃破の停止はない
    readonly Dictionary<SwordBattle, float> lastShieldRetaliation = new Dictionary<SwordBattle, float>();

    public bool TryShieldRetaliate(SwordBattle enemy, Vector2 point, int baseAttack)
    {
        if (enemy == null || enemy == this || !enemy.IsAlive || !IsAlive) return false;
        if (lastShieldRetaliation.TryGetValue(enemy, out var at) && Time.time - at < shieldRetaliateInterval) return false;
        lastShieldRetaliation[enemy] = Time.time;
        int damage = Mathf.Max(1, Mathf.RoundToInt(baseAttack * leafShieldReflectMultiplier));
        DealDamageTo(enemy, damage, point, false, false, false);
        return true;
    }

    // 輪の半径。普段は leafShieldOrbitRadius（近い敵には縮めなくても当たる）で、一番近い敵が少し離れている時だけ、
    // その距離まで広げて分身を届かせる。一番近い敵が感知射程（leafShieldSenseRadius）に少しいたら広げ始め、
    // 広げた後は実射程（leafShieldMaxOrbitRadius）まで追いかけ、そこから出て少し経ったら（または近くまで来たら）ゆっくり戻す。
    // 距離は「輪の中心（自分の刀身の根元）から、敵の当たり判定の一番近い点まで」で測る（敵が傾いていても一番近い部分に届く）
    bool shieldReaching;
    float shieldReachInTime, shieldReachOutTime;

    void UpdateLeafShieldRadius()
    {
        if (!HasActiveLeafShield)
        {
            leafShieldRadiusNow = -1f;
            shieldReaching = false;
            shieldReachInTime = shieldReachOutTime = 0f;
            return;
        }
        float usual = leafShieldOrbitRadius;
        float farthest = Mathf.Max(usual, leafShieldMaxOrbitRadius);
        // 感知射程は最大の届く距離とは別に決める（最大より長くてよい）
        float sense = Mathf.Max(usual, leafShieldSenseRadius);
        float nearest = NearestEnemyPartDistance();
        if (!shieldReaching)
        {
            bool sensed = nearest > usual && nearest <= sense;
            shieldReachInTime = sensed ? shieldReachInTime + Time.deltaTime : 0f;
            // 感知射程の中にいることも条件にする（待ち時間が0の時に、敵がいなくても追い始めていた）
            if (sensed && shieldReachInTime >= leafShieldReachStartDelay) { shieldReaching = true; shieldReachOutTime = 0f; }
        }
        else
        {
            // 近くまで来た（普段の半径で当たる）か、感知射程の外へ出た
            bool done = nearest < 0f || nearest <= usual || nearest > sense;
            shieldReachOutTime = done ? shieldReachOutTime + Time.deltaTime : 0f;
            if (shieldReachOutTime >= leafShieldReachHoldTime) { shieldReaching = false; shieldReachInTime = 0f; }
        }
        float now = LeafShieldOrbitRadiusNow;
        // 敵の一番近い点より少し外に合わせて、分身が敵の体に食い込む位置を通るようにする
        float wanted = shieldReaching && nearest >= 0f ? Mathf.Clamp(nearest + leafShieldReachDepth, usual, farthest) : usual;
        if (wanted > now)
        {
            // 広げる：残りの距離に比例した速さで近づける（どこまで広げても leafShieldReachTime でほぼ届き、着く直前は減速する）
            float k = 1f - Mathf.Exp(-3f * Time.deltaTime / Mathf.Max(.01f, leafShieldReachTime));
            leafShieldRadiusNow = Mathf.Lerp(now, wanted, k);
        }
        else
        {
            leafShieldRadiusNow = Mathf.MoveTowards(now, wanted, leafShieldRadiusReturnSpeed * Time.deltaTime);
        }
    }

    // 輪が気にする敵：オンラインは生きている敵全員（1vs3の味方は除く）、ローカル対戦は狙っている相手
    IEnumerable<SwordBattle> NearbyEnemies()
    {
        return MultiplayerOwner != null
            ? MultiplayerOwner.OpponentSwordsOf(PlayerId)
            : (controller != null && controller.enemyTarget != null
                ? new[] { controller.enemyTarget.GetComponentInParent<SwordBattle>() }
                : new SwordBattle[0]);
    }

    // 輪の中心から、一番近い敵の当たり判定の一番近い点までの距離。敵がいなければ -1。
    // ▼【修正】オンラインでは「狙っている相手」に限らず、生きている敵（1vs3の味方は除く）全員から一番近いものを探す。
    // 狙う相手は急に切り替わらないよう粘る作りなので、それだけを見ていると、届く範囲に別の敵がいても広がらなかった
    public float NearestEnemyPartDistance()
    {
        Vector2 centre = transform.position;
        float nearest = -1f;
        foreach (var enemy in NearbyEnemies())
        {
            if (enemy == null || enemy == this || !enemy.IsAlive) continue;
            foreach (var part in enemy.GetComponentsInChildren<Collider2D>())
            {
                if (part == null || !part.enabled) continue;
                float distance = Vector2.Distance(part.ClosestPoint(centre), centre);
                if (nearest < 0f || distance < nearest) nearest = distance;
            }
        }
        return nearest;
    }

    // 巨大回転斬がオレ達シールドの分身に触れた時：この一回転でまだ victim（分身の持ち主）に当たっていなければ、
    // 分身が身代わりになる（本体にはもう当たらない）。本体が先に触れていた（当てる順番待ちの）時は false
    public bool TryUseGiantSpinHitOn(SwordBattle victim)
    {
        if (currentDashType != 4 || !giantSpinHitActive || victim == null) return false;
        foreach (var pending in pendingGiantSpinHits) if (pending.target == victim) return false;
        return giantSpinHitTargets.Add(victim);
    }

    // 大回転斬がオレ達シールドの分身に触れた時：分身から離れる向きへ跳ね返って、突進を続ける
    public void BounceSpinDashOff(Vector2 from)
    {
        if (currentDashType != 3 || !isDashShooting || rb == null || rb.bodyType != RigidbodyType2D.Dynamic) return;
        Vector2 velocity = rb.linearVelocity;
        Vector2 normal = (Vector2)transform.position - from;
        if (normal.sqrMagnitude < .0001f) normal = -velocity;
        if (normal.sqrMagnitude < .0001f) return;
        normal.Normalize();
        Vector2 bounce = velocity.sqrMagnitude > .0001f ? Vector2.Reflect(velocity.normalized, normal) : normal;
        // すれ違いざまで、反射してもまだ分身の方へ向かう時は、分身から離れる向きにする
        if (Vector2.Dot(bounce, normal) < 0f) bounce = normal;
        rb.linearVelocity = bounce * Mathf.Max(velocity.magnitude, 40f);
        rb.angularVelocity = Mathf.Sign(bounce.x) * -2500f;
        spinDashVelocity = rb.linearVelocity;
        spinDashAngular = rb.angularVelocity;
    }

    // ▼【修正】分身・シールドの見た目の大きさ。本体の刀身と同じく、元画像の横幅に関係なく
    // 「刀身の太さ(SwordGenerator.targetBladeWidth) × 本体の大きさ」の横幅にそろえる倍率を返す。
    // 以前は「今装備している剣の元画像の横幅」に合わせていたため、同じ画像の分身でも、装備中の剣の画像によって大きさが変わっていた。
    // ホスト（分身・シールドを作る時）とゲスト（MultiplayerManager.CreateCloneVisual）の両方がこれを使う
    public static float CloneVisualScale(SwordBattle owner, Sprite sprite)
    {
        if (owner == null || sprite == null || sprite.bounds.size.x <= 0f) return 1f;
        var generator = owner.GetComponent<SwordGenerator>();
        float bladeWidth = generator != null ? generator.targetBladeWidth : 1.5f;
        return bladeWidth * Mathf.Abs(owner.transform.lossyScale.x) / sprite.bounds.size.x;
    }

    // 分身の行く手をふさぐ物（壁・床）。剣・分身・シールドなどの当たり判定は含めない
    public static bool IsWallForClones(Collider2D collider)
    {
        return collider != null && !collider.isTrigger &&
            collider.GetComponentInParent<SwordBattle>() == null && collider.GetComponent<SwordCloneProjectile>() == null;
    }

    // ▼【新規追加】巨大化一回転（currentDashType 4）専用のダメージ判定
    // （技の間は当たり判定をトリガー化しているためOnCollisionEnter2Dではなくこちらで処理する）。
    // ▼【修正】攻撃判定は一回転の間だけ。巨大化の途中から重なっていた相手にも当たるよう、Enterに加えてStayでも拾う。
    // 触れた相手はいったん貯め、次のFixedUpdateで近い順に当てる（2人目以降の減衰の順番を距離で決めるため）
    void OnTriggerEnter2D(Collider2D other) { QueueGiantSpinHit(other); }
    void OnTriggerStay2D(Collider2D other) { QueueGiantSpinHit(other); }

    void QueueGiantSpinHit(Collider2D other)
    {
        if (isDead || matchEnded) return;
        if (currentDashType != 4 || !giantSpinHitActive) return;

        // ▼【重要】トリガー判定はKinematic同士でも発火してしまう。
        // 他の必殺技と同様、実際のダメージ計算はDynamic（＝物理演算の権威を持つHost側）でのみ行い、
        // Client側の見た目再生（Kinematicコピー）では二重計算しない。HP等はHostからのSYNCで同期される。
        if (rb == null || rb.bodyType != RigidbodyType2D.Dynamic) return;

        SwordBattle target = other.GetComponentInParent<SwordBattle>();
        if (target == null || target == this || !target.IsAlive) return;
        // 味方（1vs3の同じ陣営）はダメージにならないので、減衰の人数にも数えない
        if (MultiplayerOwner != null && MultiplayerOwner.AreTeammates(PlayerId, target.PlayerId)) return;
        // 同じ相手には一度だけ当てる（回転中に何度もすれ違っても二重に当てない）。別の相手には引き続き当たる
        if (giantSpinHitTargets.Contains(target)) return;
        foreach (var pending in pendingGiantSpinHits) if (pending.target == target) return;
        // 最初に触れた部分で当たり方を決める（相手の柄なら弱点で2倍）
        pendingGiantSpinHits.Add((target, other));
    }

    void FlushGiantSpinHits()
    {
        if (pendingGiantSpinHits.Count == 0) return;
        Vector2 centre = transform.position;
        pendingGiantSpinHits.Sort((a, b) =>
            ((Vector2)a.target.transform.position - centre).sqrMagnitude.CompareTo(((Vector2)b.target.transform.position - centre).sqrMagnitude));
        foreach (var (target, part) in pendingGiantSpinHits)
        {
            if (target == null || !target.IsAlive || !giantSpinHitTargets.Add(target)) continue;
            // 1人目は等倍、2人目・3人目以降は減らす
            float rate = PoseSword.Multiplayer.BattlePolicies.GiantSpinHitRate(giantSpinHitTargets.Count, giantSpinSecondHitRate, giantSpinLaterHitRate);
            bool isWeakPoint = part != null && part.CompareTag("Handle") && !target.HasActiveLeafShield;
            int damage = Mathf.RoundToInt(attack * giantSpinDamageMultiplier * rate);
            if (isWeakPoint) damage *= 2;
            damage = Mathf.Max(damage, 1);

            Vector2 hitPoint = part != null ? part.ClosestPoint(transform.position) : (Vector2)target.transform.position;
            // ▼【修正】target.TakeDamage()を直接呼ぶと、target側がマルチプレイ中(MultiplayerOwner != null)の時に
            // 何もせず握りつぶしてしまう（HPはQueueHit経由のMatchRulesでのみ確定するため）。
            // DealDamageTo()を通すことで、オンラインでも正しくダメージが反映されるようにする
            DealDamageTo(target, damage, hitPoint, true, isWeakPoint, true);
        }
        pendingGiantSpinHits.Clear();
    }

    // ▼【修正】剣モード専用「一直線ジャンプダッシュ」
    IEnumerator SwordDashRoutine()
    {
        isDashing = true;
        currentDashType = 3;
        isDashShooting = false; 
        
        if (spriteRenderer != null) spriteRenderer.color = new Color(0.5f, 1f, 1f); 

        currentSp = 0f;
        dashDamageBonus = spinDashDamageMultiplier;

        // カットインが抜けてから小ジャンプに入る（以前はカットインの裏でもうジャンプが始まっていた）
        yield return StartCoroutine(PlayUltimateCutinAndWait("大回転斬!!", new Color(0.5f, 1f, 1f)));
        if (UltimateCancelled) { CancelUltimate(); dashDamageBonus = 1.0f; yield break; }
        // 味方（1vs3の同じ陣営）はすり抜ける。戻すのは EndSwordDash
        IgnoreTeammateCollisions();

        // ▼【修正】1. 小ジャンプの予備動作（Hostのみ）
        spinDashPhase = (int)SpinDashStage.Hop;
        if (rb != null && rb.bodyType == RigidbodyType2D.Dynamic)
        {
            rb.linearVelocity = new Vector2(0f, 15f); 
            rb.angularVelocity = 720f; 
        }
        
        yield return StartCoroutine(WaitSkillTime(0.2f));

        // ▼【修正】2. 空中で一瞬静止（Hostのみ）
        spinDashPhase = (int)SpinDashStage.Charge;
        if (rb != null && rb.bodyType == RigidbodyType2D.Dynamic)
        {
            rb.linearVelocity = Vector2.zero; 
            rb.gravityScale = 0f;            
            rb.angularVelocity = 1440f;
        }

        yield return StartCoroutine(WaitSkillTime(0.1f));

        isDashShooting = true; 
        spinDashPhase = (int)SpinDashStage.Dash;
        spinDashVelocity = Vector2.zero;
        spinDashAngular = 0f;
        spinSkimSurface = null;
        spinAimTarget = controller != null ? controller.enemyTarget : null;

        // ▼【修正】3. 敵に向かって一直線に発射！（Hostのみ）
        if (controller != null && controller.enemyTarget != null && rb != null && rb.bodyType == RigidbodyType2D.Dynamic)
        {
            Vector2 dirToEnemy = (controller.enemyTarget.position - transform.position).normalized;
            rb.linearVelocity = dirToEnemy * 50f; 
            
            float spinDir = Mathf.Sign(dirToEnemy.x) * -1f; 
            rb.angularVelocity = spinDir * 2500f; 
            spinDashVelocity = rb.linearVelocity;
            spinDashAngular = rb.angularVelocity;

            Debug.Log($"⚔️ 剣モード：一直線ダッシュ発動！");
        }

        yield return StartCoroutine(WaitSkillTime(2.0f));
        if (isDashing && !SwordController.isKomaMode)
        {
            EndSwordDash(false);
        }
    }

    // ▼【修正】ダッシュ終了時。hitTarget：敵の剣に当たって終わった（false なら2秒たって外れた）
    public void EndSwordDash(bool hitTarget = true)
    {
        // 相手に当たって終わった後、同じ物理ステップでもう一度呼ばれても二重に止めない
        if (!isDashing || currentDashType != 3) return;
        if (!hitTarget) spinHitTarget = null;

        // ▼【修正】無敵(isDashing)はここでは外さず、DelayEndDashFlagで物理ステップの後に外す。
        // 以前はここでisDashing=falseにしていたため、相手に当たったその瞬間に無敵が切れ、
        // 同じ衝突で相手側のOnCollisionEnter2Dが先に呼ばれなかった場合（ローカル対戦）に反撃をもらっていた
        currentDashType = 0;
        isDashShooting = false;
        RestoreTeammateCollisions();

        if (spriteRenderer != null) spriteRenderer.color = Color.white;

        // ▼【変更】以前はここで速さと回転を0にして重力を戻していた。今は終わり際の動き（SpinDashSettleRoutine）に任せる。
        // 外れた時は今の速さのまま減速を始め、当たった時は突進していた向きと反対へ弾かれる
        bool controlsPhysics = rb != null && rb.bodyType == RigidbodyType2D.Dynamic;
        // 撃ち出した直後で、まだ突進中の速さを覚えていない時は今の速さを使う
        bool remembered = hitTarget && spinTravelVelocity.sqrMagnitude > .01f;
        Vector2 velocity = controlsPhysics ? (remembered ? spinTravelVelocity : rb.linearVelocity) : Vector2.zero;
        float angular = controlsPhysics ? (remembered ? spinTravelAngular : rb.angularVelocity) : 0f;
        spinTravelVelocity = Vector2.zero;
        if (spinSettleRoutine != null) StopCoroutine(spinSettleRoutine);
        spinSettleRoutine = StartCoroutine(SpinDashSettleRoutine(hitTarget, velocity, angular));
        StartCoroutine(DelayEndDashFlag());
    }

    Vector2 spinTravelVelocity, spinSettleVelocity;
    float spinTravelAngular;
    // 突進中に保つ速さと回転。撃ち出した時に決まり、壁やシールドで跳ね返った時だけ変わる
    Vector2 spinDashVelocity;
    float spinDashAngular;
    // 撃ち出した時に狙った相手と、浅い角度で触れて沿って滑っている面（リフトの上面など）
    Transform spinAimTarget;
    Collider2D spinSkimSurface;
    Vector2 spinSkimNormal;
    float spinSkimHeight;

    // ▼【新規追加】面に沿って滑っていて、その面の端を越えた（足元からその面がなくなった）時に、一度だけ狙っていた相手へ向かい直す。
    // リフトの上から地上の相手へ撃つと、リフトに浅く触れて真横に滑り出し、端を越えてもそのまま真横へ飛んでいっていた。
    // 地上どうしでは床がずっと足元にあるので向かい直さず、床すれすれを滑り続ける。飛んでいる間ずっと追う追尾はしない
    void ReaimAfterSkimmingOffAnEdge()
    {
        if (spinSkimSurface == null) return;
        if (!spinSkimSurface.enabled || !spinSkimSurface.gameObject.activeInHierarchy) { spinSkimSurface = null; return; }
        Vector2 pivot = rb.worldCenterOfMass;
        float probe = spinSkimHeight + 1.5f;
        foreach (var hit in Physics2D.RaycastAll(pivot, -spinSkimNormal, probe))
            if (hit.collider == spinSkimSurface) return; // まだ面の上
        spinSkimSurface = null;
        var target = spinAimTarget != null ? spinAimTarget.GetComponentInParent<SwordBattle>() : null;
        if (spinAimTarget == null || (target != null && !target.IsAlive)) return;
        Vector2 toTarget = (Vector2)spinAimTarget.position - pivot;
        if (toTarget.sqrMagnitude < .0001f) return;
        Vector2 dir = toTarget.normalized;
        spinDashVelocity = dir * spinDashVelocity.magnitude;
        spinDashAngular = Mathf.Sign(dir.x) * -1f * Mathf.Abs(spinDashAngular);
    }
    SwordBattle spinHitTarget;
    Coroutine spinSettleRoutine;

    // 大回転斬の終わり際の動き。減速（当たった時は弾かれる）→ ゆっくり降りる。
    // 速さと回転は (1-k)^2 で0へ近づける（モックと同じ。当たった時に弾かれる距離が spinHitRecoilDistance になる）
    IEnumerator SpinDashSettleRoutine(bool hit, Vector2 velocity, float angular)
    {
        bool controlsPhysics = rb != null && rb.bodyType == RigidbodyType2D.Dynamic;
        float slowTime = Mathf.Max(.01f, hit ? spinHitRecoilTime : spinMissSlowTime);
        spinDashPhase = (int)(hit ? SpinDashStage.SlowHit : SpinDashStage.SlowMiss);
        Vector2 travel = velocity.sqrMagnitude > .0001f ? velocity.normalized : Vector2.zero;
        spinSettleVelocity = hit ? -travel * (3f * spinHitRecoilDistance / slowTime) : velocity;
        if (controlsPhysics) rb.gravityScale = 0f;

        float elapsed = 0f;
        while (elapsed < slowTime)
        {
            float left = 1f - elapsed / slowTime;
            if (controlsPhysics)
            {
                rb.linearVelocity = spinSettleVelocity * (left * left);
                rb.angularVelocity = angular * (left * left);
            }
            yield return null;
            if (!SkillClockStopped) elapsed += Time.deltaTime;
        }
        if (controlsPhysics) { rb.linearVelocity = Vector2.zero; rb.angularVelocity = 0f; }
        spinHitTarget = null;

        // ゆっくり降りる：重力を0から元へ戻していく。ここからは操作でき、ジャンプすると Jump() が打ち切る
        spinDashPhase = (int)SpinDashStage.Descend;
        float descendTime = Mathf.Max(.01f, spinDescendTime);
        elapsed = 0f;
        while (elapsed < descendTime)
        {
            if (controlsPhysics) rb.gravityScale = Mathf.SmoothStep(0f, 1f, elapsed / descendTime);
            yield return null;
            if (!SkillClockStopped) elapsed += Time.deltaTime;
        }
        spinSettleRoutine = null;
        EndSpinSettle();
    }

    // 大回転斬の終わり際を打ち切って、普通の動き（重力など）に戻す
    void EndSpinSettle()
    {
        if (spinSettleRoutine != null) { StopCoroutine(spinSettleRoutine); spinSettleRoutine = null; }
        spinHitTarget = null;
        bool wasSettling = spinDashPhase >= (int)SpinDashStage.SlowMiss;
        spinDashPhase = 0;
        if (wasSettling && !isDead && rb != null && rb.bodyType == RigidbodyType2D.Dynamic && controller != null) controller.ApplyPhysicsMode();
    }

    // ▼【新規追加】大回転斬の演出。段階（spinDashPhase）の変わり目を見て見た目を切り替える。
    // ゲストは段階の同期を受け取るだけなので、ホストもゲストも同じ流れで描く
    ISpinDashLook spinDashLook;
    int spinDashLookPhase;

    void UpdateSpinDashLook()
    {
        int phase = IsAlive && !SwordController.isKomaMode ? spinDashPhase : 0;
        if (phase != spinDashLookPhase)
        {
            if (spinDashLook == null && phase != 0) spinDashLook = SkillEffects.SpinDash.Create(this);
            if (spinDashLook != null) spinDashLook.SetStage((SpinDashStage)phase, SpinStageDuration((SpinDashStage)phase));
            spinDashLookPhase = phase;
        }
        if (spinDashLook != null && phase != 0) spinDashLook.Tick(Time.deltaTime);
    }

    float SpinStageDuration(SpinDashStage stage)
    {
        switch (stage)
        {
            case SpinDashStage.Hop: return .2f;
            case SpinDashStage.Charge: return .1f;
            case SpinDashStage.Dash: return 2f;
            case SpinDashStage.SlowMiss: return spinMissSlowTime;
            case SpinDashStage.SlowHit: return spinHitRecoilTime;
            case SpinDashStage.Descend: return spinDescendTime;
            default: return 0f;
        }
    }
    IEnumerator DelayEndDashFlag()
    {
        // Unityの物理演算がこのフレームの処理を終えるまで待機する
        yield return new WaitForFixedUpdate();

        // 計算が終わって安全になってから、無敵フラグを解除！
        isDashing = false;
        // 突進の上乗せ倍率もここで外す。当たった衝突のダメージ計算（同じ物理ステップ）までは残しておき、
        // 衝突の速さが足りずに使われなかった場合でも、次の通常攻撃に持ち越さない
        dashDamageBonus = 1.0f;
    }

    // ▼【新規追加】大回転斬の間だけ、味方（1vs3の同じ陣営）の剣との当たり判定を切ってすり抜けさせる。
    // 以前は味方に当たるとダメージ0のまま突進が終わっていた。切った組を覚えておき、突進の終わりに同じ組だけ戻す
    readonly List<(Collider2D mine, Collider2D theirs)> ignoredTeammateContacts = new List<(Collider2D mine, Collider2D theirs)>();

    void IgnoreTeammateCollisions()
    {
        RestoreTeammateCollisions();
        if (MultiplayerOwner == null) return;
        var mine = GetComponentsInChildren<Collider2D>();
        foreach (var mate in MultiplayerOwner.TeammateSwordsOf(PlayerId))
            foreach (var theirs in mate.GetComponentsInChildren<Collider2D>())
                foreach (var own in mine)
                {
                    Physics2D.IgnoreCollision(own, theirs, true);
                    ignoredTeammateContacts.Add((own, theirs));
                }
    }

    void RestoreTeammateCollisions()
    {
        foreach (var pair in ignoredTeammateContacts)
            if (pair.mine != null && pair.theirs != null) Physics2D.IgnoreCollision(pair.mine, pair.theirs, false);
        ignoredTeammateContacts.Clear();
    }
}
