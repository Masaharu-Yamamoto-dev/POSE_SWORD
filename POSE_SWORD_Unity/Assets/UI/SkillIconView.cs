using UnityEngine;
using UnityEngine.UI;

// ▼【新規追加】必殺技ボタンの見た目。SPの溜まり具合を丸いゲージで見せ、撃てる時だけ明るくして後ろを光らせる。
// 画像は Assets/UI/SkillIcons の層ごとのPNG(どれも同じ大きさで、丸の中心が画像の真ん中)を、
// 同じ大きさのImageに重ねて使う。重ね順(下から): glow → ring_track → ring → overRing → body。
// overRingは1vs3のボスだけが使う2周目のゲージ。通常必殺技のライン(100)を越えてから掌握斬(200)までの
// 溜まり具合を、掌握斬の色で1周目の上から塗り重ねて見せる
// 配置と画像の割り当てはメニュー「POSE SWORD/Setup Skill Icon Button」で行う(SkillIconSetup.cs)。
// どの技を出すか・どれだけ溜まっているかはSwordBattle.UpdateSpecialAttackButton()が毎フレーム渡す
public class SkillIconView : MonoBehaviour
{
    [System.Serializable]
    public class SkillSprites
    {
        public string key; // spin / giant / attack / shield / tornado / grasp（SwordBattle.SkillIconKeyと合わせる）
        public Sprite ring;
        public Sprite body;
        public Sprite bodyCharge;
        public Sprite glow;
    }

    public Image glowImage;
    public Image trackImage;
    public Image ringImage; // Filled / Radial 360 / 上から時計回り。fillAmountにSPの割合を入れる
    public Image overRingImage; // ringImageと同じ設定。2周目のゲージ(使わない時はfillAmount=0で隠す)
    public Image bodyImage;
    public Image burstImage; // 撃てるようになった時に外へ広がって消える輪。今の技のゲージ色の画像を使う
    [Tooltip("弾ませる・脈打たせる時に拡大縮小する親(SkillIcon)。ボタン本体の大きさ(タップ範囲)は変えない")]
    public RectTransform scaleRoot;
    public SkillSprites[] skills;

    [Header("撃てる時の光")]
    [Tooltip("撃てる時に後ろの光が明滅する速さ")]
    public float glowPulseSpeed = 4f;
    [Tooltip("明滅の一番暗い時の不透明度(一番明るい時は1)")]
    [Range(0f, 1f)] public float glowMinAlpha = 0.45f;
    [Tooltip("明滅に合わせて光を広げる量(1で広げない)")]
    public float glowMaxScale = 1.15f;

    [Header("撃てるようになった瞬間の演出")]
    [Tooltip("弾んだ時の一番大きい倍率")]
    public float punchScale = 1.3f;
    [Tooltip("弾む時間(秒)")]
    public float punchDuration = 0.35f;
    [Tooltip("撃てる間、ゆっくり脈打つ大きさ(0.06で6%)")]
    public float breatheAmount = 0.06f;
    [Tooltip("脈打つ速さ")]
    public float breatheSpeed = 5f;
    [Tooltip("広がる輪の最終的な大きさ")]
    public float burstScale = 1.7f;
    [Tooltip("輪が広がって消えるまでの時間(秒)")]
    public float burstDuration = 0.6f;
    [Tooltip("撃てる間、輪をもう一度広げる間隔(秒)。0以下で最初の1回だけ")]
    public float burstInterval = 1.8f;
    [Tooltip("2回目以降の輪の濃さ(最初の1回は1)")]
    [Range(0f, 1f)] public float burstRepeatAlpha = 0.55f;

    string currentKey;
    bool currentReady;
    SkillSprites current;
    string currentOverKey;
    float readySince;
    float burstStart = float.NegativeInfinity;
    float burstAlpha;
    bool scaled; // 撃てない間に毎フレーム大きさを書き直さないための目印

    RectTransform Root => scaleRoot != null ? scaleRoot
        : bodyImage != null ? bodyImage.rectTransform.parent as RectTransform : null;

