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

    [Header("モード設定")]
    public static bool isKomaMode = false; // 全体で共有するモードフラグ

    [Header("柄のオブジェクト（独楽モード時は消す）")]
    public GameObject handleObject;

    [Header("独楽モード用の力")]
    public float komaSpinTorque = -3000f; // 独楽の回転力
    public float komaHomingForce = 20f;   // 敵に向かっていく力

    [Header("【テスト用】チェックを入れると独楽モードで開始")]
    public bool testKomaMode = false;

    void Start()
    {
        // Unityエディタで実行している時だけ、インスペクタのチェックを反映する
#if UNITY_EDITOR
        isKomaMode = testKomaMode;
#endif
        // 開始時に重力と摩擦をセット
        ApplyPhysicsMode();
    }

    void Update()
    {
        // 自分に操作権限がある時だけクリックを検知する
        if (!isKomaMode && isLocalControlled && Input.GetMouseButtonDown(0))
        {
            JumpAndSpin();
        }
    }


    void FixedUpdate()
    {
        // 独楽モードで、自分に操作権限がある時だけ自動で動かす
        if (isKomaMode && swordRigidbody != null && swordRigidbody.bodyType == RigidbodyType2D.Dynamic){
            // 1. 常に高速回転させる
            swordRigidbody.AddTorque(komaSpinTorque * Time.fixedDeltaTime, ForceMode2D.Force);

            // 2. 敵の方向へジワジワ向かう（見下ろしなのでXY平面を自由に移動）
            if (enemyTarget != null)
            {
                Vector2 dirToEnemy = (enemyTarget.position - transform.position).normalized;
                swordRigidbody.AddForce(dirToEnemy * komaHomingForce * Time.fixedDeltaTime, ForceMode2D.Force);
            }
        }
    }
    void JumpAndSpin()
    {
        if (swordRigidbody != null)
        {
            swordRigidbody.linearVelocity = Vector2.zero;
            swordRigidbody.angularVelocity = 0f;

            // 基本の力と回転をセット
            Vector2 appliedForce = jumpForce;
            float appliedTorque = spinTorque;

            // ▼追加：敵が自分より「左」にいる場合は、方向を反転する！
            if (enemyTarget != null && enemyTarget.position.x < transform.position.x)
            {
                appliedForce.x *= -1f; // 左へ飛ぶように反転
                appliedTorque *= -1f;  // 回転を逆回り（反時計回り）に反転
            }

            // 計算した力でジャンプ＆回転
            swordRigidbody.AddForce(appliedForce, ForceMode2D.Impulse);
            swordRigidbody.AddTorque(appliedTorque, ForceMode2D.Impulse);
        }
    }

    
    public void NetworkJump()
    {
        // 通信越しに「飛べ！」と言われたら強制的にジャンプする
        JumpAndSpin();
    }

    public void ApplyPhysicsMode()
    {
        if (swordRigidbody == null) return;


        if (handleObject != null)
        {
            handleObject.SetActive(!isKomaMode);
        }
        
        if (isKomaMode)
        {
            // 【見下ろし独楽モード】
            swordRigidbody.gravityScale = 0f;      // 重力を完全に消す！
            swordRigidbody.linearDamping = 1.0f;      // 床の摩擦（滑りすぎないように）
            swordRigidbody.angularDamping = 0.5f;     // 回転の空気抵抗
            Debug.Log("🌀 独楽物理演算を適用（重力0・見下ろし型）");
        }
        else
        {
            // 【横視点 剣モード】
            swordRigidbody.gravityScale = 1f;      // 通常の重力（インスペクタの値に合わせてください）
            swordRigidbody.linearDamping = 0f;
            swordRigidbody.angularDamping = 0.05f;
            Debug.Log("⚔️ 剣物理演算を適用（重力あり）");
        }
    }
}