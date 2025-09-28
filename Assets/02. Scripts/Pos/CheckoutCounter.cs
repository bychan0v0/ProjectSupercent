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
            if (!busyOne && lane != null && lane.IsFrontReadyForService(out var front)) // ← Lane의 준비 판정만 사용
            {
                if (front != null && !front.Servicing)
                {
                    // 1) 실제 들고있는 개수(스택 기준으로 캡쳐)
                    var stack = front.GetComponent<StackCarrier>();
                    int count = stack != null ? stack.Count : front.WantCount;

                    // 2) VFX 총 길이로 서비스 시간 동기화
                    float vfxSec = checkoutVfx ? checkoutVfx.EstimateDuration(count) : serviceSeconds;

                    Analytics.Log("counter_service", new {
                        count,
                        unitPrice = GetUnitPrice(front.WantProduct),
                        revenue = Mathf.Max(0, GetUnitPrice(front.WantProduct) * count)
                    });
                    
                    // 3) 결제 시작 (완료 콜백: 돈/판매량 처리)
                    bool started = lane.TryStartServiceForFront(vfxSec, who =>
                    {
                        int unit    = GetUnitPrice(who.WantProduct);
                        int revenue = Mathf.Max(0, unit * count);
                        
                        SalesTracker.ReportSale(who.WantProduct, count, revenue);
                        if (moneyStacker) moneyStacker.StackAmount(revenue);
                    });

                    // 4) 시작되면 즉시 봉투 연출 실행
                    if (started && checkoutVfx)
                    {
                        // 스택에서 실물 하나 꺼내오기(없으면 null)
                        System.Func<GameObject> popOne = () => stack ? stack.PopTop() : null;

                        // 봉투에 들어간 직후 풀 회수(풀 없으면 Destroy)
                        System.Action<GameObject> onConsumeOne = go =>
                        {
                            if (!go) return;
                            var po = go.GetComponent<PooledObject>();
                            if (po != null && PoolManager.Instance != null) PoolManager.Instance.Despawn(go);
                            else Destroy(go);
                        };

                        checkoutVfx.Play(
                            who: front,
                            itemCount: count,
                            popOneVisual: popOne,
                            onConsumeOne: onConsumeOne,
                            onAllDone: null
                        );
                    }

                    if (started) { busyOne = true; yield return null; busyOne = false; }
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
