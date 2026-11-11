using System;
using UnityEngine;

// 撃破演出の「見た目」の部品。いつ・どこに出すか、いつ消すかといった共通の流れは呼び出し側
// （DamagePopup・SwordBattle・DefeatGhost）が持ち、ここは見た目だけを担当する。
// 将来エフェクトを選べるようにするときは、IDefeatEffect を実装したものを増やして DefeatEffects で選ぶ
public interface IDefeatEffect
{
    // とどめの印を書き終えるまでの秒数。倒された剣は、この間その場で止まってから吹き飛ぶ
    float KillMarkDuration { get; }
    // とどめが当たった瞬間：撃破の数字の位置に出す印（一閃・飛沫など）
    void PlayKillMark(KillMarkContext context);
    // とどめが当たった瞬間：当たった場所に出すヒットエフェクト。出せなかった時は false を返し、呼び出し側が通常の火花を出す
    bool PlayKillHit(Vector3 point, int sortingOrder);
    // 倒された剣の見た目を変える（刀身と柄）
    void ApplyDefeatedLook(SpriteRenderer[] renderers);
    // 倒された剣が消え始めるとき。剣を薄くするのは呼び出し側が行う
    void PlayVanish(Vector3 center, float scale);
    // 消えていく途中（k：0→1）に毎フレーム呼ばれる。剣自体を薄くするのは呼び出し側で、ここでは見た目の追加だけ
    void UpdateVanish(SpriteRenderer[] renderers, float k);
}

public struct KillMarkContext
{
    public Transform numberRoot;   // 撃破の数字。この子にした物は数字と一緒に動く
    public Bounds numberBounds;    // 数字の範囲（numberRoot の座標系、拡大前）
    public float worldScale;       // カメラの引き具合に合わせた倍率（数字と同じ）
    public int sortingOrder;       // 数字のすぐ後ろの描画順
    public bool persist;           // 決着の一撃：消さずに残す
    // 数字と一緒に薄れさせたい物を登録する（第2引数は不透明度の倍率）
    public Action<SpriteRenderer, float> followNumberAlpha;
}

public static class DefeatEffects
{
    static IDefeatEffect current;
    public static IDefeatEffect Current => current ?? (current = new InkDefeatEffect());
}

// 標準：墨。とどめで撃破の数字の後ろに墨の帯が走り、その後ろを袈裟（右上→左下）の一閃が横切って、払いの先から飛沫が飛ぶ。
// 剣は墨のシルエットになり、消えるときは淡い墨煙になる
public class InkDefeatEffect : IDefeatEffect
{
    const float SlashAngle = 215f;           // 筆跡の入り（画像の左端）を右上に、払いを左下に向ける
    // 一閃の長さ＝数字の高さのこの倍率。桁数やクリティカルの「!」で長さ・太さが変わらないよう、幅は見ない
    const float SlashLengthPerHeight = 4.8f;
    // 一閃の太さ。画像の縦横比のままだと細く見えるので、長さはそのままで縦だけこの倍率で太らせる
    const float SlashThickness = 2.1f;
    // 書き順：数字が出て少ししてから墨の帯を書き、続けて斜めの一閃を書いて、飛沫を飛ばす。
    // 一閃は筆が走るのが目で追える速さにする（速すぎると、線が最初からあったように見えて斬った感じが出ない）
    // 一閃は墨の帯の書き始め（UnderlineDrawDelay）と同じだけ遅らせて、帯を書いている途中から走らせる
    const float SlashDrawDelay = .33f, SlashDrawTime = .5f;
    const float Afterglow = .3f; // 一閃を書き終えてから、倒された剣が跳ね上がるまでの余韻
    // 数字の後ろを覆う墨の帯（Reactの画面と同じ筆跡）。長さ・画像の枠の高さとも数字の高さのこの倍率（一閃と同じく幅は見ない）。
    // 画像の中で墨が塗られているのは枠の高さの2〜3割ほど（真ん中で約19%）なので、枠を5.5倍にすると真ん中の墨が数字の高さくらいになる
    const float UnderlineWidthPerHeight = 4.8f, UnderlineThicknessPerHeight = 5.5f;
    // 墨の帯は少し遅らせて書き始める。当たった場所に出る撃破のヒットエフェクトは出てすぐが一番明るいので、
    // その一瞬を墨で覆わずに見せる（当たる → ヒットエフェクトが光る → 墨が走る、の順にする）
    const float UnderlineDrawDelay = .13f, UnderlineDrawTime = .25f;

