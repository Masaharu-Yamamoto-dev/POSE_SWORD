using UnityEngine;

public class BattleCamera : MonoBehaviour
{
    [Tooltip("戦闘中に画角へ収める全プレイヤー(2〜4人)。SceneControllerが試合開始時にセットする")]
    public Transform[] combatants;
    public Camera cam;

    [Header("カメラの設定")]
    public float minSize = 5f;
    public float maxSize = 25f;
    public float zoomSpeed = 5f;
    public float margin = 3f;
    public float zoomMultiplier = 0.8f;

    [Header("高さの制限")]
    public float baseY = 3f;

    [Header("カウントダウン演出設定")]
    [Tooltip("数値が大きいほど、クローズアップ時にカメラが上に移動します（剣が画面のやや下に映るようになります）")]
    public float countdownOffsetY = 2.0f; 

    // ▼【新規追加】独楽モードのカメラ重心調整
    [Header("独楽モードのカメラ重心調整")]
    [Tooltip("SwordControllerの centerOfMass のY値と同じにしてください")]
    public float komaCenterOffsetY = 2.0f;

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

    // ▼【新規追加】重心（回転の本当の中心）を取得する関数
    // ▼【修正】重心（回転の本当の中心）を取得する関数
    // ▼【修正】画像サイズや物理モードに一切左右されず、完全にブレない中心座標を直接取得する！
    private Vector3 GetSafePosition(Transform t)
    {
        if (t == null) return Vector3.zero;
        
        // オブジェクトから SwordBattle コンポーネントを取得
        SwordBattle battle = t.GetComponent<SwordBattle>();
        if (battle != null)
        {
            // 独楽モード・剣モード、Host・Clientに関わらず、完全に同期された中心を返す
            return battle.currentCenterPosition;
        }
        
        return t.position;
    }

    void LateUpdate()
    {
        Vector3 currentBasePosition = transform.position;
        float targetY;

        // 1. 戦闘中：通常追従モード
        if (isTracking)
        {
            // ▼【N人対応】生存中の全プレイヤーのバウンディングボックスで画角を決める
            // (2人の場合は従来通り、2点間の距離ベースのズームと完全に一致する)
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            int aliveCount = 0;

            if (combatants != null)
            {
                for (int i = 0; i < combatants.Length; i++)
                {
                    Transform t = combatants[i];
                    if (t == null || !t.gameObject.activeInHierarchy) continue;

                    SwordBattle battle = t.GetComponent<SwordBattle>();
                    if (battle != null && battle.IsDead) continue;

                    Vector3 pos = GetSafePosition(t);
                    minX = Mathf.Min(minX, pos.x);
                    maxX = Mathf.Max(maxX, pos.x);
                    minY = Mathf.Min(minY, pos.y);
                    maxY = Mathf.Max(maxY, pos.y);
                    aliveCount++;
                }
            }

            if (aliveCount == 0) return;

            Vector3 centerPoint = new Vector3((minX + maxX) / 2f, (minY + maxY) / 2f, 0f);
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

            float diagonal = Vector2.Distance(new Vector2(minX, minY), new Vector2(maxX, maxY));
            float targetSize = Mathf.Clamp(diagonal * zoomMultiplier + margin, minSize, maxSize);
            cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, targetSize, Time.deltaTime * zoomSpeed);
        }
        // 2. 開幕カウントダウン：演出用フォーカスモード
        else if (isCustomFocusing)
        {
            if (focusTarget != null)
            {
                // ▼【修正】カウントダウン中も重心を狙うようにする
                Vector3 focusPos = GetSafePosition(focusTarget);
                
                Vector3 targetPos = new Vector3(
                    focusPos.x, 
                    focusPos.y + countdownOffsetY, 
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