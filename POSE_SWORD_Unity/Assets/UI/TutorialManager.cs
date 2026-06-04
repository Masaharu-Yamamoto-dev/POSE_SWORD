using UnityEngine;
using TMPro;

public class TutorialManager : MonoBehaviour
{
    [Header("UIオブジェクトの紐付け")]
    [Tooltip("背景の黒い帯 (BackgroundBar) をアタッチ")]
    public GameObject backgroundBarObj;         
    [Tooltip("通常チュートリアルテキスト (TutorialMessage) をアタッチ")]
    public GameObject tutorialMessageObj;       
    [Tooltip("必殺技アラートテキスト (SpecialAttackMessage) をアタッチ")]
    public GameObject specialAttackMessageObj;  

    [Header("テロップのディレイ（余韻）設定")]
    [Tooltip("文字が完全に画面外に隠れてから処理を行うための追加マージン（ピクセル）")]
    public float exitMargin = 150f; 

    // ▼【トリプル化】通常チュートリアル用の3つのテキスト
    private TextMeshProUGUI tutorialText1;
    private TextMeshProUGUI tutorialText2;
    private TextMeshProUGUI understandText3; // ※既存の変数名重複回避のため、tutorialText3として扱います
    private TextMeshProUGUI tutorialText3;

    // 必殺技アラート用の3つのテキスト
    private TextMeshProUGUI specialText1;
    private TextMeshProUGUI specialText2;
    private TextMeshProUGUI specialText3;

    private SwordBattle playerBattle; // 自分が操作するローカルプレイヤーの監視用
    
    // 制御用タイマー・フラグ
    private float noInputTimer = 0f;
    private float scrollSpeed = 120f;
    
    private bool sp20Triggered = false;
    private bool sp40Triggered = false;
    private bool sp60Triggered = false;
    private bool isShowingUltimate = false;

    void Start()
    {
        // ▼【新規拡張】通常チュートリアルテキストを完全自動で3つにトリプル化！
        if (tutorialMessageObj != null)
        {
            tutorialText1 = tutorialMessageObj.GetComponent<TextMeshProUGUI>();
            
            GameObject cloneT2 = Instantiate(tutorialMessageObj, tutorialMessageObj.transform.parent);
            cloneT2.name = "TutorialMessage_Clone2";
            tutorialText2 = cloneT2.GetComponent<TextMeshProUGUI>();

            GameObject cloneT3 = Instantiate(tutorialMessageObj, tutorialMessageObj.transform.parent);
            cloneT3.name = "TutorialMessage_Clone3";
            tutorialText3 = cloneT3.GetComponent<TextMeshProUGUI>();
        }
        
        // 必殺技テキストを完全自動で3つにトリプル化
        if (specialAttackMessageObj != null)
        {
            specialText1 = specialAttackMessageObj.GetComponent<TextMeshProUGUI>();
            
            GameObject clone2 = Instantiate(specialAttackMessageObj, specialAttackMessageObj.transform.parent);
            clone2.name = "SpecialAttackMessage_Clone2";
            specialText2 = clone2.GetComponent<TextMeshProUGUI>();

            GameObject clone3 = Instantiate(specialAttackMessageObj, specialAttackMessageObj.transform.parent);
            clone3.name = "SpecialAttackMessage_Clone3";
            specialText3 = clone3.GetComponent<TextMeshProUGUI>();
        }

        // ゲーム開始時はBarも文字もすべて初期化（非表示）
        if (backgroundBarObj != null) backgroundBarObj.SetActive(false);
        if (tutorialText1 != null) tutorialText1.text = "";
        if (tutorialText2 != null) tutorialText2.text = "";
        if (tutorialText3 != null) tutorialText3.text = "";
        if (specialText1 != null) specialText1.text = "";
        if (specialText2 != null) specialText2.text = "";
        if (specialText3 != null) specialText3.text = "";

        FindLocalPlayer();
    }

