using UnityEngine;

// ▼【新規追加】剣モード必殺技「リーフシールド」(hiltType:"3") の、シールド1枚分の挙動。
// 本体の周りを一定半径・速度で回り続け、相手の剣に触れると受けたダメージのreflectMultiplier倍を
// 相手に返しつつ、自分もそのダメージぶんを受ける（＝被弾するほど強く反撃する）。
// HPが尽きる、または持続時間が切れると消える。
// マルチプレイ中はHost権威側にのみ実体があり、クライアント側の見た目はMultiplayerManagerの
// 分身用SYNC機構（RegisterClone/UnregisterClone）で複製される。
public class LeafShieldOrb : MonoBehaviour
{
    private SwordBattle owner;
    private float orbitRadius;
    private float orbitSpeedDeg;
    private float orbitAngleDeg;
    private float reflectMultiplier;
    private float hp;
    private MultiplayerManager multiplayerOwner;
    private string networkId;

    public void Setup(SwordBattle owner, float startAngleDeg, float orbitRadius, float orbitSpeedDeg,
        float hp, float reflectMultiplier, float duration)
    {
        this.owner = owner;
        orbitAngleDeg = startAngleDeg;
        this.orbitRadius = orbitRadius;
        this.orbitSpeedDeg = orbitSpeedDeg;
        this.hp = hp;
        this.reflectMultiplier = reflectMultiplier;
        // ▼【新規追加】このシールドが生きている間、本体は通常ダメージを受けない(SwordBattle.HasActiveLeafShield)。
        // このColliderはisTriggerで攻撃側の移動を止めないため、無敵状態にしないと反射した上に本体まで刺さっていた
        owner.activeLeafShieldCount++;
        Destroy(gameObject, duration);
    }

    // ▼ オンライン対戦時のみ呼ばれる。消滅時にMultiplayerManagerへ登録解除し、クライアント側の見た目も消す
    public void SetNetworkId(MultiplayerManager multiplayerOwner, string id)
    {
        this.multiplayerOwner = multiplayerOwner;
        networkId = id;
    }

    void OnDestroy()
    {
        if (owner != null) owner.activeLeafShieldCount = Mathf.Max(0, owner.activeLeafShieldCount - 1);
        if (multiplayerOwner != null && networkId != null) multiplayerOwner.UnregisterClone(networkId);
    }

    void Update()
    {
        if (owner == null || !owner.IsAlive) { Destroy(gameObject); return; }
        orbitAngleDeg += orbitSpeedDeg * Time.deltaTime;
        float rad = orbitAngleDeg * Mathf.Deg2Rad;
        transform.position = owner.transform.position + new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f) * orbitRadius;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        SwordBattle target = other.GetComponentInParent<SwordBattle>();
        if (target == null || target == owner || !target.IsAlive) return;

        // ▼【変更】受けたダメージ（相手の攻撃力）のreflectMultiplier倍を相手に返す
        int receivedDamage = Mathf.Max(target.attack, 1);
        int reflectedDamage = Mathf.Max(Mathf.RoundToInt(receivedDamage * reflectMultiplier), 1);
        owner.DealDamageTo(target, reflectedDamage, transform.position);

        hp -= receivedDamage;
        if (hp <= 0f) Destroy(gameObject);
    }
}
