using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CheckoutCounter : MonoBehaviour
{
    [Header("Detection (Layer-based)")]
    [SerializeField] private LayerMask playerLayers; // 인스펙터에서 Player 레이어 체크
    private readonly HashSet<Collider> _playerContacts = new();

    [SerializeField] private CheckoutVFX checkoutVfx;
    [SerializeField] private ItemTravelProfile bagToHandProfile;
    
    [Header("Service")]
    [SerializeField] private float serviceSeconds = 1.2f;
    [SerializeField] private CheckoutLane lane;

    [System.Serializable] 
    public struct PriceEntry { public ProductType product; public int unitPrice; }
    
    [Header("Pricing")]
    [SerializeField] private List<PriceEntry> priceTable = new();
    [SerializeField] private MoneyStacker moneyStacker;
    
    private bool busyOne;
    private Coroutine loop;

    private void Reset()
    {
        var col = GetComponent<Collider>();
        col.isTrigger = true; // 트리거 필수
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsPlayer(other)) return;

        _playerContacts.Add(other);
        if (loop == null) loop = StartCoroutine(CoServiceWhileAnyPlayerInside(other));
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsPlayer(other)) return;

        _playerContacts.Remove(other);
        if (_playerContacts.Count == 0 && loop != null)
        {
            StopCoroutine(loop);
            loop = null;
        }
    }

    // 자주 쓰는 레이어 체크 헬퍼
    private bool IsPlayer(Collider col)
    {
        // 루트 기준으로 레이어 판정(자식 콜라이더 레이어가 다를 수 있음)
        var root = col.transform.root.gameObject;
        return (playerLayers.value & (1 << root.layer)) != 0;
    }

    private IEnumerator CoServiceWhileAnyPlayerInside(Collider lastEntered)
    {
        while (_playerContacts.Count > 0)
        {
            if (!busyOne && lane != null && lane.IsFrontReadyForService(out var front))
            {
                if (front != null && !front.Servicing)
                {
                    // 1) 이 손님이 실제로 들고 있는 개수 (StackCarrier 사용 권장)
                    var stack = front.GetComponent<StackCarrier>();
                    int count = stack != null ? stack.Count : front.WantCount;

                    // 2) VFX 길이 계산
                    float vfxSec = checkoutVfx.EstimateDuration(count);

                    // 3) 결제 시작을 먼저 시도 → 성공한 경우에만 VFX 시작
                    bool started = lane.TryStartServiceForFront(vfxSec, who =>
                    {
                        int unit = GetUnitPrice(who.WantProduct);
                        int revenue = Mathf.Max(0, unit * count);

                        // ★ 플레이어 지갑 X → 지폐 쌓기 O
                        if (moneyStacker != null)
                        {
                            // moneyStacker.ValuePerBill = valuePerBill; // 전역으로 쓰려면 1회 설정
                            moneyStacker.StackAmount(revenue);
                        }
                    });

                    if (started)
                    {
                        busyOne = true; // ★ 재진입 잠금

                        // Pop/Consume 설정
                        GameObject PopOne() => stack ? stack.PopTop() : null;
                        void Consume(GameObject go)
                        {
                            if (!go) return;
                            PoolManager.Instance.Despawn(go); // PooledObject 방식 권장
                        }

                        // 4) 이제서야 VFX 시작 (아이템 → 봉투 → 손님 position 자식)
                        checkoutVfx.Play(front, count, PopOne, Consume, () => busyOne = false);
                    }
                }
            }
            yield return null;
        }
    }

    private int GetUnitPrice(ProductType t)
    {
        if (t == null) return 0;
        for (int i = 0; i < priceTable.Count; i++)
            if (priceTable[i].product == t) return Mathf.Max(0, priceTable[i].unitPrice);
        return 0;
    }
    
    private PlayerWallet FindAnyWalletInContacts()
    {
        // _playerContacts: OnTriggerEnter/Exit에서 관리 중인 HashSet<Collider>
        foreach (var c in _playerContacts)
        {
            if (!c) continue; // 파괴/해제된 콜라이더 방어
            var w = c.GetComponentInParent<PlayerWallet>();
            if (w != null) return w;
        }
        return null;
    }
}
