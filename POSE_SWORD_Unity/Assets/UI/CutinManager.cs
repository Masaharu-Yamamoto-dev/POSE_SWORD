using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using TMPro;

public class CutinManager : MonoBehaviour
{
    public static CutinManager Instance;

    [Header("UI参照")]
    public CanvasGroup cutinCanvasGroup; 
    public RectTransform backgroundBar;  
    public RectTransform swordImageRect; 
    public Image swordImage;             
    public RectTransform textContainer;  
    public TextMeshProUGUI swordNameText;
    public TextMeshProUGUI skillNameText;

    [Header("演出設定")]
    public float timeScaleDuringCutin = 0.05f; 
    // ▼【変更】キメ（帯と剣が入ってから抜けるまで）の秒数。技名が止まって見えるのは、これとほぼ同じ約0.9秒
    // （以前は約0.6秒で、誰が何を撃ったか読み切る前に消えていた）
    public float cutinDuration = 0.95f;

    // ▼【新規追加】見やすさの調整。デザイン（傾いた帯・剣の画像・剣の名前・技名）はそのまま
    [Header("見やすさ")]
    public float swordImageOffsetX = -170f;  // 剣の画像を左へ寄せて、技名に重ならないようにする
    public Color textOutlineColor = Color.black; // 剣の名前と技名の縁取り。帯と技の色が近くても読めるように
    public float skillNameOutlineWidth = 0f;   // 縁取りの太さ（0で縁取りなし）
    public float swordNameOutlineWidth = 0f;
    public Color skillNameShadowColor = new Color(0.08f, 0.07f, 0.1f, 0.45f); // 技名の影（後ろに同じ文字を1枚重ねる）
    public Vector2 skillNameShadowOffset = new Vector2(5f, -5f);

    // ▼【新規追加】サウンド設定
    [Header("サウンド")]
    public AudioClip cutinSound;     // カットイン専用の音
    private AudioSource audioSource; // スピーカー

    // ▼【新規追加】必殺技の中身をカットインが抜けてから始めるための目印。
    // PlayCutin のたびに番号を進め、その番号のカットインが終わった（または次のカットインに
    // 置き換えられた）かを IsFinished で聞けるようにする
    private int playSerial;
    private bool playing;
    // このカットインが Time.timeScale を下げている間 true。ヒットストップなどが
    // 終わり際に等速へ戻す時、カットインのスローまで解いてしまわないように見る
    public bool IsSlowingTime { get; private set; }

    // 技名の影（技名を複製して後ろに置く）。TextMeshProの影(Underlay)は、ビルドで削られる機能を使うため使わない
    private TextMeshProUGUI skillNameShadow;
    private Vector2 swordNameBasePosition, skillNameBasePosition;
    private Color skillColor = Color.white;

    void Awake()
    {
        Instance = this;
        if (cutinCanvasGroup != null) cutinCanvasGroup.alpha = 0f;
        SetupTextLook();

        // ▼【新規追加】スピーカーを自動で準備
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
    }

    // ▼【変更】playerColorを追加：BackgroundBarは技の色(themeColor)ではなく、プレイヤーのメインカラー(P1赤/P2青/P3黄/P4緑)で塗る
    // ▼【新規追加】useSlowMotion：Time.timeScaleを使ったスローモーションを適用するかどうか。
    // マルチプレイ中はホストの物理シミュレーション頻度まで一緒に下がって全員の試合が止まってしまうため、
    // オンライン対戦時はfalseを渡し、演出（画像・テキスト・シェイク・効果音）だけを等速で再生する
    // 戻り値は、このカットインの番号（IsFinished に渡す）
    public int PlayCutin(Sprite sprite, string swordName, string skillName, Color themeColor, Color playerColor, bool useSlowMotion = true)
    {
        if (swordImage != null)
        {
            swordImage.sprite = sprite;
            swordImage.preserveAspect = true;
        }

        if (swordNameText != null) swordNameText.text = swordName;
        if (skillNameText != null)
        {
            skillNameText.text = skillName;
            skillColor = themeColor;
            skillNameText.color = themeColor;
        }
        if (skillNameShadow != null) skillNameShadow.text = skillName;

        if (backgroundBar != null && backgroundBar.GetComponent<Image>() != null)
        {
            Color barColor = playerColor * 0.7f;
            barColor.a = 0.9f;
            backgroundBar.GetComponent<Image>().color = barColor;
        }

        // ▼【修正】直前のカットインが演出の途中（Time.timeScaleを変更した状態）で強制中断されると、
        // 後始末のコード（6.終了処理）が一切実行されずtimeScaleが遅いまま固まってしまう。
        // 新しいカットインを始める前に、念のため等速へ戻しておく
        StopAllCoroutines();
        IsSlowingTime = false;
        if (Time.timeScale != 1f && !SwordBattle.matchEnded) Time.timeScale = 1f;
        playSerial++;
        playing = true;
        StartCoroutine(PersonaStyleCutinRoutine(useSlowMotion));
        return playSerial;
    }

