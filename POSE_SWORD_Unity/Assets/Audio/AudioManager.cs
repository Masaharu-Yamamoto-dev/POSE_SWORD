using UnityEngine;
using UnityEngine.Audio; // ミキサー操作に必須

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance;

    [Header("オーディオミキサー設定")]
    public AudioMixer mainMixer;

    private bool isSlownDown = false;
    private float currentPitch = 1f;
    private float currentEcho = 0f;

    void Awake()
    {
        Instance = this;
        // ▼ 削除：ここは早すぎてミキサーが無視してしまうので消します
        // ResetSoundEffects(); 
    }

    // ▼ 追加：Awakeの後に呼ばれる Start を新しく作り、ここで呼び出します
    void Start()
    {
        ResetSoundEffects();
    }

    void Update()
    {
        // ▼ 追加・変更：時間が通常の時は「常に」通常状態に固定する（力技のバグ対策）
        if (Time.timeScale == 1f)
        {
            isSlownDown = false;
            currentPitch = 1f;
            currentEcho = 0f;
            mainMixer.SetFloat("SEPitch", 1f);
            mainMixer.SetFloat("EchoWet", 0f);
        }
        // 決着時（スローモーション中）
        else if (Time.timeScale < 1f && Time.timeScale > 0f)
        {
            isSlownDown = true;
            currentPitch = Mathf.Lerp(currentPitch, Time.timeScale, Time.unscaledDeltaTime * 10f);
            currentEcho = Mathf.Lerp(currentEcho, 0.8f, Time.unscaledDeltaTime * 5f);
            
            mainMixer.SetFloat("SEPitch", currentPitch);
            mainMixer.SetFloat("EchoWet", currentEcho);
        }
        // 完全停止時
        else if (Time.timeScale == 0f && isSlownDown)
        {
            mainMixer.SetFloat("SEPitch", 0.1f);
        }
    }

    // --- 💡 外部（Webやリトライ時）から呼ばれる音量調節関数 ---
    // volumeLevelは 0.0（消音） 〜 1.0（最大音量）で受け取る想定
    public void SetVolume(float volumeLevel)
    {
        // オーディオミキサーの音量は「デシベル（dB）」なので、対数変換して安全に適用する
        float db = Mathf.Log10(Mathf.Clamp(volumeLevel, 0.0001f, 1f)) * 20f;
        mainMixer.SetFloat("MasterVolume", db);
    }

    // サウンドエフェクトを初期状態（通常時）に戻す
    public void ResetSoundEffects()
    {
        isSlownDown = false;
        currentPitch = 1f;
        currentEcho = 0f;
        mainMixer.SetFloat("SEPitch", 1f);
        mainMixer.SetFloat("EchoWet", 0f);
    }
}