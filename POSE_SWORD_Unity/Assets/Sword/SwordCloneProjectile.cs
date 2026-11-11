using UnityEngine;

// ▼【新規追加】剣モード必殺技「分身突進」(hiltType:"2") の、分身1体分の挙動
// 相手の剣に当たるとダメージを与えて消滅し、壁・床など相手の剣以外に当たった場合もその場で消滅する
// ▼【変更】出てすぐは飛ばず、構えてから本体の合図（Launch）で1体ずつ撃ち出される。撃ち出した後しばらくは、
// 曲がる速さに上限をつけて相手の方へ向きを寄せる（小さく速く動けば振り切れる）。
// 構えている間は眷属らしく動かす：本体の位置から持ち場へ飛び出して生まれ（BeginHold）、本体について漂い、
// 1体ずつ時間差で相手へ振り向き（BeginAim）、撃つ直前に身を引いてから飛び出す（Launch）。
// 構えている間の動きは描画ごと(Update)に動かしてなめらかにする（物理の刻みで動かすとカクついて見えるため）。
// 壁は、本体から見て壁の向こうへ回り込まないよう分身の大きさの円で確かめ、重なりそうな分だけ外へ押し出して壁沿いに滑らせる
// （止めると壁際で止まっては跳ぶように見えるため）。撃つ瞬間に物理の位置を合わせる
public class SwordCloneProjectile : MonoBehaviour
{
    const float EmergeTime = .12f;        // 本体の位置から持ち場へ飛び出す時間（少し行き過ぎて戻る）
    const float BobHeight = .15f;         // 持ち場で漂う上下の幅（横にはその半分）
    const float BobSpeed = 1.4f;          // 漂う速さ（往復/秒）
    const float SwayAngle = 4f;           // 振り向く前の、刃先の小さな揺らぎ（度）
    const float FollowTime = .1f;         // 本体が動いた時に追いつくまでのおよその時間（ばねのようになめらかに追う）
    const float PullbackTime = .08f;      // 撃つ直前に身を引く時間
    const float PullbackDistance = .4f;   // 身を引く距離
    const float WallMargin = .05f;        // 壁との間に空けておく隙間
    const float HeldGrace = 3f;

    private SwordBattle owner;
    private bool dealsDamage;
    private int damage;
    private bool hasActed;
    private MultiplayerManager multiplayerOwner;
    private string cloneId;
    private Rigidbody2D body;
    private Collider2D hitArea;
    private Quaternion heldRotation;
    private bool launched;
    private Transform target;
    private SwordBattle targetSword;
    private float speed, turnRate, homingUntil, launchedAt;
    // この時刻を過ぎたら消える。撃ち出す前は、本体が撃ち出せなくなった時の保険として長めにとる
    private float vanishAt;

