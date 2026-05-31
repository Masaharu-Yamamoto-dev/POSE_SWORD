using UnityEngine;
using System;

// ▼ JSONのデータ構造に完全に一致させたクラス

public class SwordGenerator : MonoBehaviour
{
    [Header("テスト用のJSONファイル")]
    public TextAsset dummyJsonFile;

    [Header("セットアップ用")]
    public SpriteRenderer targetSpriteRenderer;
    public Rigidbody2D swordRigidbody;
    public PolygonCollider2D bladeCollider;
    public SwordBattle swordBattle;

    [Header("柄のオブジェクト（独楽モード時は消す）")]
    public GameObject handleObject; 

    [Header("刀身の理想の太さ（横幅）")]
    public float targetBladeWidth = 1.5f;

    [Header("テスト実行設定")]
    public bool generateOnStart = true; // チェックを入れるとゲーム開始時に自動生成

    void Start()
    {
        // TextAssetからJSONテキストを取り出して実行
        if (generateOnStart && dummyJsonFile != null && !string.IsNullOrEmpty(dummyJsonFile.text))
        {
            GenerateSwordFromJson(dummyJsonFile.text);
        }
    }

    public void GenerateSwordFromJson(string jsonString)
    {
        if (string.IsNullOrEmpty(jsonString)) 
        {
            Debug.LogError("❌ SwordGenerator: jsonStringが空です！");
            return;
        }

        Debug.Log($"📄 SwordGenerator.GenerateSwordFromJson 呼び出し: {jsonString.Substring(0, Mathf.Min(100, jsonString.Length))}...");
        
        SwordData data = JsonUtility.FromJson<SwordData>(jsonString);
        
        if (data == null)
        {
            Debug.LogError("❌ JSONの解析に失敗しました。形式が正しいか確認してください。");
            return;
        }

        Debug.Log($"✅ Parsed SwordData: name={data.name}, hp={data.hp}, attack={data.attack}, weight={data.weight}");

        // =========================================================
        // ⚔️ ステータスと物理演算への適用
        // =========================================================
        
        if (swordBattle != null)
        {
            // Webから来た 1〜100 の値を安全に制限（1未満や100オーバーのバグを防ぐ）
            float rawAttack = Mathf.Clamp(data.attack, 1f, 100f);
            float rawWeight = Mathf.Clamp(data.weight, 1f, 100f);

            // 1〜100の数値を「0.0 〜 1.0 の割合（パーセンテージ）」に直す
            float attackRatio = (rawAttack - 1f) / 99f;
            float weightRatio = (rawWeight - 1f) / 99f;

            // 攻撃力：割合に応じて 10 〜 90 の間に変換
            int actualAttack = Mathf.RoundToInt(Mathf.Lerp(10f, 90f, attackRatio));
            
            // 重さ：割合に応じて 7.0 〜 25.0 の間に変換
            float actualWeight = Mathf.Lerp(7f, 25f, weightRatio);
            
            // HPはそのまま、攻撃力は変換した値をセット
            swordBattle.SetupStatus(data.name, data.hp, actualAttack);

            if (swordRigidbody != null)
            {
                swordRigidbody.mass = actualWeight;
                swordRigidbody.centerOfMass = Vector2.zero; // 重心リセット
            }

            Debug.Log($"剣の生成完了: {data.name} | 見た目の攻撃力:{rawAttack} → 実攻撃力:{actualAttack} | 見た目の重さ:{rawWeight} → 実質量:{actualWeight}");
        }

        // =========================================================
        // 🎨 画像と当たり判定の反映
        // =========================================================

        if (!string.IsNullOrEmpty(data.imageStr))
        {
            string base64String = data.imageStr;
            
            // DataURL形式（data:image/png;base64,xxxxx）の場合は base64部分を抽出
            if (base64String.Contains(","))
            {
                base64String = base64String.Split(',')[1];
                Debug.Log("📌 DataURL形式を検出して Base64 に変換しました");
            }
            
            try
            {
                byte[] imageBytes = Convert.FromBase64String(base64String);
                Texture2D tex = new Texture2D(4, 4);
                
                // ▼【重要】エラーを回避するための安全装置
                bool isLoaded = tex.LoadImage(imageBytes); 
                if (!isLoaded)
                {
                    Debug.LogError("❌ 画像データの読み込みに失敗しました！");
                    return;
                }

                // Spriteの生成
                Sprite newSprite = Sprite.Create(
                    tex, 
                    new Rect(0, 0, tex.width, tex.height), 
                    new Vector2(0.5f, 0.0f),
                    100f 
                );

                if (targetSpriteRenderer != null)
                {
                    targetSpriteRenderer.sprite = newSprite;
                    
                    // 画像サイズに関わらず、理想の太さに自動リサイズする処理
                    float currentWidth = newSprite.bounds.size.x;
                    if (currentWidth > 0)
                    {
                        float scaleRatio = targetBladeWidth / currentWidth;
                        targetSpriteRenderer.transform.localScale = new Vector3(scaleRatio, scaleRatio, 1f);
                    }

                    Debug.Log($"✅ 剣のスプライトを設定し、太さを {targetBladeWidth} に統一しました");

                    // コライダーの再生成（スケール変更後に実行するのがベストです）
                    if (bladeCollider != null)
                    {
                        Destroy(bladeCollider);
                        bladeCollider = targetSpriteRenderer.gameObject.AddComponent<PolygonCollider2D>();
                        Debug.Log("✅ PolygonCollider2D を再生成しました");
                    }
                }

                // 独楽モードかどうかで、柄の表示/非表示を切り替える
                if (handleObject != null)
                {
                    handleObject.SetActive(!SwordController.isKomaMode);
                    Debug.Log($"✅ 柄の表示状態を更新しました: {!SwordController.isKomaMode}");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"❌ 画像の読み込みに失敗: {e.Message}");
            }
        }
    }
}