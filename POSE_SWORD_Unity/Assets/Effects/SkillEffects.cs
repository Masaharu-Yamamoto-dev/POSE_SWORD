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
    static ICloneRushEffect cloneRush;
    public static ICloneRushEffect CloneRush => cloneRush ?? (cloneRush = new InkCloneRushEffect());
    static ILeafShieldEffect leafShield;
    public static ILeafShieldEffect LeafShield => leafShield ?? (leafShield = new InkLeafShieldEffect());

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

// ======================================================================
// オレ達アタック
// ======================================================================

// 分身1体の段階。SwordCloneProjectile.Phase の値と同じで、オンライン対戦ではホストからゲストへ同期する
// （オレ達シールドの分身は None のまま。ゲストはこれでオレ達アタックの分身かどうかを見分ける）
public enum CloneRushPhase
{
    None = 0,
    Fly = 1, // 撃ち出されて飛んでいる
}

// 分身の消え方
public enum CloneRushEnd
{
    Fade = 0,   // 時間切れ：その場で薄れる
    Hit = 1,    // 相手に当たった：飛沫
    Burst = 2,  // 壁・巨大回転斬・シールドに当たった：小さく弾けて粒が散る
}

// オレ達アタックの本体の段階。SwordBattle.cloneRushPhase の値と同じで、オンライン対戦ではホストからゲストへ同期する
public enum CloneRushChargeStage
{
    None = 0,
    Charge = 1, // 撃ち始める前に溜める
    Fire = 2,   // 1体ずつ撃ち出している
}

public interface ICloneRushEffect
{
    // 分身（ホストの本物・ゲストの見た目）に演出を付ける
    ICloneRushLook Attach(GameObject clone, SwordBattle owner);
    // 本体の溜めと、残りの発射数の見た目
    ICloneRushChargeLook CreateCharge(SwordBattle owner);
}

public interface ICloneRushChargeLook
{
    void SetStage(CloneRushChargeStage stage, int shotsLeft, float chargeTime);
    void Tick(float deltaTime);
    void Dispose();
}

public interface ICloneRushLook
{
    // 段階が変わった時
    void SetPhase(CloneRushPhase phase);
    // 消える時。分身はこの直後に消されるので、残る演出は分身の外に作る
    void PlayEnd(CloneRushEnd kind, Vector3 position, float angleDeg);
}

public class InkCloneRushEffect : ICloneRushEffect
{
    public ICloneRushLook Attach(GameObject clone, SwordBattle owner)
    {
        var look = clone.AddComponent<InkCloneRushLook>();
        look.Init(owner);
        return look;
    }

    public ICloneRushChargeLook CreateCharge(SwordBattle owner) => new InkCloneRushCharge(owner);
}

// 標準の溜め：プレイヤーの色の光の輪と粒が本体へ縮んで集まり、本体を演出の色でふちどる。
// 本体のまわりを、残りの発射数だけ光の粒が回る（撃つたびに1つ消える）
public class InkCloneRushCharge : ICloneRushChargeLook
{
    const float Px = 1f / 21f;
    const float RingFrom = 95f, RingTo = 20f, RingWidth = 3.5f, ParticleFrom = 110f, ParticleTo = 12f, ParticleSize = 4.8f;
    const int Particles = 12, CirclePoints = 48;
    const float PipOrbitX = 44f, PipOrbitY = 18f, PipLift = 6f, PipCore = 10f, PipHalo = 18f, PipHaloAlpha = .45f, PipSpin = 5f;
    const float GlowScale = 1.12f, GlowAlpha = .5f;

    readonly SwordBattle owner;
    readonly List<GameObject> created = new List<GameObject>();
    readonly List<Material> materials = new List<Material>();
    CloneRushChargeStage stage;
    int shotsLeft;
    float stageTime, chargeTime = .45f, sinceStart;
    LineRenderer ring;
    SpriteRenderer[] particles;
    Vector2[] particleDirections;
    readonly List<(SpriteRenderer source, SpriteRenderer glow)> glows = new List<(SpriteRenderer, SpriteRenderer)>();
    readonly List<(SpriteRenderer core, SpriteRenderer halo)> pips = new List<(SpriteRenderer, SpriteRenderer)>();

    public InkCloneRushCharge(SwordBattle owner) { this.owner = owner; }

    int Order => owner.BodyRenderer != null ? owner.BodyRenderer.sortingOrder : 0;
    int Layer => owner.BodyRenderer != null ? owner.BodyRenderer.sortingLayerID : 0;
    Color FxColor => owner.PlayerMainColor;

