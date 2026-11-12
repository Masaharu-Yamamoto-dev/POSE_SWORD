using System.Collections.Generic;
using UnityEngine;
using TMPro;

// ダメージ数字。自分から見た関係（与えた／受けた／他人同士）とクリティカルかで見た目を変える。
// 同じ相手の上に続けて出たときは、前の数字と重ならないよう上へずらして重ねる（1人につき4つまで）。
// 撃破の一撃は、その場で止まって長めに残り、撃破演出の印（標準は墨の一閃と飛沫。DefeatEffects）が数字の位置に出る。
// 決着の一撃は消さずに残す。
public class DamagePopup : MonoBehaviour
{
    public enum Relation { Dealt, Taken, Others }

    // 文字の大きさは、カメラの縦の大きさがこの値の時に今までと同じになるよう合わせる。
    // カメラが引くほど数字を大きくして、画面上の大きさを保つ
    const float ReferenceOrthoSize = 10f;
    const int MaxPerTarget = 4;
    const float FadeTime = 0.3f;      // 最後のこの秒数で消えていく
    const float EvictFadeTime = 0.15f; // 5つ目が来て押し出された数字が消えるまでの秒数
    const int FrontSortingOrder = 30;
    const float LeanDistance = 1.0f;  // 当たった場所から、受けた剣の方へ寄せる距離（カメラの縦の大きさ10の時）
    // 上へ浮く速さ（以前の3から抑えた値）。プレハブに残っている古い floatSpeed が上書きしないよう定数にしている
    const float RiseSpeed = 1.0f;
    const float KoLife = 1.6f;        // 撃破の数字が残る秒数
    // 撃破の数字の大きさ。与えた／受けた／他人同士を問わず同じで、クリティカル（12）より一回り大きくして撃破だと分かるようにする
    const float KoSize = 15f;

    struct Style
    {
        public Color fill, outline, back;
        public float size, life, alpha, outlineWidth;
        public bool hasBack, slam;
    }

    static readonly List<DamagePopup> active = new List<DamagePopup>();
    // 縁取りの色・太さごとに作ったマテリアル。数字1つごとに作るとマテリアルが増え続けるので使い回す
    static readonly Dictionary<(int, Color32, float), Material> materialCache = new Dictionary<(int, Color32, float), Material>();

    TextMeshPro textMesh, backText;
    string targetId;
    bool leaving, slam, lethal, persist;
    // 撃破演出の印のうち、数字と一緒に薄れさせる物（第2要素は不透明度の倍率）
    readonly List<(SpriteRenderer renderer, float alpha)> followers = new List<(SpriteRenderer, float)>();
    float born, life, fade = FadeTime, baseAlpha = 1f, baseScale = 1f;
    Vector3 startPosition;
    // ダメージを受けた剣。生きている間は数字をこの剣についていかせ、どの剣の数字かを位置で読めるようにする
    SwordBattle follow;
    Vector3 anchor, offsetFromAnchor;
    Vector2 halfSize; // 画面上で占める大きさの半分（ワールド座標）。重なり判定に使う

    // target：ダメージを受けた剣。数字を当たった場所からその剣の方へ少し寄せて出し、以後はその剣についていく。
    // 相打ちで同じ場所に2つ出ても「与えた数字は相手側・受けた数字は自分側」に分かれる。
    // lethal（撃破の一撃）の時はついていかない。剣が吹き飛んだり、残機制で次の剣に持ち替わって
    // 出現位置へ移ったりするので、数字はその場に残す。persist（決着の一撃）の時は消さずに残す
    public static DamagePopup Spawn(GameObject prefab, Vector2 point, SwordBattle target, int damage, bool isCrit, bool lethal,
        Relation relation, Color attackerColor, string targetId, bool persist = false)
    {
        var obj = Instantiate(prefab, point, Quaternion.identity);
        var popup = obj.GetComponent<DamagePopup>();
        if (popup == null) { Destroy(obj); return null; }
        var style = StyleFor(relation, isCrit, attackerColor);
        if (lethal)
        {
            style.life = KoLife;
            style.size = KoSize;
            // 撃破の数字は、通常のヒットでもクリティカルと同じく叩きつけて出す
            style.slam = true;
            // 他人同士の撃破は、普段の半透明のままだと埋もれるので不透明にする（縁取りは攻撃した人の色のまま）
            if (relation == Relation.Others) style.alpha = 1f;
        }
        popup.Init(damage, isCrit, style, targetId, target, !lethal, lethal, persist);
        return popup;
    }