    // 構えている間
    private bool holding;
    private Vector2 slotOffset;   // 本体から見た持ち場の位置
    private Vector2 emergeFrom;   // 生まれた場所（本体の位置）
    private float heldTime, bobPhase;
    private Vector2 followVelocity;
    private bool aiming;
    private Transform aimTarget;
    private float aimElapsed, aimDuration, aimDelay, aimStartedAt;
    // 撃つ直前の身を引く動きと、その後に撃つ時の設定
    private bool windingUp;
    private float windupElapsed, homingSeconds, lifeAfterLaunch;
    private Vector2 windupFrom, windupTo;

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
        heldRotation = transform.rotation;
        // ▼【新規追加】構えている間は当たり判定を持たない（すり抜ける）。撃った瞬間から当たる。
        // 無効化(enabled=false)ではなくトリガーにしておくのは、本体や他の分身とのIgnoreCollisionを効かせたままにするため
        hitArea = GetComponent<Collider2D>();
        if (hitArea != null) hitArea.isTrigger = true;
        // 何にも当たらなかった時に消えるまでの時間（lifeTime）は、撃ち出した時から数える
        vanishAt = Time.time + lifeTime + HeldGrace;
    }

    public bool Launched => launched;

    // 生まれる：今いる場所（本体の位置）から、本体に対して slotOffset の持ち場へ飛び出し、以後は本体について漂う。
    // order は何体目か（漂う動きをずらすのに使う）
    public void BeginHold(Vector2 offsetFromOwner, int order)
    {
        holding = true;
        slotOffset = offsetFromOwner;
        emergeFrom = transform.position;
        heldTime = 0f;
        bobPhase = order * 2.1f;
        // 構えている間は物理に押されず、こちらで動かす
        if (body != null) { body.linearVelocity = Vector2.zero; body.bodyType = RigidbodyType2D.Kinematic; }
    }

    // 振り向く：delay 秒待ってから、duration の終わりまでに相手へ向き、少し回りすぎてから止まる。以後は相手を向き続ける
    public void BeginAim(Transform aimAt, float duration, float delay)
    {
        aiming = true;
        aimTarget = aimAt;
        aimElapsed = 0f;
        aimStartedAt = heldTime;
        aimDuration = duration;
        aimDelay = Mathf.Clamp(delay, 0f, duration);
    }

    // 撃ち出す合図。身を引いてから（PullbackTime後に）飛び出す。狙いは飛び出す瞬間の相手の位置で取り直す。
    // lifeTime は飛び出した時から数える
    public void Launch(Transform aimAt, float launchSpeed, float homingTurnRate, float homingSeconds, float lifeTime)
    {
        if (launched || hasActed || windingUp) return;
        target = aimAt;
        speed = launchSpeed;
        turnRate = homingTurnRate;
        this.homingSeconds = homingSeconds;
        lifeAfterLaunch = lifeTime;
        if (body == null) { Fire(); return; }
        Vector2 from = transform.position;
        Vector2 toTarget = aimAt != null ? (Vector2)aimAt.position - from : (Vector2)transform.up;
        Vector2 back = toTarget.sqrMagnitude > .0001f ? -toTarget.normalized : Vector2.zero;
        windupFrom = from;
        // 身を引く先も、分身の大きさで壁を確かめ、壁の手前で止める（大きい分身が壁に触れて、撃った瞬間に消えないように）
        windupTo = PushOutOfWalls(ClampAgainstWalls(from, from + back * PullbackDistance));
        windupElapsed = 0f;
        windingUp = true;
        if (!holding) BeginHold((Vector2)transform.position - (owner != null ? (Vector2)owner.transform.position : (Vector2)transform.position), 0);
    }

    void Fire()
    {
        windingUp = false;
        holding = false;
        launched = true;
        launchedAt = Time.time;
        targetSword = target != null ? target.GetComponentInParent<SwordBattle>() : null;
        homingUntil = Time.time + homingSeconds;
        vanishAt = Time.time + lifeAfterLaunch;
        if (body != null)
        {
            // 構えている間は描画ごとに動かしていたので、物理の位置と向きをここで合わせる
            body.position = transform.position;
            body.rotation = transform.eulerAngles.z;
            body.bodyType = RigidbodyType2D.Dynamic;
            body.gravityScale = 0f;
        }
        // 本来は壁を避けて動かしているが、念のため、飛び出す瞬間に重なっている壁とは当たらないようにする
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
        transform.rotation = FacingRotation(direction);
    }

    void Update()
    {
        // 撃ち出す前に本体が倒された（撃ち出す処理ごと止まった）時は、その場に残さず消す
        if (!launched && (owner == null || !owner.IsAlive)) { Destroy(gameObject); return; }
        if (Time.time >= vanishAt) { Destroy(gameObject); return; }
        if (!launched) UpdateHeld(Time.deltaTime);
    }

    void FixedUpdate()
    {
        if (body == null || !launched) return;

        Vector2 velocity = body.linearVelocity;
        if (velocity.sqrMagnitude < .0001f) return;
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
    }

    void UpdateHeld(float dt)
    {
        if (!holding || owner == null || dt <= 0f) return;
        heldTime += dt;
        UpdateAim();

        Vector2 current = transform.position;
        if (windingUp)
        {
            windupElapsed += dt;
            float k = Mathf.Clamp01(windupElapsed / PullbackTime);
            transform.position = Vector2.Lerp(windupFrom, windupTo, 1f - (1f - k) * (1f - k));
            if (k >= 1f) Fire();
            return;
        }

        // 持ち場：本体に対する位置に、上下（と少し横）に漂う動きを足す。本体から見て壁の手前まで、壁と重なる分は押し出す
        Vector2 ownerCentre = owner.transform.position;
        float phase = heldTime * BobSpeed * Mathf.PI * 2f + bobPhase;
        Vector2 bob = new Vector2(Mathf.Cos(phase * .5f) * BobHeight * .5f, Mathf.Sin(phase) * BobHeight);
        Vector2 slot = PushOutOfWalls(ClampAgainstWalls(ownerCentre, ownerCentre + slotOffset + bob));

        Vector2 next = heldTime < EmergeTime
            ? Vector2.LerpUnclamped(emergeFrom, slot, EaseOutBack(heldTime / EmergeTime))
            : Vector2.SmoothDamp(current, slot, ref followVelocity, FollowTime, Mathf.Infinity, dt);
        transform.position = PushOutOfWalls(next);
    }

    void UpdateAim()
    {
        Quaternion sway = Quaternion.Euler(0f, 0f, Mathf.Sin(heldTime * BobSpeed * Mathf.PI * 2f + bobPhase + 1f) * SwayAngle);
        if (!aiming) { transform.rotation = heldRotation * sway; return; }
        aimElapsed = heldTime - aimStartedAt;
        Vector2 at = aimTarget != null ? (Vector2)aimTarget.position : (Vector2)transform.position + (Vector2)transform.up;
        float span = aimDuration - aimDelay;
        float local = span > .001f ? Mathf.Clamp01((aimElapsed - aimDelay) / span) : 1f;
        Quaternion facing = FacingRotation(at - (Vector2)transform.position);
        // 振り向き始めるまでは揺らいでいて、向き終わったら相手をまっすぐ見据える
        transform.rotation = Quaternion.SlerpUnclamped(heldRotation * sway, facing, EaseOutBack(local));
    }

    // 壁と重なっている分だけ、壁の外へ押し出す（壁沿いに滑るように見える）
    Vector2 PushOutOfWalls(Vector2 position)
    {
        float radius = WorldRadius + WallMargin;
        for (int pass = 0; pass < 3; pass++)
        {
            bool moved = false;
            foreach (Collider2D wall in Physics2D.OverlapCircleAll(position, radius))
            {
                if (!SwordBattle.IsWallForClones(wall)) continue;
                Vector2 closest = wall.ClosestPoint(position);
                Vector2 away = position - closest;
                float distance = away.magnitude;
                if (distance > .0001f)
                {
                    if (distance < radius) { position += away / distance * (radius - distance); moved = true; }
                    continue;
                }
                // 中心が壁の中に入っている時は、壁の外枠のうち一番近い辺の外へ出す
                Bounds b = wall.bounds;
                float left = position.x - b.min.x, right = b.max.x - position.x, down = position.y - b.min.y, up = b.max.y - position.y;
                float least = Mathf.Min(Mathf.Min(left, right), Mathf.Min(down, up));
                if (least == up) position.y = b.max.y + radius;
                else if (least == down) position.y = b.min.y - radius;
                else if (least == left) position.x = b.min.x - radius;
                else position.x = b.max.x + radius;
                moved = true;
            }
            if (!moved) break;
        }
        return position;
    }

    // from から to へ分身の大きさの円を動かした時に、壁に当たる手前で止めた位置。
    // 動き始めの時点で既に重なっている壁（距離0の当たり）は、行き先の重なりで別に確かめるのでここでは数えない
    Vector2 ClampAgainstWalls(Vector2 from, Vector2 to)
    {
        Vector2 delta = to - from;
        float distance = delta.magnitude;
        if (distance < .0001f) return to;
        Vector2 direction = delta / distance;
        float allowed = distance;
        foreach (RaycastHit2D hit in Physics2D.CircleCastAll(from, WorldRadius, direction, distance))
        {
            if (hit.distance <= 0f || !SwordBattle.IsWallForClones(hit.collider)) continue;
            allowed = Mathf.Min(allowed, Mathf.Max(0f, hit.distance - WallMargin));
        }
        return from + direction * allowed;
    }

    // 分身の当たり判定の、見た目の大きさ込みの半径（分身は手持ちの剣に合わせて拡大縮小されている）
    float WorldRadius => hitArea is CircleCollider2D circle
        ? circle.radius * Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y))
        : .6f;

    static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }

    // 剣の画像は切っ先が上を向いているので、上向きを direction に合わせる
    static Quaternion FacingRotation(Vector2 direction)
    {
        if (direction.sqrMagnitude < .0001f) return Quaternion.identity;
        return Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f);
    }

    void OnDestroy()
    {
        if (multiplayerOwner != null && cloneId != null) multiplayerOwner.UnregisterClone(cloneId);
    }

    // ▼【新規追加】巨大回転斬の剣は技の間だけ当たり判定がトリガーになり、分身がすり抜けていた。
    // 巨大回転中の相手の剣に触れた分身は、ダメージを与えずに消える（味方の巨大回転斬では消えない）
    void OnTriggerEnter2D(Collider2D other)
    {
        // 構えている間は何にも当たらない（巨大回転斬に触れても消えない）
        if (hasActed || !launched) return;
        SwordBattle sword = other.GetComponentInParent<SwordBattle>();
        if (sword == null || sword == owner || sword.currentDashType != 4) return;
        if (owner != null && owner.MultiplayerOwner != null && owner.MultiplayerOwner.AreTeammates(owner.PlayerId, sword.PlayerId)) return;
        hasActed = true;
        Destroy(gameObject);
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
                // オレ達アタック：分身が実際に減らしたHPの一部を本体が回復する（ライフスティール）
                owner.DealDamageTo(target, damage, hitPoint, true, false, false, owner.cloneLifestealRate);
            }
            Destroy(gameObject);
            return;
        }

        if (target == null)
        {
            // 壁・床など、相手の剣以外に当たったら消える
            Debug.Log($"💨 分身が壁・床に当たって消えました: {collision.collider.name}, " +
                (launched ? $"撃ってから{Time.time - launchedAt:F2}秒" : "撃つ前") + $", 位置={transform.position}");
            hasActed = true;
            Destroy(gameObject);
        }
    }
}
