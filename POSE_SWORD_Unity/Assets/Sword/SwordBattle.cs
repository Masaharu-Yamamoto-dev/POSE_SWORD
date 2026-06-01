using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using TMPro;

public class SwordBattle : MonoBehaviour
{
    [Header("ステータス")]
    public string swordName = "ダミー剣";
    public int hp = 100;
    private int maxHp; 
    public int attack = 10;

    [Header("UI設定")]
    public Slider hpBar;        // 手前の緑ゲージ
    public Slider delayHpBar;   // ▼【追加】奥の赤ゲージ
    public TextMeshProUGUI nameText; // ▼ 【変更】Text から TextMeshProUGUI に変更
    public TextMeshProUGUI hpText;   // ▼ 【変更】Text から TextMeshProUGUI に変更   

    [Header("物理・ダメージ調整")]
    public float bounceForce = 500f;
    public float impactMultiplier = 0.05f;
    public float maxImpactValue = 40f;
    public float pointyAngleThreshold = 80f;
    public float critThreshold = 3000f; 
    [Header("ダメージポップアップ")]
    public GameObject damagePopupPrefab;

[Header("演出（エフェクト）")]
public ParticleSystem critEffectPrefab;   
public ParticleSystem guardEffectPrefab;  
private SpriteRenderer spriteRenderer;    
private SwordController controller;       

// ▼追加：サウンド用の変数
[Header("サウンド")]
public AudioClip normalHitSound; // 通常ヒット音
public AudioClip critHitSound;   // クリティカル・弱点音
private AudioSource audioSource; // 音を鳴らすスピーカー    
public static bool matchEnded = false;

[Header("必殺技（SPゲージ）設定")]
    public Slider spGaugeBar;          // SPゲージのUI（Slider）
    public TextMeshProUGUI spText;
    public float currentSp = 0f;       // 現在のSP
    public float maxSp = 100f;         // SPの最大値
    public float passiveSpFill = 5f;   // 1秒間に自動で溜まる量
    public float damageSpMultiplier = 0.5f; // 受けたダメージの何倍をSPに変換するか
 
    // 突進状態の管理用
    [HideInInspector] public bool isDashing = false;
    [HideInInspector] public bool isDashShooting = false; // ▼【新規追加】発射したかどうか
    private float dashDamageBonus = 1.0f; // 突進中の追加ダメージ倍率

    // SwordBattle.cs の変数宣言エリアに追加
    // SwordBattle.cs の変数宣言エリアに追加
    [HideInInspector] public Vector2 trueVelocity; // 独自の真の速度
    private Vector2 lastPosition;
    private Rigidbody2D rb;
    private bool isDead = false;

    // ▼【新規追加】カメラが追従するための、画像サイズに左右されない本物の中心座標
    [HideInInspector] public Vector3 currentCenterPosition;

    public static bool isRoundStarted = false; // ★新規追加：ラウンドが開始したか

    void Start()
    {   
        matchEnded = false;
        isRoundStarted = false; // ★開始時は一回 false にする
        rb = GetComponent<Rigidbody2D>();
        controller = GetComponent<SwordController>();
        Transform blade = transform.Find("Blade");
        if (blade != null) spriteRenderer = blade.GetComponent<SpriteRenderer>();
        // ▼追加：スピーカー（AudioSource）を取得、無ければ自動で追加する
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();

        maxHp = hp;
        UpdateUI();
        lastPosition = transform.position;
        
        // ▼【新規追加】初期値は通常の座標にしておく
        currentCenterPosition = transform.position;
    }

    void FixedUpdate()
    {
        trueVelocity = ((Vector2)transform.position - lastPosition) / Time.fixedDeltaTime;
        lastPosition = transform.position;

        // ▼【新規追加】Host側（物理演算が動いている側）は、自分の本物の回転中心を毎フレーム記録する
        if (rb != null && rb.bodyType == RigidbodyType2D.Dynamic)
        {
            if (SwordController.isKomaMode)
            {
                // 物理エンジンが回している中心（worldCenterOfMass）を正確に捉える！
                currentCenterPosition = new Vector3(rb.worldCenterOfMass.x, rb.worldCenterOfMass.y, transform.position.z);
            }
            else
            {
                currentCenterPosition = transform.position;
            }
        }
    }