    public void SetStage(CloneRushChargeStage next, int shots, float charge)
    {
        if (next != stage) { stageTime = 0f; if (next == CloneRushChargeStage.Charge) sinceStart = 0f; }
        stage = next;
        shotsLeft = shots;
        chargeTime = Mathf.Max(.01f, charge);
        if (next == CloneRushChargeStage.None) Hide();
        else if (glows.Count == 0) BuildGlows();
    }

    public void Tick(float dt)
    {
        stageTime += dt;
        sinceStart += dt;
        if (stage == CloneRushChargeStage.None) return;
        Vector3 centre = owner.transform.position;
        // 溜め：光の輪と粒が縮んで集まる
        bool charging = stage == CloneRushChargeStage.Charge;
        float k = Mathf.Clamp01(stageTime / chargeTime), ease = k * k * k;
        if (ring == null) ring = NewRing();
        ring.enabled = charging;
        if (charging)
        {
            float radius = Mathf.Lerp(RingFrom, RingTo, ease) * Px;
            for (int i = 0; i < CirclePoints; i++)
            {
                float a = i * Mathf.PI * 2f / CirclePoints;
                ring.SetPosition(i, centre + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius);
            }
            ring.widthMultiplier = RingWidth * Px;
            Color c = FxColor; c.a = .3f + .7f * k;
            ring.startColor = ring.endColor = c;
        }
        if (particles == null) BuildParticles();
        float distance = Mathf.Lerp(ParticleFrom, ParticleTo, ease) * Px;
        Color pc = FxColor; pc.a = k;
        for (int i = 0; i < Particles; i++)
        {
            particles[i].enabled = charging;
            if (!charging) continue;
            particles[i].transform.position = centre + (Vector3)(particleDirections[i] * distance);
            particles[i].transform.localScale = Vector3.one * (ParticleSize * Px);
            particles[i].color = pc;
        }
        // ふちどり：本体の絵の写しを少し大きくして、演出の色で後ろに敷く
        foreach (var (source, glow) in glows)
        {
            bool show = source != null && source.enabled && !source.forceRenderingOff;
            glow.enabled = show;
            if (!show) continue;
            glow.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            glow.transform.localScale = source.transform.lossyScale * GlowScale;
            glow.sprite = source.sprite;
            Color gc = FxColor; gc.a = GlowAlpha * (charging ? k : 1f);
            glow.color = gc;
        }
        // 残りの発射数：本体のまわりを光の粒が回る
        while (pips.Count < 3)
            pips.Add((NewSprite("CloneRushPip", InkAssets.Dot, Order + 4), NewSprite("CloneRushPipHalo", InkAssets.Dot, Order + 3)));
        for (int n = 0; n < pips.Count; n++)
        {
            var (core, halo) = pips[n];
            bool show = n < shotsLeft;
            core.enabled = halo.enabled = show;
            if (!show) continue;
            float appear = Mathf.Clamp01((sinceStart - .1f - n * .08f) / .2f);
            appear = 1f - Mathf.Pow(1f - appear, 3f);
            float a = sinceStart * PipSpin + n * Mathf.PI * 2f / 3f;
            Vector3 at = centre + new Vector3(Mathf.Cos(a) * PipOrbitX, Mathf.Sin(a) * PipOrbitY + PipLift, 0f) * Px;
            core.transform.position = halo.transform.position = at;
            core.transform.localScale = Vector3.one * (PipCore * Px * appear);
            halo.transform.localScale = Vector3.one * (PipHalo * Px * appear);
            core.color = new Color(1f, 1f, 1f, .95f);
            Color hc = FxColor; hc.a = PipHaloAlpha;
            halo.color = hc;
        }
    }

    void BuildGlows()
    {
        foreach (var source in owner.GetComponentsInChildren<SpriteRenderer>())
        {
            if (source == null || source.sprite == null || source.name.StartsWith(InkDefeatEffect.BleedName)) continue;
            var glow = NewSprite("CloneRushGlow", source.sprite, Order - 1);
            glow.sharedMaterial = source.sharedMaterial;
            glows.Add((source, glow));
        }
    }