    // ▼【新規追加】ライフスティール（オレ達アタック）で回復した量。緑の「+数字」を、回復した剣の上に
    // ダメージの数字より少し小さく出し、ダメージの数字と同じく剣についていく
    public static DamagePopup SpawnHeal(GameObject prefab, SwordBattle healed, int amount, string healedId)
    {
        if (prefab == null || healed == null || amount <= 0) return null;
        var obj = Instantiate(prefab, (Vector2)healed.currentCenterPosition + Vector2.up * 2f, Quaternion.identity);
        var popup = obj.GetComponent<DamagePopup>();
        if (popup == null) { Destroy(obj); return null; }
        var style = new Style { fill = new Color32(120, 235, 140, 255), outline = new Color32(8, 48, 20, 255), outlineWidth = .28f,
            size = 8f, life = 1.0f, alpha = 1f };
        popup.Init(amount, false, style, healedId, healed, true, false, false, "+" + amount);
        return popup;
    }

    // ローカル対戦（TakeDamage）と旧NetworkManager経由の呼び出し用。自分が与えたダメージとして出す
    public void Setup(int damageAmount, bool isCrit)
    {
        Init(damageAmount, isCrit, StyleFor(Relation.Dealt, isCrit, Color.white), null, null);
    }

    public static void ClearAll()
    {
        foreach (var popup in active.ToArray()) if (popup != null) Destroy(popup.gameObject);
        active.Clear();
    }

    static Style StyleFor(Relation relation, bool isCrit, Color attackerColor)
    {
        var dark = new Color32(20, 20, 28, 230);
        switch (relation)
        {
            case Relation.Dealt:
                return isCrit
                    ? new Style { fill = new Color32(255, 211, 74, 255), outline = new Color32(90, 42, 0, 255), outlineWidth = .32f, size = 12f, life = 1.3f, alpha = 1f }
                    : new Style { fill = Color.white, outline = new Color32(27, 27, 34, 255), outlineWidth = .28f, size = 9f, life = 1.0f, alpha = 1f };
            case Relation.Taken:
                return isCrit
                    ? new Style { fill = new Color32(255, 59, 46, 255), outline = Color.white, outlineWidth = .25f, size = 12f, life = 1.3f, alpha = 1f,
                        hasBack = true, back = new Color32(42, 6, 6, 255) }
                    : new Style { fill = new Color32(255, 75, 62, 255), outline = new Color32(42, 6, 6, 255), outlineWidth = .28f, size = 9f, life = 1.0f, alpha = 1f };
            default:
                // 他人同士：小さく半透明。縁取りの内側を攻撃した人の色、外側を暗い色にして、どの背景でも読めるようにする
                attackerColor.a = 1f;
                return isCrit
                    ? new Style { fill = Color.white, outline = attackerColor, outlineWidth = .25f, size = 6.7f, life = 0.9f, alpha = .92f, hasBack = true, back = dark }
                    : new Style { fill = Color.white, outline = attackerColor, outlineWidth = .25f, size = 5.7f, life = 0.7f, alpha = .82f, hasBack = true, back = dark };
        }
    }

