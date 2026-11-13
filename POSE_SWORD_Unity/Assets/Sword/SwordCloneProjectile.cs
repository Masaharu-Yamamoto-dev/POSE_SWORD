using UnityEngine;

// ▼【新規追加】剣モード必殺技「オレ達アタック」(hiltType:"2") の、分身1体分の挙動
// 相手の剣に当たるとダメージを与えて消滅し、壁・床など相手の剣以外に当たった場合もその場で消滅する。
// ▼【変更】本体が溜めてから、本体の位置で1体ずつ作り、すぐ撃ち出す（FireNow）。以前の「持ち場へ飛び出して漂い、
// 構えて、身を引いてから撃つ」予備動作はやめた（3体が同時に動いてごちゃつき、頭上にリフトがあると撃った直後に当たって消えていたため）。
// 撃ち出した後しばらくは、曲がる速さに上限をつけて相手の方へ向きを寄せる（小さく速く動けば振り切れる）。
// 撃った後に狙った相手が倒れたら、次の相手が進む向きの前方（SwordBattle.cloneRetargetCone 度以内）にいる時だけ向きを変える。
// 見た目の演出（撃った所の輪・軌跡・飛沫など）は SkillEffects.CloneRush が出す
public class SwordCloneProjectile : MonoBehaviour
{
    private SwordBattle owner;
    private bool dealsDamage;
    private int damage;
    private bool hasActed;
    private MultiplayerManager multiplayerOwner;
    private string cloneId;
    private Rigidbody2D body;
    private Collider2D hitArea;
    private bool launched;
    private Transform target;
    private SwordBattle targetSword;
    private float speed, turnRate, homingUntil, launchedAt, homingSeconds, lifeAfterLaunch;
    // この時刻を過ぎたら消える
    private float vanishAt;

