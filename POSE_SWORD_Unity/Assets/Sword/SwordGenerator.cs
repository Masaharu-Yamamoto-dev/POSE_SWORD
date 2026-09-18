using UnityEngine;
using System;

// ▼ JSONのデータ構造に完全に一致させたクラス

public class SwordGenerator : MonoBehaviour
{
    public bool LastGenerationSucceeded { get; private set; }
    private Sprite generatedSprite;
    private Texture2D generatedTexture;
    [Header("テスト用のJSONファイル")]
    public TextAsset dummyJsonFile;

    [Header("セットアップ用")]
    public SpriteRenderer targetSpriteRenderer;
    public Rigidbody2D swordRigidbody;
    public PolygonCollider2D bladeCollider;
    public SwordBattle swordBattle;

    [Header("柄のオブジェクト（独楽モード時は消す）")]
    public GameObject handleObject;
    // ▼【新規追加】柄オブジェクトのInspectorで設定された元のlocalScale(=handleSprite0の画像サイズに対して
    // 手動で調整済みのスケール)を、最初にhiltTypeで切り替える前に一度だけ記録しておく。
    // handleSprite1〜3は元画像のピクセルサイズがhandleSprite0とバラバラなため、これを基準に
    // 毎回スケールを正規化しないと、切り替えた柄が極端に小さく/大きく表示されてしまう
    private Vector3? handleBaselineScale;

    [Header("柄（つか）の見た目：hiltTypeごとの画像(React側の武器庫の柄と対応)")]
    public Sprite handleSprite0; // "0"(未指定/不明な値含む) = 普通の柄
    public Sprite handleSprite1; // "1" = 武骨な柄
    public Sprite handleSprite2; // "2" = 悪魔の柄
    public Sprite handleSprite3; // "3" = 大翼の柄

    [Header("刀身の理想の太さ（横幅）")]
    public float targetBladeWidth = 1.5f;

    [Header("テスト実行設定")]
    public bool generateOnStart = true; // チェックを入れるとゲーム開始時に自動生成

    void Start()
    {
        // TextAssetからJSONテキストを取り出して実行
        if (generateOnStart && dummyJsonFile != null && !string.IsNullOrEmpty(dummyJsonFile.text))
        {
            GenerateSwordFromJson(dummyJsonFile.text);
        }
    }