    void BuildParticles()
    {
        particles = new SpriteRenderer[Particles];
        particleDirections = new Vector2[Particles];
        for (int i = 0; i < Particles; i++)
        {
            particles[i] = NewSprite("CloneRushChargeParticle", InkAssets.Dot, Order + 1);
            float a = Random.Range(0f, Mathf.PI * 2f);
            particleDirections[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(.7f, 1.2f);
        }
    }

    LineRenderer NewRing()
    {
        var obj = new GameObject("CloneRushChargeRing");
        created.Add(obj);
        var line = obj.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.loop = true;
        line.positionCount = CirclePoints;
        var material = SkillEffects.NewLineMaterial();
        if (material != null) materials.Add(material);
        line.material = material;
        line.sortingLayerID = Layer;
        line.sortingOrder = Order + 1;
        return line;
    }

    SpriteRenderer NewSprite(string objectName, Sprite sprite, int order)
    {
        var obj = new GameObject(objectName);
        created.Add(obj);
        var r = obj.AddComponent<SpriteRenderer>();
        r.sprite = sprite;
        r.sortingLayerID = Layer;
        r.sortingOrder = order;
        r.enabled = false;
        return r;
    }

    void Hide()
    {
        if (ring != null) ring.enabled = false;
        if (particles != null) foreach (var p in particles) if (p != null) p.enabled = false;
        foreach (var (_, glow) in glows) if (glow != null) Destroy(glow.gameObject);
        glows.Clear();
        foreach (var (core, halo) in pips) { if (core != null) core.enabled = false; if (halo != null) halo.enabled = false; }
    }

    static void Destroy(Object obj) { if (obj != null) Object.Destroy(obj); }

    public void Dispose()
    {
        glows.Clear();
        pips.Clear();
        foreach (var obj in created) Destroy(obj);
        created.Clear();
        foreach (var m in materials) Destroy(m);
        materials.Clear();
    }
}

// 標準：撃った所（本体の位置）に閃光と輪、飛ぶ間は色の濃淡の軌跡。当たると刃先が刺さったように見せて飛沫、
// 壁などでは小さく弾ける。演出の色はプレイヤーの色（軌跡は分身と同じ明るい色合い）
public class InkCloneRushLook : MonoBehaviour, ICloneRushLook
{
    // 見本（HTMLのモック）のピクセルをワールド単位に直す倍率（大回転斬と同じ：秒速50 = 1050px）
    const float Px = 1f / 21f;
    const float CoreShade = .72f;
    // 撃った所
    const float LaunchFlashSize = 24f, LaunchFlashTime = .09f, LaunchRingFrom = 6f, LaunchRingTo = 34f, LaunchRingTime = .38f;
    // 軌跡（長さは 120px ぶん：秒速43で約0.13秒）
    const float TrailTime = .13f, TrailOuter = 16f, TrailCore = 5f, TrailOuterAlpha = .32f, TrailCoreAlpha = .65f;
    // 当たった・弾けた
    const float HitSplashScale = .75f;
    const float StabTime = .18f; // 当たった時に刺さって見せる写しが薄れるまで

    SwordBattle owner;
    SpriteRenderer body;
    CloneRushPhase phase = CloneRushPhase.None;
    Transform trailRoot;
    TrailRenderer[] trails;

    Color FxColor => owner != null ? owner.PlayerMainColor : Color.white;
    int Order => body != null ? body.sortingOrder : 0;
    int Layer => body != null ? body.sortingLayerID : 0;

    public void Init(SwordBattle owner)
    {
        this.owner = owner;
        body = GetComponent<SpriteRenderer>();
    }

    public void SetPhase(CloneRushPhase next)
    {
        if (next == phase) return;
        CloneRushPhase previous = phase;
        phase = next;
        // 撃った所の閃光と輪は本体の位置に出す（ゲストの見た目の分身は、最初に届いた時にはもう少し飛んでいるため）
        if (next == CloneRushPhase.Fly && previous == CloneRushPhase.None) BeginFly(owner != null ? owner.transform.position : transform.position);
    }

    void LateUpdate()
    {
        if (trailRoot != null) trailRoot.position = transform.position;
    }

    void BeginFly(Vector3 at)
    {
        SkillFlash.Play(at, LaunchFlashSize * Px, LaunchFlashTime, Layer, Order + 2);
        ExpandingRing.Play(at, LaunchRingFrom * Px, LaunchRingTo * Px, 3f * Px, 3f * Px, LaunchRingTime, FxColor, Layer, Order + 1);
        if (trailRoot != null) return;
        // 軌跡は分身の子にしない（分身の大きさに引きずられず、消えた後も少し残すため）
        trailRoot = new GameObject("CloneRushTrail").transform;
        trailRoot.position = transform.position;
        trails = new TrailRenderer[2];
        float[] widths = { TrailOuter, TrailCore };
        float[] alphas = { TrailOuterAlpha, TrailCoreAlpha };
        for (int i = 0; i < 2; i++)
        {
            var child = new GameObject("Trail" + i);
            child.transform.SetParent(trailRoot, false);
            var trail = child.AddComponent<TrailRenderer>();
            trail.time = TrailTime;
            trail.minVertexDistance = .05f;
            trail.widthMultiplier = widths[i] * Px;
            trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, i == 0 ? .06f : 0f);
            trail.material = SkillEffects.NewLineMaterial();
            trail.sortingLayerID = Layer;
            trail.sortingOrder = Order - 3 + i;
            // 分身と同じ明るい色合いで、少し透けさせる（濃いと、明るくした分身より軌跡の方が目立つため）
            Color tint = owner != null ? owner.CloneTint : Color.white;
            Color c = i == 0 ? tint : tint * CoreShade;
            c.a = alphas[i];
            trail.startColor = trail.endColor = c;
            trails[i] = trail;
        }
    }

