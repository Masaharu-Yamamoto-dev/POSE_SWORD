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

        SwordBattle target = collision.gameObject.GetComponent<SwordBattle>();
        if (target != null && target != this)
        {
            if (rb != null)
            {
                Vector2 bounceDir = (transform.position - collision.transform.position).normalized;
                bounceDir.y += 0.5f; 
                rb.AddForce(bounceDir.normalized * bounceForce, ForceMode2D.Impulse);
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
}