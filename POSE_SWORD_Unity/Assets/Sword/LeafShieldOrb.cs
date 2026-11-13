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
// ▼【新規追加】出る時は本体かららせんを描いて輪の上へ広がり出て、効果が切れる時はらせんを描いて本体へ吸い込まれる。
// 動きはホストが付けるので、ゲストの画面にも位置の同期でそのまま出る。割れ方・消え方の演出は SkillEffects.LeafShield
// マルチプレイ中はHost権威側にのみ実体があり、クライアント側の見た目はMultiplayerManagerの
// 分身用SYNC機構（RegisterClone/UnregisterClone/SetCloneColor）で複製される。
public class LeafShieldOrb : MonoBehaviour
{
    const float RetouchInterval = .3f;
    // 耐久が1になった分身の見た目の濃さ（半透明にして見分けられるようにする）
    const float WornAlpha = .75f;
    // 展開：本体から輪の上へ広がり出る時間と、1枚ずつずらす間隔、広がりながら回る量（回転）
    const float DeployTime = .5f, DeployStagger = .06f, DeploySpin = .6f;
    // 効果切れ：本体へ吸い込まれる時間と、1枚ずつずらす間隔、吸い込まれながら回る量
    const float RecallTime = .45f, RecallStagger = .04f, RecallSpin = .5f;

    private SwordBattle owner;
    private float orbitAngleDeg;
    private SpriteRenderer look;
    private Color baseColor;
    private MultiplayerManager multiplayerOwner;
    private string networkId;
    private bool broken;
    private int order;
    private float bornAt, expireAt, recallStartedAt = -1f;
    // 吸い込まれ始めた時の輪の半径と回る速さ。吸い込まれる間は本体の輪の大きさに引きずられないよう、これを使う
    // （効果切れで本体の輪がすぐ普段の大きさへ戻るため、敵を追って広がっている最中だと半径が途中で飛んでガタついていた）
    private float recallRadius, recallSpeed, lastRadius = -1f;
    private LeafShieldEnd endKind = LeafShieldEnd.Break;
    private bool endPlayed;
    private readonly Dictionary<Object, float> lastTouched = new Dictionary<Object, float>();

    public int Durability { get; private set; }

    public void Setup(SwordBattle owner, float startAngleDeg, int durability, float duration, int order = 0)
    {
        this.owner = owner;
        this.order = order;
        orbitAngleDeg = startAngleDeg;
        Durability = Mathf.Max(1, durability);
        look = GetComponent<SpriteRenderer>();
        if (look != null) baseColor = look.color;
        // 本体は、シールドが1枚でも残っている間は受けるダメージが減る（SwordBattle.HasActiveLeafShield）
        owner.RegisterShieldOrb(this);
        bornAt = Time.time;
        expireAt = Time.time + duration;
        transform.position = owner.transform.position;
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
        EndWith(LeafShieldEnd.Break);
    }

    // 割れた・吸い込まれ終わった：消え方の演出を出してから消す。演出は OnDestroy では出さない
    // （場面を閉じる時に一緒に消される時に新しい物を作ると、閉じた場面に残ってしまうため）
    void EndWith(LeafShieldEnd kind)
    {
        if (endPlayed) return;
        endPlayed = true;
        endKind = kind;
        if (owner != null && owner.IsAlive) GetComponent<ILeafShieldLook>()?.PlayEnd(kind);
        Destroy(gameObject);
    }

    // 効果切れ：守るのをやめ（本体のダメージ半減も外れる）、本体へ吸い込まれてから消える
    void BeginRecall()
    {
        if (recallStartedAt >= 0f) return;
        recallStartedAt = Time.time;
        recallRadius = lastRadius > 0f ? lastRadius : owner != null ? owner.LeafShieldOrbitRadiusNow : 0f;
        recallSpeed = owner != null ? owner.LeafShieldOrbitSpeedNow : 0f;
        broken = true;
        if (owner != null) owner.UnregisterShieldOrb(this);
        foreach (var c in GetComponents<Collider2D>()) c.enabled = false;
    }

    void OnDestroy()
    {
        if (owner != null) owner.UnregisterShieldOrb(this);
        // 割れた・効果が切れた時は、消え方もゲストへ送る（本体が倒された・場面を閉じた時などは送らない）
        if (multiplayerOwner != null && networkId != null)
        {
            if (endPlayed && owner != null && owner.IsAlive) multiplayerOwner.UnregisterClone(networkId, (int)endKind, transform.position, 0f);
            else multiplayerOwner.UnregisterClone(networkId);
        }
    }

    void Update()
    {
        if (owner == null || !owner.IsAlive) { Destroy(gameObject); return; }
        if (recallStartedAt < 0f && Time.time >= expireAt) BeginRecall();
        bool recalling = recallStartedAt >= 0f;
        orbitAngleDeg += (recalling ? recallSpeed : owner.LeafShieldOrbitSpeedNow) * Time.deltaTime;
        float radius = recalling ? recallRadius : owner.LeafShieldOrbitRadiusNow, extraTurn = 0f;
        // 展開：本体かららせんを描いて輪の上へ広がり出る
        float deploy = Mathf.Clamp01((Time.time - bornAt - order * DeployStagger) / DeployTime);
        if (deploy < 1f)
        {
            float e = 1f - Mathf.Pow(1f - deploy, 3f);
            radius *= e;
            extraTurn -= (1f - e) * 360f * DeploySpin;
        }
        // 効果切れ：らせんを描いて本体へ吸い込まれ、吸い込まれたら消える
        if (recalling)
        {
            float recall = Mathf.Clamp01((Time.time - recallStartedAt - order * RecallStagger) / RecallTime);
            if (recall >= 1f) { EndWith(LeafShieldEnd.Expire); return; }
            float e = recall * recall * recall;
            radius *= 1f - e;
            extraTurn += e * 360f * RecallSpin;
        }
        if (!recalling) lastRadius = radius;
        float rad = (orbitAngleDeg + extraTurn) * Mathf.Deg2Rad;
        transform.position = owner.transform.position + new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f) * radius;
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