    // ---------- 消える時 ----------
    public void PlayEnd(CloneRushEnd kind, Vector3 position, float angleDeg)
    {
        Vector2 dir = new Vector2(Mathf.Cos(angleDeg * Mathf.Deg2Rad), Mathf.Sin(angleDeg * Mathf.Deg2Rad));
        switch (kind)
        {
            case CloneRushEnd.Hit:
                // 刃先が当たった所（position）へ刺さるように、写しを少し前へ突き出してから薄れさせる
                if (body != null)
                {
                    float half = body.bounds.extents.magnitude * .8f;
                    Vector3 stab = position - (Vector3)(dir * half * .85f);
                    stab.z = transform.position.z;
                    FadingCopy.Play(body, StabTime, 0f, stab);
                }
                SpinDashSplash.Play(position, dir, FxColor * .85f, Layer, Order + 2, HitSplashScale);
                break;
            case CloneRushEnd.Burst:
                SkillFlash.Play(position, 20f * Px, .09f, Layer, Order + 2);
                ExpandingRing.Play(position, 4f * Px, 22f * Px, 2f * Px, 2f * Px, .45f, FxColor, Layer, Order + 1);
                SpinDashSplash.Play(position, -dir, FxColor * .85f, Layer, Order + 2, .4f);
                break;
            default:
                if (body != null) FadingCopy.Play(body, .25f, 0f);
                break;
        }
        // 軌跡は残して、伸びるのを止めて薄れさせる。当たった時は、先を当たった所まで伸ばす
        // （分身の真ん中で止まると、刃先の分だけ相手との間に隙間が見えるため）
        if (trailRoot != null)
        {
            Vector3 head = kind == CloneRushEnd.Hit || kind == CloneRushEnd.Burst ? position : transform.position;
            head.z = trailRoot.position.z;
            foreach (var trail in trails)
                if (trail != null) { trail.AddPosition(head); trail.emitting = false; trail.autodestruct = true; }
            trailRoot.position = head;
            trailRoot = null;
        }
    }

    void OnDestroy()
    {
        if (trailRoot != null) Destroy(trailRoot.gameObject);
    }
}

// 広がって消える輪（演出の色）
public class ExpandingRing : MonoBehaviour
{
    const int Points = 48;
    LineRenderer line;
    float from, to, widthFrom, widthTo, duration, time;
    Color color;

    public static void Play(Vector3 centre, float fromRadius, float toRadius, float widthFrom, float widthTo, float duration, Color color, int sortingLayer, int sortingOrder)
    {
        var obj = new GameObject("SkillRing");
        obj.transform.position = centre;
        var ring = obj.AddComponent<ExpandingRing>();
        ring.from = fromRadius; ring.to = toRadius; ring.widthFrom = widthFrom; ring.widthTo = widthTo;
        ring.duration = Mathf.Max(.01f, duration); ring.color = color;
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
        if (time >= duration) { Destroy(gameObject); return; }
        Apply();
    }

    void Apply()
    {
        float k = Mathf.Clamp01(time / duration);
        float radius = Mathf.Lerp(from, to, 1f - Mathf.Pow(1f - k, 3f));
        Vector3 centre = transform.position;
        for (int i = 0; i < Points; i++)
        {
            float a = i * Mathf.PI * 2f / Points;
            line.SetPosition(i, centre + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius);
        }
        line.widthMultiplier = Mathf.Lerp(widthFrom, widthTo, k);
        Color c = color; c.a = 1f - k;
        line.startColor = line.endColor = c;
    }

    void OnDestroy()
    {
        if (line != null && line.material != null) Destroy(line.material);
    }
}

// 白い閃光（丸）。すぐ消える
public class SkillFlash : MonoBehaviour
{
    SpriteRenderer look;
    float duration, time;

