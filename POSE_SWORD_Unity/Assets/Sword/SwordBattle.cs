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
    private float dashDamageBonus = 1.0f; // 突進中の追加ダメージ倍率

    // SwordBattle.cs の変数宣言エリアに追加
    [HideInInspector] public Vector2 trueVelocity; // 独自の真の速度
    private Vector2 lastPosition;
    private Rigidbody2D rb;
    private bool isDead = false;

    void Start()
    {   
        matchEnded = false;
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
    }

    void FixedUpdate()
    {
        trueVelocity = ((Vector2)transform.position - lastPosition) / Time.fixedDeltaTime;
        lastPosition = transform.position;
    }

    // ▼【追加】毎フレーム呼ばれる関数
    void Update()
    {
        // 赤ゲージが存在し、かつ緑ゲージよりも多い場合（ダメージを受けた直後）
        if (delayHpBar != null && hpBar != null && delayHpBar.value > hpBar.value)
        {
            // Lerp（線形補間）を使って、徐々に減速しながら滑らかに追従させる
            // Time.unscaledDeltaTime を使うことで、ヒットストップで時間が止まっていてもUIは動く！
            delayHpBar.value = Mathf.Lerp(delayHpBar.value, hpBar.value, 5f * Time.unscaledDeltaTime);
            
            // 誤差レベルまで近づいたらピッタリ合わせる
            if (delayHpBar.value - hpBar.value < 0.5f)
            {
                delayHpBar.value = hpBar.value;
            }
        }
        if (!isDead && !matchEnded)
        {
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

            // ▼【変更】独楽モードかどうかの判定を外し、TryActionを呼ぶ！
            if (Input.GetMouseButtonDown(0) && controller != null && controller.isLocalControlled)
            {
                TryAction(); 
            }
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
        if (!SwordController.isKomaMode && isDashing)
        {
            EndSwordDash();
        }
        SwordBattle target = collision.gameObject.GetComponent<SwordBattle>();
        if (target != null && target != this)
        {
            if (rb != null)
            {
                if (!wasDashing)
                {
                    Vector2 bounceDir = (transform.position - collision.transform.position).normalized;
                    
                    // ▼【修正】独楽モード（見下ろし）の時は上（Y軸）に跳ねさせない！
                    if (!SwordController.isKomaMode) {
                        bounceDir.y += 0.5f; 
                    }
                    
                    // ▼【修正】質量（mass）を掛けることで、重い剣同士でも「ガキンッ！」と大きく弾かれる！
                    rb.AddForce(bounceDir.normalized * (bounceForce * rb.mass), ForceMode2D.Impulse);
                }
            }

            Collider2D myCollider = collision.otherCollider;
            Collider2D targetCollider = collision.collider;

            if (myCollider.CompareTag("Handle")) return;

            float impact = collision.relativeVelocity.magnitude;
            if (impact > 2.0f)
            {
                float clampedImpact = Mathf.Min(impact, maxImpactValue);
                int damage = Mathf.RoundToInt(clampedImpact * this.attack * impactMultiplier);
                damage = Mathf.Max(damage, 1);

                bool isCrit = false;
                bool isWeakPoint = false;
                Vector2 hitPoint = collision.GetContact(0).point;

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
                        damage = Mathf.RoundToInt(damage * 1.2f);
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

        // 音を鳴らす（クリティカルの細かい判定は通信量が重くなるので通常音で代用）
        if (normalHitSound != null) audioSource.PlayOneShot(normalHitSound);

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
    IEnumerator DashRoutine()
    {
        isDashing = true;
        if (spriteRenderer != null) spriteRenderer.color = new Color(1f, 0.5f, 0.5f);
        // 現在のSPを全て記録し、ゲージを空にする
        float consumedSp = currentSp;
        currentSp = 0f;

        // SPの量に応じて、突進の「強さ」と「ボーナス倍率」を計算
        // 例: SP100なら、突進力300、ダメージボーナス2倍
        float dashPower = consumedSp; 
        dashDamageBonus = 1.0f + (consumedSp / 100f); 

        // 敵の方向を計算
        if (controller != null && controller.enemyTarget != null)
        {
            Vector2 dirToEnemy = (controller.enemyTarget.position - transform.position).normalized;
            
            // 瞬間的な超加速（Impulse）を与える！
            rb.AddForce(dirToEnemy * dashPower, ForceMode2D.Impulse);
            
            // ついでに回転速度もブーストする
            rb.AddTorque(-dashPower * 50f, ForceMode2D.Impulse); 

            Debug.Log($"🚀 SP消費 {consumedSp:F0} で突進！ ボーナス倍率 {dashDamageBonus:F1}x");
        }

        // SPの量に応じて、突進状態の有効時間を決める（0.2秒 〜 0.5秒など）
        float dashDuration = Mathf.Clamp(consumedSp / 200f, 0.1f, 0.4f);
        yield return new WaitForSeconds(dashDuration);

        if (rb != null)
        {
            rb.linearVelocity *= 0.1f; // 移動速度を10%まで一気に落とす
            rb.angularVelocity *= 0.3f; // 上がりすぎた回転速度も落ち着かせる
        }

        isDashing = false;
        dashDamageBonus = 1.0f;
        if (spriteRenderer != null) spriteRenderer.color = Color.white;
    }

    // ▼【新規追加】外部（自分自身やNetworkManager）からダッシュを試みる関数
    // ▼【変更】TryDash をやめて、モードごとに動きを変える TryAction に！
    public void TryAction()
    {
        if (isDead || matchEnded || isDashing) return;

        if (SwordController.isKomaMode)
        {
            // 【独楽モード】SPが20以上なら小出しに突進できる
            if (currentSp >= 20f)
            {
                StartCoroutine(DashRoutine()); // 既存の突進
            }
        }
        else
        {
            // 【剣モード】SPがMAX（100）の時だけ、次のクリックが「必殺ダッシュ」になる！
            if (currentSp >= maxSp)
            {
                StartCoroutine(SwordDashRoutine());
            }
            else if (controller != null)
            {
                // SPが足りない場合は、今まで通りの「通常ジャンプ」
                controller.NetworkJump(); 
            }
        }
    }

    // ▼【新規追加】剣モード専用のド派手な「大回転斬りダッシュ」
    // ▼【変更】剣モード専用「一直線ジャンプダッシュ」
    IEnumerator SwordDashRoutine()
    {
        isDashing = true;
        
        if (spriteRenderer != null) spriteRenderer.color = new Color(0.5f, 1f, 1f); 

        currentSp = 0f;
        dashDamageBonus = 3.0f; // ダメージ2倍ボーナス！

        // 1. 小ジャンプの予備動作（ふわっと浮く）
        if (rb != null)
        {
            rb.linearVelocity = new Vector2(0f, 15f); // 上向きの速度だけを与える
            rb.angularVelocity = 720f; // 1秒間に2回転
        }
        
        // 0.2秒ほど浮き上がるのを待つ
        yield return new WaitForSeconds(0.2f);

        // 2. 空中で一瞬静止（タメ演出＆一直線の準備）
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero; // ピタッと止まる
            rb.gravityScale = 0f;             // 重力を切る
            
            // ▼ ここでさらに回転を上げて、発射直前の「ギュイィィン！」という感じを出します
            rb.angularVelocity = 1440f;
        }

        // 0.1秒タメる
        yield return new WaitForSeconds(0.1f);

        // 3. 敵に向かって一直線に発射！
        if (controller != null && controller.enemyTarget != null && rb != null)
        {
            Vector2 dirToEnemy = (controller.enemyTarget.position - transform.position).normalized;
            
            // 速度（Velocity）を直接設定して、重さに関係なく弾丸のように真っ直ぐ飛ばす！
            rb.linearVelocity = dirToEnemy * 50f; // ★速すぎる場合はこの数値を下げてください
            
            float spinDir = Mathf.Sign(dirToEnemy.x) * -1f; // 進む方向に合わせた回転
            rb.angularVelocity = spinDir * 2500f; // ★数値を上げるとより激しく回ります

            Debug.Log($"⚔️ 剣モード：一直線ダッシュ発動！");
        }

        // 4. 最大2秒経っても何にも当たらなかったら自動で解除する（宇宙の彼方へ飛んでいくのを防ぐ）
        yield return new WaitForSeconds(2.0f);
        if (isDashing && !SwordController.isKomaMode)
        {
            EndSwordDash();
            dashDamageBonus = 1.0f; // ダメージボーナスも元に戻す
        }
    }

    // ▼【新規追加】剣モードのダッシュを強制終了させる関数
    public void EndSwordDash()
    {
        if (!isDashing) return;

        isDashing = false;
        
        if (spriteRenderer != null) spriteRenderer.color = Color.white;
        
        if (rb != null)
        {
            // 速度と回転をゼロにして空中でピタッと止める
            rb.linearVelocity = Vector2.zero; 
            rb.angularVelocity = 0f;
            
            // 重力を元のモード（剣モードなら重力あり）に戻す
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