using UnityEngine;

public class SwordController : MonoBehaviour
{
    [Header("動かす剣のRigidbody")]
    public Rigidbody2D swordRigidbody;

    // ▼追加：敵の位置を把握するための枠
    [Header("ターゲット（敵）")]
    public Transform enemyTarget; 

    [Header("ジャンプの力（X:横, Y:縦）")]
    public Vector2 jumpForce = new Vector2(300f, 1000f); 

    [Header("回転の力（マイナスで時計回り）")]
    public float spinTorque = -500f;

    // 【追加1】上部の変数宣言のところ
    [Header("操作権限")]
    public bool isLocalControlled = true;
    [HideInInspector] public MultiplayerManager multiplayer;
    // ▼【新規追加】1vs3：ボスの制圧を受けている間はtrue。Host側が毎FixedUpdateに更新する。
    // 独楽モードは操作なしでも敵へ自動追尾するため、追尾力もここで止めないと拘束にならない
    [HideInInspector] public bool suppressed;

    [Header("モード設定")]
    public static bool isKomaMode = false; // 全体で共有するモードフラグ

    [Header("柄のオブジェクト（独楽モード時は消す）")]
    public GameObject handleObject;

    // ▼【新規追加】柄(つか)の見た目：hiltTypeごとの画像。マルチプレイ複製元のこのコンポーネントに
    // 設定しておくと、MultiplayerManager.CreateSwordが実行時に生成するSwordGeneratorへコピーする
    // (SwordGeneratorはテンプレートに常設されておらず、複製時にAddComponentされるためInspectorで
    // 直接設定した値を持てない。SwordControllerは常設なので、こちらに置いてコピーする方式にした)
    [Header("柄（つか）の見た目：hiltTypeごとの画像(React側の武器庫の柄と対応)")]
    public Sprite handleSprite0; // "0"(未指定/不明な値含む) = 普通の柄
    public Sprite handleSprite1; // "1" = 武骨な柄
    public Sprite handleSprite2; // "2" = 悪魔の柄
    public Sprite handleSprite3; // "3" = 大翼の柄

    [Header("独楽モード用の力")]
    public float komaSpinTorque = -3000f; // 独楽の回転力
    public float komaHomingForce = 20f;   // 敵に向かっていく力
    public float maxKomaSpinSpeed = 1500f;

    [Header("【テスト用】チェックを入れると独楽モードで開始")]
    public bool testKomaMode = false;

    // ▼【調査用】生成直後だけでなく、試合が始まってしばらく経ってからも柄の状態を記録して、
    // 「生成時は正常だったのに後から非表示になった」ケースを追えるようにする
    private float handleDiagnosticTimer = 2f;
    private bool handleDiagnosticLogged = false;

    void Start()
    {
        // Unityエディタで実行している時だけ、インスペクタのチェックを反映する
#if UNITY_EDITOR
        if (multiplayer == null) isKomaMode = testKomaMode;
#endif
        // 開始時に重力と摩擦をセット
        ApplyPhysicsMode();
    }

    void Update()
    {
        // // ▼【修正】剣モード：ラウンド開始（GO!）が出るまでクリック操作（ジャンプ）を受け付けない
        // if (SwordBattle.isRoundStarted && !isKomaMode && isLocalControlled && Input.GetMouseButtonDown(0))
        // {
        //     JumpAndSpin();
        // }

        // ▼【N人対応】毎フレーム、生存中で最も近い相手を自動でターゲットにする
        // (本物のマルチプレイ中はMultiplayerManager.UpdateTargets()が専用ロジックでenemyTargetを決めるため、ここでは触らない)
        if (multiplayer == null) RefreshEnemyTarget();

        // ▼【調査用ログ】生成から一定時間後(試合が動き出した頃)の柄の状態をもう一度記録する。
        // 生成直後のログと比較して、後から何かが非表示にしていないかを確認するため
        if (!handleDiagnosticLogged)
        {
            handleDiagnosticTimer -= Time.deltaTime;
            if (handleDiagnosticTimer <= 0f)
            {
                handleDiagnosticLogged = true;
                bool? hostFlag = multiplayer != null ? multiplayer.IsHost : (bool?)null;
                Debug.Log($"⏱️ [遅延チェック] 柄の状態(生成の数秒後): sword={gameObject.name}, IsHost={hostFlag}, " +
                    $"isLocalControlled={isLocalControlled}, isKomaMode={isKomaMode}, " +
                    $"handleObject={(handleObject != null ? handleObject.name + "(id=" + handleObject.GetInstanceID() + ")" : "null")}, " +
                    $"activeSelf={(handleObject != null ? handleObject.activeSelf.ToString() : "n/a")}, " +
                    $"activeInHierarchy={(handleObject != null ? handleObject.activeInHierarchy.ToString() : "n/a")}");
            }
        }
    }