    public static void Play(Vector3 position, float size, float duration, int sortingLayer, int sortingOrder)
    {
        var obj = new GameObject("SkillFlash");
        obj.transform.position = position;
        obj.transform.localScale = Vector3.one * size;
        var flash = obj.AddComponent<SkillFlash>();
        flash.duration = Mathf.Max(.01f, duration);
        flash.look = obj.AddComponent<SpriteRenderer>();
        flash.look.sprite = InkAssets.Dot;
        flash.look.sortingLayerID = sortingLayer;
        flash.look.sortingOrder = sortingOrder;
    }

    void Update()
    {
        time += Time.deltaTime;
        if (time >= duration) { Destroy(gameObject); return; }
        look.color = new Color(1f, 1f, 1f, 1f - time / duration);
    }
}

// 消える分身の写し：その場（rise だけ上へ）で薄れて消える
public class FadingCopy : MonoBehaviour
{
    SpriteRenderer look;
    Vector3 from, to;
    Color color;
    float duration, rise, time;

    public static void Play(SpriteRenderer source, float duration, float rise) { Play(source, duration, rise, source.transform.position); }

    // moveTo：薄れながら最初の数分の1の時間でそこまで動く（当たった時に刃先を刺す）
    public static void Play(SpriteRenderer source, float duration, float rise, Vector3 moveTo)
    {
        var obj = new GameObject("CloneRushFade");
        obj.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
        obj.transform.localScale = source.transform.lossyScale;
        var copy = obj.AddComponent<FadingCopy>();
        copy.look = obj.AddComponent<SpriteRenderer>();
        copy.look.sprite = source.sprite;
        copy.look.sharedMaterial = source.sharedMaterial;
        copy.look.sortingLayerID = source.sortingLayerID;
        copy.look.sortingOrder = source.sortingOrder;
        copy.color = source.color;
        copy.from = obj.transform.position;
        copy.to = moveTo;
        copy.duration = Mathf.Max(.01f, duration);
        copy.rise = rise;
        copy.look.color = copy.color;
    }

    void Update()
    {
        time += Time.deltaTime;
        if (time >= duration) { Destroy(gameObject); return; }
        float k = time / duration;
        float thrust = 1f - Mathf.Pow(1f - Mathf.Clamp01(k * 3f), 3f);
        transform.position = Vector3.Lerp(from, to, thrust) + Vector3.up * (rise * (1f - Mathf.Pow(1f - k, 3f)));
        Color c = color; c.a = color.a * (1f - k);
        look.color = c;
    }
}

// ======================================================================
// オレ達シールド
// ======================================================================

// シールドの分身の消え方。オンライン対戦ではホストからゲストへ同期する（MultiplayerCloneEnd.kind。オレ達アタックの値と重ならない）
public enum LeafShieldEnd
{
    Break = 10,  // 2回削られて割れた
    Expire = 11, // 効果が切れて本体へ吸い込まれた
}

public interface ILeafShieldEffect
{
    // シールドの分身（ホストの本物・ゲストの見た目）に演出を付ける
    ILeafShieldLook AttachOrb(GameObject orb, SwordBattle owner);
    // 本体の、守っている間のふちどりと、殴られた時の盾の光
    ILeafShieldAura CreateAura(SwordBattle owner);
}

public interface ILeafShieldLook
{
    // 消える時。分身はこの直後に消されるので、残る演出は分身の外に作る
    void PlayEnd(LeafShieldEnd kind);
}

public interface ILeafShieldAura
{
    void Tick(bool shielded, float deltaTime);
    // 殴られた向き（本体から相手の方）に盾の光を出す
    void PlayGuard(Vector2 towardAttacker);
    void Dispose();
}

public class InkLeafShieldEffect : ILeafShieldEffect
{
    public ILeafShieldLook AttachOrb(GameObject orb, SwordBattle owner)
    {
        var look = orb.AddComponent<InkLeafShieldLook>();
        look.Init(owner);
        return look;
    }

    public ILeafShieldAura CreateAura(SwordBattle owner) => new InkLeafShieldAura(owner);
}

