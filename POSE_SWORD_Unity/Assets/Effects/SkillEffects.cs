using System.Collections.Generic;
using UnityEngine;

// 必殺技の「見た目」の部品。技の段取り（いつ跳ぶ・いつ撃ち出す・いつ終わる）は SwordBattle が持ち、
// ここは段階が変わった時と毎フレームの見た目だけを担当する。
// 将来エフェクトを選べるようにするときは、ISpinDashEffect を実装したものを増やして SkillEffects で選ぶ
public static class SkillEffects
{
    static ISpinDashEffect spinDash;
    public static ISpinDashEffect SpinDash => spinDash ?? (spinDash = new InkSpinDashEffect());
    static IGiantSpinEffect giantSpin;
    public static IGiantSpinEffect GiantSpin => giantSpin ?? (giantSpin = new InkGiantSpinEffect());

    static Sprite starSprite;
    // 五角の星（大きさ1ワールド単位、白）。色は SpriteRenderer.color で付ける
    public static Sprite Star
    {
        get
        {
            if (starSprite != null) return starSprite;
            const int size = 64;
            var points = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float r = i % 2 == 0 ? 1f : .45f, a = Mathf.PI / 2f + i * Mathf.PI / 5f;
                points[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // 縁を少しなめらかにするため、1ピクセルを4点で調べる
                    int inside = 0;
                    for (int sy = 0; sy < 2; sy++)
                        for (int sx = 0; sx < 2; sx++)
                        {
                            var q = new Vector2((x + .25f + .5f * sx) / size * 2f - 1f, (y + .25f + .5f * sy) / size * 2f - 1f);
                            if (InPolygon(q, points)) inside++;
                        }
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(inside * 255 / 4));
                }
            texture.SetPixels32(pixels);
            texture.Apply();
            return starSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), size, 0, SpriteMeshType.FullRect);
        }
    }

    static bool InPolygon(Vector2 p, Vector2[] polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            if ((polygon[i].y > p.y) != (polygon[j].y > p.y) &&
                p.x < (polygon[j].x - polygon[i].x) * (p.y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x)
                inside = !inside;
        return inside;
    }

    public static Material NewLineMaterial()
    {
        var shader = Shader.Find("Sprites/Default");
        return shader != null ? new Material(shader) : null;
    }
}

// 大回転斬の段階。SwordBattle.spinDashPhase の値と同じで、オンライン対戦ではホストからゲストへ同期する
public enum SpinDashStage
{
    None = 0,
    Hop = 1,       // 小ジャンプ
    Charge = 2,    // 空中で一瞬止まる（溜め）
    Dash = 3,      // 突進
    SlowMiss = 4,  // 外れて効果が切れた：減速して回転がゆるむ
    SlowHit = 5,   // 敵に当たった：少し弾かれて回転がゆるむ
    Descend = 6,   // ゆっくり降りる（操作できる）
}

public interface ISpinDashEffect
{
    // 剣1本ぶんの見た目を作る。作った物は ISpinDashLook.Dispose で片付ける
    ISpinDashLook Create(SwordBattle sword);
}

public interface ISpinDashLook
{
    // 段階が変わった時。duration はその段階の長さ（秒）
    void SetStage(SpinDashStage stage, float duration);
    // 毎フレーム（LateUpdate）。剣が動いた後に呼ばれる
    void Tick(float deltaTime);
    void Dispose();
}

// 標準：小ジャンプで風が巻き、溜めで光の輪が縮んで集まり、撃ち出す時に閃光と衝撃の輪。
// 突進中は筆の軌跡と回転の残像を引き、効果が切れると減速（弾かれ）の間にすっと消える
public class InkSpinDashEffect : ISpinDashEffect
{
    public ISpinDashLook Create(SwordBattle sword) => new InkSpinDashLook(sword);
}

public class InkSpinDashLook : ISpinDashLook
{
    // 見本（HTMLのモック）のピクセルをワールド単位に直す倍率。見本の突進は秒速1050px、ゲームは秒速50なので 1/21
    const float Px = 1f / 21f;
    static readonly Color SpinColor = new Color(.5f, 1f, 1f);
    // 筆の軌跡の芯：プレイヤーの色をこの倍率で暗くした濃い色
    const float TrailCoreShade = .72f;

    // 小ジャンプの風：3本の弧が剣のまわりを回りながら縮む
    const int WindArcs = 3, WindArcPoints = 12;
    const float WindArcLength = 1.6f, WindTurnSpeed = 14f, WindRadiusFrom = 70f, WindRadiusTo = 44f, WindWidth = 3f, WindAlpha = .55f;
    // 溜め：光の輪と光の粒が剣へ縮んで集まる
    const int ChargeDots = 12;
    const float ChargeRingFrom = 95f, ChargeRingTo = 14f, ChargeRingWidth = 4f, ChargeDotFrom = 110f, ChargeDotTo = 10f, ChargeDotSize = 5f;
    // 筆の軌跡（色の濃淡）：外側にプレイヤーの色の淡いにじみ、内側に同じ色を濃くした芯。幅は先頭の太さ（後ろへ細くなる）。
    // 以前は真ん中に墨の線と白い芯があったが、目立ちすぎるのでやめた
    const float TrailTime = .16f;
    // 軌跡の長さは時間ではなく道のりで決める（突進の速さ秒速50で TrailTime ぶん＝8）。
    // 時間で決めると、壁や障害物で跳ねて遅くなった時に軌跡が短く弱くなる。遅い時に伸ばしすぎないよう上限を置く
    const float TrailLength = 50f * TrailTime, TrailTimeMax = .4f;
    // 残像と光の円盤は、剣が動いている間だけ出す。壁や足場に引っかかって止まっている時に出しっぱなしだと、
    // 動いているのか止まっているのか分からないため。この速さ以下で消え、MovingSpeed 以上で全部見える
    const float StoppedSpeed = 4f, MovingSpeed = 16f, MotionFadeSpeed = 10f;
    static readonly float[] TrailWidths = { 38f, 12f };
    static readonly float[] TrailTailWidths = { 2f, 0f };
    static readonly float[] TrailAlphas = { .45f, .95f };
    // 他の剣に当たった時の飛沫：当たった所（剣の中心から突進の向きへ HitSplashForward）から、突進の向きの先へ飛ぶ
    const float HitSplashForward = 22f, HitSplashShade = .85f;
    // 回転の残像：剣の後ろに3枚。回転の向きと反対へ少しずつずらす
    const int Afterimages = 3;
    const float AfterimageStep = 28.6f, AfterimageAlpha = .38f, SpinDiscSize = 104f, SpinDiscAlpha = .14f;

