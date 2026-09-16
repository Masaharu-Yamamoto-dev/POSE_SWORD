using UnityEngine;

// ▼【新規追加】剣モード必殺技「分身突進」(hiltType:"2") の、分身1体分の挙動
// 相手の剣に当たるとダメージを与えて消滅し、壁・床など相手の剣以外に当たった場合もその場で消滅する
public class SwordCloneProjectile : MonoBehaviour
{
    private SwordBattle owner;
    private bool dealsDamage;
    private int damage;
    private bool hasActed;
    private MultiplayerManager multiplayerOwner;
    private string cloneId;

    // ▼【重要】dealsDamageはSwordBattle側でrb.bodyType==Dynamic（Host権威）かどうかを判定した結果を渡す。
    // Client側の見た目再生では実際のダメージ計算をせず、消滅演出のみ行う。
    // ▼【新規追加】multiplayerOwner/cloneIdは、この分身がHost権威のもの（オンライン対戦中）の場合のみ渡される。
    // 消滅時にMultiplayerManagerへ登録解除することで、SYNC配信からも消える＝クライアント側の分身も消える。
    public void Setup(SwordBattle owner, bool dealsDamage, int damage, float lifeTime,
        MultiplayerManager multiplayerOwner = null, string cloneId = null)
    {
        this.owner = owner;
        this.dealsDamage = dealsDamage;
        this.damage = damage;
        this.multiplayerOwner = multiplayerOwner;
        this.cloneId = cloneId;
        Destroy(gameObject, lifeTime);
    }

    void OnDestroy()
    {
        if (multiplayerOwner != null && cloneId != null) multiplayerOwner.UnregisterClone(cloneId);
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
                // ▼【修正】target.TakeDamage()を直接呼ぶと、targetがマルチプレイ中(MultiplayerOwner != null)の時に
                // 何もせず握りつぶしてしまう（HPはQueueHit経由のMatchRulesでのみ確定するため）。
                // GiantSpin/LeafShieldと同様にDealDamageTo()を通すことで、オンラインでも正しくダメージが反映されるようにする
                owner.DealDamageTo(target, damage, hitPoint, true, false);
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
