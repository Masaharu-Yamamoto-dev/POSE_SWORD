using UnityEngine;

// ▼【新規追加】剣モード必殺技「分身突進」(handleId:2) の、分身1体分の挙動
// 相手の剣に当たるとダメージを与えて消滅し、壁・床など相手の剣以外に当たった場合もその場で消滅する
public class SwordCloneProjectile : MonoBehaviour
{
    private SwordBattle owner;
    private bool dealsDamage;
    private int damage;
    private bool hasActed;

    // ▼【重要】dealsDamageはSwordBattle側でrb.bodyType==Dynamic（Host権威）かどうかを判定した結果を渡す。
    // Client側の見た目再生では実際のダメージ計算をせず、消滅演出のみ行う。
    public void Setup(SwordBattle owner, bool dealsDamage, int damage, float lifeTime)
    {
        this.owner = owner;
        this.dealsDamage = dealsDamage;
        this.damage = damage;
        Destroy(gameObject, lifeTime);
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (hasActed) return;

        SwordBattle target = collision.gameObject.GetComponentInParent<SwordBattle>();
        if (target != null && target != owner)
        {
            hasActed = true;
            if (dealsDamage)
            {
                Vector2 hitPoint = collision.GetContact(0).point;
                target.TakeDamage(damage, hitPoint, true, false);
            }
            Destroy(gameObject);
            return;
        }

        if (target == null)
        {
            // 壁・床など、相手の剣以外に当たったら消える
            hasActed = true;
            Destroy(gameObject);
        }
    }
}
