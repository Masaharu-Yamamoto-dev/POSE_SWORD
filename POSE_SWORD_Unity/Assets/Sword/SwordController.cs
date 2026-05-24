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

    void Update()
    {
        // 自分に操作権限がある時だけクリックを検知する
        if (isLocalControlled && Input.GetMouseButtonDown(0))
        {
            JumpAndSpin();
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
}