    public float KillMarkDuration => SlashDrawDelay + SlashDrawTime + Afterglow;

    // 撃破の一撃のヒットエフェクト（Resources/DefeatEffects/KillHitElectric）。
    // 【試し中】Cartoon FX Remaster の「CFXR3 Hit Electric C (Air)」をそのまま複製したもの。
    // 画面の揺れは足さない方針なので、複製の方だけ CFXR 自身のカメラシェイクを切ってある
    const string KillHitPath = "DefeatEffects/KillHitElectric";
    // 撃破の数字と墨の印の後ろに出すので、元の大きさだとそれに埋もれる。はみ出して見えるよう大きくする
    const float KillHitScale = 2f;
    // 撃破のヒットエフェクトの色：紅紫（マゼンタ）。プレイヤーの4色・ダメージの数字・
    // クリティカルの黄のどれとも被らない色にしている。青みに寄せすぎると、1vs3の掌握斬の紫に近づく。
    // 元の青白の色を置き換える（明るさの移り変わりは元のエフェクトのまま）
    static readonly Color KillHitColor = new Color(1f, .3f, .8f);
    // 明るさ。色味（上の KillHitColor）はそのままで、全体をこの倍率で暗くする（1で元の明るさ）
    const float KillHitBrightness = .6f;
    // 火花は電流の筋より白に寄せて、芯が光って見えるようにする（0で筋と同じ色、1で白）
    const float KillHitSparkLighten = .15f;
    static GameObject killHitPrefab;
    static bool killHitLoaded;

    public bool PlayKillHit(Vector3 point, int sortingOrder)
    {
        if (!killHitLoaded)
        {
            killHitLoaded = true;
            killHitPrefab = Resources.Load<GameObject>(KillHitPath);
            if (killHitPrefab == null) Debug.LogWarning("撃破のヒットエフェクトが見つかりません: Resources/" + KillHitPath);
        }
        if (killHitPrefab == null) return false;
        var spawned = UnityEngine.Object.Instantiate(killHitPrefab, point, Quaternion.identity);
        spawned.transform.localScale = Vector3.one * KillHitScale;
        Color arcs = KillHitColor * KillHitBrightness;
        arcs.a = 1f;
        Color sparks = Color.Lerp(arcs, Color.white, KillHitSparkLighten);
        foreach (var system in spawned.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = system.main;
            main.startColor = system.name.StartsWith("Sparks") ? sparks : arcs;
        }
        foreach (var r in spawned.GetComponentsInChildren<ParticleSystemRenderer>(true)) r.sortingOrder = sortingOrder + r.sortingOrder;
        return true;
    }
    // 見本（1280×720、カメラの縦の大きさ10）のピクセルをワールド単位に直す倍率
    const float Px = 1f / 36f;
    // 飛沫の大きさの倍率（見本の値に対して）。大きくするほど丸い形が目立つので、見た目を確かめながら決める
    const float FleckSize = 1.8f;

