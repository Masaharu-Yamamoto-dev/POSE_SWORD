using UnityEngine;

public class BackgroundManager : MonoBehaviour
{
    public static BackgroundManager Instance;

    [Header("右側の色オブジェクト（RightColor）")]
    public Transform rightColorTransform; 
    
    [Header("境界線の移動範囲")]
    public float minX = -15f; 
    public float maxX = 15f;  

    // ▼【N人対応】固定2人参照はやめ、毎フレームNetworkManagerから
    // 「自分」と「生存中の他プレイヤーの平均HP」を取得して左右の傾きに反映する

    [Header("斜めの角度")]
    public float baseAngle = 15f; 

    [Header("グラデーションの幅の割合")]
    [Range(0.01f, 1.0f)] public float gradientRatio = 0.05f; 

    private float currentShake = 0f;
    private float shakeDecay = 5f;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        if (rightColorTransform != null)
        {
            GenerateHalfGradientSprite();
        }
    }

    void GenerateHalfGradientSprite()
    {
        // ▼【大修正】右側に「赤い尻尾」を長く伸ばすために、幅を1024に拡大！
        int width = 1024;  
        int height = 500;
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear; 

        int gradCenter = 256; // グラデーションの基準点を左から256ピクセル目に固定
        int gradHalf = Mathf.RoundToInt(256f * gradientRatio / 2f); 

        for (int x = 0; x < width; x++)
        {
            float alpha = 1f;

            if (x < gradCenter - gradHalf)
            {
                alpha = 0f; // 左は透明（青が見える）
            }
            else if (x > gradCenter + gradHalf)
            {
                // ▼【重要】グラデーションより右側（256～1024）は、全て真っ赤にする！
                // これにより、どれだけ左に移動しても右端が途切れない！
                alpha = 1f; 
            }
            else
            {
                alpha = (float)(x - (gradCenter - gradHalf)) / (gradHalf * 2f);
            }

            Color pixelColor = new Color(1, 1, 1, alpha);
            for (int y = 0; y < height; y++)
            {
                tex.SetPixel(x, y, pixelColor);
            }
        }
        tex.Apply();

        // ▼【大修正】画像の中心（ピボット）を、「グラデーションのド真ん中」に設定する魔法！
        Vector2 pivot = new Vector2((float)gradCenter / width, 0.5f);

        // スプライト作成
        Sprite gradSprite = Sprite.Create(tex, new Rect(0, 0, width, height), pivot, 256f);

        SpriteRenderer sr = rightColorTransform.GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            sr.sprite = gradSprite;
        }
    }

    void Update()
    {
        if (rightColorTransform == null || NetworkManager.Instance == null) return;

        GameObject mySwordObj = NetworkManager.Instance.GetMySword();
        SwordBattle mine = mySwordObj != null ? mySwordObj.GetComponent<SwordBattle>() : null;
        if (mine == null) return;

        // ▼【N人対応】自分 vs 生存中の他プレイヤーの平均HPで左右の傾きを決める(2人時は従来と同じ計算になる)
        float othersHpSum = 0f;
        int othersCount = 0;
        GameObject[] swords = NetworkManager.Instance.playerSwords;
        for (int i = 0; i < swords.Length; i++)
        {
            GameObject obj = swords[i];
            if (obj == null || obj == mySwordObj || !obj.activeInHierarchy) continue;
            SwordBattle battle = obj.GetComponent<SwordBattle>();
            if (battle == null) continue;
            othersHpSum += battle.hp;
            othersCount++;
        }
        if (othersCount == 0) return;

        float hostHp = mine.hp;
        float clientHp = othersHpSum / othersCount;
        float totalHp = hostHp + clientHp;

        float ratio = 0.5f;
        if (totalHp > 0)
        {
            ratio = hostHp / totalHp;
        }

        // 少しのダメージでもダイナミックに動く
        float dynamicRatio = (ratio - 0.5f) * 1.5f + 0.5f;
        dynamicRatio = Mathf.Clamp(dynamicRatio, 0f, 1f); 

        float targetX = Mathf.Lerp(minX, maxX, dynamicRatio);

        // ▼【修正維持】チカチカする揺れを「ズシン」という重い揺れに軽減済み！
        float shakeAngle = 0f;
        if (currentShake > 0)
        {
            currentShake -= Time.unscaledDeltaTime * shakeDecay;
            if (currentShake < 0) currentShake = 0;
            shakeAngle = Mathf.Sin(Time.unscaledTime * 5f) * currentShake * 0.05f; // 速度と揺れ幅を低減
        }

        Vector3 currentPos = rightColorTransform.position;
        currentPos.x = Mathf.Lerp(currentPos.x, targetX, Time.unscaledDeltaTime * 5f);
        
        float offsetX = Mathf.Sin(Time.unscaledTime * 5f) * currentShake * 0.05f; // 横揺れも低減
        
        rightColorTransform.position = new Vector3(currentPos.x + offsetX, currentPos.y, currentPos.z);
        rightColorTransform.rotation = Quaternion.Euler(0, 0, baseAngle + shakeAngle);
    }

    public void TriggerImpact(float impactStrength)
    {
        currentShake = Mathf.Min(currentShake + impactStrength, 4f);
    }
}