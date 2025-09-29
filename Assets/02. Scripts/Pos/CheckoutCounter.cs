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
    
    [SerializeField] private bool isDineInCounter = false;   // 이 카운터가 다인인용인가?
    [SerializeField] private TableSeatManager tableSeats;    // 좌석 매니저
    [SerializeField] private UnlockableArea tableAreaUnlock; // 테이블 구역 해금(옵션)
    
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
        if (loop == null) loop = StartCoroutine(CoServiceWhileAnyPlayerInside());
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

    private IEnumerator CoServiceWhileAnyPlayerInside()
{
    while (_playerContacts.Count > 0)
    {
        if (!busyOne && lane != null && lane.IsFrontReadyForService(out var front))
        {
            if (front != null && !front.Servicing)
            {
                if (isDineInCounter)
                {
                    if (!front.IsDineIn) { yield return null; continue; }
                    if (!IsTableAreaAvailable()) { yield return null; continue; }

                    if (!tableSeats.TryReserve(out var seatAnchor, out var tablePlace, front) || !seatAnchor)
                    { yield return null; continue; }

                    int unit = GetUnitPrice(front.WantProduct);
                    front.PrepareReservedSeat(seatAnchor, tablePlace, unit);
                    front.InjectDineInMoneyStacker(moneyStacker); 

                    bool started = lane.TryStartServiceForFront(0.2f, who =>
                    {
                        // 결제/돈쌓기는 없음. 좌석은 이미 예약되어 있으므로 바로 테이블로 보낸다.
                        who.AfterCheckout();
                    });
                    if (started) { busyOne = true; try { yield return null; } finally { busyOne = false; } }
                }
                else
                {
                    // ── 기존 테이크아웃 로직 그대로 (판매/돈쌓기/VFX 포함)
                    var stack = front.GetComponent<StackCarrier>();
                    int count = stack != null ? stack.Count : front.WantCount;
                    float vfxSec = checkoutVfx ? checkoutVfx.EstimateDuration(count) : serviceSeconds;

                    bool started = lane.TryStartServiceForFront(vfxSec, who =>
                    {
                        int unit    = GetUnitPrice(who.WantProduct);
                        int revenue = Mathf.Max(0, unit * count);
                        SalesTracker.ReportSale(who.WantProduct, count, revenue);
                        if (moneyStacker) moneyStacker.StackAmount(revenue);
                    });

                    if (started && checkoutVfx)
                    {
                        System.Func<GameObject> popOne = () => stack ? stack.PopTop() : null;
                        System.Action<GameObject> onConsumeOne = go =>
                        {
                            if (!go) return;
                            var po = go.GetComponent<PooledObject>();
                            if (po != null && PoolManager.Instance != null) PoolManager.Instance.Despawn(go);
                            else Destroy(go);
                        };
                        checkoutVfx.Play(front, count, popOne, onConsumeOne, null);
                    }

                    if (started) { busyOne = true; try { yield return null; } finally { busyOne = false; } }
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
    
    private bool IsTableAreaAvailable()
    {
        if (!isDineInCounter) return false;
        if (tableAreaUnlock && !tableAreaUnlock.IsUnlocked) return false; // 해금이 필요 없다면 이 줄 삭제
        return tableSeats && tableSeats.HasFreeSeat();
    }
}