    public void PlayKillMark(KillMarkContext c)
    {
        if (c.numberRoot == null) return;
        PlayUnderline(c);
        Texture2D texture = InkAssets.SlashTexture;
        if (texture == null) return;
        Bounds b = c.numberBounds;
        float length = b.size.y * SlashLengthPerHeight;
        float height = length * texture.height / texture.width * SlashThickness;
        // 数字の下半分の後ろを横切るように、少し下げた位置を中心にする
        Vector3 center = new Vector3(b.center.x, b.center.y - b.size.y * .25f, .03f);
        Vector3 along = Quaternion.Euler(0f, 0f, SlashAngle) * Vector3.right;

        InkStroke(c, "Slash", texture, center - along * (length / 2f), SlashAngle,
            new Vector3(length / (texture.width / InkAssets.PixelsPerUnit), height / (texture.height / InkAssets.PixelsPerUnit), 1f),
            c.sortingOrder - 1, SlashDrawDelay, SlashDrawTime);

        // 飛沫：払いの先から振り飛ばす細長い飛沫と細かい霧、線の途中から脇へこぼれる飛沫、入りの近くの墨だまり
        float s = c.worldScale;
        Vector3 worldAlong = c.numberRoot.TransformDirection(along).normalized;
        float worldLength = length * s;
        Vector3 worldCenter = c.numberRoot.TransformPoint(center);
        Vector3 tip = worldCenter + worldAlong * (worldLength * .42f);
        float baseAngle = SlashAngle;
        int order2 = c.sortingOrder - 3;
        float afterDraw = SlashDrawDelay + SlashDrawTime * .7f;
        for (int i = 0; i < 5; i++)
        {
            float l = UnityEngine.Random.Range(12f, 34f) * Px * s * FleckSize;
            InkPiece.Fleck(tip, baseAngle + UnityEngine.Random.Range(-17f, 17f), UnityEngine.Random.Range(60f, 280f) * Px * s,
                new Vector2(l, l * UnityEngine.Random.Range(.24f, .34f)), UnityEngine.Random.value < .7f ? .95f : .6f, afterDraw, c.persist, order2);
        }
        for (int i = 0; i < 10; i++)
        {
            float d = UnityEngine.Random.Range(1.5f, 4f) * Px * s * FleckSize;
            InkPiece.Fleck(tip, baseAngle + UnityEngine.Random.Range(-31f, 31f), UnityEngine.Random.Range(30f, 290f) * Px * s,
                new Vector2(d, d), .85f, afterDraw, c.persist, order2);
        }
        for (int i = 0; i < 5; i++)
        {
            float t = UnityEngine.Random.Range(-.4f, .4f);
            float side = UnityEngine.Random.value < .5f ? 1f : -1f;
            float d = UnityEngine.Random.Range(1.5f, 4.5f) * Px * s * FleckSize;
            InkPiece.Fleck(worldCenter + worldAlong * (t * worldLength), baseAngle + side * UnityEngine.Random.Range(63f, 92f),
                UnityEngine.Random.Range(20f, 90f) * Px * s, new Vector2(d, d), .8f, SlashDrawDelay + (t + .4f) / .8f * SlashDrawTime * .55f, c.persist, order2);
        }
        for (int i = 0; i < 2; i++)
        {
            float t = UnityEngine.Random.Range(-.35f, -.15f);
            float d = UnityEngine.Random.Range(6f, 11f) * Px * s * FleckSize;
            InkPiece.Fleck(worldCenter + worldAlong * (t * worldLength), UnityEngine.Random.Range(0f, 360f),
                UnityEngine.Random.Range(18f, 58f) * Px * s, new Vector2(d, d * UnityEngine.Random.Range(.8f, 1.1f)), .92f, SlashDrawDelay, c.persist, order2);
        }
    }

    // 数字の後ろに敷く墨の帯。左から右へ走り、数字と一緒に薄れる
    static void PlayUnderline(KillMarkContext c)
    {
        Texture2D texture = InkAssets.StrokeTexture;
        if (texture == null) return;
        Bounds b = c.numberBounds;
        float width = b.size.y * UnderlineWidthPerHeight;
        float height = b.size.y * UnderlineThicknessPerHeight;
        // 左端を基準に置く。高さは数字の中心に合わせて、墨の帯が数字の後ろを覆うようにする
        InkStroke(c, "Underline", texture, new Vector3(b.center.x - width / 2f, b.center.y, .02f), 0f,
            new Vector3(width / (texture.width / InkAssets.PixelsPerUnit), height / (texture.height / InkAssets.PixelsPerUnit), 1f),
            c.sortingOrder, UnderlineDrawDelay, UnderlineDrawTime);
    }

