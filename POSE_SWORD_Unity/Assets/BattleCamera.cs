using UnityEngine;

public class BattleCamera : MonoBehaviour
{
    public Transform player;
    public Transform enemy;
    public Camera cam;

    [Header("カメラの設定")]
    public float minSize = 5f;
    public float maxSize = 25f;
    public float zoomSpeed = 5f;
    public float margin = 3f;
    public float zoomMultiplier = 0.8f;

    [Header("高さの制限")]
    public float baseY = 3f;

    private float shakeDuration = 0f;
    private float shakeMagnitude = 0.5f;

    // ▼追加：カメラの追従を止めるためのフラグと固定位置
    private bool isTracking = true;
    private Vector3 fixedPosition;

    public void TriggerShake(float duration, float magnitude)
    {
        shakeDuration = duration;
        shakeMagnitude = magnitude;
    }

    // ▼追加：決着時にカメラを固定する関数
    public void StopTracking()
    {
        isTracking = false;
        fixedPosition = transform.position; // 今の位置を記録して固定！
    }

    void LateUpdate()
    {
        if (player == null || enemy == null) return;

        // 今の「基準となる位置」を変数に入れておく
        Vector3 currentBasePosition = transform.position;

        // 戦闘中：追従モード
        if (isTracking)
        {
            Vector3 centerPoint = (player.position + enemy.position) / 2f;
            float targetY = Mathf.Max(baseY, centerPoint.y * 0.3f); 
            Vector3 targetPosition = new Vector3(centerPoint.x, targetY, -10f);
            
            currentBasePosition = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * 5f);
            transform.position = currentBasePosition;

            float distance = Vector2.Distance(player.position, enemy.position);
            float targetSize = Mathf.Clamp(distance * zoomMultiplier + margin, minSize, maxSize);
            cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, targetSize, Time.deltaTime * zoomSpeed);
        }
        // 決着後：固定モード
        else
        {
            currentBasePosition = fixedPosition;
            transform.position = currentBasePosition;
        }

        // 揺れ（シェイク）の処理
        if (shakeDuration > 0)
        {
            // 基準位置からランダムにズラすことで、揺れが終わった後に元の位置にピタッと戻る
            transform.position = currentBasePosition + (Vector3)Random.insideUnitCircle * shakeMagnitude;
            shakeDuration -= Time.unscaledDeltaTime; 
        }
    }
}