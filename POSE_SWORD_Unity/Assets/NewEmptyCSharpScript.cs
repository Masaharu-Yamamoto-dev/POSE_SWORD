using System.Collections.Generic;
using UnityEngine;

public class RideablePlatform : MonoBehaviour
{
    // 床に乗っているオブジェクトを記憶しておくリスト
    private List<Transform> riders = new List<Transform>();
    
    // 1フレーム前の床の位置
    private Vector3 previousPosition;

    void Start()
    {
        // 最初の位置を記憶
        previousPosition = transform.position;
    }

    // Updateの後に実行されるLateUpdateを使うことで、移動のガクつきを防ぎます
    void LateUpdate()
    {
        // この1フレームで床がどれだけ動いたか（移動量）を計算
        Vector3 deltaPosition = transform.position - previousPosition;

        // リストに登録されている（床に乗っている）すべてのオブジェクトを同じ距離だけ動かす
        foreach (Transform rider in riders)
        {
            rider.position += deltaPosition;
        }

        // 現在の床の位置を、次回の計算のために記憶
        previousPosition = transform.position;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        // 上から乗ってきた場合のみ
        if (collision.contacts[0].normal.y < -0.5f)
        {
            // 親子関係（SetParent）は使わず、リストにキャラクターを登録するだけ！
            if (!riders.Contains(collision.transform))
            {
                riders.Add(collision.transform);
            }
        }
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        // 床から離れたらリストから解除する
        if (riders.Contains(collision.transform))
        {
            riders.Remove(collision.transform);
        }
    }
}