    public void GenerateSwordFromJson(string jsonString)
    {
        LastGenerationSucceeded = false;
        if (string.IsNullOrEmpty(jsonString)) 
        {
            Debug.LogError("❌ SwordGenerator: jsonStringが空です！");
            return;
        }

        Debug.Log($"📄 SwordGenerator.GenerateSwordFromJson 呼び出し: {jsonString.Substring(0, Mathf.Min(100, jsonString.Length))}...");
        
        SwordData data = JsonUtility.FromJson<SwordData>(jsonString);
        
        if (data == null)
        {
            Debug.LogError("❌ JSONの解析に失敗しました。形式が正しいか確認してください。");
            return;
        }

        Debug.Log($"✅ Parsed SwordData: name={data.name}, hp={data.hp}, attack={data.attack}, weight={data.weight}");

        // =========================================================
        // ⚔️ ステータスと物理演算への適用
        // =========================================================
        
        if (swordBattle != null)
        {
            // Webから来た 1〜100 の値を安全に制限（1未満や100オーバーのバグを防ぐ）
            float rawAttack = Mathf.Clamp(data.attack, 1f, 100f);
            float rawWeight = Mathf.Clamp(data.weight, 1f, 100f);

            // 1〜100の数値を「0.0 〜 1.0 の割合（パーセンテージ）」に直す
            float attackRatio = (rawAttack - 1f) / 99f;
            float weightRatio = (rawWeight - 1f) / 99f;

            // 攻撃力：割合に応じて 10 〜 90 の間に変換
            int actualAttack = Mathf.RoundToInt(Mathf.Lerp(10f, 90f, attackRatio));
            
            // 重さ：割合に応じて 7.0 〜 25.0 の間に変換
            float actualWeight = Mathf.Lerp(7f, 25f, weightRatio);
            
            // HPはそのまま、攻撃力は変換した値をセット
            swordBattle.SetupStatus(data.name, data.hp, actualAttack, data.hiltType);

            if (swordRigidbody != null)
            {
                swordRigidbody.mass = actualWeight;
                swordRigidbody.centerOfMass = Vector2.zero; // 重心リセット
            }

            Debug.Log($"剣の生成完了: {data.name} | 見た目の攻撃力:{rawAttack} → 実攻撃力:{actualAttack} | 見た目の重さ:{rawWeight} → 実質量:{actualWeight}");
        }

        // ▼【修正】柄(hiltType)の表示/非表示・見た目切り替えは、刀身の画像(imageStr)とは独立したデータ。
        // 以前は下の「画像と当たり判定の反映」ブロック(imageStrが空、あるいはBase64デコード/画像読み込みに
        // 失敗すると丸ごとスキップされる)の内側でしか柄を更新しておらず、クライアント側で同期された
        // imageStrが一時的に空・不正だった場合に柄だけ生成/更新されずに残ってしまうことがあった。
        // 刀身画像の成否に関わらず必ず柄を更新するため、先に独立して呼び出す
        ApplyHandleAppearance(data);

        // =========================================================
        // 🎨 画像と当たり判定の反映
        // =========================================================

        if (!string.IsNullOrEmpty(data.imageStr))
        {
            string base64String = data.imageStr;

            // DataURL形式（data:image/png;base64,xxxxx）の場合は base64部分を抽出
            if (base64String.Contains(","))
            {
                base64String = base64String.Split(',')[1];
                Debug.Log("📌 DataURL形式を検出して Base64 に変換しました");
            }

            try
            {
                byte[] imageBytes = Convert.FromBase64String(base64String);
                Texture2D tex = new Texture2D(4, 4);

                // ▼【重要】エラーを回避するための安全装置
                bool isLoaded = tex.LoadImage(imageBytes);
                if (!isLoaded)
                {
                    Destroy(tex);
                    Debug.LogError("❌ 画像データの読み込みに失敗しました！");
                    return;
                }

                // Spriteの生成
                Sprite newSprite = Sprite.Create(
                    tex,
                    new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.0f),
                    100f
                );

                if (targetSpriteRenderer != null)
                {
                    targetSpriteRenderer.sprite = newSprite;

                    // 画像サイズに関わらず、理想の太さに自動リサイズする処理
                    float currentWidth = newSprite.bounds.size.x;
                    if (currentWidth > 0)
                    {
                        float scaleRatio = targetBladeWidth / currentWidth;
                        targetSpriteRenderer.transform.localScale = new Vector3(scaleRatio, scaleRatio, 1f);
                    }

                    Debug.Log($"✅ 剣のスプライトを設定し、太さを {targetBladeWidth} に統一しました");

                    // コライダーの再生成（スケール変更後に実行するのがベストです）
                    if (bladeCollider != null)
                    {
                        bladeCollider.enabled = false;
                        Destroy(bladeCollider);
                        bladeCollider = targetSpriteRenderer.gameObject.AddComponent<PolygonCollider2D>();
                        Debug.Log("✅ PolygonCollider2D を再生成しました");
                    }
                }
                if (generatedSprite != null) Destroy(generatedSprite);
                if (generatedTexture != null) Destroy(generatedTexture);
                generatedSprite = newSprite;
                generatedTexture = tex;
                LastGenerationSucceeded = true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"❌ 画像の読み込みに失敗: {e.Message}");
            }
        }
    }

    // ▼【新規追加】柄(Handle-A)の表示/非表示(独楽モードかどうか)とhiltTypeに応じた見た目(スプライト)切り替え。
    // 刀身の画像(imageStr)生成とは完全に独立しており、imageStrが空/読み込み失敗でも必ず呼ばれる
    void ApplyHandleAppearance(SwordData data)
    {
        if (handleObject == null) return;

        // ▼ hiltTypeで切り替える前の、Inspectorで手動調整済みのlocalScaleを一度だけ記録する
        // (これがhandleSprite0の画像サイズに対して正しくチューニングされている前提の基準値)
        if (handleBaselineScale == null) handleBaselineScale = handleObject.transform.localScale;

        // ▼ hiltType(柄の種類)に応じて、柄の見た目(スプライト)もReact側の
        // 武器庫(SwordListScreen.jsx / HILT_DATABASE)と対応する画像に切り替える
        var handleRenderer = handleObject.GetComponent<SpriteRenderer>();
        if (handleRenderer != null)
        {
            Sprite handleSprite = handleSprite0;
            switch (data.hiltType)
            {
                case "1": handleSprite = handleSprite1; break;
                case "2": handleSprite = handleSprite2; break;
                case "3": handleSprite = handleSprite3; break;
            }
            if (handleSprite != null)
            {
                handleRenderer.sprite = handleSprite;
                // ▼ handleSprite0以外は元画像のピクセルサイズがバラバラなため、
                // handleSprite0を基準に横幅を正規化してスケールを補正する
                // (これが無いと切り替えた柄が極端に小さく/大きく表示される)
                float defaultWidth = handleSprite0 != null ? handleSprite0.bounds.size.x : 0f;
                float newWidth = handleSprite.bounds.size.x;
                if (defaultWidth > 0f && newWidth > 0f)
                    handleObject.transform.localScale = handleBaselineScale.Value * (defaultWidth / newWidth);
            }
        }
        handleObject.SetActive(!SwordController.isKomaMode);
        // ▼ 柄を(残機の持ち替えなどで)差し替えた時に、人物の刀身画像より
        // 手前に来てしまうことがあったため、Z座標を明示的に奥へ固定して重ならないようにする
        Vector3 handlePos = handleObject.transform.localPosition;
        handleObject.transform.localPosition = new Vector3(handlePos.x, handlePos.y, -5f);
        // ▼【調査用ログ】実機のクライアント側だけ柄が透明/非表示になる不具合を追うための診断ログ。
        // ログ上は有効(active/sprite割り当て済み)なのに画面には出ない、という状況を追うため、
        // 位置・スケール・描画順まで含めて詳しく出す
        var sr = handleObject.GetComponent<SpriteRenderer>();
        Debug.Log($"✅ 柄の表示状態を更新しました: active={!SwordController.isKomaMode}, " +
            $"sword={gameObject.name}, handleObj={handleObject.name}(id={handleObject.GetInstanceID()}), " +
            $"activeSelf={handleObject.activeSelf}, sprite={(sr != null ? sr.sprite?.name : "no-renderer")}, " +
            $"color={(sr != null ? sr.color.ToString() : "n/a")}, hiltType={data.hiltType}, " +
            $"localPos={handleObject.transform.localPosition}, worldPos={handleObject.transform.position}, " +
            $"localScale={handleObject.transform.localScale}, lossyScale={handleObject.transform.lossyScale}, " +
            $"sortLayer={(sr != null ? sr.sortingLayerID : -1)}, sortOrder={(sr != null ? sr.sortingOrder : -1)}, " +
            $"spriteBounds={(sr != null && sr.sprite != null ? sr.sprite.bounds.size.ToString() : "n/a")}, " +
            $"rootPos={transform.position}, rootScale={transform.lossyScale}, " +
            $"bladePos={(targetSpriteRenderer != null ? targetSpriteRenderer.transform.position.ToString() : "n/a")}, " +
            $"bladeSortOrder={(targetSpriteRenderer != null ? targetSpriteRenderer.sortingOrder.ToString() : "n/a")}");
    }

    void OnDestroy()
    {
        if (generatedSprite != null) Destroy(generatedSprite);
        if (generatedTexture != null) Destroy(generatedTexture);
    }

    // ▼【新規追加】残機モードの分身演出用：base64画像からSpriteだけを単体で生成する。
    // GenerateSwordFromJsonとは独立した用途(装備していない残機の剣の見た目を分身に流用する)のため、
    // どのGameObject/コライダーにも紐付けず、呼び出し側がSpriteRendererへ割り当てて使う
    public static Sprite CreateSpriteFromBase64(string imageStr)
    {
        if (string.IsNullOrEmpty(imageStr)) return null;
        string base64String = imageStr;
        if (base64String.Contains(",")) base64String = base64String.Split(',')[1];
        try
        {
            byte[] imageBytes = Convert.FromBase64String(base64String);
            Texture2D tex = new Texture2D(4, 4);
            if (!tex.LoadImage(imageBytes))
            {
                Destroy(tex);
                return null;
            }
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0f), 100f);
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ 分身用スプライトの読み込みに失敗: {e.Message}");
            return null;
        }
    }
}