    // label：数字の代わりに出す文字（回復の「+数字」など）。省略すると damage をそのまま出す
    void Init(int damage, bool isCrit, Style style, string target, SwordBattle followTarget, bool keepFollowing = false,
        bool isLethal = false, bool keep = false, string label = null)
    {
        textMesh = GetComponent<TextMeshPro>();
        if (textMesh == null) { Destroy(gameObject); return; }
        targetId = target; life = style.life; baseAlpha = style.alpha;
        slam = isCrit || style.slam; lethal = isLethal; persist = keep;
        born = Time.unscaledTime;
        var cam = Camera.main;
        baseScale = cam != null && cam.orthographic ? Mathf.Clamp(cam.orthographicSize / ReferenceOrthoSize, .6f, 3f) : 1f;

        Material source = textMesh.fontSharedMaterial;
        textMesh.text = label ?? (isCrit ? damage + "!" : damage.ToString());
        textMesh.fontSize = style.size;
        textMesh.color = style.fill;
        textMesh.textWrappingMode = TextWrappingModes.NoWrap;
        // プレハブは上揃えなので、文字が置いた位置より下にはみ出していた。中央揃えにして、
        // 置いた位置＝文字の中心にする（重なり判定もこの前提）
        textMesh.alignment = TextAlignmentOptions.Center;
        textMesh.fontSharedMaterial = OutlineMaterial(source, style.outline, style.outlineWidth);
        textMesh.GetComponent<MeshRenderer>().sortingOrder = FrontSortingOrder;
        if (style.hasBack) backText = CreateBackLayer(source, style.back);

        transform.localScale = Vector3.one * baseScale;
        textMesh.ForceMeshUpdate();
        halfSize = (Vector2)textMesh.bounds.extents * baseScale;
        // 撃破の数字は、後ろに一閃が斜めに走るので、重なり判定の範囲を広めにとる
        if (lethal) halfSize *= 1.35f;
        if (followTarget != null) LeanTowardTarget(followTarget.currentCenterPosition);
        if (target != null) PlaceAwayFromOthers();
        startPosition = transform.position;
        // 置く場所が決まってから、撃破演出の印を数字の位置に出す（飛沫はこの時点の位置から飛ぶ）
        if (lethal)
            DefeatEffects.Current.PlayKillMark(new KillMarkContext {
                numberRoot = transform, numberBounds = textMesh.bounds, worldScale = baseScale,
                sortingOrder = FrontSortingOrder - 2, persist = persist,
                followNumberAlpha = (renderer, alpha) => followers.Add((renderer, alpha)) });
        if (followTarget != null && keepFollowing)
        {
            follow = followTarget;
            anchor = followTarget.currentCenterPosition;
            offsetFromAnchor = transform.position - anchor;
        }
        active.Add(this);
        SetAlpha(baseAlpha);
    }

    // 太い暗い縁取りだけを描く、同じ文字の影。手前の数字の縁取りのさらに外側に見える
    TextMeshPro CreateBackLayer(Material source, Color color)
    {
        TMP_FontAsset font = textMesh.font != null ? textMesh.font : TMP_Settings.defaultFontAsset;
        if (font == null) return null;
        // 有効なまま TextMeshPro を足すと、フォントを入れる前に一度メッシュを作ろうとして
        // 「Can't Generate Mesh, No Font Asset has been assigned.」が出る。非表示で作り、設定し終えてから表示する
        var obj = new GameObject("Outline");
        obj.SetActive(false);
        obj.transform.SetParent(transform, false);
        obj.transform.localPosition = new Vector3(0f, 0f, .01f);
        var back = obj.AddComponent<TextMeshPro>();
        back.font = font;
        back.fontSize = textMesh.fontSize;
        back.alignment = textMesh.alignment;
        back.textWrappingMode = TextWrappingModes.NoWrap;
        back.rectTransform.sizeDelta = textMesh.rectTransform.sizeDelta;
        back.rectTransform.pivot = textMesh.rectTransform.pivot;
        back.margin = textMesh.margin;
        back.text = textMesh.text;
        back.color = color;
        back.fontSharedMaterial = OutlineMaterial(source, color, .5f);
        back.GetComponent<MeshRenderer>().sortingOrder = FrontSortingOrder - 1;
        obj.SetActive(true);
        return back;
    }

    static Material OutlineMaterial(Material source, Color outline, float width)
    {
        var key = (source.GetInstanceID(), (Color32)outline, width);
        if (materialCache.TryGetValue(key, out var cached) && cached != null) return cached;
        var mat = new Material(source) { name = source.name + " (DamagePopup)" };
        mat.SetColor(ShaderUtilities.ID_OutlineColor, outline);
        mat.SetFloat(ShaderUtilities.ID_OutlineWidth, width);
        // 縁取りは文字の輪郭をまたいで内外に半分ずつ付くので、その分だけ文字を太らせて本体の細りを防ぐ
        mat.SetFloat(ShaderUtilities.ID_FaceDilate, width * .5f);
        ShaderUtilities.UpdateShaderRatios(mat);
        materialCache[key] = mat;
        return mat;
    }

