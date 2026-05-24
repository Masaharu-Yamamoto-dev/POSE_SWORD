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
        if (dummyJsonFile != null)
        {
            GenerateSwordFromJson(dummyJsonFile.text);
        }
    }

    // 以降の GenerateSwordFromJson(string jsonString) の中身はそのまま！
    public void GenerateSwordFromJson(string jsonString)
    {
        if (string.IsNullOrEmpty(jsonString)) return;

        SwordData data = JsonUtility.FromJson<SwordData>(jsonString);
        
        if (swordRigidbody != null)
        {
            swordRigidbody.mass = data.weight;
            swordRigidbody.centerOfMass = Vector2.zero;
        }

        if (swordBattle != null)
        {
            // ▼ 変更：作成した関数を使って初期化する
            swordBattle.SetupStatus(data.name, data.hp, data.attack);
        }

        if (!string.IsNullOrEmpty(data.imageData))
        {
            byte[] imageBytes = Convert.FromBase64String(data.imageData);
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