// 標準：輪の上に着くと小さな輪が弾け、輪を広げている間は通った跡に短い軌跡。削れると少し薄くなり、
// 割れると閃光とともに砕けて破片が散る。効果が切れて吸い込まれると、本体で輪が広がる。
// 触れて返した時（削れた・割れた時に近くに敵がいる）は、敵の方へ飛沫。演出の色はプレイヤーの色
public class InkLeafShieldLook : MonoBehaviour, ILeafShieldLook
{
    const float Px = 1f / 21f;
    const float ArriveAfter = .4f, ArriveRingFrom = 6f, ArriveRingTo = 26f, ArriveRingTime = .3f;
    // 広げている間の軌跡。分身は輪の上を回るので、通った位置をつなぐのではなく、本体を中心に分身の後ろへ伸びる円弧として
    // 毎フレーム描く（通った位置をつなぐと、速く回る分身では1フレームの移動が大きく、折れ線のようにカクついていた）。
    // 長さは「普段の回る速さ × TrailTime」の角度ぶん。色は分身と同じ明るい色合いで、少し透けさせる
    const float TrailTime = .25f, TrailOuter = 9f, TrailCore = 3f, TrailOuterAlpha = .3f, TrailCoreAlpha = .6f, TrailCoreShade = .8f;
    const int TrailPoints = 20;
    const float TrailFadeSpeed = 6f;
    const float WornAlphaBelow = .9f;
    const float SplashNear = 2.5f, SplashScale = .6f;
    const int Shards = 8;

    static readonly Dictionary<SwordBattle, float> lastRecallRing = new Dictionary<SwordBattle, float>();

    SwordBattle owner;
    SpriteRenderer body;
    float sinceBorn;
    bool arrived, worn;
    LineRenderer[] trails;
    float trailLevel;

    Color FxColor => owner != null ? owner.PlayerMainColor : Color.white;
    int Order => body != null ? body.sortingOrder : 0;
    int Layer => body != null ? body.sortingLayerID : 0;

    public void Init(SwordBattle owner)
    {
        this.owner = owner;
        body = GetComponent<SpriteRenderer>();
    }

    void LateUpdate()
    {
        if (owner == null || body == null) return;
        sinceBorn += Time.deltaTime;
        float distance = Vector2.Distance(transform.position, owner.transform.position);
        // 展開して輪の上に着いた
        if (!arrived && sinceBorn >= ArriveAfter)
        {
            arrived = true;
            if (distance > owner.leafShieldOrbitRadius * .7f)
                ExpandingRing.Play(transform.position, ArriveRingFrom * Px, ArriveRingTo * Px, 2.5f * Px, 2.5f * Px, ArriveRingTime, FxColor, Layer, Order + 1);
        }
        // 輪を広げている間の軌跡
        bool reaching = arrived && distance > owner.leafShieldOrbitRadius + .3f;
        trailLevel = Mathf.MoveTowards(trailLevel, reaching ? 1f : 0f, TrailFadeSpeed * Time.deltaTime);
        UpdateTrail(distance);
        // 削れた（少し薄くなった）：近くに敵がいれば飛沫
        if (!worn && body.color.a < WornAlphaBelow)
        {
            worn = true;
            SplashTowardNearbyEnemy();
        }
    }

    void UpdateTrail(float radius)
    {
        if (trails == null)
        {
            trails = new LineRenderer[2];
            float[] widths = { TrailOuter, TrailCore };
            for (int i = 0; i < 2; i++)
            {
                var line = new GameObject("LeafShieldTrail").AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.positionCount = TrailPoints;
                line.material = SkillEffects.NewLineMaterial();
                line.widthMultiplier = widths[i] * Px;
                line.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
                line.numCapVertices = 2;
                line.sortingLayerID = Layer;
                line.sortingOrder = Order - 2 + i;
                trails[i] = line;
            }
        }
        bool show = trailLevel > 0f && radius > .01f;
        foreach (var line in trails) line.enabled = show;
        if (!show) return;
        Vector3 centre = owner.transform.position;
        Vector2 offset = transform.position - centre;
        float head = Mathf.Atan2(offset.y, offset.x);
        // 分身は角度が増える向き（反時計回り）に回るので、後ろは角度の小さい側
        float span = owner.leafShieldOrbitSpeed * TrailTime * Mathf.Deg2Rad;
        for (int k = 0; k < TrailPoints; k++)
        {
            float a = head - span * k / (TrailPoints - 1);
            Vector3 at = centre + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius;
            at.z = transform.position.z;
            trails[0].SetPosition(k, at);
            trails[1].SetPosition(k, at);
        }
        Color tint = owner.CloneTint;
        float[] alphas = { TrailOuterAlpha, TrailCoreAlpha };
        for (int i = 0; i < 2; i++)
        {
            Color c = i == 0 ? tint : tint * TrailCoreShade;
            c.a = alphas[i] * trailLevel;
            Color tail = c; tail.a = 0f;
            trails[i].startColor = c;
            trails[i].endColor = tail;
        }
    }

