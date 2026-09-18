using UnityEngine;

public class RelativePingPongMovement : MonoBehaviour
{

    [SerializeField, Tooltip("移動するスピード")]
    private float speed = 3.0f;

    [SerializeField, Tooltip("往復する幅（距離）")]
    private float distance = 5.0f;

    [SerializeField, Tooltip("チェックを入れるとX軸(横)移動、外すとY軸(縦)移動")]
    private bool isHorizontal = true;

    private Vector2 startLocalPos;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        // ゲーム開始時の「親オブジェクトから見た相対座標」を記憶しておく
        startLocalPos = transform.localPosition;
    }

    // Update is called once per frame
    void Update()
    {
        float offset = Mathf.PingPong(Time.time * speed, distance) - (distance / 2f);

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
}
