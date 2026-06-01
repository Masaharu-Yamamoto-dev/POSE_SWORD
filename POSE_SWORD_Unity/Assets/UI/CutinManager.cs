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
    public float cutinDuration = 0.8f;         

    // ▼【新規追加】サウンド設定
    [Header("サウンド")]
    public AudioClip cutinSound;     // カットイン専用の音
    private AudioSource audioSource; // スピーカー

    void Awake()
    {
        Instance = this;
        if (cutinCanvasGroup != null) cutinCanvasGroup.alpha = 0f;

        // ▼【新規追加】スピーカーを自動で準備
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
    }

    public void PlayCutin(Sprite sprite, string swordName, string skillName, Color themeColor)
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
            skillNameText.color = themeColor;
        }

        // if (backgroundBar != null && backgroundBar.GetComponent<Image>() != null)
        // {
        //     Color barColor = themeColor * 0.7f;
        //     barColor.a = 0.9f;
        //     backgroundBar.GetComponent<Image>().color = barColor;
        // }

        StopAllCoroutines();
        StartCoroutine(PersonaStyleCutinRoutine());
    }

    IEnumerator PersonaStyleCutinRoutine()
    {
        // ▼【新規追加】カットイン開始と同時に専用の音を鳴らす！
        if (cutinSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(cutinSound);
        }

        // 1. スローモーション＆全体表示
        Time.timeScale = timeScaleDuringCutin;
        cutinCanvasGroup.alpha = 1f;

        backgroundBar.anchoredPosition = new Vector2(1500f, 500f); 
        swordImageRect.anchoredPosition = new Vector2(-1500f, -500f); 
        swordImageRect.localScale = new Vector3(2f, 2f, 1f); 
        
        if (textContainer != null) {
            textContainer.localScale = new Vector3(4f, 4f, 1f); 
            textContainer.gameObject.SetActive(false);
        }

        // 2. 帯と剣が交差するようにシュバッ！と入る
        float timer = 0f;
        float slideInTime = 0.15f; 
        while (timer < slideInTime)
        {
            timer += Time.unscaledDeltaTime;
            float progress = timer / slideInTime;
            float easeOut = 1f - Mathf.Pow(1f - progress, 4f); 

            backgroundBar.anchoredPosition = Vector2.Lerp(new Vector2(1500f, 500f), Vector2.zero, easeOut);
            swordImageRect.anchoredPosition = Vector2.Lerp(new Vector2(-1500f, -500f), Vector2.zero, easeOut);
            swordImageRect.localScale = Vector3.Lerp(new Vector3(2f, 2f, 1f), Vector3.one, easeOut);
            
            yield return null;
        }

        // 3. テキストが「ドンッ！」と叩きつけられる
        if (textContainer != null) {
            textContainer.gameObject.SetActive(true);
            timer = 0f;
            float textSlamTime = 0.1f;
            while (timer < textSlamTime)
            {
                timer += Time.unscaledDeltaTime;
                float progress = timer / textSlamTime;
                textContainer.localScale = Vector3.Lerp(new Vector3(4f, 4f, 1f), Vector3.one, progress);
                yield return null;
            }
            
            BattleCamera cam = Camera.main.GetComponent<BattleCamera>();
            if (cam != null) cam.TriggerShake(0.2f, 0.6f);
        }

        // 4. キメの静止時間
        timer = 0f;
        while (timer < cutinDuration)
        {
            timer += Time.unscaledDeltaTime;
            swordImageRect.anchoredPosition += new Vector2(100f, 50f) * Time.unscaledDeltaTime; 
            backgroundBar.anchoredPosition += new Vector2(-50f, -25f) * Time.unscaledDeltaTime; 
            yield return null;
        }

        // 5. 画面外へシュバッ！と消える
        timer = 0f;
        float slideOutTime = 0.1f;
        while (timer < slideOutTime)
        {
            timer += Time.unscaledDeltaTime;
            float progress = timer / slideOutTime;
            float easeIn = progress * progress * progress; 

            backgroundBar.anchoredPosition = Vector2.Lerp(backgroundBar.anchoredPosition, new Vector2(-1500f, -500f), easeIn); 
            swordImageRect.anchoredPosition = Vector2.Lerp(swordImageRect.anchoredPosition, new Vector2(1500f, 500f), easeIn);
            cutinCanvasGroup.alpha = 1f - progress;
            yield return null;
        }

        // 6. 終了処理
        cutinCanvasGroup.alpha = 0f;
        if (!SwordBattle.matchEnded) 
        {
            Time.timeScale = 1f;
        }
    }
}