    // ▼【追加】毎フレーム呼ばれる関数
    void Update()
    {
        if (delayHpBar != null && hpBar != null && delayHpBar.value > hpBar.value)
        {
            delayHpBar.value = Mathf.Lerp(delayHpBar.value, hpBar.value, 5f * Time.unscaledDeltaTime);
            if (delayHpBar.value - hpBar.value < 0.5f) delayHpBar.value = hpBar.value;
        }

        if (!isRoundStarted || isDead || matchEnded) return;

        currentSp += passiveSpFill * Time.deltaTime;
        currentSp = Mathf.Clamp(currentSp, 0f, maxSp);

        if (spGaugeBar != null)
        {
            spGaugeBar.maxValue = maxSp;
            spGaugeBar.value = currentSp;
        }

        if (spText != null)
        {
            spText.text = $"SP: {Mathf.FloorToInt(currentSp)} / {maxSp}";
        }

        // ▼【変更】画面の「右半分」か「左半分」かを判定してジャンプ！
        if (Input.GetMouseButtonDown(0) && controller != null && controller.isLocalControlled)
        {
            // クリックしたX座標が、画面幅の半分より大きければ「右（true）」、小さければ「左（false）」
            bool clickedRight = Input.mousePosition.x > (Screen.width / 2f);
            
            TryAction(clickedRight); 
        }
    }

    // ▼追加：SwordGeneratorから正しいタイミングで呼ばれる初期化関数
    public void SetupStatus(string newName, int newHp, int newAttack)
    {
        swordName = newName;
        hp = newHp;
        maxHp = newHp;
        attack = newAttack;
        UpdateUI();
    }