    readonly SwordBattle sword;
    readonly List<Object> created = new List<Object>();
    SpinDashStage stage;
    float stageTime, stageDuration, hopTime;
    float lastDeltaTime, trailSpeed = 50f, motion = 1f;
    Vector2 travelDirection = Vector2.down;
    Vector3 lastPivot;
    float fade = 1f;
    float lastRotation, spinSign = 1f;

    LineRenderer[] windArcs;
    LineRenderer chargeRing;
    SpriteRenderer[] chargeDots;
    Vector2[] chargeDotDirections;
    Transform trailRoot;
    TrailRenderer[] trails;
    SpriteRenderer spinDisc;
    readonly List<(SpriteRenderer source, SpriteRenderer[] ghosts)> afterimages = new List<(SpriteRenderer, SpriteRenderer[])>();

    public InkSpinDashLook(SwordBattle sword)
    {
        this.sword = sword;
        lastRotation = sword.transform.eulerAngles.z;
    }

    int BaseOrder => sword.BodyRenderer != null ? sword.BodyRenderer.sortingOrder : 0;
    int SortingLayer => sword.BodyRenderer != null ? sword.BodyRenderer.sortingLayerID : 0;
    Color PlayerColor => sword.PlayerMainColor;

    public void SetStage(SpinDashStage next, float duration)
    {
        SpinDashStage previous = stage;
        stage = next;
        stageTime = 0f;
        stageDuration = Mathf.Max(.0001f, duration);
        if (next == SpinDashStage.Hop || (next == SpinDashStage.Charge && previous != SpinDashStage.Hop)) hopTime = 0f;
        if (next == SpinDashStage.Dash) BeginDash();
        if (next == SpinDashStage.SlowMiss || next == SpinDashStage.SlowHit) fade = 1f;
        // 他の剣に当たって弾かれ始めた時：プレイヤーの色の飛沫。段階は同期されているので、ゲストの画面にも出る
        if (next == SpinDashStage.SlowHit && previous == SpinDashStage.Dash)
            SpinDashSplash.Play((Vector2)sword.transform.position + travelDirection * (HitSplashForward * Px), travelDirection,
                PlayerColor * HitSplashShade, SortingLayer, BaseOrder + 2);
        if (next == SpinDashStage.Descend || next == SpinDashStage.None) EndTrail();
        if (next != SpinDashStage.Hop && next != SpinDashStage.Charge) ShowWind(false);
        if (next != SpinDashStage.Charge) ShowCharge(false);
    }

    public void Tick(float dt)
    {
        stageTime += dt;
        lastDeltaTime = dt;
        UpdateSpinSign();
        switch (stage)
        {
            case SpinDashStage.Hop:
                hopTime += dt;
                UpdateWind();
                break;
            case SpinDashStage.Charge:
                hopTime += dt;
                UpdateWind();
                UpdateCharge(Mathf.Clamp01(stageTime / stageDuration));
                break;
            case SpinDashStage.Dash:
                fade = 1f;
                UpdateDash();
                break;
            case SpinDashStage.SlowMiss:
            case SpinDashStage.SlowHit:
                fade = 1f - Mathf.Clamp01(stageTime / stageDuration);
                UpdateDash();
                break;
        }
    }

    // 回転の向き（残像をずらす向き）。ゲストは剣を回していないので、見えている角度の変化から決める
    void UpdateSpinSign()
    {
        float rotation = sword.transform.eulerAngles.z;
        float delta = Mathf.DeltaAngle(lastRotation, rotation);
        lastRotation = rotation;
        if (Mathf.Abs(delta) > .5f) spinSign = Mathf.Sign(delta);
    }

    // ---------- 小ジャンプの風 ----------
    void UpdateWind()
    {
        if (windArcs == null)
        {
            windArcs = new LineRenderer[WindArcs];
            for (int i = 0; i < WindArcs; i++) windArcs[i] = NewLine("SpinDashWind", WindArcPoints, false, BaseOrder + 1);
        }
        // 小ジャンプ＋溜めの長さ（0.3秒）で一巡する
        float k = Mathf.Clamp01(hopTime / .3f);
        float radius = Mathf.Lerp(WindRadiusFrom, WindRadiusTo, k) * Px;
        Color c = Color.white;
        c.a = WindAlpha * Mathf.Sin(Mathf.PI * k);
        Vector3 centre = sword.transform.position;
        for (int i = 0; i < WindArcs; i++)
        {
            var line = windArcs[i];
            float start = -hopTime * WindTurnSpeed + i * Mathf.PI * 2f / WindArcs;
            for (int p = 0; p < WindArcPoints; p++)
            {
                float a = start + WindArcLength * p / (WindArcPoints - 1);
                line.SetPosition(p, centre + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius);
            }
            line.widthMultiplier = WindWidth * Px;
            line.startColor = line.endColor = c;
            line.enabled = true;
        }
    }

    void ShowWind(bool show)
    {
        if (windArcs == null) return;
        foreach (var line in windArcs) if (line != null) line.enabled = show;
    }

    // ---------- 溜め ----------
    void UpdateCharge(float k)
    {
        if (chargeRing == null)
        {
            chargeRing = NewLine("SpinDashCharge", 48, true, BaseOrder + 1);
            chargeDots = new SpriteRenderer[ChargeDots];
            chargeDotDirections = new Vector2[ChargeDots];
            for (int i = 0; i < ChargeDots; i++)
            {
                chargeDots[i] = NewSprite("SpinDashChargeDot", InkAssets.Dot, BaseOrder + 1);
                float a = Random.Range(0f, Mathf.PI * 2f);
                chargeDotDirections[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(.7f, 1.2f);
            }
        }
        float ease = k * k * k;
        Vector3 centre = sword.transform.position;
        SetCircle(chargeRing, centre, Mathf.Lerp(ChargeRingFrom, ChargeRingTo, ease) * Px);
        chargeRing.widthMultiplier = ChargeRingWidth * Px;
        Color ring = PlayerColor;
        ring.a = .4f + .6f * k;
        chargeRing.startColor = chargeRing.endColor = ring;
        chargeRing.enabled = true;
        float dotDistance = Mathf.Lerp(ChargeDotFrom, ChargeDotTo, ease) * Px;
        Color dot = PlayerColor;
        dot.a = k;
        for (int i = 0; i < ChargeDots; i++)
        {
            var r = chargeDots[i];
            r.transform.position = centre + (Vector3)(chargeDotDirections[i] * dotDistance);
            r.transform.localScale = Vector3.one * (ChargeDotSize * Px);
            r.color = dot;
            r.enabled = true;
        }
    }

    void ShowCharge(bool show)
    {
        if (chargeRing != null) chargeRing.enabled = show;
        if (chargeDots != null) foreach (var r in chargeDots) if (r != null) r.enabled = show;
    }

    // ---------- 突進 ----------
    void BeginDash()
    {
        fade = 1f;
        SpinDashBurst.Play(sword.transform.position, PlayerColor, SortingLayer, BaseOrder + 2);

        if (trailRoot == null)
        {
            var root = new GameObject("SpinDashTrail");
            created.Add(root);
            trailRoot = root.transform;
            trails = new TrailRenderer[TrailWidths.Length];
            for (int i = 0; i < TrailWidths.Length; i++)
            {
                var child = new GameObject("Trail" + i);
                child.transform.SetParent(trailRoot, false);
                var trail = child.AddComponent<TrailRenderer>();
                trail.time = TrailTime;
                trail.minVertexDistance = .05f;
                trail.widthMultiplier = TrailWidths[i] * Px;
                trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, TrailTailWidths[i] / TrailWidths[i]);
                trail.numCapVertices = 2;
                trail.material = NewMaterial();
                trail.sortingLayerID = SortingLayer;
                // 剣より奥に、にじみ → 芯の順で重ねる
                trail.sortingOrder = BaseOrder - 4 + i;
                trails[i] = trail;
            }
        }
        trailRoot.position = sword.transform.position;
        lastPivot = sword.transform.position;
        trailSpeed = 50f;
        motion = 1f;
        foreach (var trail in trails) { trail.Clear(); trail.time = TrailTime; trail.emitting = true; }

        BuildAfterimages();
    }