    // ▼【新規追加】文字の見え方を整える：剣の名前と技名に灰色の縁取り、技名の後ろに影を1枚。
    // 文字は剣の画像より手前に描く（剣の画像を左へ寄せても、重なった所で文字が隠れないように）
    void SetupTextLook()
    {
        if (textContainer != null) textContainer.SetAsLastSibling();
        if (swordNameText != null)
        {
            swordNameBasePosition = swordNameText.rectTransform.anchoredPosition;
            swordNameText.outlineColor = textOutlineColor;
            swordNameText.outlineWidth = swordNameOutlineWidth;
        }
        if (skillNameText == null) return;
        skillNameBasePosition = skillNameText.rectTransform.anchoredPosition;
        skillNameShadow = Instantiate(skillNameText, skillNameText.transform.parent);
        skillNameShadow.name = "CutinSkillNameShadow";
        skillNameShadow.transform.SetSiblingIndex(skillNameText.transform.GetSiblingIndex());
        skillNameShadow.rectTransform.anchoredPosition = skillNameBasePosition + skillNameShadowOffset;
        skillNameShadow.color = skillNameShadowColor;
        // 影も縁取りの分だけ太らせ、技名の形どおりに後ろへ落ちて見えるようにする
        skillNameShadow.outlineColor = skillNameShadowColor;
        skillNameShadow.outlineWidth = skillNameOutlineWidth;
        skillNameText.outlineColor = textOutlineColor;
        skillNameText.outlineWidth = skillNameOutlineWidth;
    }

    // 演出の途中で止められた時（シーンの片付けなど）も、待っている必殺技が止まったままにならないようにする
    void OnDisable()
    {
        playing = false;
        IsSlowingTime = false;
    }

    // ▼【新規追加】外から再生中のカットインを打ち切る。StopAllCoroutinesを直接呼ぶと
    // 「スロー中」「再生中」の印が立ったまま残り、ヒットストップ後に等速へ戻らなくなるため、こちらを使う。
    // Time.timeScaleは呼び出し側で戻す
    public void StopCutin()
    {
        StopAllCoroutines();
        playing = false;
        IsSlowingTime = false;
        if (cutinCanvasGroup != null) cutinCanvasGroup.alpha = 0f;
    }

    // ▼【新規追加】serial のカットインが終わったか。後から別のカットインに置き換えられた場合も終わった扱いにする
    // （置き換えた側のカットインを待つと、同時に撃った必殺技がいつまでも始まらなくなるため）
    public bool IsFinished(int serial)
    {
        return serial != playSerial || !playing;
    }