    public void UpdateUI()
    {
        if (hpBar != null)
        {
            hpBar.maxValue = maxHp;
            hpBar.value = hp;
        }

        // ▼【追加】初期化時（HP満タン時）は赤ゲージも瞬時に合わせる
        if (delayHpBar != null && hp == maxHp)
        {
            delayHpBar.maxValue = maxHp;
            delayHpBar.value = hp;
        }

        if (nameText != null) nameText.text = swordName;
        if (hpText != null) hpText.text = $"{hp} / {maxHp}";
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (isDead) return;
        bool wasDashing = isDashing;
        
        SwordBattle target = collision.gameObject.GetComponent<SwordBattle>();

        // ==========================================
        // ▼【修正1】剣モード：必殺技ダッシュ中の「地形バウンド」
        // ==========================================
        if (!SwordController.isKomaMode && isDashing)
        {
            if (!isDashShooting) return;
            if (target == null)
            {
                // 相手の剣ではなく「壁」や「床」にぶつかった場合はピンボールのように跳ね返る！
                if (rb != null && collision.contacts.Length > 0)
                {
                    Vector2 inDirection = rb.linearVelocity.normalized;
                    Vector2 normal = collision.contacts[0].normal;
                    Vector2 bounceDir = Vector2.Reflect(inDirection, normal);
                    
                    // スピードを維持して反射（最低40fは担保して超高速をキープ）
                    float currentSpeed = Mathf.Max(rb.linearVelocity.magnitude, 40f);
                    rb.linearVelocity = bounceDir * currentSpeed; 
                    
                    // 回転も進行方向に合わせる
                    rb.angularVelocity = Mathf.Sign(bounceDir.x) * -2500f;

                    // 壁に当たった音とエフェクト
                    if (normalHitSound != null) audioSource.PlayOneShot(normalHitSound);
                    if (guardEffectPrefab != null) Instantiate(guardEffectPrefab, collision.contacts[0].point, Quaternion.identity);
                    
                    // 画面も軽く揺らす
                    BattleCamera cam = Camera.main.GetComponent<BattleCamera>();
                    if (cam != null) cam.TriggerShake(0.1f, 0.2f);
                }
                // ダッシュは終わらせず、ここで処理を抜ける（反射し続ける）
                return;
            }
            else
            {
                // 相手の剣に当たった時は突き刺さってダッシュ終了
                EndSwordDash();
            }
        }

        // 相手の剣との衝突
        if (target != null && target != this)
        {
            Collider2D myCollider = collision.otherCollider;
            Collider2D targetCollider = collision.collider;

            // ==========================================
            // ▼【修正2】柄同士の鍔迫り合い（ガキン！と大きく弾く）
            // ==========================================
            bool isHandleClash = myCollider.CompareTag("Handle") && targetCollider.CompareTag("Handle");
            if (isHandleClash && !wasDashing)
            {
                // 火花を出して音を鳴らす
                if (guardEffectPrefab != null) Instantiate(guardEffectPrefab, collision.contacts[0].point, Quaternion.identity);
                if (normalHitSound != null) audioSource.PlayOneShot(normalHitSound);

                if (rb != null)
                {
                    Vector2 clashBounce = (transform.position - target.transform.position).normalized;
                    clashBounce.y += 1.0f; // やや上方向に激しく弾く
                    rb.AddForce(clashBounce * (bounceForce * rb.mass * 1.5f), ForceMode2D.Impulse);
                }
                return; // 鍔迫り合いなのでダメージ計算はせず終了
            }

            if (rb != null)
            {
                if (!wasDashing)
                {
                    Vector2 bounceDir = (transform.position - collision.transform.position).normalized;
                    if (!SwordController.isKomaMode) {
                        bounceDir.y += 0.5f; 
                    }
                    rb.AddForce(bounceDir.normalized * (bounceForce * rb.mass), ForceMode2D.Impulse);
                }
            }

            // ==========================================
            // ▼【修正3】柄ガードのブレイク（必殺技中は柄で当たっても攻撃判定！）
            // ==========================================
            // 自分が「ダッシュ中ではない通常時」だけ、自分の柄が当たった時の攻撃をキャンセルする。
            // つまり、必殺技中は柄だろうが何だろうが問答無用で相手を叩き斬る！
            if (!wasDashing && myCollider.CompareTag("Handle")) return;

            float impact = collision.relativeVelocity.magnitude;
            
            // ===== 以降は既存のコードがそのまま続きます =====
            if (impact > 2.0f)
            {
                float clampedImpact = Mathf.Min(impact, maxImpactValue);
                int damage = Mathf.RoundToInt(clampedImpact * this.attack * impactMultiplier);
                damage = Mathf.Max(damage, 1);

                bool isCrit = false;
                bool isWeakPoint = false;
                Vector2 hitPoint = collision.GetContact(0).point;

                // 相手の剣との衝突

            // ==========================================
            // ▼【新規追加】独楽モード：カウンター（必殺技ブレイク）！
            // ==========================================
            // 自分が竜巻（ダッシュ）中で、相手も小ダッシュ（または竜巻）で突っ込んできた場合
            if (SwordController.isKomaMode && wasDashing && target.isDashing)
            {
                isDashing = false; // 竜巻を強制終了！
                isDashShooting = false;
                
                Debug.Log("💥 カウンター炸裂！相手の突進によって竜巻がブレイクされた！");
                
                // 竜巻を破られたペナルティとして、大きく後方に弾き飛ばされる
                if (rb != null)
                {
                    Vector2 breakBounce = (transform.position - target.transform.position).normalized;
                    rb.AddForce(breakBounce * (bounceForce * rb.mass * 2.0f), ForceMode2D.Impulse);
                }
                
                // 画面を激しく揺らしてブレイク成功を演出
                BattleCamera cam = Camera.main.GetComponent<BattleCamera>();
                if (cam != null) cam.TriggerShake(0.2f, 0.4f);
            }


                if (SwordController.isKomaMode)
                {
                    // 【独楽モード：ダメージ調整】
                    // 独楽は回転による相対速度(impact)が常に高くなりやすく、連続ヒットもするため、
                    // 基礎ダメージの時点で少しデバフ（0.6倍）をかけて剣モードの水準に合わせます。
                    damage = Mathf.Max(Mathf.RoundToInt(damage * 0.6f), 1);

                    Rigidbody2D targetRb = collision.gameObject.GetComponent<Rigidbody2D>();
                    
                    float myMass = rb.mass;
                    float targetMass = (targetRb != null) ? targetRb.mass : 1f;

                    float mySpeed = rb.linearVelocity.magnitude;
                    float targetSpeed = (targetRb != null) ? targetRb.linearVelocity.magnitude : 0f;

                    float myEnergy = myMass * (mySpeed * mySpeed);
                    float targetEnergy = targetMass * (targetSpeed * targetSpeed);

                    bool amIStronger = (myEnergy > targetEnergy);
                    
                    float m = rb.mass;
                    float v = impact; 
                    float kineticEnergy = m * (v * v);

                    float hitRadius = Vector2.Distance(transform.position, hitPoint);
                    float centrifugalBonus = 1.0f + (hitRadius * 0.5f); 

                    float komaPower = kineticEnergy * centrifugalBonus;

                    // ▼【修正】エネルギー倍率の上がり方を緩やかにし、最大でも「1.5倍」までに制限する
                    float powerMultiplier = 1.0f + (komaPower / 20000f); 
                    powerMultiplier = Mathf.Clamp(powerMultiplier, 1.0f, 1.5f); 
                    damage = Mathf.RoundToInt(damage * powerMultiplier);

                    // ▼【修正】ダッシュ時のバグ（/5で減っていた）を修正しつつ、強すぎない1.2倍ボーナスに
                    if (isDashing)
                    {
                        damage = Mathf.RoundToInt(damage * 2f);
                    }

                    bool isSharp = (myCollider is PolygonCollider2D myPoly && IsPointy(hitPoint, myPoly));

                    // ▼【修正】クリティカルの倍率もマイルドに抑え、剣モードに近い削り合いにする
                    if (amIStronger && komaPower > critThreshold && isSharp)
                    {
                        // ①【絶・クリティカル】（約1.8倍）
                        damage = Mathf.RoundToInt(damage * 1.8f);
                        isCrit = true;
                        Debug.Log($"💥 独楽【絶・クリティカル】! 破壊力と鋭利さが合わさった！");
                    }
                    else if (amIStronger && komaPower > critThreshold)
                    {
                        // ②【超破壊】（約1.3倍）
                        damage = Mathf.RoundToInt(damage * 1.3f);
                        isCrit = true;
                        Debug.Log($"💥 独楽【超破壊】! (自エネ: {myEnergy:F0} > 敵エネ: {targetEnergy:F0})");
                    }
                    else if (isSharp)
                    {
                        // ③【鋭利（カウンター）】（約1.3倍）
                        damage = Mathf.RoundToInt(damage * 1.3f);
                        isCrit = true;
                        Debug.Log($"💥 独楽【鋭利】! 一矢報いるカウンター直撃！");
                    }
                    else
                    {
                        // ④ どれも満たさない場合は通常ダメージ
                    }
                }
                else
                {
                    if (wasDashing)
                    {
                        damage = Mathf.RoundToInt(damage * dashDamageBonus);
                        dashDamageBonus = 1.0f; // 使ったらリセットする
                    }
                    if (myCollider is PolygonCollider2D myPoly && IsPointy(hitPoint, myPoly))
                    {
                        damage *= 3;
                        isCrit = true;
                    }

                // 相手の柄（弱点）判定
                    if (targetCollider.CompareTag("Handle"))
                    {
                        damage *= 2;
                        isWeakPoint = true;
                    }
                }
                    // ▼変更：TakeDamageの結果（倒したかどうか）を受け取る
                    bool killedTarget = target.TakeDamage(damage, hitPoint, isCrit, isWeakPoint);

                    // ▼追加：もし自分が勝者になったなら、弾き飛ぶのをキャンセル！
                    if (killedTarget)
                    {
                        // 弾き飛ばされた勢い（横方向の速度）と回転をピタッと止める
                        rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y); 
                        rb.angularVelocity = 0f;
                        
                        // 勝負がついたので、クリックしても動かないように操作を無効化する
                        if (controller != null) controller.enabled = false;
                        
                        Debug.Log("🏆 勝者決定！その場にカッコよく着地します。");
                    }
                }
            }
        }
    
    bool IsPointy(Vector2 hitPointWorld, PolygonCollider2D poly)
    {
        Vector2[] points = poly.points;
        if (points.Length < 3) return false;
        Vector2 hitPointLocal = poly.transform.InverseTransformPoint(hitPointWorld);
        int closestIndex = 0;
        float minDistance = float.MaxValue;
        for (int i = 0; i < points.Length; i++)
        {
            float dist = Vector2.Distance(hitPointLocal, points[i]);
            if (dist < minDistance)
            {
                minDistance = dist;
                closestIndex = i;
            }
        }
        Vector2 prevPoint = points[(closestIndex - 1 + points.Length) % points.Length];
        Vector2 currentPoint = points[closestIndex];
        Vector2 nextPoint = points[(closestIndex + 1) % points.Length];
        Vector2 dir1 = prevPoint - currentPoint;
        Vector2 dir2 = nextPoint - currentPoint;
        return Vector2.Angle(dir1, dir2) < pointyAngleThreshold;
    }

    // ▼変更：void から bool に変更
    public bool TakeDamage(int damage, Vector2 hitPos, bool isCrit, bool isWeakPoint)
    {
        if (isDead) return false; // 変更
        if (isDashing)
        {
            Debug.Log("🛡️ 突進中につき無敵！攻撃を弾いた！");
            // ※もし「キンッ！」という弾き音（パリィ音）があればここで鳴らすと最高です
            // if (parrySound != null) audioSource.PlayOneShot(parrySound);
            return false; 
        }

        float damagePercentage = ((float)damage / maxHp) * 100f; 
        currentSp = Mathf.Clamp(currentSp + (damagePercentage * damageSpMultiplier), 0f, maxSp);

        hp -= damage;
        if (hp < 0) hp = 0;
        UpdateUI();

        // ▼【新規追加】ダメージ数値をポップアップさせる！
        if (damagePopupPrefab != null && damage > 0)
        {
            // 剣がぶつかった位置(hitPos)に数字を出す
            GameObject popup = Instantiate(damagePopupPrefab, hitPos, Quaternion.identity);
            DamagePopup popupScript = popup.GetComponent<DamagePopup>();
            if (popupScript != null) popupScript.Setup(damage, isCrit || isWeakPoint);
        }

        if (hp == 0)
        {
            matchEnded = true;
            // 時間の奪い合いを防ぐため、進行中のヒットストップなどを全て強制キャンセル
            StopAllCoroutines();
            
            // 決着時は確定で派手なクリティカル音を鳴らす
            if (critHitSound != null) audioSource.PlayOneShot(critHitSound);
            
            // ド派手な決着演出コルーチンを開始
            StartCoroutine(DefeatRoutine());
            
            return true; // 相手を倒したことを教える
        }

        if (isCrit || isWeakPoint)
        {
            if (critHitSound != null) audioSource.PlayOneShot(critHitSound);
        }
        else
        {
            if (normalHitSound != null) audioSource.PlayOneShot(normalHitSound);
        }
        if (damage >= 20 || isCrit || isWeakPoint)
        {
            StartCoroutine(HitStopRoutine(0.1f));
            BattleCamera cam = Camera.main.GetComponent<BattleCamera>();
            if (cam != null) cam.TriggerShake(0.1f, 0.3f);

            // ▼【新規追加】ダメージに応じて背景のオーラ境界線を激しく揺らす！
            if (BackgroundManager.Instance != null)
            {
                // ダメージの量に応じて揺れの強さを変える
                BackgroundManager.Instance.TriggerImpact(damage * 0.1f);
            }
        }

        if (isCrit && critEffectPrefab != null) Instantiate(critEffectPrefab, hitPos, Quaternion.identity);
        else if (isWeakPoint && guardEffectPrefab != null) Instantiate(guardEffectPrefab, hitPos, Quaternion.identity);

        if (hp <= 0)
        {
            StartCoroutine(DefeatRoutine());
            return true; // ▼追加：相手を倒したことを教える！
        }

        return false; // ▼追加：まだ倒していない
    }

    IEnumerator HitStopRoutine(float duration)
    {
        if (matchEnded) yield break;
        Time.timeScale = 0.05f; 
        yield return new WaitForSecondsRealtime(duration); 
        if (!isDead && !matchEnded) 
        {
            Time.timeScale = 1f; 
        }
    }

    // ▼変更：ド派手な決着演出
    IEnumerator DefeatRoutine()
    {
        isDead = true;
        if (controller != null) controller.enabled = false;

        // カメラを激しく揺らす！
        BattleCamera cam = Camera.main.GetComponent<BattleCamera>();
        
        if (cam != null)
        {
            cam.StopTracking(); // 追従をストップして位置をロック！
            cam.TriggerShake(1.5f, 1.2f);
        }

        // 剣が黒くなり、コライダーを消してすり抜けるようにする（めり込み防止）
        if (spriteRenderer != null) spriteRenderer.color = new Color(0.2f, 0.2f, 0.2f); 
        Collider2D[] colliders = GetComponentsInChildren<Collider2D>();
        foreach (var col in colliders) col.enabled = false;

        // 天高く吹き飛ばし、超高速で回転させる！
        rb.gravityScale = 2f; 
        rb.linearVelocity = Vector2.zero; // 今の勢いをリセット
        rb.AddForce(new Vector2(Random.Range(-500f, 500f), 2000f), ForceMode2D.Impulse); 
        rb.AddTorque(5000f, ForceMode2D.Impulse); 

        // スローモーション発動
        Time.timeScale = 0.15f; 
        
        // 2.5秒間（現実時間）劇的なスローモーションと画面揺れを見せる
        yield return new WaitForSecondsRealtime(2.5f);

        // ゲーム完全停止
        Time.timeScale = 0f; 
    }

    public void PlayClientDamageEffect(int damage)
    {
        if (isDead || matchEnded) return;

        if (normalHitSound != null) audioSource.PlayOneShot(normalHitSound);

        // ▼【新規追加】通信相手の画面にもダメージ数値を出す
        if (damagePopupPrefab != null && damage > 0)
        {
            // クライアント側は正確なhitPosが分からないので、剣の少し上にランダムにずらして出す
            Vector3 spawnPos = transform.position + (Vector3)Random.insideUnitCircle * 1.5f;
            GameObject popup = Instantiate(damagePopupPrefab, spawnPos, Quaternion.identity);
            DamagePopup popupScript = popup.GetComponent<DamagePopup>();
            
            // クライアント側は通信節約のためクリティカル判定を受け取っていないので、ダメージが20以上なら赤(Crit扱い)にする
            if (popupScript != null) popupScript.Setup(damage, damage >= 20);
        }

        if (damage >= 20)
        {
            // 画面揺れとオーラ揺れをクライアントの画面でも発動！
            BattleCamera cam = Camera.main.GetComponent<BattleCamera>();
            if (cam != null) cam.TriggerShake(0.1f, 0.3f);

            if (BackgroundManager.Instance != null)
            {
                BackgroundManager.Instance.TriggerImpact(damage * 0.1f);
            }
        }
    }
    // ▼【新規追加】突進アクション
    // ▼【変更】SPの量によって技を分岐させる
    // ▼【変更】自分のアクションを実行しつつ、その名前をWebに送る！
    public void TryAction(bool clickedRight = true)
    {
        if (isDead || matchEnded || isDashing) return;

        string actionName = ""; // ★Webに送る用のアクション名

        if (SwordController.isKomaMode)
        {
            if (currentSp >= 70f)
            {
                StartCoroutine(TornadoDashRoutine());
                actionName = "Tornado";
            }
            else if (currentSp >= 20f)
            {
                StartCoroutine(DashRoutine()); 
                actionName = "KomaDash";
            }
        }
        else
        {
            if (currentSp >= maxSp)
            {
                StartCoroutine(SwordDashRoutine());
                actionName = "SwordDash";
            }
            else if (controller != null)
            {
                controller.NetworkJump(clickedRight); 
                actionName = clickedRight ? "JumpRight" : "JumpLeft";
            }
        }

        // ▼【新規追加】自分が操作した時だけ、Web（React）側にアクションを伝える！
        if (controller != null && controller.isLocalControlled && !string.IsNullOrEmpty(actionName))
        {
            InputMessage msg = new InputMessage();
            msg.action = actionName;
            NetworkManager.Instance.SendData("INPUT", JsonUtility.ToJson(msg));
        }
    }

    // ▼【新規追加】通信相手のアクションを「強制発動」させる受信専用関数！
    public void ExecuteRemoteAction(string actionName)
    {
        if (isDead || matchEnded || isDashing) return;

        Debug.Log($"📡 相手からの通信を受信！ 強制発動: {actionName}");

        if (actionName == "Tornado")
        {
            currentSp = 0f; // 強制消費
            StartCoroutine(TornadoDashRoutine());
        }
        else if (actionName == "KomaDash")
        {
            currentSp = 0f;
            StartCoroutine(DashRoutine());
        }
        else if (actionName == "SwordDash")
        {
            currentSp = 0f;
            StartCoroutine(SwordDashRoutine());
        }
        else if (actionName == "JumpRight")
        {
            if (controller != null) controller.NetworkJump(true);
        }
        else if (actionName == "JumpLeft")
        {
            if (controller != null) controller.NetworkJump(false);
        }
    }

    // ▼【修正】独楽モード：牽制の小ダッシュ（SP20〜69）
    // ▼【修正】独楽モード：牽制の小ダッシュ
    IEnumerator DashRoutine()
    {
        isDashing = true;
        if (spriteRenderer != null) spriteRenderer.color = new Color(1f, 0.5f, 0.5f);
        
        float consumedSp = currentSp;
        currentSp = 0f; 

        // ▼【修正】rb.bodyType == RigidbodyType2D.Dynamic を追加（Hostのみ動く）
        if (controller != null && controller.enemyTarget != null && rb != null && rb.bodyType == RigidbodyType2D.Dynamic)
        {
            Vector2 dirToEnemy = (controller.enemyTarget.position - transform.position).normalized;
            rb.AddForce(dirToEnemy * (consumedSp * 1.5f), ForceMode2D.Impulse);
            rb.AddTorque(-consumedSp * 50f, ForceMode2D.Impulse); 
        }

        yield return new WaitForSeconds(0.2f);

        isDashing = false;
        if (spriteRenderer != null) spriteRenderer.color = Color.white;
    }

    // ▼【修正】独楽モード：超必殺「竜巻」
    IEnumerator TornadoDashRoutine()
    {
        isDashing = true;
        float consumedSp = currentSp;
        currentSp = 0f; 

        Debug.Log($"🌪️ 独楽モード：超必殺【竜巻】発動！ (消費SP: {consumedSp:F0})");

        if (CutinManager.Instance != null && spriteRenderer != null)
        {
            CutinManager.Instance.PlayCutin(spriteRenderer.sprite, swordName, "超必殺・竜巻!!", new Color(1f, 0.8f, 0.2f));
        }

        if (spriteRenderer != null) spriteRenderer.color = new Color(1f, 0.8f, 0.2f);

        float duration = 0.8f; 
        float timer = 0f;

        while (timer < duration)
        {
            if (!isDashing || isDead || matchEnded) break;

            // ▼【修正】rb.bodyType == RigidbodyType2D.Dynamic を追加（Hostのみ動く）
            if (controller != null && controller.enemyTarget != null && rb != null && rb.bodyType == RigidbodyType2D.Dynamic)
            {
                Vector2 dirToEnemy = (controller.enemyTarget.position - transform.position).normalized;
                rb.AddForce(dirToEnemy * (rb.mass * 30f), ForceMode2D.Force);
                rb.AddTorque(2000f * rb.mass, ForceMode2D.Force);
            }

            if (Random.value > 0.7f && guardEffectPrefab != null)
            {
                Instantiate(guardEffectPrefab, transform.position + (Vector3)Random.insideUnitCircle * 1.5f, Quaternion.identity);
            }

            timer += Time.deltaTime;
            yield return null; 
        }

        // ▼【修正】ブレーキもHostのみ
        if (rb != null && rb.bodyType == RigidbodyType2D.Dynamic)
        {
            rb.linearVelocity *= 0.5f; 
        }

        isDashing = false;
        if (spriteRenderer != null) spriteRenderer.color = Color.white;
    }

    // ▼【修正】剣モード専用「一直線ジャンプダッシュ」
    IEnumerator SwordDashRoutine()
    {
        isDashing = true;
        isDashShooting = false; 
        
        if (spriteRenderer != null) spriteRenderer.color = new Color(0.5f, 1f, 1f); 

        currentSp = 0f;
        dashDamageBonus = 5.0f; 

        if (CutinManager.Instance != null && spriteRenderer != null)
        {
            CutinManager.Instance.PlayCutin(spriteRenderer.sprite, swordName, "大回転斬りダッシュ!!", new Color(0.5f, 1f, 1f));
        }

        // ▼【修正】1. 小ジャンプの予備動作（Hostのみ）
        if (rb != null && rb.bodyType == RigidbodyType2D.Dynamic)
        {
            rb.linearVelocity = new Vector2(0f, 15f); 
            rb.angularVelocity = 720f; 
        }
        
        yield return new WaitForSeconds(0.2f);

        // ▼【修正】2. 空中で一瞬静止（Hostのみ）
        if (rb != null && rb.bodyType == RigidbodyType2D.Dynamic)
        {
            rb.linearVelocity = Vector2.zero; 
            rb.gravityScale = 0f;            
            rb.angularVelocity = 1440f;
        }

        yield return new WaitForSeconds(0.1f);

        isDashShooting = true; 

        // ▼【修正】3. 敵に向かって一直線に発射！（Hostのみ）
        if (controller != null && controller.enemyTarget != null && rb != null && rb.bodyType == RigidbodyType2D.Dynamic)
        {
            Vector2 dirToEnemy = (controller.enemyTarget.position - transform.position).normalized;
            rb.linearVelocity = dirToEnemy * 50f; 
            
            float spinDir = Mathf.Sign(dirToEnemy.x) * -1f; 
            rb.angularVelocity = spinDir * 2500f; 

            Debug.Log($"⚔️ 剣モード：一直線ダッシュ発動！");
        }

        yield return new WaitForSeconds(2.0f);
        if (isDashing && !SwordController.isKomaMode)
        {
            EndSwordDash();
            dashDamageBonus = 1.0f; 
        }
    }

    // ▼【修正】ダッシュ終了時
    public void EndSwordDash()
    {
        if (!isDashing) return;

        isDashing = false;
        isDashShooting = false; 
        
        if (spriteRenderer != null) spriteRenderer.color = Color.white;
        
        // ▼【修正】停止処理もHostのみ
        if (rb != null && rb.bodyType == RigidbodyType2D.Dynamic)
        {
            rb.linearVelocity = Vector2.zero; 
            rb.angularVelocity = 0f;
            if (controller != null) controller.ApplyPhysicsMode();
        }
        StartCoroutine(DelayEndDashFlag());
    }
    IEnumerator DelayEndDashFlag()
    {
        // Unityの物理演算がこのフレームの処理を終えるまで待機する
        yield return new WaitForFixedUpdate();
        
        // 計算が終わって安全になってから、無敵フラグを解除！
        isDashing = false;
    }
}