    void UpdateDash()
    {
        Vector3 pivot = sword.transform.position;
        if (trailRoot != null)
        {
            trailRoot.position = pivot;
            // 見えている動きから速さを測る（ゲストは剣を物理で動かしていないため）。ヒットストップで止まっている間は測らない
            if (stage == SpinDashStage.Dash && lastDeltaTime > .0001f)
            {
                Vector3 step = pivot - lastPivot;
                float speed = step.magnitude / lastDeltaTime;
                if (speed > StoppedSpeed) travelDirection = ((Vector2)step).normalized;
                trailSpeed = Mathf.Lerp(trailSpeed, speed, .3f);
                float time = Mathf.Clamp(TrailLength / Mathf.Max(trailSpeed, .01f), TrailTime, TrailTimeMax);
                foreach (var trail in trails) trail.time = time;
                float target = Mathf.InverseLerp(StoppedSpeed, MovingSpeed, trailSpeed);
                motion = Mathf.MoveTowards(motion, target, MotionFadeSpeed * lastDeltaTime);
            }
            lastPivot = pivot;
            if (stage != SpinDashStage.Dash) foreach (var trail in trails) trail.emitting = false;
            Color core = PlayerColor * TrailCoreShade;
            Color[] colors = { PlayerColor, core };
            for (int i = 0; i < trails.Length; i++)
            {
                Color c = colors[i];
                c.a = TrailAlphas[i] * fade;
                trails[i].startColor = trails[i].endColor = c;
            }
        }

        if (spinDisc == null) spinDisc = NewSprite("SpinDashDisc", InkAssets.Dot, BaseOrder - 6);
        spinDisc.transform.position = pivot;
        spinDisc.transform.localScale = Vector3.one * (SpinDiscSize * Px);
        Color disc = SpinColor;
        disc.a = SpinDiscAlpha * fade * motion;
        spinDisc.color = disc;
        spinDisc.enabled = fade * motion > 0f;

        foreach (var (source, ghosts) in afterimages)
        {
            for (int k = 0; k < ghosts.Length; k++)
            {
                var ghost = ghosts[k];
                if (ghost == null) continue;
                bool show = source != null && source.enabled && fade * motion > 0f;
                ghost.enabled = show;
                if (!show) continue;
                var turn = Quaternion.Euler(0f, 0f, -spinSign * AfterimageStep * (k + 1));
                ghost.transform.position = pivot + turn * (source.transform.position - pivot);
                ghost.transform.rotation = turn * source.transform.rotation;
                ghost.transform.localScale = source.transform.lossyScale;
                ghost.sprite = source.sprite;
                ghost.flipX = source.flipX; ghost.flipY = source.flipY;
                Color c = SpinColor;
                c.a = AfterimageAlpha / (k + 1) * fade * motion;
                ghost.color = c;
            }
        }
    }

    // 剣を形作る絵（刀身と柄）を写した残像を作る。持ち替えで絵が変わることがあるので、突進のたびに作り直す
    void BuildAfterimages()
    {
        ClearAfterimages();
        int order = BaseOrder - 5;
        foreach (var source in sword.GetComponentsInChildren<SpriteRenderer>())
        {
            if (source == null || source.sprite == null || !source.enabled || source.name.StartsWith(InkDefeatEffect.BleedName)) continue;
            var ghosts = new SpriteRenderer[Afterimages];
            for (int k = 0; k < Afterimages; k++)
            {
                ghosts[k] = NewSprite("SpinDashAfterimage", source.sprite, order);
                ghosts[k].sharedMaterial = source.sharedMaterial;
                ghosts[k].enabled = false;
            }
            afterimages.Add((source, ghosts));
        }
    }

    void ClearAfterimages()
    {
        foreach (var (_, ghosts) in afterimages)
            foreach (var ghost in ghosts)
                if (ghost != null) { created.Remove(ghost.gameObject); Object.Destroy(ghost.gameObject); }
        afterimages.Clear();
    }

    void EndTrail()
    {
        fade = 0f;
        if (trails != null) foreach (var trail in trails) if (trail != null) { trail.emitting = false; trail.Clear(); }
        if (spinDisc != null) spinDisc.enabled = false;
        ClearAfterimages();
    }

    public void Dispose()
    {
        afterimages.Clear();
        foreach (var obj in created)
        {
            if (obj == null) continue;
            if (obj is GameObject go)
            {
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    if (r.sharedMaterial != null && r.sharedMaterial.name == MaterialName) Object.Destroy(r.sharedMaterial);
                Object.Destroy(go);
            }
            else Object.Destroy(obj);
        }
        created.Clear();
    }

    // ---------- 部品 ----------
    const string MaterialName = "SpinDashLine";

    static Material NewMaterial()
    {
        var shader = Shader.Find("Sprites/Default");
        return shader != null ? new Material(shader) { name = MaterialName } : null;
    }

    // 剣の子にすると大きさ（巨大化など）に引きずられるので、どれも独立したオブジェクトにして Dispose で片付ける
    LineRenderer NewLine(string objectName, int points, bool loop, int order)
    {
        var obj = new GameObject(objectName);
        created.Add(obj);
        var line = obj.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.loop = loop;
        line.positionCount = points;
        line.numCapVertices = 2;
        line.material = NewMaterial();
        line.sortingLayerID = SortingLayer;
        line.sortingOrder = order;
        line.enabled = false;
        return line;
    }

    SpriteRenderer NewSprite(string objectName, Sprite sprite, int order)
    {
        var obj = new GameObject(objectName);
        created.Add(obj);
        var r = obj.AddComponent<SpriteRenderer>();
        r.sprite = sprite;
        r.sortingLayerID = SortingLayer;
        r.sortingOrder = order;
        r.enabled = false;
        return r;
    }

