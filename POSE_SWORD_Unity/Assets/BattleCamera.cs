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

    // ▼【新規追加】カウントダウン演出時の上下位置を調整する枠！
    [Header("カウントダウン演出設定")]
    [Tooltip("数値が大きいほど、クローズアップ時にカメラが上に移動します（剣が画面のやや下に映るようになります）")]
    public float countdownOffsetY = 2.0f; 

    private float shakeDuration = 0f;
    private float shakeMagnitude = 0.5f;

    private bool isTracking = true;
    private Vector3 fixedPosition;

    private bool isCustomFocusing = false;
    private Transform focusTarget;
    private float focusSize;

    public void TriggerShake(float duration, float magnitude)
    {
        shakeDuration = duration;
        shakeMagnitude = magnitude;
    }

    public void StopTracking()
    {
        isTracking = false;
        isCustomFocusing = false; 
        fixedPosition = transform.position; 
    }

    public void FocusOnTarget(Transform target, float zoomSize)
    {
        isTracking = false;
        isCustomFocusing = true;
        focusTarget = target;
        focusSize = zoomSize;
    }

    public void ResetFocus()
    {
        isCustomFocusing = false;
        isTracking = true; 
    }

    void LateUpdate()
    {
        Vector3 currentBasePosition = transform.position;
        float targetY;

        // 1. 戦闘中：通常追従モード
        if (isTracking)
        {
            if (player == null || enemy == null) return;

            Vector3 centerPoint = (player.position + enemy.position) / 2f;
            if (!SwordController.isKomaMode)
            {
                targetY = Mathf.Max(baseY, centerPoint.y * 0.3f); 
            }
            else
            {
                targetY = centerPoint.y;
            }
            Vector3 targetPosition = new Vector3(centerPoint.x, targetY, -10f);
            
            currentBasePosition = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * 5f);
            transform.position = currentBasePosition;

            float distance = Vector2.Distance(player.position, enemy.position);
            float targetSize = Mathf.Clamp(distance * zoomMultiplier + margin, minSize, maxSize);
            cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, targetSize, Time.deltaTime * zoomSpeed);
        }
        // 2. 開幕カウントダウン：演出用フォーカスモード
        else if (isCustomFocusing)
        {
            if (focusTarget != null)
            {
                // ★【修正】Y座標の計算に countdownOffsetY をプラスして、カメラを少し上に持ち上げる！
                Vector3 targetPos = new Vector3(
                    focusTarget.position.x, 
                    focusTarget.position.y + countdownOffsetY, 
                    -10f
                );
                
                currentBasePosition = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * 8f);
            }
            transform.position = currentBasePosition;
            cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, focusSize, Time.deltaTime * 8f);
        }
        // 3. 決着後：完全固定モード
        else
        {
            currentBasePosition = fixedPosition;
            transform.position = currentBasePosition;
        }

        // 揺れ（シェイク）の処理
        if (shakeDuration > 0)
        {
            transform.position = currentBasePosition + (Vector3)Random.insideUnitCircle * shakeMagnitude;
            shakeDuration -= Time.unscaledDeltaTime; 
        }
    }
}