    // 触れて返した時：近くの敵の方へ飛沫
    void SplashTowardNearbyEnemy()
    {
        if (owner == null) return;
        SwordBattle nearest = null;
        float best = SplashNear;
        foreach (var enemy in owner.EffectOpponents())
        {
            if (enemy == null) continue;
            float d = Vector2.Distance(enemy.transform.position, transform.position);
            if (d < best) { best = d; nearest = enemy; }
        }
        if (nearest == null) return;
        Vector2 dir = (Vector2)(nearest.transform.position - transform.position);
        SpinDashSplash.Play(transform.position, dir.sqrMagnitude > .0001f ? dir.normalized : Vector2.right, FxColor * .85f, Layer, Order + 2, SplashScale);
    }

    public void PlayEnd(LeafShieldEnd kind)
    {
        if (kind == LeafShieldEnd.Break)
        {
            Vector3 at = transform.position;
            SkillFlash.Play(at, 32f * Px, .09f, Layer, Order + 2);
            ExpandingRing.Play(at, 6f * Px, 34f * Px, 2.5f * Px, 2.5f * Px, .45f, FxColor, Layer, Order + 1);
            // 破片は分身と同じ色（プレイヤーの色）。半分は少し明るくして、粒の重なりが見分けられるようにする
            Color shard = body != null ? body.color : FxColor;
            shard.a = 1f;
            for (int i = 0; i < Shards; i++) ShieldShard.Play(at, i % 2 == 0 ? shard : Color.Lerp(shard, Color.white, .35f), Layer, Order + 2);
            SplashTowardNearbyEnemy();
        }
        else if (kind == LeafShieldEnd.Expire && owner != null &&
            (!lastRecallRing.TryGetValue(owner, out float last) || Time.time - last > .5f))
        {
            // 吸い込まれた所（本体）で輪が広がる（5枚続けて吸い込まれるので、1回だけ）
            lastRecallRing[owner] = Time.time;
            ExpandingRing.Play(owner.transform.position, 20f * Px, 80f * Px, 4f * Px, .5f * Px, .35f, FxColor, Layer, Order + 1);
        }
    }

    void OnDestroy()
    {
        if (trails != null) foreach (var line in trails) if (line != null) { if (line.material != null) Destroy(line.material); Destroy(line.gameObject); }
    }
}

// 割れた分身の破片：細い粒が外へ飛んで、落ちながら回って薄れる
public class ShieldShard : MonoBehaviour
{
    const float Px = 1f / 21f, Duration = .45f, Gravity = 30f * 21f * Px;
    SpriteRenderer look;
    Vector2 velocity;
    float spin, size, time;
    Color color;

    public static void Play(Vector3 at, Color color, int sortingLayer, int sortingOrder)
    {
        var obj = new GameObject("LeafShieldShard");
        obj.transform.position = at;
        var shard = obj.AddComponent<ShieldShard>();
        float angle = Random.Range(0f, Mathf.PI * 2f);
        shard.velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Random.Range(45f, 100f) * Px / Duration;
        shard.spin = Random.Range(-540f, 540f);
        shard.size = Random.Range(10f, 20f) * Px;
        shard.color = color;
        shard.look = obj.AddComponent<SpriteRenderer>();
        shard.look.sprite = InkAssets.Dot;
        shard.look.sortingLayerID = sortingLayer;
        shard.look.sortingOrder = sortingOrder;
        shard.Apply();
    }

    void Update()
    {
        time += Time.deltaTime;
        if (time >= Duration) { Destroy(gameObject); return; }
        velocity += Vector2.down * Gravity * Time.deltaTime;
        transform.position += (Vector3)(velocity * Time.deltaTime);
        transform.Rotate(0f, 0f, spin * Time.deltaTime);
        Apply();
    }

    void Apply()
    {
        float k = Mathf.Clamp01(time / Duration);
        float s = size * (1f - .5f * k);
        transform.localScale = new Vector3(s, s * .35f, 1f);
        Color c = color; c.a = 1f - k;
        look.color = c;
    }
}

// 本体の、守っている間のふちどり（演出の色で、本体の絵の写しを少し大きくして後ろに敷く）と、殴られた時の盾の光
public class InkLeafShieldAura : ILeafShieldAura
{
    const float GlowScale = 1.12f, GlowAlpha = .4f, FadeSpeed = 5f;
    readonly SwordBattle owner;
    readonly List<(SpriteRenderer source, SpriteRenderer glow)> glows = new List<(SpriteRenderer, SpriteRenderer)>();
    float level;

    public InkLeafShieldAura(SwordBattle owner) { this.owner = owner; }