    static void SetCircle(LineRenderer line, Vector3 centre, float radius)
    {
        int n = line.positionCount;
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2f / n;
            line.SetPosition(i, centre + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius);
        }
    }
}

// 大回転斬を撃ち出した場所に出る、閃光と衝撃の輪（白い輪の内側にプレイヤーの色の輪）。出した場所に残り、剣にはついていかない
public class SpinDashBurst : MonoBehaviour
{
    const float Px = 1f / 21f;
    const float FlashSize = 84f, FlashTime = .08f, RingTime = .4f;
    const float OuterFrom = 12f, OuterTo = 110f, OuterWidth = 6f, InnerFrom = 8f, InnerTo = 90f, InnerWidth = 3f;
    const int RingPoints = 48;

    SpriteRenderer flash;
    LineRenderer outer, inner;
    Color playerColor;
    float time;

    public static void Play(Vector3 position, Color playerColor, int sortingLayer, int sortingOrder)
    {
        var obj = new GameObject("SpinDashBurst");
        obj.transform.position = position;
        var burst = obj.AddComponent<SpinDashBurst>();
        burst.playerColor = playerColor;
        burst.flash = obj.AddComponent<SpriteRenderer>();
        burst.flash.sprite = InkAssets.Dot;
        burst.flash.sortingLayerID = sortingLayer;
        burst.flash.sortingOrder = sortingOrder;
        burst.outer = Ring(obj.transform, "Outer", sortingLayer, sortingOrder + 1);
        burst.inner = Ring(obj.transform, "Inner", sortingLayer, sortingOrder + 2);
        burst.Apply();
    }

    static LineRenderer Ring(Transform parent, string objectName, int sortingLayer, int sortingOrder)
    {
        var obj = new GameObject(objectName);
        obj.transform.SetParent(parent, false);
        var line = obj.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.loop = true;
        line.positionCount = RingPoints;
        var shader = Shader.Find("Sprites/Default");
        if (shader != null) line.material = new Material(shader);
        line.sortingLayerID = sortingLayer;
        line.sortingOrder = sortingOrder;
        return line;
    }

    void Update()
    {
        time += Time.deltaTime;
        if (time >= RingTime) { Destroy(gameObject); return; }
        Apply();
    }

    void Apply()
    {
        Vector3 centre = transform.position;
        float flashK = Mathf.Clamp01(time / FlashTime);
        flash.transform.localScale = Vector3.one * (FlashSize * Px);
        flash.color = new Color(1f, 1f, 1f, 1f - flashK);
        float k = Mathf.Clamp01(time / RingTime);
        float ease = 1f - Mathf.Pow(1f - k, 3f);
        SetRing(outer, centre, Mathf.Lerp(OuterFrom, OuterTo, ease) * Px, OuterWidth * Px, new Color(1f, 1f, 1f, 1f - k));
        Color c = playerColor;
        c.a = 1f - k;
        SetRing(inner, centre, Mathf.Lerp(InnerFrom, InnerTo, ease) * Px, InnerWidth * Px, c);
    }

    static void SetRing(LineRenderer line, Vector3 centre, float radius, float width, Color color)
    {
        for (int i = 0; i < RingPoints; i++)
        {
            float a = i * Mathf.PI * 2f / RingPoints;
            line.SetPosition(i, centre + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius);
        }
        line.widthMultiplier = width;
        line.startColor = line.endColor = color;
    }

    void OnDestroy()
    {
        foreach (var line in new[] { outer, inner })
            if (line != null && line.material != null) Destroy(line.material);
    }
}

// 大回転斬が他の剣に当たった所から、突進の向きの先へ飛ぶ飛沫（プレイヤーの色）。
// 大きい粒は前へ狭く、小さい粒は広めに散る。ヒットストップ中も止めずに飛ばす
public class SpinDashSplash : MonoBehaviour
{
    const float Px = 1f / 21f;
    const float Duration = .45f;
    // (粒の数, 飛ぶ距離, 粒の大きさ, 散る角度[ラジアン])
    static readonly (int count, float reach, float size, float spread)[] Layers = { (16, 120f, 12f, 1.5f), (10, 55f, 6f, 2.4f) };

    struct Drop { public SpriteRenderer renderer; public Vector2 direction; public float distance, size; }
    Drop[] drops;
    Color color;
    float time;

    public static void Play(Vector2 position, Vector2 direction, Color color, int sortingLayer, int sortingOrder, float scale = 1f)
    {
        var obj = new GameObject("SpinDashSplash");
        obj.transform.position = position;
        var splash = obj.AddComponent<SpinDashSplash>();
        splash.color = color;
        float baseAngle = Mathf.Atan2(direction.y, direction.x);
        var list = new System.Collections.Generic.List<Drop>();
        foreach (var layer in Layers)
            for (int i = 0; i < layer.count; i++)
            {
                var child = new GameObject("Drop");
                child.transform.SetParent(obj.transform, false);
                var r = child.AddComponent<SpriteRenderer>();
                r.sprite = InkAssets.Dot;
                r.sortingLayerID = sortingLayer;
                r.sortingOrder = sortingOrder;
                float angle = baseAngle + (Random.value - .5f) * layer.spread;
                list.Add(new Drop
                {
                    renderer = r,
                    direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)),
                    distance = layer.reach * Px * scale * (.25f + Random.value * .85f),
                    size = layer.size * Px * scale * (.4f + Random.value * .9f),
                });
            }
        splash.drops = list.ToArray();
        splash.Apply();
    }

    void Update()
    {
        time += Time.unscaledDeltaTime;
        if (time >= Duration) { Destroy(gameObject); return; }
        Apply();
    }

    void Apply()
    {
        float k = Mathf.Clamp01(time / Duration);
        float ease = 1f - Mathf.Pow(1f - k, 3f);
        Color c = color;
        c.a = 1f - k;
        foreach (var drop in drops)
        {
            drop.renderer.transform.localPosition = drop.direction * (drop.distance * ease);
            drop.renderer.transform.localScale = Vector3.one * (drop.size * (1f - .6f * k));
            drop.renderer.color = c;
        }
    }
}

// ======================================================================
// 巨大回転斬
// ======================================================================

// 巨大回転斬の段階。SwordBattle.giantSpinPhase の値と同じで、オンライン対戦ではホストからゲストへ同期する。
// Stagger（ふらつき）だけは同期せず、技番号（ふらつき）から決める
public enum GiantSpinStage
{
    None = 0,
    HiltGlow = 1, // 柄が光る
    Grow = 2,     // 3段階で巨大化
    Windup = 3,   // 振りかぶり
    Spin = 4,     // 一回転（攻撃判定はここだけ）
    Linger = 5,   // 余韻
    Shrink = 6,   // 縮む
    Stagger = 7,  // ふらつき
}

