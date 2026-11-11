using UnityEngine;

// やられた瞬間の「デュクシ」：倒された剣（刀身と柄）を一瞬だけ小刻みに震わせる。
// 剣の本体（物理）は動かさず、見た目のパーツの位置だけをずらすので、ホストの物理計算には影響しない。
// 剣側のコルーチンとは別に動くので、決着で StopAllCoroutines されても、最後は必ず元の位置に戻す
public class DefeatJitter : MonoBehaviour
{
    public const float Duration = .5f;
    const float Amplitude = 3f;    // ワールド単位（カメラの縦の大きさ10の時）
    const float Frequency = 225f;  // 大きいほど細かく震える（横はこの値、縦はこの0.75倍の速さで往復）
    static readonly bool Decays = false; // true にすると、だんだん弱まる

    Transform[] parts;
    Vector3[] baseLocal;
    float born;

    public static void Play(GameObject root, SpriteRenderer[] renderers)
    {
        if (root == null || renderers == null) return;
        var old = root.GetComponent<DefeatJitter>();
        if (old != null) Destroy(old);
        var jitter = root.AddComponent<DefeatJitter>();
        jitter.parts = System.Array.ConvertAll(renderers, r => r != null ? r.transform : null);
        jitter.baseLocal = System.Array.ConvertAll(jitter.parts, p => p != null ? p.localPosition : Vector3.zero);
        jitter.born = Time.unscaledTime;
    }

    void Update()
    {
        float age = Time.unscaledTime - born;
        if (age >= Duration) { Destroy(this); return; }
        float k = Decays ? 1f - age / Duration : 1f;
        // 速い往復の震え。ずらす量は世界の長さで決め、親の拡大率で割って子の座標に直す
        Vector3 world = new Vector3(Mathf.Sin(age * Frequency), Mathf.Cos(age * Frequency * .75f) * .6f, 0f) * (Amplitude * k);
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] == null) continue;
            Transform parent = parts[i].parent;
            Vector3 local = parent != null ? parent.InverseTransformVector(world) : world;
            parts[i].localPosition = baseLocal[i] + local;
        }
    }

    void OnDestroy()
    {
        for (int i = 0; parts != null && i < parts.Length; i++)
            if (parts[i] != null) parts[i].localPosition = baseLocal[i];
    }
}
