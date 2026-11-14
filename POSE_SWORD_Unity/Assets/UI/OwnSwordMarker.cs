using UnityEngine;

// 自分の剣の真上に出す、プレイヤー色の小さな逆三角。どれが自分の剣かを見分けるための印。
// 剣の子にすると回転や巨大化まで一緒にかかるので、別のオブジェクトとして毎フレーム剣の上へついていかせる。
// ・剣がどの向きでも重ならないよう、剣の中心から「剣のいちばん遠い端までの距離」だけ真上に置く（回転しても変わらない）
// ・ダメージ数字と同じく、カメラが引くほど大きくして画面上の大きさを保つ
// ・倒されている間は薄れて消え、持ち替えて生き返ればまた出る
public class OwnSwordMarker : MonoBehaviour
{
    const float ReferenceOrthoSize = 10f;
    const float WorldWidth = 0.7f;     // カメラの縦の大きさ10の時の、三角の幅（ワールド座標）
    const float Gap = 0.35f;           // 剣の端から三角の先までの隙間（同上）
    const float BobHeight = 0.12f;     // 上下に浮く幅（同上）
    const float BobSpeed = 4.5f;
    const float FadeSpeed = 8f;
    // 剣から見た中心の位置を、同期で届く値へ寄せていく速さ。小さいほどブレないが、持ち替えなどで中心が変わった時に追いつくのが遅い
    const float CenterSettleSpeed = 6f;
    // ダメージ数字（30）より奥、ヒットエフェクトや倒された剣（20〜22）より手前
    const int SortingOrder = 25;

    static Sprite sharedSprite;

    SwordBattle target;
    SpriteRenderer marker;
    SpriteRenderer[] swordRenderers;
    float alpha;
    Vector3 localCenter;
    bool hasLocalCenter;

    public static OwnSwordMarker Attach(SwordBattle sword)
    {
        if (sword == null) return null;
        var obj = new GameObject("OwnSwordMarker");
        var m = obj.AddComponent<OwnSwordMarker>();
        m.target = sword;
        m.marker = obj.AddComponent<SpriteRenderer>();
        m.marker.sprite = SharedSprite();
        m.marker.sortingOrder = SortingOrder;
        var body = sword.BodyRenderer;
        if (body != null) m.marker.sortingLayerID = body.sortingLayerID;
        m.marker.color = WithAlpha(sword.PlayerMainColor, 0f);
        m.Follow();
        return m;
    }

    void LateUpdate()
    {
        if (target == null) { Destroy(gameObject); return; }
        float goal = target.IsAlive && target.gameObject.activeInHierarchy ? 1f : 0f;
        alpha = Mathf.MoveTowards(alpha, goal, FadeSpeed * Time.unscaledDeltaTime);
        marker.color = WithAlpha(target.PlayerMainColor, alpha);
        marker.enabled = alpha > 0f;
        if (marker.enabled) Follow();
    }

    void Follow()
    {
        var cam = Camera.main;
        float zoom = cam != null && cam.orthographic ? cam.orthographicSize / ReferenceOrthoSize : 1f;
        Vector3 center = SwordCenter();
        float bob = (Mathf.Sin(Time.unscaledTime * BobSpeed) * 0.5f + 0.5f) * BobHeight;
        float y = center.y + SwordRadius() + (Gap + bob) * zoom;
        // 先端（スプライトの基準点）を剣の真上に置く
        transform.SetPositionAndRotation(new Vector3(center.x, y, center.z), Quaternion.identity);
        float size = WorldWidth * zoom / sharedSprite.bounds.size.x;
        transform.localScale = new Vector3(size, size, 1f);
    }

    // 剣の回転の中心。currentCenterPosition はゲストだと同期が届いた時だけ飛び飛びに変わり、なめらかに動く剣の絵とずれるので、
    // 回すと三角がブレていた。剣から見た中心の位置（剣の中では動かない）をゆっくり追いかけて持ち、
    // 毎フレーム「今描かれている剣の位置と向き」から中心を求め直す
    Vector3 SwordCenter()
    {
        Transform t = target.transform;
        Vector3 measured = t.InverseTransformPoint(target.currentCenterPosition);
        if (!hasLocalCenter) { localCenter = measured; hasLocalCenter = true; }
        else localCenter = Vector3.Lerp(localCenter, measured, 1f - Mathf.Exp(-CenterSettleSpeed * Time.unscaledDeltaTime));
        return t.TransformPoint(localCenter);
    }

    // 剣の中心から、刀身と柄の絵のいちばん遠い角までの距離。剣から見た位置で測るので、向きにも同期の揺れにも左右されない。
    // 技のエフェクトも剣の子に付くことがあるので、測るのは刀身と柄だけにする
    float SwordRadius()
    {
        if (swordRenderers == null)
        {
            var controller = target.GetComponent<SwordController>();
            var handle = controller != null ? controller.handleObject : null;
            swordRenderers = new[] { target.BodyRenderer, handle != null ? handle.GetComponent<SpriteRenderer>() : null };
        }
        Transform root = target.transform;
        float max = 0f;
        foreach (var r in swordRenderers)
        {
            if (r == null || r.sprite == null || !r.enabled) continue;
            Bounds b = r.sprite.bounds;
            for (int i = 0; i < 4; i++)
            {
                Vector3 corner = new Vector3(i % 2 == 0 ? b.min.x : b.max.x, i < 2 ? b.min.y : b.max.y, 0f);
                Vector2 d = (Vector2)(root.InverseTransformPoint(r.transform.TransformPoint(corner)) - localCenter);
                max = Mathf.Max(max, d.sqrMagnitude);
            }
        }
        // 剣の大きさは縦横同じ倍率（巨大化もX/Yそろえて変わる）なので、X の倍率でワールドの長さに直す
        return Mathf.Sqrt(max) * Mathf.Abs(root.lossyScale.x);
    }

    static Color WithAlpha(Color c, float a) { c.a = a; return c; }

    // 白い塗りと墨色の縁取りの逆三角。色は SpriteRenderer.color で掛けるので、塗りだけがプレイヤー色になり、縁は暗いまま残る。
    // 基準点は下の先端
    static Sprite SharedSprite()
    {
        if (sharedSprite != null) return sharedSprite;
        const int W = 64, H = 56;
        const float outline = 5f;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        Vector2 tip = new Vector2(W / 2f, 2f), left = new Vector2(3f, H - 3f), right = new Vector2(W - 3f, H - 3f);
        Color ink = new Color(0.06f, 0.06f, 0.08f, 1f);
        var pixels = new Color[W * H];
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
            // 3辺それぞれへの内側向きの距離のうち、いちばん近いもの（外側なら負）
            float d = Mathf.Min(EdgeDistance(p, left, right), Mathf.Min(EdgeDistance(p, right, tip), EdgeDistance(p, tip, left)));
            float shape = Mathf.Clamp01(d + 0.5f);
            float fill = Mathf.Clamp01(d - outline + 0.5f);
            Color c = Color.Lerp(ink, Color.white, fill);
            c.a = shape;
            pixels[y * W + x] = c;
        }
        tex.SetPixels(pixels);
        tex.Apply();
        sharedSprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, tip.y / H), 100f);
        return sharedSprite;
    }

    // a→b の辺から点 p までの距離。三角の頂点は時計回りに並べてあるので、内側が正になる
    static float EdgeDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 e = (b - a).normalized;
        Vector2 n = new Vector2(e.y, -e.x);
        return Vector2.Dot(p - a, n);
    }
}