public interface IGiantSpinEffect
{
    IGiantSpinLook Create(SwordBattle sword);
}

public interface IGiantSpinLook
{
    void SetStage(GiantSpinStage stage, float duration);
    void Tick(float deltaTime);
    void Dispose();
}

public class InkGiantSpinEffect : IGiantSpinEffect
{
    public IGiantSpinLook Create(SwordBattle sword) => new InkGiantSpinLook(sword);
}

// 標準：柄が光って光の粒が集まり、巨大化の各段で輪が広がる。届く範囲は欠けのない細い円。
// 一回転では刀身の通った跡に扇形の斬撃の跡（色の濃淡）と残像。当たると飛沫。ふらつきは揺れと黄色い星。
// 演出の色はプレイヤーの色（剣そのものが赤くなるのは技の見分けなので、ここでは変えない）
public class InkGiantSpinLook : IGiantSpinLook
{
    // 見本（HTMLのモック）は、普段の剣の背の高さが72px、柄から頭の先まで(届く範囲)がその1.05倍。
    // 見本のピクセルをワールド単位に直す倍率は、技を始めた時の届く範囲から決める
    const float MockReach = 72f * 1.05f;
    const float CoreShade = .72f;

    // 柄が光る
    const int GlowParticles = 12;
    const float GlowHaloSize = 68f, GlowHaloAlpha = .35f, GlowParticleFrom = 80f, GlowParticleTo = 6f, GlowParticleSize = 5.2f;
    const float SparkleFrom = 8f, SparkleTo = 30f, SparkleTime = .1f;
    static readonly Color Gold = new Color(1f, .85f, .3f);
    // 巨大化の輪は GiantSpinRing：各段で回転の中心から、その段の届く範囲まで広がる
    // 届く範囲の円
    const int CirclePoints = 64;
    const float CircleAppearTime = .15f, CircleWidth = .0045f, CircleSpinWidth = .01f, CircleAlpha = .55f, CircleSpinAlpha = .9f;
    // 斬撃の跡：刀身の後ろ SlashSpan 度の扇形。外側の淡いにじみ（半径の .4〜1）と、濃い刃先（.84〜1）。
    // 外側は届く範囲の円にぴったり合わせる（はみ出すと円より遠くまで届くように見えるため）
    const int SlashSlices = 16;
    const float SlashSpan = 110f, SlashInner = .4f, SlashEdgeInner = .84f, SlashOuter = 1f, SlashAlpha = .32f, SlashEdgeAlpha = .9f;
    // 残像
    const int Afterimages = 2;
    const float AfterimageStep = 14f, AfterimageAlpha = .26f;
    // 当たった時の飛沫（大回転斬の飛沫を少し大きく）
    const float SplashScale = 1.25f;
    // ふらつき：左右に揺れ、頭の上を黄色い星が回る
    const float WobbleAngle = 7f, WobbleSpeed = 17f, WobbleDecay = .4f;
    const int Stars = 3;
    const float StarOrbitX = 18f, StarOrbitY = 5f, StarSize = 13f, StarSpin = 7f, StarLift = 16f, StarOutline = 1.25f;
    static readonly Color StarColor = new Color32(255, 210, 63, 255), StarEdge = new Color32(184, 134, 11, 255);

    readonly SwordBattle sword;
    readonly List<GameObject> created = new List<GameObject>();
    readonly List<Material> materials = new List<Material>();
    GiantSpinStage stage;
    float stageTime, stageDuration, unit = 1f / 21f, sinceGrow, sinceSpin;
    int growRingsShown;
    float lastRotation, spinSign = 1f, swept;
    Vector3 bladeLocalDirection = Vector3.up;

    SpriteRenderer halo;
    SpriteRenderer[] particles;
    Vector2[] particleDirections;
    LineRenderer circle;
    Mesh slashMesh;
    MeshRenderer slashRenderer;
    float slashHeadAngle, slashSpanNow;
    readonly List<(SpriteRenderer source, SpriteRenderer[] ghosts)> afterimages = new List<(SpriteRenderer, SpriteRenderer[])>();
    readonly Dictionary<SwordBattle, int> opponentHp = new Dictionary<SwordBattle, int>();
    readonly List<(SpriteRenderer source, SpriteRenderer standIn)> standIns = new List<(SpriteRenderer, SpriteRenderer)>();
    SpriteRenderer[] stars, starEdges;

    public InkGiantSpinLook(SwordBattle sword)
    {
        this.sword = sword;
        lastRotation = sword.transform.eulerAngles.z;
    }

    int BaseOrder => sword.BodyRenderer != null ? sword.BodyRenderer.sortingOrder : 0;
    int SortingLayer => sword.BodyRenderer != null ? sword.BodyRenderer.sortingLayerID : 0;
    Color FxColor => sword.PlayerMainColor;
    Vector3 Pivot => sword.transform.position;

    public void SetStage(GiantSpinStage next, float duration)
    {
        GiantSpinStage previous = stage;
        stage = next;
        stageTime = 0f;
        stageDuration = Mathf.Max(.0001f, duration);

        if (next == GiantSpinStage.HiltGlow || (next == GiantSpinStage.Grow && previous != GiantSpinStage.HiltGlow))
            unit = Mathf.Max(.0001f, sword.GiantSpinReachNow / MockReach);
        if (previous == GiantSpinStage.HiltGlow) PlayHiltSparkle();
        if (next == GiantSpinStage.Grow) { sinceGrow = 0f; growRingsShown = 0; }
        if (next == GiantSpinStage.Spin) BeginSpin();
        if (next != GiantSpinStage.HiltGlow) ShowGlow(false);
        if (next != GiantSpinStage.Spin && next != GiantSpinStage.Linger) { if (slashRenderer != null) slashRenderer.enabled = false; }
        if (next != GiantSpinStage.Spin) ClearAfterimages();
        if (next == GiantSpinStage.Stagger) BeginStagger(); else EndStagger();
        if (next == GiantSpinStage.Shrink || next == GiantSpinStage.Stagger || next == GiantSpinStage.None) { if (circle != null) circle.enabled = false; }
    }