    // ▼【N人対応】NetworkManagerが持つ全プレイヤーの中から、自分以外・生存中・最も近い相手を探す
    // (MultiplayerManager管理下ではない、ローカルデバッグ用の2〜4人プレイでのみ使われる)
    void RefreshEnemyTarget()
    {
        if (NetworkManager.Instance == null) return;

        GameObject[] swords = NetworkManager.Instance.playerSwords;
        Transform nearest = null;
        float nearestDist = float.MaxValue;

        for (int i = 0; i < swords.Length; i++)
        {
            GameObject obj = swords[i];
            if (obj == null || obj == gameObject || !obj.activeInHierarchy) continue;

            SwordBattle otherBattle = obj.GetComponent<SwordBattle>();
            if (otherBattle != null && otherBattle.IsDead) continue;

            float dist = (obj.transform.position - transform.position).sqrMagnitude;
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = obj.transform;
            }
        }

        if (nearest != null) enemyTarget = nearest;
    }


    void FixedUpdate()
    {
        // ▼【修正】ローカルは配置直後から独楽が回り続けるため、マルチプレイのカウントダウン中も
        // (IsPlayingではなくIsSimulatingで)同じように回転させる。相手への追尾力はSwordBattle.isRoundStarted
        // 側のガードでこれまで通りGO!が出るまで働かない
        if (multiplayer != null && (!multiplayer.IsHost || !multiplayer.IsSimulating)) return;
        // 独楽モードで、自分に操作権限がある時だけ自動で動かす
        if (isKomaMode && swordRigidbody != null && swordRigidbody.bodyType == RigidbodyType2D.Dynamic)
        {
            // ==========================================
            // 1. 高速回転（カウントダウン中もその場でギュイィィンと回り続ける！）
            // ==========================================
            swordRigidbody.AddTorque(komaSpinTorque * swordRigidbody.mass * Time.fixedDeltaTime, ForceMode2D.Force);
            float currentSpin = swordRigidbody.angularVelocity;
            swordRigidbody.angularVelocity = Mathf.Clamp(currentSpin, -maxKomaSpinSpeed, maxKomaSpinSpeed);
            
            // ==========================================
            // 2. 敵の方向へ向かう（GO! の合図が出た時だけ追尾を開始する！）
            // ==========================================
            // 制圧中は追尾しない。回転力(上の処理)には触れないので、その場で回り続ける見た目になる
            if (SwordBattle.isRoundStarted && enemyTarget != null && !suppressed)
            {
                Vector2 dirToEnemy = (enemyTarget.position - transform.position).normalized;
                swordRigidbody.AddForce(dirToEnemy * komaHomingForce * swordRigidbody.mass * Time.fixedDeltaTime, ForceMode2D.Force);
            }
        }
    }

    public void JumpAndSpin(bool jumpRight)
    {
        // ▼【修正】物理演算がオン（Host側）の時だけ力を加える！
        if (swordRigidbody == null || swordRigidbody.bodyType != RigidbodyType2D.Dynamic) return;

        Vector2 appliedForce = jumpForce;
        float appliedTorque = spinTorque;

        if (!jumpRight)
        {
            appliedForce.x *= -1f; 
            appliedTorque *= -1f;  
        }

        Vector2 currentVel = swordRigidbody.linearVelocity;
        if (currentVel.y > 0) currentVel.y *= 0.5f; 
        swordRigidbody.linearVelocity = currentVel;

        swordRigidbody.AddForce(appliedForce, ForceMode2D.Impulse);
        swordRigidbody.AddTorque(appliedTorque, ForceMode2D.Impulse);
    }

    public void NetworkJump(bool jumpRight)
    {
        JumpAndSpin(jumpRight);
    }

    // ▼【新規追加】柄(Handle-A)はInstantiate複製時、参照先が自分の階層外にあると複製先へ
    // 付け替わらない(Unityの仕様)ため、Editorの配線ミスや複製のタイミング次第で「他人の柄」を
    // 参照したままになることがあった(実機でクライアント側の柄だけ表示されない不具合の原因)。
    // 呼び出し側(複製直後やモード切り替え前)でこれを呼ぶと、自分の子から名前で柄を探し直して
    // 必ず自分自身の柄を参照するように補正できる。柄の見た目(スプライト)切り替えには一切関与しない。
    public void ResolveOwnHandle()
    {
        Transform handle = transform.Find("Handle-A");
        if (handle != null) handleObject = handle.gameObject;
    }

    public void ApplyPhysicsMode()
    {
        if (swordRigidbody == null) return;

        if (handleObject != null) handleObject.SetActive(!isKomaMode);
        
        if (isKomaMode)
        {
            swordRigidbody.gravityScale = 0f;      
            swordRigidbody.linearDamping = 1.0f;      
            swordRigidbody.angularDamping = 0f;     
            
            // ▼【新規追加】重心（回転軸）を「柄」から「刃の中央（Y軸+2.0など）」に引き上げる！
            // ※剣の長さによって、1.5f や 2.5f など気持ちいい位置に調整してください
            swordRigidbody.centerOfMass = new Vector2(0f, 2.0f); 
            
            Debug.Log("🌀 独楽物理演算を適用（重力0・重心を上に移動）");
        }
        else
        {
            swordRigidbody.gravityScale = 1f;      
            swordRigidbody.linearDamping = 0f;
            swordRigidbody.angularDamping = 0.05f;
            
            // ▼【新規追加】剣モードの時は、振り子のように安定させるため重心を柄に戻す
            swordRigidbody.centerOfMass = Vector2.zero; 
            
            Debug.Log("⚔️ 剣物理演算を適用（重力あり・重心リセット）");
        }
    }
}
