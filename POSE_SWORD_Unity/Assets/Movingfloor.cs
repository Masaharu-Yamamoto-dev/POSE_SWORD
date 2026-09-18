using UnityEngine;
using System.Collections.Generic;

public class RelativePingPongMovement : MonoBehaviour
{

    [SerializeField, Tooltip("移動するスピード（1秒あたりに進む距離）")]
    private float speed = 3.0f;

    [SerializeField, Tooltip("往復する幅（距離）。Point A / Point Bを両方割り当てている場合はこちらは使われない")]
    private float distance = 5.0f;

    [SerializeField, Tooltip("チェックを入れるとX軸(横)移動、外すとY軸(縦)移動。Point A / Point Bを両方割り当てている場合はこちらは使われない")]
    private bool isHorizontal = true;

    [Header("2点間を往復させたい場合（任意）")]
    [Tooltip("両方割り当てると、Distance/Is Horizontalの代わりにこの2点のワールド座標間を直接往復するようになる。" +
        "シーンに空のGameObjectを2つ置いてドラッグするだけで、Sceneビュー上で視覚的に始点・終点を決められる。")]
    [SerializeField] private Transform pointA;
    [SerializeField] private Transform pointB;

    private Vector2 startLocalPos;
    private Vector3 previousPosition;

    // ▼【新規追加】今この足場に触れている(乗っている)Rigidbody2Dの集合。Parent-Childにはしない
    // (Goundは非均一な巨大スケールを持っており、子にすると剣のスケールが歪んでしまう。また剣の位置・
    // スケールはSYNC補間や必殺技演出など他の場所からも直接書き換えられているため、親子関係にすると
    // それらと競合する)。代わりに、触れている間だけ足場の移動量をそのまま加算することで、
    // 実質的に「足場と一緒に動くグループ」と同じ見た目を、既存の処理と衝突せずに実現する
    private readonly HashSet<Rigidbody2D> riders = new HashSet<Rigidbody2D>();

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        // ゲーム開始時の「親オブジェクトから見た相対座標」を記憶しておく
        startLocalPos = transform.localPosition;
        previousPosition = transform.position;
    }

    // ▼【新規追加】Time.timeだと「各端末のアプリ起動からの経過時間」になってしまい、Host/ゲストで
    // 起動タイミングがズレていると足場の往復の位相もズレてしまう(ゲスト端末で見ると足場の位置と
    // 剣の位置が微妙に合わない見た目になる)。SwordBattle.roundStartUnscaledTimeが記録済みなら
    // 「試合開始(isRoundStartedがtrueになった瞬間)からの経過時間」を基準にすることで、
    // Host/ゲスト双方でほぼ同じ位相になるようにする(まだ試合が始まっていない時だけTime.unscaledTimeに戻す)
    float ElapsedSinceRoundStart()
    {
        return SwordBattle.roundStartUnscaledTime >= 0f
            ? Time.unscaledTime - SwordBattle.roundStartUnscaledTime
            : Time.unscaledTime;
    }

    // Update is called once per frame
    void Update()
    {
        float t01Basis = ElapsedSinceRoundStart() * speed;

        // ▼【新規追加】Point A / Point Bが両方割り当てられていたら、その2点のワールド座標間を
        // 直接往復する。speedは「1秒あたりに進む距離」として、2点間の実際の距離から周期を逆算するので、
        // 下のDistance方式と体感速度が揃うようにしている
        if (pointA != null && pointB != null)
        {
            float pointDistance = Vector3.Distance(pointA.position, pointB.position);
            float t = pointDistance > 0f ? Mathf.PingPong(t01Basis, pointDistance) / pointDistance : 0f;
            transform.position = Vector3.Lerp(pointA.position, pointB.position, t);
        }
        else
        {
            float offset = Mathf.PingPong(t01Basis, distance) - (distance / 2f);

            // transform.position ではなく transform.localPosition を更新する
            if (isHorizontal)
            {
                transform.localPosition = new Vector2(startLocalPos.x + offset, startLocalPos.y);
            }
            else
            {
                transform.localPosition = new Vector2(startLocalPos.x, startLocalPos.y + offset);
            }
        }

        // ▼【新規追加】足場が今フレームで動いた分だけ、乗っているRigidbody2Dも一緒にずらす
        Vector3 delta = transform.position - previousPosition;
        if (delta.sqrMagnitude > 0f)
        {
            foreach (var rider in riders)
            {
                if (rider != null) rider.position += (Vector2)delta;
            }
        }
        previousPosition = transform.position;
    }

    // ▼ 物理的に接触している間だけ「乗っている」とみなす。壁など、Rigidbody2Dを持たない
    // コライダーとの接触(collision.rigidbody == null)は無視する
    void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.rigidbody != null) riders.Add(collision.rigidbody);
    }

    void OnCollisionExit2D(Collision2D collision)
    {
        if (collision.rigidbody != null) riders.Remove(collision.rigidbody);
    }
}