    public void Tick(float dt)
    {
        stageTime += dt;
        sinceGrow += dt;
        sinceSpin += dt;
        float rotation = sword.transform.eulerAngles.z;
        float delta = Mathf.DeltaAngle(lastRotation, rotation);
        lastRotation = rotation;
        if (Mathf.Abs(delta) > .5f) spinSign = Mathf.Sign(delta);

        switch (stage)
        {
            case GiantSpinStage.HiltGlow: UpdateGlow(Mathf.Clamp01(stageTime / stageDuration)); break;
            case GiantSpinStage.Grow:
                while (growRingsShown < 3 && stageTime >= stageDuration * growRingsShown / 3f)
                {
                    // その段で届く範囲：今の届く範囲を今の倍率で割り、その段の倍率を掛ける
                    float step = (growRingsShown + 1) / 3f;
                    float reachAtStep = unit * MockReach * Mathf.Lerp(1f, sword.giantSpinScale, step);
                    GiantSpinRing.Play(Pivot, unit * 10f, reachAtStep, FxColor, SortingLayer, BaseOrder + 1);
                    growRingsShown++;
                }
                UpdateCircle();
                break;
            case GiantSpinStage.Windup: UpdateCircle(); break;
            case GiantSpinStage.Spin:
                swept += Mathf.Abs(delta);
                UpdateCircle();
                UpdateSlash(1f);
                UpdateAfterimages();
                CheckHits();
                break;
            case GiantSpinStage.Linger:
                UpdateCircle();
                UpdateSlash(1f - Mathf.Clamp01(stageTime / stageDuration));
                CheckHits();
                break;
            case GiantSpinStage.Stagger: UpdateStagger(); break;
        }
    }

    // ---------- 柄が光る ----------
    Vector3 HiltPosition
    {
        get
        {
            foreach (var t in sword.GetComponentsInChildren<Transform>())
                if (t.CompareTag("Handle")) return t.position;
            return Pivot;
        }
    }