    void Update()
    {
        if (playerBattle == null)
        {
            FindLocalPlayer();
            if (playerBattle == null) return; 
        }

        // 【共通ルール】必殺技（ダッシュ）が発動した瞬間にすべてをリセット・消去
        if (playerBattle.isDashing)
        {
            ClearAllTickers();
            sp20Triggered = false;
            sp40Triggered = false;
            sp60Triggered = false;
            isShowingUltimate = false;
            noInputTimer = 0f;
            return;
        }

        // 各ゲームモード別のチュートリアルロジックを実行
        if (SwordController.isKomaMode)
        {
            HandleKomaModeTutorial();
        }
        else
        {
            HandleSwordModeTutorial();
        }

        // ▼【ロジック差し替え】通常チュートリアルも3本の列車で永久ループ移動！
        ScrollNormalTextTriple(scrollSpeed);

        // 必殺技テキストの3本の列車永久ループ
        ScrollSpecialTextTriple(350f);

        // 画面に流れる文字が何もなくなったら、Bar自体を全自動で非表示にする
        UpdateBarVisibility();
    }

    void FindLocalPlayer()
    {
        if (NetworkManager.Instance == null) return;

        GameObject mySwordObj = NetworkManager.Instance.isHost ? NetworkManager.Instance.hostSword : NetworkManager.Instance.clientSword;

        if (mySwordObj != null)
        {
            var battleComp = mySwordObj.GetComponent<SwordBattle>();
            var controllerComp = mySwordObj.GetComponent<SwordController>();

            if (battleComp != null && controllerComp != null && controllerComp.isLocalControlled)
            {
                playerBattle = battleComp;
                Debug.Log($"🎯 TutorialManager: 操作対象「{battleComp.swordName}」の監視を開始しました。");
            }
        }
    }

    // ⚔️ 剣モードのロジック
    void HandleSwordModeTutorial()
    {
        if (playerBattle.currentSp >= playerBattle.maxSp)
        {
            if (!isShowingUltimate)
            {
                ClearNormalTickers(); // 通常テキストは隠す
                string ultMessage = "　　必殺技打てます！　　必殺技打てます！　　必殺技打てます！　　必殺技打てます！　　　";
                SetSpecialTickerTripleText(ultMessage);
                isShowingUltimate = true;
            }
            return;
        }
        else
        {
            if (isShowingUltimate)
            {
                ClearSpecialTickers();
                isShowingUltimate = false;
            }
        }

        if (Input.GetMouseButtonDown(0))
        {
            noInputTimer = 0f;
            ClearNormalTickers(); // ⭕ 操作が入ったら通常チュートリアル車両をすべて消去！
        }
        else
        {
            if (string.IsNullOrEmpty(tutorialText1.text) && !isShowingUltimate)
            {
                noInputTimer += Time.deltaTime;
                if (noInputTimer >= 5f)
                {
                    scrollSpeed = 130f; 
                    // ⭕ トリプル形式で文字をセット！操作があるまで無限に回ります
                    SetNormalTickerTripleText("クリック/タップで移動　画面左右で移動方向が変わります");
                }
            }
        }
    }

    // 🌀 独楽モードのロジック
    void HandleKomaModeTutorial()
    {
        if (playerBattle.currentSp >= 70f)
        {
            if (!isShowingUltimate)
            {
                ClearNormalTickers(); // 通常テキストは隠す
                string ultMessage = "　　必殺技打てます！　　必殺技打てます！　　必殺技打てます！　　必殺技打てます！　　　";
                SetSpecialTickerTripleText(ultMessage);
                isShowingUltimate = true;
            }
            return;
        }
        else
        {
            if (isShowingUltimate)
            {
                ClearSpecialTickers();
                isShowingUltimate = false;
            }
        }

        float sp = playerBattle.currentSp;

        if (sp >= 60f && !sp60Triggered)
        {
            TriggerKomaAlert();
            sp60Triggered = true;
        }
        else if (sp >= 40f && sp < 60f && !sp40Triggered)
        {
            TriggerKomaAlert();
            sp40Triggered = true;
        }
        else if (sp >= 20f && sp < 40f && !sp20Triggered)
        {
            TriggerKomaAlert();
            sp20Triggered = true;
        }

        if (Input.GetMouseButtonDown(0))
        {
            ClearNormalTickers(); // ⭕ 操作が入ったら消去
        }
    }