    IEnumerator PersonaStyleCutinRoutine(bool useSlowMotion)
    {
        // ▼【新規追加】カットイン開始と同時に専用の音を鳴らす！
        if (cutinSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(cutinSound);
        }

        // 1. スローモーション＆全体表示
        // ▼【修正】ここが決着シーンの一撃でなくても、matchEndedが既にtrue（＝他の対戦で
        // 試合がもう終わっている）ならスローを開始しない。開始してしまうと、このカットインの
        // 終了処理（6.）はmatchEnded中はtimeScaleを戻さない仕様のため、そのままスローで固まってしまう
        if (useSlowMotion && !SwordBattle.matchEnded)
        {
            Time.timeScale = timeScaleDuringCutin;
            IsSlowingTime = true;
        }
        cutinCanvasGroup.alpha = 1f;
        if (textContainer != null)
        {
            textContainer.gameObject.SetActive(true);
            textContainer.localScale = Vector3.one;
        }

        // ▼【変更】見やすさのための流れ（デザインはそのまま）。時間はすべて実時間：
        //   0〜0.15秒   帯と剣が反対側から滑り込む（今までどおり）
        //   0.05〜0.15秒 技名を帯の入りに重ねて出し、1.8倍から等倍へ（以前は帯が入りきってから4倍から）
        //   0.1〜0.18秒  剣の名前が少し遅れて出る
        //   キメ（cutinDuration）：帯は止めたまま（以前は流れていた）。剣の画像だけゆっくり流れる
        //   最後の0.15秒：帯・剣・文字がまとめて抜ける
        const float slideInTime = 0.15f, slideOutTime = 0.15f;
        const float skillAppearAt = 0.05f, skillSettleTime = 0.1f, nameAppearAt = 0.1f, nameFadeTime = 0.08f;
        Vector2 swordRest = new Vector2(swordImageOffsetX, 0f);
        Vector2 bandFrom = new Vector2(1500f, 500f), bandTo = new Vector2(-1500f, -500f);
        Vector2 swordFrom = swordRest + new Vector2(-1500f, -500f), swordTo = swordRest + new Vector2(1500f, 500f);
        Vector2 swordDrift = new Vector2(60f, 30f); // キメの間、剣の画像だけがゆっくり流れる速さ（毎秒）
        float outStart = slideInTime + cutinDuration, total = outStart + slideOutTime;
        bool shook = false;
        float timer = 0f;
        while (timer < total)
        {
            timer += Time.unscaledDeltaTime;
            float t = Mathf.Min(timer, total);

            // 帯：入ったらその場で止める
            if (t < slideInTime)
                backgroundBar.anchoredPosition = Vector2.Lerp(bandFrom, Vector2.zero, EaseOut4(t / slideInTime));
            else if (t < outStart)
                backgroundBar.anchoredPosition = Vector2.zero;
            else
                backgroundBar.anchoredPosition = Vector2.Lerp(Vector2.zero, bandTo, EaseIn3((t - outStart) / slideOutTime));

            // 剣の画像：左へ寄せた位置へ入り、キメの間はゆっくり流れる
            Vector2 swordHeld = swordRest + swordDrift * Mathf.Max(0f, Mathf.Min(t, outStart) - slideInTime);
            if (t < slideInTime)
            {
                float e = EaseOut4(t / slideInTime);
                swordImageRect.anchoredPosition = Vector2.Lerp(swordFrom, swordRest, e);
                swordImageRect.localScale = Vector3.Lerp(new Vector3(2f, 2f, 1f), Vector3.one, e);
            }
            else
            {
                swordImageRect.localScale = Vector3.one;
                swordImageRect.anchoredPosition = t < outStart ? swordHeld : Vector2.Lerp(swordHeld, swordTo, EaseIn3((t - outStart) / slideOutTime));
            }

            // 技名：帯の入りに重ねて早めに出し、1.8倍から等倍へ
            if (skillNameText != null)
            {
                float k = Mathf.Clamp01((t - skillAppearAt) / skillSettleTime);
                float alpha = Mathf.Clamp01(k * 3f);
                Vector3 scale = Vector3.one * Mathf.Lerp(1.8f, 1f, EaseOut4(k));
                Color c = skillColor; c.a = skillColor.a * alpha;
                skillNameText.color = c;
                skillNameText.rectTransform.localScale = scale;
                if (skillNameShadow != null)
                {
                    Color sc = skillNameShadowColor; sc.a *= alpha;
                    skillNameShadow.color = sc;
                    skillNameShadow.rectTransform.localScale = scale;
                }
            }
            // 剣の名前：少し遅れて、横から少し滑りながら出る
            if (swordNameText != null)
            {
                float a = Mathf.Clamp01((t - nameAppearAt) / nameFadeTime);
                Color c = swordNameText.color; c.a = a;
                swordNameText.color = c;
                swordNameText.rectTransform.anchoredPosition = swordNameBasePosition + new Vector2((1f - a) * 20f, 0f);
            }
            // 技名が落ち着いたところで、今までどおり画面を揺らす
            if (!shook && t >= skillAppearAt + skillSettleTime)
            {
                shook = true;
                BattleCamera cam = Camera.main != null ? Camera.main.GetComponent<BattleCamera>() : null;
                if (cam != null) cam.TriggerShake(0.2f, 0.6f);
            }
            // 抜ける時は全体を薄くする
            if (t >= outStart) cutinCanvasGroup.alpha = 1f - Mathf.Clamp01((t - outStart) / slideOutTime);
            yield return null;
        }

        // 終了処理
        cutinCanvasGroup.alpha = 0f;
        IsSlowingTime = false;
        if (useSlowMotion && !SwordBattle.matchEnded)
        {
            Time.timeScale = 1f;
        }
        playing = false;
    }

    static float EaseOut4(float t) { t = Mathf.Clamp01(t); return 1f - Mathf.Pow(1f - t, 4f); }
    static float EaseIn3(float t) { t = Mathf.Clamp01(t); return t * t * t; }
}