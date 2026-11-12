using System.Collections.Generic;
using UnityEngine;

// ▼【新規追加】剣モード必殺技「オレ達シールド」(hiltType:"3") の、シールド（分身）1枚分の挙動。
// 本体の周りを回り続け、半径と回る速さは本体が決める（SwordBattle.LeafShieldOrbitRadiusNow / LeafShieldOrbitSpeedNow：
// 一番近い敵へ輪を広げて追っている間は、速く回る）。
// ▼【変更】耐久は回数制（durability。0で割れる）。HP制はやめた。
//   ・敵の剣が触れた：敵に「自分（持ち主）の攻撃力×倍率」を返し（SwordBattle.TryShieldRetaliate）、この分身の耐久が1減る。
//     同じ敵からは RetouchInterval 秒に1回まで（刃と柄が続けて触れても二重に数えない）
//   ・大回転斬が触れた：この分身が割れ、突進は跳ね返って続く（本体にはこの時点では入らない）
//   ・巨大回転斬が触れた：本体より先に触れたなら、この分身が割れて本体は無傷（その巨大回転斬は本体にもう当たらない）
//   ・オレ達アタックの分身が触れた：相打ちで両方消える
// 本体が被弾した時に耐久が減るのは SwordBattle.OnShieldedBodyHit 側。
// マルチプレイ中はHost権威側にのみ実体があり、クライアント側の見た目はMultiplayerManagerの
// 分身用SYNC機構（RegisterClone/UnregisterClone/SetCloneColor）で複製される。
public class LeafShieldOrb : MonoBehaviour
{
    const float RetouchInterval = .3f;
    // 耐久が1になった分身の見た目の濃さ（半透明にして見分けられるようにする）
    const float WornAlpha = .5f;

    private SwordBattle owner;
    private float orbitAngleDeg;
    private SpriteRenderer look;
    private Color baseColor;
    private MultiplayerManager multiplayerOwner;
    private string networkId;
    private bool broken;
    private readonly Dictionary<Object, float> lastTouched = new Dictionary<Object, float>();

    public int Durability { get; private set; }

    public void Setup(SwordBattle owner, float startAngleDeg, int durability, float duration)
    {
        this.owner = owner;
        orbitAngleDeg = startAngleDeg;
        Durability = Mathf.Max(1, durability);
        look = GetComponent<SpriteRenderer>();
        if (look != null) baseColor = look.color;
        // 本体は、シールドが1枚でも残っている間は受けるダメージが減る（SwordBattle.HasActiveLeafShield）
        owner.RegisterShieldOrb(this);
        Destroy(gameObject, duration);
    }

    // ▼ オンライン対戦時のみ呼ばれる。消滅時にMultiplayerManagerへ登録解除し、クライアント側の見た目も消す
    public void SetNetworkId(MultiplayerManager multiplayerOwner, string id)
    {
        this.multiplayerOwner = multiplayerOwner;
        networkId = id;
    }

    // 耐久を減らす。0で割れる。耐久が残っていれば半透明にして、削れていることを見せる
    public void Wear(int amount)
    {
        if (broken || amount <= 0) return;
        Durability -= amount;
        if (Durability <= 0) { Break(); return; }
        if (look != null)
        {
            var c = baseColor;
            c.a = baseColor.a * WornAlpha;
            look.color = c;
            if (multiplayerOwner != null && networkId != null) multiplayerOwner.SetCloneColor(networkId, c);
        }
    }

    public void Break()
    {
        if (broken) return;
        broken = true;
        Durability = 0;
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        if (owner != null) owner.UnregisterShieldOrb(this);
        if (multiplayerOwner != null && networkId != null) multiplayerOwner.UnregisterClone(networkId);
    }

    void Update()
    {
        if (owner == null || !owner.IsAlive) { Destroy(gameObject); return; }
        orbitAngleDeg += owner.LeafShieldOrbitSpeedNow * Time.deltaTime;
        float rad = orbitAngleDeg * Mathf.Deg2Rad;
        transform.position = owner.transform.position + new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f) * owner.LeafShieldOrbitRadiusNow;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (broken || owner == null || !owner.IsAlive) return;

        // オレ達アタックの分身：撃ち出された相手の分身と相打ちで消える（構えている分身や、味方・自分の分身は素通り）
        SwordCloneProjectile clone = other.GetComponent<SwordCloneProjectile>();
        if (clone != null)
        {
            SwordBattle cloneOwner = clone.Owner;
            if (!clone.Launched || cloneOwner == null || cloneOwner == owner || AreTeammates(cloneOwner)) return;
            clone.Vanish();
            Break();
            return;
        }

        SwordBattle target = other.GetComponentInParent<SwordBattle>();
        if (target == null || target == owner || !target.IsAlive || AreTeammates(target)) return;

        // 巨大回転斬：本体より先に分身に触れたら、この分身が割れて本体は無傷（その一回転では本体にもう当たらない）
        if (target.currentDashType == 4)
        {
            if (target.TryUseGiantSpinHitOn(owner)) Break();
            return;
        }

        // 大回転斬：この分身が割れ、突進は跳ね返って続く
        if (target.currentDashType == 3 && target.isDashShooting)
        {
            target.BounceSpinDashOff(transform.position);
            Break();
            return;
        }

        // ふつうに触れた：同じ敵からは RetouchInterval 秒に1回まで
        if (lastTouched.TryGetValue(target, out var touchedAt) && Time.time - touchedAt < RetouchInterval) return;
        lastTouched[target] = Time.time;
        // 敵に返す（無敵の相手には通らないが、耐久は減る）
        owner.TryShieldRetaliate(target, transform.position, owner.attack);
        Wear(1);
    }

    bool AreTeammates(SwordBattle other)
    {
        return owner.MultiplayerOwner != null && owner.MultiplayerOwner.AreTeammates(owner.PlayerId, other.PlayerId);
    }
}