    void UpdateGlow(float k)
    {
        if (halo == null)
        {
            halo = NewSprite("GiantSpinHiltHalo", InkAssets.Dot, BaseOrder + 2);
            particles = new SpriteRenderer[GlowParticles];
            particleDirections = new Vector2[GlowParticles];
            for (int i = 0; i < GlowParticles; i++)
            {
                particles[i] = NewSprite("GiantSpinHiltParticle", InkAssets.Dot, BaseOrder + 2);
                float a = Random.Range(0f, Mathf.PI * 2f);
                particleDirections[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(.7f, 1.2f);
            }
        }
        Vector3 hilt = HiltPosition;
        halo.transform.position = hilt;
        halo.transform.localScale = Vector3.one * (GlowHaloSize * unit);
        Color h = Gold; h.a = GlowHaloAlpha * Mathf.Sin(Mathf.PI * k);
        halo.color = h;
        halo.enabled = true;
        float distance = Mathf.Lerp(GlowParticleFrom, GlowParticleTo, k * k * k) * unit;
        Color c = Gold; c.a = k;
        for (int i = 0; i < GlowParticles; i++)
        {
            particles[i].transform.position = hilt + (Vector3)(particleDirections[i] * distance);
            particles[i].transform.localScale = Vector3.one * (GlowParticleSize * unit);
            particles[i].color = c;
            particles[i].enabled = true;
        }
    }

    void ShowGlow(bool show)
    {
        if (halo != null) halo.enabled = show;
        if (particles != null) foreach (var p in particles) if (p != null) p.enabled = show;
    }

    // 光り終わりに、柄で小さくきらめく（白い十字の光）
    void PlayHiltSparkle()
    {
        SkillSparkle.Play(HiltPosition, SparkleFrom * unit, SparkleTo * unit, SparkleTime, SortingLayer, BaseOrder + 3);
    }

    // ---------- 届く範囲の円 ----------
    void UpdateCircle()
    {
        if (circle == null)
        {
            var obj = NewObject("GiantSpinReach_" + sword.name);
            circle = obj.AddComponent<LineRenderer>();
            circle.useWorldSpace = true;
            circle.loop = true;
            circle.positionCount = CirclePoints;
            circle.material = Track(SkillEffects.NewLineMaterial());
            circle.sortingLayerID = SortingLayer;
            circle.sortingOrder = BaseOrder - 1;
        }
        float radius = sword.GiantSpinReachNow;
        // 巨大化が始まって少し大きくなってから出す（元の大きさのうちは出さない）
        bool show = radius > unit * MockReach * 1.05f;
        circle.enabled = show;
        if (!show) return;
        Vector3 centre = Pivot;
        for (int i = 0; i < CirclePoints; i++)
        {
            float a = i * Mathf.PI * 2f / CirclePoints;
            circle.SetPosition(i, centre + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius);
        }
        float appear = 1f - Mathf.Pow(1f - Mathf.Clamp01(sinceGrow / CircleAppearTime), 3f);
        float spinning = stage == GiantSpinStage.Spin || stage == GiantSpinStage.Linger ? Mathf.Clamp01(sinceSpin / .06f) : 0f;
        float fade = stage == GiantSpinStage.Linger ? 1f - Mathf.Clamp01(stageTime / stageDuration) : 1f;
        Color c = Color.Lerp(FxColor, FxColor * .8f, spinning);
        c.a = Mathf.Lerp(CircleAlpha, CircleSpinAlpha, spinning) * appear * fade;
        circle.startColor = circle.endColor = c;
        circle.widthMultiplier = radius * Mathf.Lerp(CircleWidth, CircleSpinWidth, spinning);
    }

    // ---------- 一回転 ----------
    void BeginSpin()
    {
        sinceSpin = 0f;
        swept = 0f;
        // 刀身の一番遠い点の向きを、剣から見た向きとして覚える（斬撃の跡の先頭をそこに合わせる）
        Vector3 pivot = Pivot;
        float best = -1f;
        foreach (var shape in sword.GetComponentsInChildren<PolygonCollider2D>(true))
            for (int p = 0; p < shape.pathCount; p++)
                foreach (var point in shape.GetPath(p))
                {
                    Vector3 world = shape.transform.TransformPoint(point + shape.offset);
                    float d = ((Vector2)(world - pivot)).sqrMagnitude;
                    if (d > best) { best = d; bladeLocalDirection = sword.transform.InverseTransformDirection(world - pivot); }
                }
        opponentHp.Clear();
        foreach (var enemy in sword.EffectOpponents()) if (enemy != null) opponentHp[enemy] = enemy.hp;
        BuildAfterimages();
    }

    float HeadAngle
    {
        get
        {
            Vector3 d = sword.transform.TransformDirection(bladeLocalDirection);
            return Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        }
    }

    void UpdateSlash(float fade)
    {
        if (slashRenderer == null)
        {
            var obj = NewObject("GiantSpinSlash");
            obj.AddComponent<MeshFilter>().sharedMesh = slashMesh = new Mesh();
            slashMesh.MarkDynamic();
            slashRenderer = obj.AddComponent<MeshRenderer>();
            slashRenderer.sharedMaterial = Track(SkillEffects.NewLineMaterial());
            slashRenderer.sortingLayerID = SortingLayer;
            slashRenderer.sortingOrder = BaseOrder - 3;
        }
        if (stage == GiantSpinStage.Spin)
        {
            slashHeadAngle = HeadAngle;
            slashSpanNow = Mathf.Min(SlashSpan, swept);
        }
        float reach = sword.GiantSpinReachNow;
        Vector3 pivot = Pivot;
        Color outer = FxColor, edge = FxColor * CoreShade;
        int n = SlashSlices;
        var vertices = new Vector3[(n + 1) * 4];
        var colors = new Color[(n + 1) * 4];
        var triangles = new int[n * 12];
        for (int j = 0; j <= n; j++)
        {
            float w = 1f - (float)j / n;
            float a = (slashHeadAngle - spinSign * slashSpanNow * j / n) * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
            int v = j * 4;
            vertices[v] = pivot + dir * (reach * SlashInner);
            vertices[v + 1] = pivot + dir * (reach * SlashOuter);
            vertices[v + 2] = pivot + dir * (reach * SlashEdgeInner);
            vertices[v + 3] = pivot + dir * (reach * SlashOuter);
            Color o = outer; o.a = SlashAlpha * w * fade;
            Color e = edge; e.a = SlashEdgeAlpha * w * w * fade;
            colors[v] = colors[v + 1] = o;
            colors[v + 2] = colors[v + 3] = e;
        }
        for (int j = 0; j < n; j++)
        {
            int v = j * 4, t = j * 6;
            // 外側の淡いにじみ（先に描く）
            triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 5;
            triangles[t + 3] = v; triangles[t + 4] = v + 5; triangles[t + 5] = v + 4;
            // 濃い刃先（後に描いて上に重ねる）
            int u = n * 6 + j * 6;
            triangles[u] = v + 2; triangles[u + 1] = v + 3; triangles[u + 2] = v + 7;
            triangles[u + 3] = v + 2; triangles[u + 4] = v + 7; triangles[u + 5] = v + 6;
        }
        slashMesh.Clear();
        slashMesh.vertices = vertices;
        slashMesh.colors = colors;
        slashMesh.triangles = triangles;
        slashMesh.RecalculateBounds();
        slashRenderer.enabled = fade > 0f && slashSpanNow > 0f;
    }

    void BuildAfterimages()
    {
        ClearAfterimages();
        foreach (var source in sword.GetComponentsInChildren<SpriteRenderer>())
        {
            if (source == null || source.sprite == null || !source.enabled || source.name.StartsWith(InkDefeatEffect.BleedName)) continue;
            var ghosts = new SpriteRenderer[Afterimages];
            for (int k = 0; k < Afterimages; k++)
            {
                ghosts[k] = NewSprite("GiantSpinAfterimage", source.sprite, BaseOrder - 2);
                ghosts[k].sharedMaterial = source.sharedMaterial;
            }
            afterimages.Add((source, ghosts));
        }
    }

    void UpdateAfterimages()
    {
        Vector3 pivot = Pivot;
        foreach (var (source, ghosts) in afterimages)
            for (int k = 0; k < ghosts.Length; k++)
            {
                var ghost = ghosts[k];
                bool show = source != null && source.enabled && swept > AfterimageStep * (k + 1);
                ghost.enabled = show;
                if (!show) continue;
                var turn = Quaternion.Euler(0f, 0f, -spinSign * AfterimageStep * (k + 1));
                ghost.transform.position = pivot + turn * (source.transform.position - pivot);
                ghost.transform.rotation = turn * source.transform.rotation;
                ghost.transform.localScale = source.transform.lossyScale;
                ghost.sprite = source.sprite;
                ghost.flipX = source.flipX; ghost.flipY = source.flipY;
                Color c = FxColor; c.a = AfterimageAlpha / (k + 1);
                ghost.color = c;
            }
    }

    void ClearAfterimages()
    {
        foreach (var (_, ghosts) in afterimages)
            foreach (var ghost in ghosts)
                if (ghost != null) { created.Remove(ghost.gameObject); Object.Destroy(ghost.gameObject); }
        afterimages.Clear();
    }

    // 当たった時：この一回転の間に相手のHPが減ったら、そこから振り抜く向きへ飛沫を飛ばす。
    // HPはホストもゲストも同期で同じになるので、どちらの画面でも同じように出る
    void CheckHits()
    {
        Vector2 pivot = Pivot;
        foreach (var enemy in sword.EffectOpponents())
        {
            if (enemy == null) continue;
            if (!opponentHp.TryGetValue(enemy, out int before)) { opponentHp[enemy] = enemy.hp; continue; }
            if (enemy.hp >= before) continue;
            opponentHp[enemy] = enemy.hp;
            Vector2 at = enemy.BodyRenderer != null ? (Vector2)enemy.BodyRenderer.bounds.center : (Vector2)enemy.transform.position;
            Vector2 radial = at - pivot;
            Vector2 tangent = radial.sqrMagnitude > .0001f ? new Vector2(-radial.y, radial.x).normalized * spinSign : Vector2.up;
            SpinDashSplash.Play(at, tangent, FxColor * .85f, SortingLayer, BaseOrder + 2, SplashScale);
        }
    }

    // ---------- ふらつき ----------
    // 剣の絵（刀身と柄）の写しを揺らして見せ、本物の絵は隠す。本物を回すと当たり判定まで回ってしまうため
    void BeginStagger()
    {
        EndStagger();
        unit = Mathf.Max(.0001f, sword.GiantSpinReachNow / MockReach);
        foreach (var source in sword.GetComponentsInChildren<SpriteRenderer>())
        {
            if (source == null || source.sprite == null || !source.enabled || source.name.StartsWith(InkDefeatEffect.BleedName)) continue;
            var standIn = NewSprite("GiantStaggerStandIn", source.sprite, source.sortingOrder);
            standIn.sharedMaterial = source.sharedMaterial;
            standIn.sortingLayerID = source.sortingLayerID;
            standIns.Add((source, standIn));
            source.forceRenderingOff = true;
        }
        if (stars == null)
        {
            stars = new SpriteRenderer[Stars];
            starEdges = new SpriteRenderer[Stars];
            for (int i = 0; i < Stars; i++)
            {
                starEdges[i] = NewSprite("GiantStaggerStarEdge", SkillEffects.Star, BaseOrder + 3);
                stars[i] = NewSprite("GiantStaggerStar", SkillEffects.Star, BaseOrder + 4);
            }
        }
    }

    void UpdateStagger()
    {
        float t = stageTime;
        float wobble = WobbleAngle * Mathf.Sin(t * WobbleSpeed) * (1f - WobbleDecay * Mathf.Clamp01(t));
        var turn = Quaternion.Euler(0f, 0f, wobble);
        Vector3 pivot = Pivot;
        Bounds bounds = new Bounds(pivot, Vector3.zero);
        bool any = false;
        foreach (var (source, standIn) in standIns)
        {
            if (source == null || standIn == null) continue;
            source.forceRenderingOff = true;
            standIn.enabled = source.enabled;
            standIn.transform.position = pivot + turn * (source.transform.position - pivot);
            standIn.transform.rotation = turn * source.transform.rotation;
            standIn.transform.localScale = source.transform.lossyScale;
            standIn.sprite = source.sprite;
            standIn.flipX = source.flipX; standIn.flipY = source.flipY;
            standIn.color = source.color;
            if (any) bounds.Encapsulate(standIn.bounds); else { bounds = standIn.bounds; any = true; }
        }
        // 頭の上（剣の一番上）を、黄色い星が横長の輪を描いて回る
        Vector3 top = new Vector3(bounds.center.x, bounds.max.y + StarLift * unit, pivot.z);
        for (int i = 0; i < Stars; i++)
        {
            float a = t * StarSpin + i * Mathf.PI * 2f / Stars, depth = Mathf.Sin(a);
            Vector3 at = top + new Vector3(Mathf.Cos(a) * StarOrbitX, depth * StarOrbitY, 0f) * unit;
            float size = StarSize * unit * (1f + depth * .18f);
            stars[i].transform.SetPositionAndRotation(at, Quaternion.Euler(0f, 0f, -t * 230f + i * 40f));
            stars[i].transform.localScale = Vector3.one * size;
            stars[i].color = StarColor;
            stars[i].enabled = true;
            starEdges[i].transform.SetPositionAndRotation(at, stars[i].transform.rotation);
            starEdges[i].transform.localScale = Vector3.one * (size * StarOutline);
            starEdges[i].color = StarEdge;
            starEdges[i].enabled = true;
        }
    }

    void EndStagger()
    {
        foreach (var (source, standIn) in standIns)
        {
            if (source != null) source.forceRenderingOff = false;
            if (standIn != null) { created.Remove(standIn.gameObject); Object.Destroy(standIn.gameObject); }
        }
        standIns.Clear();
        if (stars != null)
            for (int i = 0; i < Stars; i++)
            {
                if (stars[i] != null) stars[i].enabled = false;
                if (starEdges[i] != null) starEdges[i].enabled = false;
            }
    }

    public void Dispose()
    {
        EndStagger();
        afterimages.Clear();
        foreach (var obj in created) if (obj != null) Object.Destroy(obj);
        created.Clear();
        foreach (var m in materials) if (m != null) Object.Destroy(m);
        materials.Clear();
        if (slashMesh != null) Object.Destroy(slashMesh);
    }

    // ---------- 部品 ----------
    GameObject NewObject(string objectName)
    {
        var obj = new GameObject(objectName);
        created.Add(obj);
        return obj;
    }

    SpriteRenderer NewSprite(string objectName, Sprite sprite, int order)
    {
        var r = NewObject(objectName).AddComponent<SpriteRenderer>();
        r.sprite = sprite;
        r.sortingLayerID = SortingLayer;
        r.sortingOrder = order;
        r.enabled = false;
        return r;
    }

    Material Track(Material material)
    {
        if (material != null) materials.Add(material);
        return material;
    }
}

// 巨大化の各段で、回転の中心から広がって消える輪（演出の色）
public class GiantSpinRing : MonoBehaviour
{
    const int Points = 64;
    const float Duration = .2f, WidthFrom = .03f, WidthTo = .01f; // 幅は広がりきった時の半径への割合
    LineRenderer line;
    float from, to, time;
    Color color;

