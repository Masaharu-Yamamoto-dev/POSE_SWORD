using UnityEngine;
using System;

public class SwordGenerator : MonoBehaviour
{
    // ▼ここを string から TextAsset に変更！
    [Header("テスト用のJSONファイル")]
    public TextAsset dummyJsonFile;

    [Header("セットアップ用")]
    public SpriteRenderer targetSpriteRenderer;
    public Rigidbody2D swordRigidbody;
    public PolygonCollider2D bladeCollider;

    public SwordBattle swordBattle;
    public float desiredHeight = 5f; 

    [Header("柄のオブジェクト（独楽モード時は消す）")]
    public GameObject handleObject; 

    [Header("【テスト用】合成テスト画像")]
    public Texture2D testTexture;
    public SpriteRenderer bladeRenderer;

    // ▼【追加】柄にぴったり収まる理想の太さ（Unity上のサイズ）
    [Header("刀身の理想の太さ（横幅）")]
    public float targetBladeWidth = 1.5f; // 柄の太さに合わせて調整してください！

    [ContextMenu("テスト: 柄の合成を実行！")]
    public void TestHandleSynthesis()
    {
        if (testTexture == null || bladeRenderer == null) return;

        // 1. Spriteの生成（底辺中央を基準点にするのはそのまま）
        Sprite testSprite = Sprite.Create(
            testTexture,
            new Rect(0, 0, testTexture.width, testTexture.height),
            new Vector2(0.5f, 0.0f), 
            100f
        );

        bladeRenderer.sprite = testSprite;

        // ▼【新規追加】画像サイズに関わらず、理想の太さに自動リサイズする処理
        // 生成されたSpriteの実際の横幅（Unit）を取得
        float currentWidth = testSprite.bounds.size.x;
        
        // 理想の太さにするための倍率を計算（例: 理想1.5 ÷ 実際3.0 = 0.5倍）
        float scaleRatio = targetBladeWidth / currentWidth;
        
        // 縦横の比率（アスペクト比）を保ったままスケールを適用
        bladeRenderer.transform.localScale = new Vector3(scaleRatio, scaleRatio, 1f);


        // 3. モードを「剣モード」にして柄を表示する
        SwordController.isKomaMode = false;
        if (handleObject != null)
        {
            handleObject.SetActive(true);
        }

        Debug.Log($"⚔️ リサイズ完了！倍率: {scaleRatio}x");
    }
    void Start()
    {
        // ▼ Startの中身もファイルからテキストを取り出すように変更
        // if (dummyJsonFile != null)
        // {
        //     GenerateSwordFromJson(dummyJsonFile.text);
        // }
    }

