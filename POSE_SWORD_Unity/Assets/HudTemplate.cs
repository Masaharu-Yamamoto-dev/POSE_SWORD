using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 3・4人目のように動的生成されるプレイヤー用HPバーの配置を、
// Editor上でドラッグして視覚的に決められるようにするためのテンプレート。
// 1・2人目のHPバーと同じ構成（Slider/テキスト）のUIをこのコンポーネントが付いたオブジェクトの下（または参照先）に用意し、
// 各フィールドに割り当てておくと、そのRectTransformの位置がそのまま複製先のHPバー位置として使われる。
public class HudTemplate : MonoBehaviour
{
    public Slider hpBar;
    public Slider delayHpBar;
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI hpText;
    public Slider spGaugeBar;
    public TextMeshProUGUI spText;
}