    void TriggerKomaAlert()
    {
        if (isShowingUltimate) return; 
        scrollSpeed = 150f;
        SetNormalTickerTripleText("クリック/タップでSPを消費して牽制できます");
    }

    // ▼【新規拡張】通常チュートリアル用のトリプル初期位置セット（右端外側から綺麗に3連連結）
    void SetNormalTickerTripleText(string content)
    {
        if (tutorialText1 == null || tutorialText2 == null || tutorialText3 == null) return;

        tutorialText1.text = content;
        tutorialText2.text = content;
        tutorialText3.text = content;

        if (backgroundBarObj != null) backgroundBarObj.SetActive(true);

        float barWidth = GetBarWidth();
        float textWidth = tutorialText1.preferredWidth;

        float startX = (barWidth / 2f) + (textWidth / 2f);
        
        tutorialText1.rectTransform.anchoredPosition = new Vector2(startX, tutorialText1.rectTransform.anchoredPosition.y);
        tutorialText2.rectTransform.anchoredPosition = new Vector2(startX + textWidth, tutorialText2.rectTransform.anchoredPosition.y);
        tutorialText3.rectTransform.anchoredPosition = new Vector2(startX + (textWidth * 2f), tutorialText3.rectTransform.anchoredPosition.y);
    }

    // 必殺技アラート用のトリプル初期位置セット
    void SetSpecialTickerTripleText(string content)
    {
        if (specialText1 == null || specialText2 == null || specialText3 == null) return;

        specialText1.text = content;
        specialText2.text = content;
        specialText3.text = content;

        if (backgroundBarObj != null) backgroundBarObj.SetActive(true);

        float barWidth = GetBarWidth();
        float textWidth = specialText1.preferredWidth;

        float startX = (barWidth / 2f) + (textWidth / 2f);
        
        specialText1.rectTransform.anchoredPosition = new Vector2(startX, specialText1.rectTransform.anchoredPosition.y);
        specialText2.rectTransform.anchoredPosition = new Vector2(startX + textWidth, specialText2.rectTransform.anchoredPosition.y);
        specialText3.rectTransform.anchoredPosition = new Vector2(startX + (textWidth * 2f), specialText3.rectTransform.anchoredPosition.y);
    }

    // ▼【新規拡張】通常チュートリアル用の「2番手が消えたら1番手が回り込む」ループアルゴリズム
    void ScrollNormalTextTriple(float speed)
    {
        if (tutorialText1 == null || tutorialText2 == null || tutorialText3 == null || string.IsNullOrEmpty(tutorialText1.text)) return;

        tutorialText1.rectTransform.anchoredPosition += Vector2.left * speed * Time.unscaledDeltaTime;
        tutorialText2.rectTransform.anchoredPosition += Vector2.left * speed * Time.unscaledDeltaTime;
        tutorialText3.rectTransform.anchoredPosition += Vector2.left * speed * Time.unscaledDeltaTime;

        float barWidth = GetBarWidth();
        float textWidth = tutorialText1.preferredWidth;
        float resetThresholdX = -(barWidth / 2f) - (textWidth / 2f) - exitMargin;

        // 左にある順（1番手、2番手、3番手）にソート
        var first = tutorialText1; var second = tutorialText2; var third = tutorialText3;
        if (second.rectTransform.anchoredPosition.x < first.rectTransform.anchoredPosition.x) { var t = first; first = second; second = t; }
        if (third.rectTransform.anchoredPosition.x < first.rectTransform.anchoredPosition.x) { var t = first; first = third; third = t; }
        if (third.rectTransform.anchoredPosition.x < second.rectTransform.anchoredPosition.x) { var t = second; second = third; third = t; }

        // 2番手が左端を越えたら、1番手だけを3番手の後ろへ回り込ませる
        if (second.rectTransform.anchoredPosition.x < resetThresholdX)
        {
            float thirdX = third.rectTransform.anchoredPosition.x;
            first.rectTransform.anchoredPosition = new Vector2(thirdX + textWidth, first.rectTransform.anchoredPosition.y);
        }
    }