    // 以降の GenerateSwordFromJson(string jsonString) の中身はそのまま！
    public void GenerateSwordFromJson(string jsonString)
    {
        if (string.IsNullOrEmpty(jsonString)) 
        {
            Debug.LogError("❌ SwordGenerator: jsonStringが空です！");
            return;
        }

        Debug.Log($"📄 SwordGenerator.GenerateSwordFromJson 呼び出し: {jsonString.Substring(0, Mathf.Min(100, jsonString.Length))}...");
        
        SwordData data = JsonUtility.FromJson<SwordData>(jsonString);
        Debug.Log($"✅ Parsed SwordData: name={data.name}, hp={data.hp}, attack={data.attack}, weight={data.weight}");
        
        if (swordRigidbody != null)
        {
            swordRigidbody.mass = data.weight;
            swordRigidbody.centerOfMass = Vector2.zero;
        }

        // =========================================================
    // JSONから剣を生成する関数の中身（ステータス反映部分）
    // =========================================================
    
    // 1. JSONのデータをパースしたとする (data という変数に入っている想定)
    // SwordData data = JsonUtility.FromJson<SwordData>(jsonString);

        if (swordBattle != null)
        {
            // --- 📊 パラメータの変換（マッピング） ---
            
            // Webから来た 1〜100 の値を安全に制限（1未満や100オーバーのバグを防ぐ）
            float rawAttack = Mathf.Clamp(data.attack, 1f, 100f);
            float rawWeight = Mathf.Clamp(data.weight, 1f, 100f);

            // 1〜100の数値を「0.0 〜 1.0 の割合（パーセンテージ）」に直す
            float attackRatio = (rawAttack - 1f) / 99f;
            float weightRatio = (rawWeight - 1f) / 99f;

            // ゲームの仕様に合わせた実際の数値に変換（Lerp関数）
            // 攻撃力：割合に応じて 10 〜 100 の間に変換
            int actualAttack = Mathf.RoundToInt(Mathf.Lerp(10f, 90f, attackRatio));
            
            // 重さ：割合に応じて 5.0 〜 30.0 の間に変換
            float actualWeight = Mathf.Lerp(7f, 25f, weightRatio);

            // --- ⚔️ ステータスと物理演算への適用 ---
            
            // HPはそのまま（100〜1000）、攻撃力は変換した値をセット
            swordBattle.SetupStatus(data.name, data.hp, actualAttack);

            // 剣のRigidbody2Dを取得して、重さ（mass）を適用
            Rigidbody2D rb = swordBattle.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.mass = actualWeight;
            }

            Debug.Log($"剣の生成完了: {data.name} | 見た目の攻撃力:{rawAttack} → 実攻撃力:{actualAttack} | 見た目の重さ:{rawWeight} → 実質量:{actualWeight}");
            
        }

        if (!string.IsNullOrEmpty(data.imageStr))
        {
            string base64String = data.imageStr;
            
            // 【修正】DataURL形式（data:image/png;base64,xxxxx）の場合は base64部分を抽出
            if (base64String.Contains(","))
            {
                base64String = base64String.Split(',')[1];
                Debug.Log("📌 DataURL形式を検出して Base64 に変換しました");
            }
            
            try
            {
                byte[] imageBytes = Convert.FromBase64String(base64String);
                Texture2D tex = new Texture2D(2, 2);
                tex.LoadImage(imageBytes); 

                // ▼ ここから差し替え ▼

                // 1. Spriteの生成（テスト時と同じく 100f 固定で生成します）
                Sprite newSprite = Sprite.Create(
                    tex, 
                    new Rect(0, 0, tex.width, tex.height), 
                    new Vector2(0.5f, 0.0f),
                    100f 
                );

                if (targetSpriteRenderer != null)
                {
                    targetSpriteRenderer.sprite = newSprite;
                    
                    // 2. 画像サイズに関わらず、理想の太さに自動リサイズする処理
                    float currentWidth = newSprite.bounds.size.x;
                    if (currentWidth > 0)
                    {
                        float scaleRatio = targetBladeWidth / currentWidth;
                        targetSpriteRenderer.transform.localScale = new Vector3(scaleRatio, scaleRatio, 1f);
                    }

                    Debug.Log($"✅ 剣のスプライトを設定し、太さを {targetBladeWidth} に統一しました");

                    // 3. コライダーの再生成（スケール変更後に実行するのがベストです）
                    if (bladeCollider != null)
                    {
                        Destroy(bladeCollider);
                        bladeCollider = targetSpriteRenderer.gameObject.AddComponent<PolygonCollider2D>();
                        Debug.Log("✅ PolygonCollider2D を再生成しました");
                    }
                }

                // 4. 独楽モードかどうかで、柄の表示/非表示を切り替える
                if (handleObject != null)
                {
                    handleObject.SetActive(!SwordController.isKomaMode);
                    Debug.Log($"✅ 柄の表示状態を更新しました: {!SwordController.isKomaMode}");
                }

                // ▲ ここまで差し替え ▲
            }
            catch (System.Exception e)
            {
                Debug.LogError($"❌ 画像の読み込みに失敗: {e.Message}\n Base64文字列: {base64String.Substring(0, Mathf.Min(50, base64String.Length))}...");
            }
            
        }
    }
}