    public void Tick(bool shielded, float dt)
    {
        level = Mathf.MoveTowards(level, shielded ? 1f : 0f, FadeSpeed * dt);
        if (level <= 0f)
        {
            foreach (var (_, glow) in glows) if (glow != null) Object.Destroy(glow.gameObject);
            glows.Clear();
            return;
        }
        if (glows.Count == 0)
            foreach (var source in owner.GetComponentsInChildren<SpriteRenderer>())
            {
                if (source == null || source.sprite == null || source.name.StartsWith(InkDefeatEffect.BleedName)) continue;
                var glow = new GameObject("LeafShieldAura").AddComponent<SpriteRenderer>();
                glow.sharedMaterial = source.sharedMaterial;
                glow.sortingLayerID = source.sortingLayerID;
                glow.sortingOrder = source.sortingOrder - 1;
                glows.Add((source, glow));
            }
        foreach (var (source, glow) in glows)
        {
            if (glow == null) continue;
            bool show = source != null && source.enabled && !source.forceRenderingOff;
            glow.enabled = show;
            if (!show) continue;
            glow.sprite = source.sprite;
            glow.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            glow.transform.localScale = source.transform.lossyScale * GlowScale;
            Color c = owner.PlayerMainColor; c.a = GlowAlpha * level;
            glow.color = c;
        }
    }

    public void PlayGuard(Vector2 towardAttacker)
    {
        var body = owner.BodyRenderer;
        float radius = body != null ? body.bounds.extents.magnitude * .75f : 1.5f;
        ShieldGuardArc.Play(owner.transform, towardAttacker, radius, owner.PlayerMainColor,
            body != null ? body.sortingLayerID : 0, (body != null ? body.sortingOrder : 0) + 3);
    }

    public void Dispose()
    {
        foreach (var (_, glow) in glows) if (glow != null) Object.Destroy(glow.gameObject);
        glows.Clear();
    }
}

// 殴られた向きに、本体の前で一瞬出る弧の形の盾の光（演出の色の太い弧と、白い細い弧）
public class ShieldGuardArc : MonoBehaviour
{
    const int Points = 24;
    const float Duration = .3f, OuterSpan = 60f, InnerSpan = 50f, Grow = .23f, OuterWidth = .16f, InnerWidth = .06f;
    Transform centre;
    LineRenderer outer, inner;
    float baseAngle, radius, time;
    Color color;

    public static void Play(Transform centre, Vector2 toward, float radius, Color color, int sortingLayer, int sortingOrder)
    {
        var obj = new GameObject("LeafShieldGuard");
        var arc = obj.AddComponent<ShieldGuardArc>();
        arc.centre = centre;
        arc.radius = radius;
        arc.color = color;
        arc.baseAngle = toward.sqrMagnitude > .0001f ? Mathf.Atan2(toward.y, toward.x) : 0f;
        arc.outer = Line(obj.transform, sortingLayer, sortingOrder);
        arc.inner = Line(obj.transform, sortingLayer, sortingOrder + 1);
        arc.Apply();
    }

    static LineRenderer Line(Transform parent, int sortingLayer, int sortingOrder)
    {
        var obj = new GameObject("Arc");
        obj.transform.SetParent(parent, false);
        var line = obj.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = Points;
        line.numCapVertices = 4;
        line.material = SkillEffects.NewLineMaterial();
        line.sortingLayerID = sortingLayer;
        line.sortingOrder = sortingOrder;
        return line;
    }

    void Update()
    {
        time += Time.deltaTime;
        if (time >= Duration || centre == null) { Destroy(gameObject); return; }
        Apply();
    }

    void Apply()
    {
        float k = Mathf.Clamp01(time / Duration);
        float r = radius * (1f + Grow * (1f - Mathf.Pow(1f - k, 3f)));
        SetArc(outer, r, OuterSpan, radius * OuterWidth * (1f - .5f * k), color, 1f - k);
        SetArc(inner, r, InnerSpan, radius * InnerWidth * (1f - .5f * k), Color.white, 1f - k);
    }

    void SetArc(LineRenderer line, float r, float span, float width, Color c, float alpha)
    {
        Vector3 at = centre.position;
        for (int i = 0; i < Points; i++)
        {
            float a = baseAngle + Mathf.Lerp(-span, span, i / (Points - 1f)) * Mathf.Deg2Rad;
            line.SetPosition(i, at + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * r);
        }
        line.widthMultiplier = width;
        c.a = alpha;
        line.startColor = line.endColor = c;
    }

    void OnDestroy()
    {
        foreach (var line in new[] { outer, inner }) if (line != null && line.material != null) Destroy(line.material);
    }
}
