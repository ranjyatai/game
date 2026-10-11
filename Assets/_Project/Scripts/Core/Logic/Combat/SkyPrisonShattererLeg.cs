using UnityEngine;

/// <summary>
/// 震碎者的一条腿的「受击部位」。挂在腿的碰撞体(圆柱形，直接就是模型的形状)那个物体上。
/// 玩家的近战判定框 / 子弹命中这个碰撞体时，UnitActionModuleRuntime.ResolveSkillHit 会认出这个组件：
///   · 这一下伤害先扣在这条腿的血量上(AbsorbHit)；
///   · 同时只有 LegBodyShare 比例的伤害传给 BOSS 本体——所以打腿是为了拆腿，不是白打；
///   · 腿血量归零 = 断腿：碰撞体关闭、永久断掉，不再修复(用户 10-10 拍板)；断了的腿不再迈步、不再出冲击波，也不再有接触伤害。
/// </summary>
[DisallowMultipleComponent]
public class SkyPrisonShattererLeg : MonoBehaviour
{
    public SkyPrisonShattererAI Owner;
    public int Index;
    public float Hp = -1f;
    public float MaxHp;
    public bool Broken;

    private Material _mat;
    private Collider _col;
    private float _flash;
    private static readonly Color BaseColor = new Color(0.16f, 0.16f, 0.19f);
    private static readonly Color FlashColor = new Color(1f, 0.38f, 0.38f);
    private static readonly Color BrokenColor = new Color(0.07f, 0.06f, 0.07f);

    public void Init(SkyPrisonShattererAI owner, int index, Renderer visual, Collider col)
    {
        Owner = owner; Index = index; _col = col;
        if (visual != null) { visual.material = new Material(visual.sharedMaterial); _mat = visual.material; _mat.color = BaseColor; }
    }

    // 这一下的伤害：先扣腿的血，再把传给本体的部分缩到 LegBodyShare
    public void AbsorbHit(ref float damage)
    {
        if (Broken || damage <= 0f) return;
        if (Hp < 0f) { MaxHp = Owner != null ? Owner.LegMaxHp : 100f; Hp = MaxHp; }
        Hp -= damage;
        _flash = 1f;
        damage *= Owner != null ? Owner.LegBodyShare : 0.3f;
        if (Hp <= 0f) Break();
    }

    private void Break()
    {
        Broken = true;
        if (_col != null) _col.enabled = false;
        if (_mat != null) _mat.color = BrokenColor;
        if (Owner != null) Owner.OnLegBroken(Index);
    }

    private void Update()
    {
        if (Broken || _mat == null || _flash <= 0f) return;
        _flash = Mathf.MoveTowards(_flash, 0f, 5f * Time.deltaTime);
        _mat.color = Color.Lerp(BaseColor, FlashColor, _flash);
    }
}
