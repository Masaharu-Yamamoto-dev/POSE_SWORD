using System;
using UnityEngine;
using UnityEngine.UI;

// 残機制で剣が1本壊れたとき、HPパネルの残機アイコンを墨の線で消す（左から筆が走る）。
// 消し終わったら done を呼ぶので、呼び出し側はそこでアイコンの並びを更新する
public class InkCrossOut : MonoBehaviour
{
    const float Delay = .15f, DrawTime = .18f, Hold = .3f;

    Image icon, cross;
    Action done;
    float born;
    bool finished;

    public static void Play(Image icon, Action done)
    {
        Sprite stroke = InkAssets.StrokeSprite;
        if (icon == null || stroke == null || !icon.gameObject.activeInHierarchy) { done?.Invoke(); return; }
        var obj = new GameObject("InkCross", typeof(RectTransform), typeof(Image));
        var rect = obj.GetComponent<RectTransform>();
        rect.SetParent(icon.rectTransform, false);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        Vector2 size = icon.rectTransform.rect.size;
        rect.sizeDelta = new Vector2(size.x * 2.4f, size.y * 1.1f);
        rect.localRotation = Quaternion.Euler(0f, 0f, 28f);
        var image = obj.GetComponent<Image>();
        image.sprite = stroke;
        image.color = InkAssets.Ink;
        image.type = Image.Type.Filled;
        image.fillMethod = Image.FillMethod.Horizontal;
        image.fillOrigin = (int)Image.OriginHorizontal.Left;
        image.fillAmount = 0f;
        image.raycastTarget = false;
        var crossOut = obj.AddComponent<InkCrossOut>();
        crossOut.icon = icon; crossOut.cross = image; crossOut.done = done;
        crossOut.born = Time.unscaledTime;
    }

    void Update()
    {
        float age = Time.unscaledTime - born;
        float k = Mathf.Clamp01((age - Delay) / DrawTime);
        cross.fillAmount = k;
        if (icon != null) { var c = icon.color; c.a = 1f - .65f * k; icon.color = c; }
        if (age < Delay + DrawTime + Hold) return;
        Finish();
    }

    void Finish()
    {
        if (finished) return;
        finished = true;
        if (icon != null) { var c = icon.color; c.a = 1f; icon.color = c; }
        done?.Invoke();
        Destroy(gameObject);
    }

    // 試合の終わりなどで途中で消されても、アイコンの並びの更新だけは必ず行う
    void OnDestroy()
    {
        if (finished) return;
        finished = true;
        if (icon != null) { var c = icon.color; c.a = 1f; icon.color = c; }
        done?.Invoke();
    }
}
