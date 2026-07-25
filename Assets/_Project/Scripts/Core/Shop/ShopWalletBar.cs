using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// 商店购物视图"结账"按钮上方的钱包总览条——列出项目里所有 CurrencyDefinition
    /// 各自当前持有量（用户明确要求："结账上方展示玩家现在所有货币的所持金"，
    /// 不止一种代币，还有纪念章之类的）。货币种类列表在编辑器生成时烤进 slots，
    /// 运行时只负责按 CurrencyRuntime 刷新各自的数字，新增货币种类只需要重新生成一次
    /// prefab，不用改这个组件本身。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShopWalletBar : MonoBehaviour
    {
        [System.Serializable]
        public class Slot
        {
            public CurrencyDefinition currency;
            public Text amountText;
        }

        [SerializeField] private List<Slot> slots = new List<Slot>();

        private void OnEnable()
        {
            Refresh();
            if (CurrencyRuntime.Instance != null)
                CurrencyRuntime.Instance.OnCurrencyChanged += OnCurrencyChanged;
        }

        private void OnDisable()
        {
            if (CurrencyRuntime.Instance != null)
                CurrencyRuntime.Instance.OnCurrencyChanged -= OnCurrencyChanged;
        }

        private void OnCurrencyChanged(string currencyId, long delta) => Refresh();

        private void Refresh()
        {
            var rt = CurrencyRuntime.Instance;
            foreach (var slot in slots)
            {
                if (slot?.currency == null || slot.amountText == null) continue;
                long amount = rt != null ? rt.Get(slot.currency.currencyId) : 0;
                slot.amountText.text = amount.ToString("N0");
            }
        }
    }
}