    public static void Play(Vector3 centre, float fromRadius, float toRadius, Color color, int sortingLayer, int sortingOrder)
    {
        var obj = new GameObject("GiantSpinGrowRing");
        obj.transform.position = centre;
        var ring = obj.AddComponent<GiantSpinRing>();
        ring.from = fromRadius; ring.to = toRadius; ring.color = color;
        ring.line = obj.AddComponent<LineRenderer>();
        ring.line.useWorldSpace = true;
        ring.line.loop = true;
        ring.line.positionCount = Points;
        ring.line.material = SkillEffects.NewLineMaterial();
        ring.line.sortingLayerID = sortingLayer;
        ring.line.sortingOrder = sortingOrder;
        ring.Apply();
    }

    void Update()
    {
        time += Time.deltaTime;
        if (time >= Duration) { Destroy(gameObject); return; }
        Apply();
    }

    void Apply()
    {
        float k = Mathf.Clamp01(time / Duration);
        float radius = Mathf.Lerp(from, to, 1f - Mathf.Pow(1f - k, 3f));
        Vector3 centre = transform.position;
        for (int i = 0; i < Points; i++)
        {
            float a = i * Mathf.PI * 2f / Points;
            line.SetPosition(i, centre + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius);
        }
        line.widthMultiplier = to * Mathf.Lerp(WidthFrom, WidthTo, k);
        Color c = color; c.a = 1f - k;
        line.startColor = line.endColor = c;
    }

    void OnDestroy()
    {
        if (line != null && line.material != null) Destroy(line.material);
    }
}

// 小さなきらめき：白い十字の光（縁のやわらかい丸を細長く伸ばして2枚重ねる）と白い芯。大きくなりながら消える
public class SkillSparkle : MonoBehaviour
{
    SpriteRenderer horizontal, vertical, core;
    float from, to, duration, time;

    public static void Play(Vector3 position, float fromSize, float toSize, float duration, int sortingLayer, int sortingOrder)
    {
        var obj = new GameObject("SkillSparkle");
        obj.transform.position = position;
        var sparkle = obj.AddComponent<SkillSparkle>();
        sparkle.from = fromSize; sparkle.to = toSize; sparkle.duration = Mathf.Max(.01f, duration);
        sparkle.horizontal = Part(obj.transform, sortingLayer, sortingOrder);
        sparkle.vertical = Part(obj.transform, sortingLayer, sortingOrder);
        sparkle.core = Part(obj.transform, sortingLayer, sortingOrder + 1);
        sparkle.Apply();
    }

    static SpriteRenderer Part(Transform parent, int sortingLayer, int sortingOrder)
    {
        var obj = new GameObject("Part");
        obj.transform.SetParent(parent, false);
        var r = obj.AddComponent<SpriteRenderer>();
        r.sprite = InkAssets.Dot;
        r.sortingLayerID = sortingLayer;
        r.sortingOrder = sortingOrder;
        return r;
    }

    void Update()
    {
        time += Time.deltaTime;
        if (time >= duration) { Destroy(gameObject); return; }
        Apply();
    }

    void Apply()
    {
        float k = Mathf.Clamp01(time / duration);
        float size = Mathf.Lerp(from, to, 1f - Mathf.Pow(1f - k, 3f)) * 2f;
        Color c = new Color(1f, 1f, 1f, 1f - k);
        horizontal.transform.localScale = new Vector3(size, size * .16f, 1f);
        vertical.transform.localScale = new Vector3(size * .16f, size, 1f);
        core.transform.localScale = Vector3.one * (size * .3f);
        horizontal.color = vertical.color = core.color = c;
    }
}
