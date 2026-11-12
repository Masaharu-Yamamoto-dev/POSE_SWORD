using UnityEngine;

// 墨の演出で使う画像。Resources に置いた筆跡の画像と、コードで作る小さな画像（丸い飛沫・淡い墨煙）をまとめて持つ。
// 筆跡の画像はどれも「白い形＋透明」なので、SpriteRenderer.color で好きな色に塗れる（標準は墨色）
public static class InkAssets
{
    public static readonly Color Ink = new Color32(23, 20, 18, 255);
    // 1ワールド単位あたりのピクセル数。スプライトの大きさの換算に使う
    public const float PixelsPerUnit = 100f;

    static Texture2D strokeTexture, slashTexture;
    static Sprite strokeSprite, dotSprite, cloudSprite;
    static bool loaded;

    // Reactの画面と同じ横向きの筆跡（Resources/DamagePopup/sumi_touka.png）。残機アイコンを消す線に使う
    public static Texture2D StrokeTexture { get { Load(); return strokeTexture; } }
    public static Sprite StrokeSprite { get { Load(); return strokeSprite; } }
    // 斬撃の一閃：入りで溜め、最後は細く抜けてかすれる筆跡（Resources/DamagePopup/slash_harai_solid.png）。左端が入り。
    // 元にした画像は線の真ん中までかすれが強く不自然だったので、真ん中のかすれを埋めて払いの先だけに残してある
    public static Texture2D SlashTexture { get { Load(); return slashTexture; } }

    static void Load()
    {
        if (loaded) return;
        loaded = true;
        strokeTexture = Resources.Load<Texture2D>("DamagePopup/sumi_touka");
        slashTexture = Resources.Load<Texture2D>("DamagePopup/slash_harai_solid");
        if (strokeTexture == null) Debug.LogWarning("墨の線の画像が見つかりません: Resources/DamagePopup/sumi_touka");
        else strokeSprite = LeftPart(strokeTexture, 1f, new Vector2(.5f, .5f));
        if (slashTexture == null) Debug.LogWarning("斬撃の筆跡の画像が見つかりません: Resources/DamagePopup/slash_harai_solid");
    }

    // 画像の左から fraction の割合だけを切り出したスプライト。基準点を左端にすれば、伸ばさずに右へ現れていく
    public static Sprite LeftPart(Texture2D texture, float fraction, Vector2 pivot)
    {
        int width = Mathf.Clamp(Mathf.RoundToInt(texture.width * fraction), 1, texture.width);
        return Sprite.Create(texture, new Rect(0, 0, width, texture.height), pivot, PixelsPerUnit, 0, SpriteMeshType.FullRect);
    }

    // 縁が少しだけやわらかい丸（大きさ1ワールド単位）。飛沫に使い、縦横の倍率を変えれば細長い飛沫になる
    public static Sprite Dot
    {
        get
        {
            if (dotSprite == null) dotSprite = MakeRadial(32, r => Mathf.Clamp01((1f - r) * 8f));
            return dotSprite;
        }
    }

    // 中心が濃く、外へ向かって消えていく淡い墨煙（大きさ1ワールド単位）
    public static Sprite Cloud
    {
        get
        {
            if (cloudSprite == null) cloudSprite = MakeRadial(64, r => r < .38f ? Mathf.Lerp(.5f, .28f, r / .38f)
                : r < .58f ? Mathf.Lerp(.28f, .08f, (r - .38f) / .2f) : r < .72f ? Mathf.Lerp(.08f, 0f, (r - .58f) / .14f) : 0f);
            return cloudSprite;
        }
    }

    // 中心からの距離（0〜1）で不透明度を決める白い丸。色は SpriteRenderer.color で墨色にする
    static Sprite MakeRadial(int size, System.Func<float, float> alphaAt)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var pixels = new Color32[size * size];
        float half = size / 2f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float r = Mathf.Sqrt((x + .5f - half) * (x + .5f - half) + (y + .5f - half) * (y + .5f - half)) / half;
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alphaAt(r)) * 255));
            }
        texture.SetPixels32(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), size, 0, SpriteMeshType.FullRect);
    }
}