    // overKey/overFillは2周目のゲージ。overKeyの技のゲージ色で、overFill(0〜1)の分だけ1周目の上から塗る
    public void Show(string key, float fill, bool ready, string overKey = null, float overFill = 0f)
    {
        if (overRingImage != null)
        {
            if (overKey != currentOverKey)
            {
                currentOverKey = overKey;
                var over = overKey != null ? Find(overKey) : null;
                overRingImage.sprite = over != null ? over.ring : null;
            }
            // スプライトが無いImageは白い四角で描かれるので、使わない時はImageごと切る
            bool on = overRingImage.sprite != null && overFill > 0f;
            if (overRingImage.enabled != on) overRingImage.enabled = on;
            if (on && !Mathf.Approximately(overRingImage.fillAmount, overFill)) overRingImage.fillAmount = overFill;
        }

        if (key != currentKey)
        {
            currentKey = key;
            current = Find(key);
            if (current != null)
            {
                if (ringImage != null) ringImage.sprite = current.ring;
                if (glowImage != null) glowImage.sprite = current.glow;
            }
            currentReady = !ready; // 下で必ず本体の画像を差し替えさせる
        }
        if (current == null) return;

        if (ringImage != null && !Mathf.Approximately(ringImage.fillAmount, fill)) ringImage.fillAmount = fill;
        if (ready != currentReady)
        {
            currentReady = ready;
            if (bodyImage != null) bodyImage.sprite = ready ? current.body : current.bodyCharge;
            if (glowImage != null) glowImage.enabled = ready;
            // 撃てるようになった瞬間(と、ボスが掌握斬に切り替わった瞬間)に弾ませて輪を広げる
            if (ready)
            {
                readySince = Time.unscaledTime;
                StartBurst(1f);
            }
        }
    }

    void StartBurst(float alpha)
    {
        if (burstImage == null || current == null) return;
        burstImage.sprite = current.ring;
        burstStart = Time.unscaledTime;
        burstAlpha = alpha;
    }

    SkillSprites Find(string key)
    {
        if (skills == null) return null;
        foreach (var s in skills)
            if (s != null && s.key == key) return s;
        return null;
    }

    // カットイン中はtimeScaleが落ちるので、それに引きずられないよう演出はすべてunscaledTimeで動かす
    void Update()
    {
        float now = Time.unscaledTime;
        UpdateBurst(now);

        var root = Root;
        if (!currentReady)
        {
            if (scaled)
            {
                scaled = false;
                if (root != null) root.localScale = Vector3.one;
                if (glowImage != null) glowImage.rectTransform.localScale = Vector3.one;
            }
            return;
        }
        scaled = true;

        // 最初に一度大きく弾み、そのあとはゆっくり脈打ち続ける
        float t = now - readySince;
        float scale;
        if (t < punchDuration) scale = 1f + (punchScale - 1f) * Mathf.Sin(t / punchDuration * Mathf.PI);
        else scale = 1f + breatheAmount * (0.5f - 0.5f * Mathf.Cos((t - punchDuration) * breatheSpeed));
        if (root != null) root.localScale = new Vector3(scale, scale, 1f);

        if (glowImage != null)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(now * glowPulseSpeed);
            var c = glowImage.color;
            c.a = Mathf.Lerp(glowMinAlpha, 1f, pulse);
            glowImage.color = c;
            float g = Mathf.Lerp(1f, glowMaxScale, pulse);
            glowImage.rectTransform.localScale = new Vector3(g, g, 1f);
        }

        if (burstInterval > 0f && now - burstStart >= burstInterval) StartBurst(burstRepeatAlpha);
    }

    void UpdateBurst(float now)
    {
        if (burstImage == null) return;
        float p = burstDuration > 0f ? (now - burstStart) / burstDuration : 1f;
        bool on = p < 1f && burstImage.sprite != null;
        if (burstImage.enabled != on) burstImage.enabled = on;
        if (!on) return;
        float ease = 1f - (1f - p) * (1f - p); // 出だしが速く、外へ行くほどゆっくり
        float s = Mathf.Lerp(1f, burstScale, ease);
        burstImage.rectTransform.localScale = new Vector3(s, s, 1f);
        var c = burstImage.color;
        c.a = burstAlpha * (1f - p);
        burstImage.color = c;
    }
}