    // 筆跡1本：墨色で数字の子として置く。左から右へ走り、数字と一緒に薄れる。left は筆跡の左端（入り）の位置
    static void InkStroke(KillMarkContext c, string name, Texture2D texture, Vector3 left, float angle, Vector3 scale, int order, float delay, float time)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(c.numberRoot, false);
        obj.transform.localPosition = left;
        obj.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
        obj.transform.localScale = scale;
        var renderer = obj.AddComponent<SpriteRenderer>();
        renderer.color = InkAssets.Ink;
        renderer.sortingOrder = order;
        InkPiece.Reveal(renderer, texture, delay, time);
        c.followNumberAlpha?.Invoke(renderer, 1f);
    }

    // 倒された剣は暗くせず「色が抜けた」見た目にする：少し灰色寄りに落としたうえで半透明にする。
    // 暗くすると墨の黒や黒い場外に埋もれ、シルエットのような剣で戦っているプレイヤーとも見分けにくいため
    static readonly Color DefeatedTint = new Color(.82f, .82f, .82f);
    const float DefeatedAlpha = .5f;

    public void ApplyDefeatedLook(SpriteRenderer[] renderers)
    {
        foreach (var renderer in renderers)
        {
            if (renderer == null) continue;
            Color tint = DefeatedTint;
            tint.a = renderer.color.a * DefeatedAlpha;
            renderer.color = tint;
        }
    }

    // にじみ：画像をぼかせないので、薄い墨の写しを4方向へ少しずつずらして広げ、ぼやけて溶けるように見せる。
    // 写しは各パーツの子にするので、吹き飛んでいる剣にもついていく
    public const string BleedName = "InkBleed";
    const float BleedSpread = .28f, BleedAlpha = .3f;
    static readonly Vector2[] BleedDirections = { new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) };

    public void UpdateVanish(SpriteRenderer[] renderers, float k)
    {
        foreach (var renderer in renderers)
        {
            if (renderer == null || renderer.sprite == null) continue;
            Transform parent = renderer.transform;
            float unit = Mathf.Max(.0001f, Mathf.Abs(parent.lossyScale.x));
            for (int i = 0; i < BleedDirections.Length; i++)
            {
                Transform copy = parent.Find(BleedName + i);
                SpriteRenderer copyRenderer;
                if (copy == null)
                {
                    var obj = new GameObject(BleedName + i);
                    obj.transform.SetParent(parent, false);
                    copyRenderer = obj.AddComponent<SpriteRenderer>();
                    copyRenderer.sprite = renderer.sprite;
                    copyRenderer.sharedMaterial = renderer.sharedMaterial;
                    copyRenderer.flipX = renderer.flipX; copyRenderer.flipY = renderer.flipY;
                    copyRenderer.sortingLayerID = renderer.sortingLayerID;
                    copyRenderer.sortingOrder = renderer.sortingOrder - 1;
                    copy = obj.transform;
                }
                else copyRenderer = copy.GetComponent<SpriteRenderer>();
                // 世界の長さでずらしたいので、親の拡大率で割る（回転はしているが、にじみの向きは問わない）
                copy.localPosition = new Vector3(BleedDirections[i].x, BleedDirections[i].y, 0f).normalized * (BleedSpread * k / unit);
                Color c = DefeatedTint;
                c.a = (BleedAlpha * Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI) + BleedAlpha * .5f * (1f - k)) * DefeatedAlpha;
                copyRenderer.color = c;
            }
        }
    }

    public void PlayVanish(Vector3 center, float scale)
    {
        InkPiece.Cloud(center, 90f * Px * scale, 260f * Px * scale, .8f, .9f, DefeatGhost.VanishCloudSortingOrder);
        InkPiece.Cloud(center + new Vector3(20f, 25f, 0f) * Px * scale, 60f * Px * scale, 180f * Px * scale, .7f, .6f, DefeatGhost.VanishCloudSortingOrder);
    }
}