    // 必殺技用の「2番手が消えたら1番手が回り込む」ループアルゴリズム
    void ScrollSpecialTextTriple(float speed)
    {
        if (specialText1 == null || specialText2 == null || specialText3 == null || string.IsNullOrEmpty(specialText1.text)) return;

        specialText1.rectTransform.anchoredPosition += Vector2.left * speed * Time.unscaledDeltaTime;
        specialText2.rectTransform.anchoredPosition += Vector2.left * speed * Time.unscaledDeltaTime;
        specialText3.rectTransform.anchoredPosition += Vector2.left * speed * Time.unscaledDeltaTime;

        float barWidth = GetBarWidth();
        float textWidth = specialText1.preferredWidth;
        float resetThresholdX = -(barWidth / 2f) - (textWidth / 2f) - exitMargin;

        var first = specialText1; var second = specialText2; var third = specialText3;
        if (second.rectTransform.anchoredPosition.x < first.rectTransform.anchoredPosition.x) { var t = first; first = second; second = t; }
        if (third.rectTransform.anchoredPosition.x < first.rectTransform.anchoredPosition.x) { var t = first; first = third; third = t; }
        if (third.rectTransform.anchoredPosition.x < second.rectTransform.anchoredPosition.x) { var t = second; second = third; third = t; }

        if (second.rectTransform.anchoredPosition.x < resetThresholdX)
        {
            float thirdX = third.rectTransform.anchoredPosition.x;
            first.rectTransform.anchoredPosition = new Vector2(thirdX + textWidth, first.rectTransform.anchoredPosition.y);
        }
    }

    float GetBarWidth()
    {
        if (backgroundBarObj != null)
        {
            RectTransform barRect = backgroundBarObj.GetComponent<RectTransform>();
            if (barRect != null) return barRect.rect.width;
        }
        return 800f;
    }

    void UpdateBarVisibility()
    {
        if (backgroundBarObj == null) return;

        bool shouldShowBar = (!string.IsNullOrEmpty(tutorialText1?.text)) || 
                             (!string.IsNullOrEmpty(tutorialText2?.text)) || 
                             (!string.IsNullOrEmpty(tutorialText3?.text)) || 
                             (!string.IsNullOrEmpty(specialText1?.text)) || 
                             (!string.IsNullOrEmpty(specialText2?.text)) || 
                             (!string.IsNullOrEmpty(specialText3?.text));
        
        if (backgroundBarObj.activeSelf != shouldShowBar)
        {
            backgroundBarObj.SetActive(shouldShowBar);
        }
    }

    void ClearNormalTickers()
    {
        if (tutorialText1 != null) tutorialText1.text = "";
        if (tutorialText2 != null) tutorialText2.text = "";
        if (tutorialText3 != null) tutorialText3.text = "";
    }

    void ClearSpecialTickers()
    {
        if (specialText1 != null) specialText1.text = "";
        if (specialText2 != null) specialText2.text = "";
        if (specialText3 != null) specialText3.text = "";
    }

    void ClearAllTickers()
    {
        ClearNormalTickers();
        ClearSpecialTickers();
        if (backgroundBarObj != null) backgroundBarObj.SetActive(false); 
    }
}