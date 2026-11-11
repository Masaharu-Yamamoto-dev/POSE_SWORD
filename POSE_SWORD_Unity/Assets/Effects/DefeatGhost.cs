using System.Collections.Generic;
using UnityEngine;

// 残機制で剣が1本壊れたときの「抜け殻」。ふだんは壊れた剣自身が撃破と同じやられ演出をしてから次の剣が出るので使わない。
// ゲストが通信の都合で壊れた状態(HP0)を受け取れず、いきなり次の剣への持ち替えが届いた時だけ、本物の剣はすぐ出現位置へ
// 移るので、壊れた瞬間の見た目（刀身と柄）だけを写したコピーをその場に残し、撃破と同じやられ方（墨に染まって吹き飛び、消える）をさせる。
// 物理エンジンは使わず自前で動かすので、ホストの物理計算や試合の進み方には影響しない
public class DefeatGhost : MonoBehaviour
{
    // 撃破された剣と同じ間合い：吹き飛んでからこの秒数で消え始め、VanishTime 秒で消え切る
    public const float VanishDelay = .75f, VanishTime = .6f;
    // 吹き飛び方（撃破された本物の剣も同じ値）：上へ跳ね上がり、左右には少しだけ。回転は1秒で1回転。
    // 消え始めるまでに頂点を少し過ぎるくらいの高さで、落ちてくる距離は短い
    public const float KnockUp = 16f, KnockSide = 3f, Spin = 360f, GravityScale = 1.5f;
    // 決着の一撃で倒された剣は、もっと高く飛ばす（決着のスローでゆっくり上がっていくのをカメラが追う）
    public const float FinalKnockUp = 24f;
    // 描画順：やられた剣は、ほかの剣より手前、ヒットエフェクト・ダメージ数字・墨より奥に出す
    // （撃破した側の剣に隠れて見えにくくならないように）。生きている剣とヒットエフェクトはもともと0
    public const int DefeatedSortingOffset = 20, HitEffectSortingOrder = 22, VanishCloudSortingOrder = 21;

    static readonly List<DefeatGhost> active = new List<DefeatGhost>();

    SpriteRenderer[] renderers;
    float[] startAlpha;
    MultiplayerManager owner;
    // 剣から引き取った刀身の画像。抜け殻が消えるときに破棄する
    Sprite ownedSprite;
    Texture2D ownedTexture;
    Vector3 velocity, startScale;
    float spin, age, hold;
    bool vanishing;

    public static void Spawn(SwordBattle source, MultiplayerManager owner)
    {
        if (source == null) return;
        var root = new GameObject("DefeatGhost");
        root.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
        var copies = new List<SpriteRenderer>();
        foreach (var original in source.GetComponentsInChildren<SpriteRenderer>())
        {
            if (!original.enabled || original.sprite == null) continue;
            var part = new GameObject(original.name);
            part.transform.SetPositionAndRotation(original.transform.position, original.transform.rotation);
            part.transform.localScale = original.transform.lossyScale;
            part.transform.SetParent(root.transform, true);
            var copy = part.AddComponent<SpriteRenderer>();
            copy.sprite = original.sprite;
            copy.sharedMaterial = original.sharedMaterial;
            copy.flipX = original.flipX; copy.flipY = original.flipY;
            copy.drawMode = original.drawMode;
            if (original.drawMode != SpriteDrawMode.Simple) copy.size = original.size;
            copy.sortingLayerID = original.sortingLayerID;
            copy.sortingOrder = original.sortingOrder + DefeatedSortingOffset;
            copy.color = original.color;
            copies.Add(copy);
        }
        var ghost = root.AddComponent<DefeatGhost>();
        ghost.renderers = copies.ToArray();
        // 直後に次の剣を生成すると、今の刀身の画像は破棄されてしまう。抜け殻が引き取って、消えるまで使う
        var generator = source.GetComponent<SwordGenerator>();
        if (generator != null) (ghost.ownedSprite, ghost.ownedTexture) = generator.DetachGeneratedAssets();
        ghost.owner = owner;
        ghost.startScale = root.transform.localScale;
        // 撃破された剣と同じく、上へ軽く跳ね上げて回す（左右はランダム）
        ghost.velocity = new Vector3(Random.Range(-KnockSide, KnockSide), KnockUp, 0f);
        ghost.spin = Random.value < .5f ? -Spin : Spin;
        ghost.hold = DefeatEffects.Current.KillMarkDuration;
        DefeatJitter.Play(root, ghost.renderers);
        if (owner != null) owner.AddCameraTarget(root.transform);
        active.Add(ghost);
    }

    public static void ClearAll()
    {
        foreach (var ghost in active.ToArray()) if (ghost != null) Destroy(ghost.gameObject);
        active.Clear();
    }

    // 試合の時間で動く（撃破された本物の剣と同じく、スローがかかれば一緒に遅くなる）。
    // とどめの印を書き終えるまでは、その場で止まる（最初に一瞬震える）。そのあと吹き飛んで消える
    void Update()
    {
        float dt = Time.deltaTime;
        age += dt;
        if (age < hold) return;
        if (startAlpha == null)
        {
            // 跳ね上がる瞬間に、倒された見た目（色が抜ける）にする
            DefeatEffects.Current.ApplyDefeatedLook(renderers);
            startAlpha = System.Array.ConvertAll(renderers, r => r != null ? r.color.a : 0f);
        }
        float flying = age - hold;
        velocity.y += Physics2D.gravity.y * GravityScale * dt;
        transform.position += velocity * dt;
        transform.Rotate(0f, 0f, spin * dt);
        if (flying < VanishDelay) return;
        if (!vanishing)
        {
            vanishing = true;
            DefeatEffects.Current.PlayVanish(Center(), 1f);
        }
        float k = Mathf.Clamp01((flying - VanishDelay) / VanishTime);
        transform.localScale = startScale * (1f + .12f * k);
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null) { var c = renderers[i].color; c.a = startAlpha[i] * (1f - k); renderers[i].color = c; }
        DefeatEffects.Current.UpdateVanish(renderers, k);
        if (k >= 1f) Destroy(gameObject);
    }

    Vector3 Center()
    {
        var bounds = new Bounds(transform.position, Vector3.zero);
        foreach (var r in renderers) if (r != null) bounds.Encapsulate(r.bounds);
        return bounds.center;
    }

    void OnDestroy()
    {
        active.Remove(this);
        if (owner != null) owner.RemoveCameraTarget(transform);
        if (ownedSprite != null) Destroy(ownedSprite);
        if (ownedTexture != null) Destroy(ownedTexture);
    }
}