    // 演出用の段階。オンライン対戦ではゲストへ同期する（MultiplayerManager）
    public CloneRushPhase Phase { get; private set; } = CloneRushPhase.None;
    // 演出（ホストの分身。ゲストは MultiplayerManager が見た目の分身に付ける）
    public ICloneRushLook Look { get; set; }
    // 消え方（演出とゲストへの同期に使う）
    private CloneRushEnd endKind = CloneRushEnd.Fade;
    private Vector2 flyDirection;
    private Vector2 endPosition;
    private float endAngle;

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
        body = GetComponent<Rigidbody2D>();
        // 撃つまでは当たり判定を持たない（すり抜ける）。撃った瞬間から当たる。
        // 無効化(enabled=false)ではなくトリガーにしておくのは、本体や他の分身とのIgnoreCollisionを効かせたままにするため
        hitArea = GetComponent<Collider2D>();
        if (hitArea != null) hitArea.isTrigger = true;
        vanishAt = Time.time + lifeTime;
    }

    public bool Launched => launched;
    // 撃ち出した位置（本体の位置）
    public Vector2 LaunchedFrom { get; private set; }
    public SwordBattle Owner => owner;

    // オレ達シールドの分身と相打ちになった時など：その場で小さく弾けて消える
    public void Vanish()
    {
        End(CloneRushEnd.Burst);
    }

    void End(CloneRushEnd kind) { End(kind, transform.position); }

    // at：消える場所。相手に当たった時は接触点（軌跡をそこまで伸ばし、刃先がそこへ刺さって見えるようにする）
    void End(CloneRushEnd kind, Vector2 at)
    {
        hasActed = true;
        endKind = kind;
        endPosition = at;
        // ぶつかった後の速さは衝突で変わっているので、直前の飛ぶ向きを使う
        endAngle = flyDirection.sqrMagnitude > .0001f
            ? Mathf.Atan2(flyDirection.y, flyDirection.x) * Mathf.Rad2Deg
            : transform.eulerAngles.z + 90f;
        Destroy(gameObject);
    }

    // すぐ撃ち出す。target がいなければ direction へまっすぐ
    public void FireNow(Transform aimAt, Vector2 direction, float launchSpeed, float homingTurnRate, float homingSeconds, float lifeTime)
    {
        if (launched || hasActed) return;
        target = aimAt;
        speed = launchSpeed;
        turnRate = homingTurnRate;
        this.homingSeconds = homingSeconds;
        lifeAfterLaunch = lifeTime;
        if (direction.sqrMagnitude > .0001f) transform.rotation = FacingRotation(direction);
        Fire();
    }

    void Fire()
    {
        launched = true;
        launchedAt = Time.time;
        LaunchedFrom = transform.position;
        targetSword = target != null ? target.GetComponentInParent<SwordBattle>() : null;
        homingUntil = Time.time + homingSeconds;
        vanishAt = Time.time + lifeAfterLaunch;
        if (body != null)
        {
            body.position = transform.position;
            body.rotation = transform.eulerAngles.z;
            body.bodyType = RigidbodyType2D.Dynamic;
            body.gravityScale = 0f;
        }
        // 本体の位置から撃つので壁と重なることはまずないが、念のため、飛び出す瞬間に重なっている壁とは当たらないようにする
        if (hitArea != null)
        {
            foreach (Collider2D overlapped in Physics2D.OverlapCircleAll(transform.position, WorldRadius))
                if (SwordBattle.IsWallForClones(overlapped)) Physics2D.IgnoreCollision(hitArea, overlapped, true);
            // 撃った瞬間から当たる。この時点で相手と重なっていれば、次の物理ステップでそのまま当たる
            hitArea.isTrigger = false;
        }
        Vector2 direction = target != null ? (Vector2)target.position - (Vector2)transform.position : (Vector2)transform.up;
        if (direction.sqrMagnitude < .0001f) direction = transform.up;
        direction.Normalize();
        if (body != null) body.linearVelocity = direction * speed;
        flyDirection = direction * speed;
        transform.rotation = FacingRotation(direction);
        Phase = CloneRushPhase.Fly;
        Look?.SetPhase(Phase);
    }

    void Update()
    {
        if (Time.time >= vanishAt) End(CloneRushEnd.Fade);
    }

    void FixedUpdate()
    {
        if (body == null || !launched) return;

        Vector2 velocity = body.linearVelocity;
        if (velocity.sqrMagnitude < .0001f) return;
        // ▼【新規追加】狙った相手が倒れた：次の相手が進む向きの前方にいる時だけ、そちらへ向きを変える。それ以外は真っすぐ
        if (target != null && targetSword != null && !targetSword.IsAlive)
        {
            target = null;
            Transform next = owner != null ? owner.PickCloneTarget() : null;
            if (next != null && Vector2.Angle(velocity, (Vector2)next.position - body.position) <= owner.cloneRetargetCone)
            {
                target = next;
                targetSword = next.GetComponentInParent<SwordBattle>();
                homingUntil = Time.time + owner.cloneRetargetTime;
                turnRate = owner.cloneRetargetTurnRate;
            }
        }
        // 追尾：決めた時間だけ、相手の方へ1ステップあたり turnRate×時間 までしか曲がらない
        if (Time.time < homingUntil && target != null && (targetSword == null || targetSword.IsAlive))
        {
            Vector2 toTarget = (Vector2)target.position - body.position;
            float maxTurn = turnRate * Time.fixedDeltaTime;
            float turn = Mathf.Clamp(Vector2.SignedAngle(velocity, toTarget), -maxTurn, maxTurn);
            velocity = (Vector2)(Quaternion.Euler(0f, 0f, turn) * velocity);
        }
        body.linearVelocity = velocity.normalized * speed;
        transform.rotation = FacingRotation(body.linearVelocity);
        flyDirection = body.linearVelocity;
    }

    // 分身の当たり判定の、見た目の大きさ込みの半径（分身は手持ちの剣に合わせて拡大縮小されている）
    float WorldRadius => hitArea is CircleCollider2D circle
        ? circle.radius * Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y))
        : .6f;

    // 剣の画像は切っ先が上を向いているので、上向きを direction に合わせる
    static Quaternion FacingRotation(Vector2 direction)
    {
        if (direction.sqrMagnitude < .0001f) return Quaternion.identity;
        return Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f);
    }

    void OnDestroy()
    {
        Vector2 at = hasActed ? endPosition : (Vector2)transform.position;
        // 消える演出は、分身が自分で消えた時（End を通った時）だけ出す。場面を閉じる時などに一緒に消される時は出さない
        // （OnDestroy で新しい物を作ると、閉じた場面に残ってしまうため）
        if (hasActed) Look?.PlayEnd(endKind, at, endAngle);
        if (multiplayerOwner != null && cloneId != null) multiplayerOwner.UnregisterClone(cloneId, endKind, at, endAngle);
    }

    // ▼【新規追加】巨大回転斬の剣は技の間だけ当たり判定がトリガーになり、分身がすり抜けていた。
    // 巨大回転中の相手の剣に触れた分身は、ダメージを与えずに消える（味方の巨大回転斬では消えない）
    void OnTriggerEnter2D(Collider2D other)
    {
        if (hasActed || !launched) return;
        SwordBattle sword = other.GetComponentInParent<SwordBattle>();
        if (sword == null || sword == owner || sword.currentDashType != 4) return;
        if (owner != null && owner.MultiplayerOwner != null && owner.MultiplayerOwner.AreTeammates(owner.PlayerId, sword.PlayerId)) return;
        End(CloneRushEnd.Burst);
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (hasActed) return;

        SwordBattle target = collision.gameObject.GetComponentInParent<SwordBattle>();
        if (target != null && target != owner)
        {
            Vector2 hitPoint = collision.contactCount > 0 ? collision.GetContact(0).point : (Vector2)transform.position;
            if (dealsDamage)
            {
                // ▼【修正】target.TakeDamage()を直接呼ぶと、targetがマルチプレイ中(MultiplayerOwner != null)の時に
                // 何もせず握りつぶしてしまう（HPはQueueHit経由のMatchRulesでのみ確定するため）。
                // GiantSpin/LeafShieldと同様にDealDamageTo()を通すことで、オンラインでも正しくダメージが反映されるようにする
                // オレ達アタック：分身が実際に減らしたHPの一部を本体が回復する（ライフスティール）
                owner.DealDamageTo(target, damage, hitPoint, true, false, false, owner.cloneLifestealRate);
            }
            End(CloneRushEnd.Hit, hitPoint);
            return;
        }

        if (target == null)
        {
            // 壁・床など、相手の剣以外に当たったら消える
            Debug.Log($"💨 分身が壁・床に当たって消えました: {collision.collider.name}, " +
                (launched ? $"撃ってから{Time.time - launchedAt:F2}秒" : "撃つ前") + $", 位置={transform.position}");
            End(CloneRushEnd.Burst);
        }
    }
}
