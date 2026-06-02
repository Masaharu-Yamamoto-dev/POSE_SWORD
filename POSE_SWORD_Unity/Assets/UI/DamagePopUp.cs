using UnityEngine;
using TMPro;

public class DamagePopup : MonoBehaviour
{
    private TextMeshPro textMesh;
    public float floatSpeed = 3f;  // 浮き上がるスピード
    public float fadeSpeed = 1.5f; // 消えるスピード
    
    private Color textColor;
    private Vector3 moveVector;

    public void Setup(int damageAmount, bool isCrit)
    {
        textMesh = GetComponent<TextMeshPro>();
        textMesh.text = damageAmount.ToString();

        // 少し左右にランダムに散らして、連続ヒット時に数字が重ならないようにする
        moveVector = new Vector3(Random.Range(-0.5f, 0.5f), 1f, 0f).normalized * floatSpeed;

        if (isCrit)
        {
            textMesh.fontSize = 8f;
            textColor = new Color(1f, 0.2f, 0.2f); // クリティカルは赤色
            // 他の数字より手前に表示して目立たせる
            transform.position += new Vector3(0, 0, -1f);
        }
        else
        {
            textMesh.fontSize = 5f;
            textColor = Color.white; // 通常は白色
        }

        textMesh.color = textColor;
        
        // 1秒後に自動でオブジェクトを破壊する（メモリ節約）
        Destroy(gameObject, 1.0f);
    }

    void Update()
    {
        // 上方向に移動
        transform.position += moveVector * Time.deltaTime;

        // 徐々に透明にする
        textColor.a -= fadeSpeed * Time.deltaTime;
        textMesh.color = textColor;
    }
}