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
            byte[] imageBytes = Convert.FromBase64String(data.imageStr);
            Texture2D tex = new Texture2D(2, 2);
            tex.LoadImage(imageBytes); 

            float ppu = tex.height / desiredHeight; // ピクセル密度を自動計算

            Sprite newSprite = Sprite.Create(
                tex, 
                new Rect(0, 0, tex.width, tex.height), 
                new Vector2(0.5f, 0.0f),
                ppu // ← 計算した密度を指定
            );

            if (targetSpriteRenderer != null)
            {
                targetSpriteRenderer.sprite = newSprite;

                if (bladeCollider != null)
                {
                    Destroy(bladeCollider);
                    bladeCollider = targetSpriteRenderer.gameObject.AddComponent<PolygonCollider2D>();
                }
            }
        }
    }
}