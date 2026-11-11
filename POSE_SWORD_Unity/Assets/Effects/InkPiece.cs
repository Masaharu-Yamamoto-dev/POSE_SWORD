using System.Collections.Generic;
using UnityEngine;

// 墨の演出の部品1つ分の動き。どれも実時間で動く（ヒットストップや決着のスローに巻き込まれず、ダメージ数字と時間がそろう）
//   Reveal：筆跡の画像を左から右へ見える範囲を広げて「筆が走る」ように見せる（薄れ方は持ち主の数字に任せる）
//   Fleck ：飛沫。決めた向きへ飛んですぐ止まり、その場に少し残ってから薄れる
//   Cloud ：淡い墨煙。ゆっくり広がりながら薄れる
// persist の物は、出し切った姿のまま消さずに残す（決着の一撃）。試合の終わりに ClearAll でまとめて消す
public class InkPiece : MonoBehaviour
{
    enum Kind { Reveal, Fleck, Cloud }

    const float FleckHold = .4f, FleckFade = .8f;

    static readonly List<InkPiece> active = new List<InkPiece>();

    Kind kind;
    SpriteRenderer spriteRenderer;
    float born, delay, duration;
    bool persist;
    // Reveal
    Texture2D revealTexture;
    Sprite partialSprite;
    float shown = -1f;
    // Fleck
    Vector3 origin, direction;
    float distance, baseAlpha;
    // Cloud
    float fromSize, toSize;

    public static InkPiece Reveal(SpriteRenderer spriteRenderer, Texture2D texture, float delay, float duration)
    {
        var piece = spriteRenderer.gameObject.AddComponent<InkPiece>();
        piece.Init(Kind.Reveal, spriteRenderer, delay, duration, false);
        piece.revealTexture = texture;
        spriteRenderer.sprite = null;
        return piece;
    }

    // angle：飛ぶ向き（度）。size：飛沫の縦横（ワールド単位。細長い飛沫は x を長くする）
    public static void Fleck(Vector3 position, float angle, float distance, Vector2 size, float alpha, float delay,
        bool persist, int sortingOrder)
    {
        var obj = new GameObject("InkFleck");
        obj.transform.position = position;
        obj.transform.rotation = Quaternion.Euler(0f, 0f, angle);
        obj.transform.localScale = new Vector3(size.x, size.y, 1f);
        var spriteRenderer = obj.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = InkAssets.Dot;
        spriteRenderer.sortingOrder = sortingOrder;
        var piece = obj.AddComponent<InkPiece>();
        piece.Init(Kind.Fleck, spriteRenderer, delay, FleckHold + FleckFade, persist);
        piece.origin = position;
        piece.direction = Quaternion.Euler(0f, 0f, angle) * Vector3.right;
        piece.distance = distance;
        piece.baseAlpha = alpha;
        piece.Apply(0f, 0f);
    }

    public static void Cloud(Vector3 position, float fromSize, float toSize, float life, float alpha, int sortingOrder)
    {
        var obj = new GameObject("InkCloud");
        obj.transform.position = position;
        var spriteRenderer = obj.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = InkAssets.Cloud;
        spriteRenderer.sortingOrder = sortingOrder;
        var piece = obj.AddComponent<InkPiece>();
        piece.Init(Kind.Cloud, spriteRenderer, 0f, life, false);
        piece.fromSize = fromSize; piece.toSize = toSize; piece.baseAlpha = alpha;
        piece.Apply(0f, 0f);
    }

    public static void ClearAll()
    {
        foreach (var piece in active.ToArray()) if (piece != null && piece.kind != Kind.Reveal) Destroy(piece.gameObject);
        active.Clear();
    }

    void Init(Kind pieceKind, SpriteRenderer renderer, float startDelay, float pieceDuration, bool keep)
    {
        kind = pieceKind; spriteRenderer = renderer; delay = startDelay; duration = pieceDuration; persist = keep;
        born = Time.unscaledTime;
        active.Add(this);
    }

    void Update()
    {
        float age = Time.unscaledTime - born;
        if (kind == Kind.Reveal) { UpdateReveal(age); return; }
        float k = age - delay;
        if (persist) k = Mathf.Min(k, FleckHold - .01f); // 出し切った姿のまま止める
        else if (k >= duration) { Destroy(gameObject); return; }
        Apply(age, k);
    }

    void Apply(float age, float k)
    {
        Color c = InkAssets.Ink;
        if (kind == Kind.Fleck)
        {
            float t = Mathf.Max(0f, k);
            transform.position = origin + direction * (distance * (1f - Mathf.Exp(-t * 13f)));
            float fade = t < FleckHold ? 1f : Mathf.Clamp01(1f - (t - FleckHold) / FleckFade);
            c.a = k < 0f ? 0f : baseAlpha * fade;
        }
        else
        {
            float t = Mathf.Clamp01(k / duration);
            float size = Mathf.Lerp(fromSize, toSize, 1f - (1f - t) * (1f - t));
            transform.localScale = new Vector3(size, size, 1f);
            c.a = baseAlpha * (1f - t);
        }
        spriteRenderer.color = c;
    }

    void UpdateReveal(float age)
    {
        if (revealTexture == null || spriteRenderer == null) return;
        float k = Mathf.Clamp01((age - delay) / duration);
        k = 1f - (1f - k) * (1f - k);
        if (Mathf.Approximately(k, shown)) return;
        shown = k;
        if (partialSprite != null) { Destroy(partialSprite); partialSprite = null; }
        spriteRenderer.sprite = k <= 0f ? null : partialSprite = InkAssets.LeftPart(revealTexture, k, new Vector2(0f, .5f));
        if (k >= 1f) enabled = false; // 走り切ったら、最後のスプライトのまま止める
    }

    void OnDestroy()
    {
        active.Remove(this);
        if (partialSprite != null) Destroy(partialSprite);
    }
}
