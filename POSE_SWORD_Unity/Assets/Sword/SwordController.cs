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
    public float maxKomaSpinSpeed = 1500f;

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
        // // ▼【修正】剣モード：ラウンド開始（GO!）が出るまでクリック操作（ジャンプ）を受け付けない
        // if (SwordBattle.isRoundStarted && !isKomaMode && isLocalControlled && Input.GetMouseButtonDown(0))
        // {
        //     JumpAndSpin();
        // }
    }


    void FixedUpdate()
    {
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
            if (SwordBattle.isRoundStarted && enemyTarget != null)
            {
                Vector2 dirToEnemy = (enemyTarget.position - transform.position).normalized;
                swordRigidbody.AddForce(dirToEnemy * komaHomingForce * swordRigidbody.mass * Time.fixedDeltaTime, ForceMode2D.Force);
            }
        }
    }

    public void JumpAndSpin(bool jumpRight)
    {
        if (swordRigidbody == null) return;

        Vector2 appliedForce = jumpForce;
        float appliedTorque = spinTorque;

        // 左に飛ぶ場合は力と回転を反転！
        if (!jumpRight)
        {
            appliedForce.x *= -1f; 
            appliedTorque *= -1f;  
        }

        // ▼【追加】連続タップした時にキビキビ動くように、上向きの勢いを少し殺してから再ジャンプする
        Vector2 currentVel = swordRigidbody.linearVelocity;
        if (currentVel.y > 0) currentVel.y *= 0.5f; 
        swordRigidbody.linearVelocity = currentVel;

        swordRigidbody.AddForce(appliedForce, ForceMode2D.Impulse);
        swordRigidbody.AddTorque(appliedTorque, ForceMode2D.Impulse);
    }

    
    public void NetworkJump()
    {
        bool jumpRight = true;
        if (enemyTarget != null && enemyTarget.position.x < transform.position.x)
        {
            jumpRight = false; // 敵が左にいれば左に飛ぶ
        }
        JumpAndSpin(jumpRight);
    }

    public void NetworkJump(bool jumpRight)
    {
        JumpAndSpin(jumpRight);
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