    // 当たった場所から、ダメージを受けた剣の中心の方へ少しだけ寄せる（中心を越えるほどは動かさない）
    void LeanTowardTarget(Vector2 targetCenter)
    {
        Vector2 toTarget = targetCenter - (Vector2)transform.position;
        float distance = toTarget.magnitude;
        if (distance < .01f) return;
        float lean = Mathf.Min(LeanDistance * baseScale, distance * .8f);
        transform.position += (Vector3)(toTarget / distance * lean);
    }

    // 表示中の数字（相手を問わず）と重なるなら、その上へずらす。
    // 同じ相手の上に出せるのは4つまでで、5つ目からはその相手の一番古い数字を早めに消す（撃破の数字は消さない）。
    // 撃破の数字が出たときは、その相手の上に残っている数字を早めに消して、とどめの数字だけを残す
    void PlaceAwayFromOthers()
    {
        var sameTarget = active.FindAll(p => p != null && !p.leaving && p.targetId == targetId);
        var removable = sameTarget.FindAll(p => !p.lethal);
        if (lethal) removable.ForEach(p => p.LeaveEarly());
        else if (sameTarget.Count >= MaxPerTarget && removable.Count > 0) removable[0].LeaveEarly();

        var shown = active.FindAll(p => p != null && p != this && !p.leaving);
        Vector3 origin = transform.position;
        Vector3 pos = origin;
        for (int i = 0; i < 8; i++)
        {
            var overlap = shown.Find(p => Overlaps(p, pos));
            if (overlap == null) break;
            // 重なった数字のすぐ上へ。左右にも少し振って、縦一列に積み上がって見えるのを避ける
            float lift = (halfSize.y + overlap.halfSize.y) * .9f;
            pos = new Vector3(origin.x + (i % 2 == 0 ? .3f : -.2f) * baseScale, overlap.transform.position.y + lift, origin.z);
        }
        transform.position = pos;
    }

    bool Overlaps(DamagePopup other, Vector3 pos)
    {
        Vector3 d = other.transform.position - pos;
        return Mathf.Abs(d.x) < (halfSize.x + other.halfSize.x) * .9f &&
               Mathf.Abs(d.y) < (halfSize.y + other.halfSize.y) * .85f;
    }

    void LeaveEarly()
    {
        leaving = true;
        float age = Time.unscaledTime - born;
        life = Mathf.Min(life, age + EvictFadeTime);
        fade = EvictFadeTime;
    }

    // ヒットストップや決着のスローに巻き込まれないよう、実時間で動かす
    void Update()
    {
        if (textMesh == null) return;
        float age = Time.unscaledTime - born;
        if (!persist && age >= life) { Destroy(gameObject); return; }

        float s;
        if (slam) s = age < .1f ? 2f - (1f - Mathf.Pow(1f - age / .1f, 3f)) : 1f;   // 2倍から叩きつける
        else s = age < .06f ? .6f + age / .06f * .55f : age < .12f ? 1.15f - (age - .06f) / .06f * .15f : 1f; // ポンと出る
        transform.localScale = Vector3.one * (baseScale * s);
        // 受けた剣が生きている間は、その中心からの位置を保ったままついていく（回転にはついていかない）。
        // 中心はゲストでは同期の間隔でしか更新されないので、なめらかに追わせてカクつきを隠す。
        // 撃破された剣は吹き飛んでいくので、その時点で追うのをやめて数字をその場に残す
        if (follow != null && follow.IsAlive)
        {
            anchor = Vector3.Lerp(anchor, follow.currentCenterPosition, 1f - Mathf.Exp(-20f * Time.unscaledDeltaTime));
            startPosition = anchor + offsetFromAnchor;
        }
        // 撃破の数字は浮かずにその場で止まる
        transform.position = startPosition + Vector3.up * (lethal ? 0f : RiseSpeed * baseScale * age);

        // 決着の一撃は薄れさせない（試合の終わりに ClearAll で消える）
        float a = !persist && age > life - fade ? Mathf.Clamp01((life - age) / fade) : 1f;
        SetAlpha(baseAlpha * a);
    }

    void SetAlpha(float a)
    {
        textMesh.alpha = a;
        if (backText != null) backText.alpha = a;
        foreach (var follower in followers)
            if (follower.renderer != null) { var c = follower.renderer.color; c.a = a * follower.alpha; follower.renderer.color = c; }
    }

    void OnDestroy()
    {
        active.Remove(this);
    }
}
