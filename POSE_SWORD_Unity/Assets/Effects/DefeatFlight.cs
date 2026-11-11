using UnityEngine;

// 決着の一撃で倒された剣を、止めずにすぐ跳ね上げる。決着後は試合の物理を止めていて、ゲストには位置も届かないので、
// 物理ではなく各自の画面でこのコンポーネントが動かす（跳ね上がり方は撃破・抜け殻と同じ値）。
// 試合の時間で動くので、決着のスローでゆっくり飛び、時間が止まるとその場で止まる。カメラはこの剣を追う
public class DefeatFlight : MonoBehaviour
{
    Vector3 velocity;
    float spin;

    public static void Launch(GameObject target)
    {
        if (target == null || target.GetComponent<DefeatFlight>() != null) return;
        DefeatEffects.Current.ApplyDefeatedLook(target.GetComponentsInChildren<SpriteRenderer>());
        var flight = target.AddComponent<DefeatFlight>();
        flight.velocity = new Vector3(Random.Range(-DefeatGhost.KnockSide, DefeatGhost.KnockSide), DefeatGhost.FinalKnockUp, 0f);
        flight.spin = Random.value < .5f ? -DefeatGhost.Spin : DefeatGhost.Spin;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        velocity.y += Physics2D.gravity.y * DefeatGhost.GravityScale * dt;
        transform.position += velocity * dt;
        transform.Rotate(0f, 0f, spin * dt);
    }
}
