using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class CustomerAgent : MonoBehaviour
{
    public enum State
    {
        None,
        EnterWalk,
        ToShelfSlot,
        WaitingPickup,
        ToCheckout,
        InQueue,
        ExitToAnchor,
        ExitBeyond
    }

    public enum GoalType { None, Enter, ShelfSlot, CheckoutSlot, ExitAnchor, ExitFinal }

    [Header("Move")]
    [SerializeField] private NavMeshAgent agent;
    [SerializeField, Min(0f)] private float arriveThreshold = 0.15f;

    [Header("Demand (Runtime)")]
    [SerializeField] private ProductType wantProduct;
    [SerializeField, Min(1)] private int wantCount = 1;
    public ProductType WantProduct => wantProduct;
    public int WantCount => wantCount;

    [Header("Flow - Enter")]
    [SerializeField] private Vector3 enterOffset = new Vector3(0f, 0f, -4f);

    [Header("Pickup Loop")]
    [SerializeField, Min(0.05f)] private float restockCheckInterval = 0.4f; // 재고 재확인 주기

    [Header("Components")]
    [SerializeField] private AutoItemTransfer transfer;          // 고객은 Manual 모드 권장
    [SerializeField] private MonoBehaviour carrierBehaviour;     // IProductCarrier 구현체
    private IProductCarrier carrier;

    [Header("Visual Anchors")]
    [SerializeField] private Transform bagHoldParent;            // 봉투가 붙을 손님 자식(상품 position)
    public Transform BagHoldParent => bagHoldParent ? bagHoldParent : transform;

    // ===== Runtime =====
    private State state = State.None;
    private GoalType currentGoal = GoalType.None;

    private ShelfWaitingArea shelf;
    private Transform myShelfSlot;
    private CheckoutLane lane;

    private Vector3 spawnWorld;
    private bool hasSpawnWorld = false;

    public bool Servicing { get; private set; }

    public event Action<CustomerAgent> OnReturnedToPool;

    private void Reset()
    {
        agent = GetComponent<NavMeshAgent>();
        transfer = GetComponent<AutoItemTransfer>();
        carrierBehaviour = GetComponent<IProductCarrier>() as MonoBehaviour;
    }

    private void OnEnable()
    {
        state = State.None;
        currentGoal = GoalType.None;
        Servicing = false;

        myShelfSlot = null;
        shelf = null;
        lane = null;

        if (!agent) agent = GetComponent<NavMeshAgent>();
        if (!transfer) transfer = GetComponent<AutoItemTransfer>();
        carrier = (carrierBehaviour as IProductCarrier) ?? GetComponent<IProductCarrier>();

        hasSpawnWorld = false;
    }

    private void OnDisable()
    {
        if (shelf && myShelfSlot) shelf.Release(myShelfSlot);
        myShelfSlot = null;

        // 재사용 안전: 봉투/비주얼 정리 + (옵션) 들고 있던 물건 비우기
        CleanupCarriedBags();
        if (wantProduct) WipeCarryOf(wantProduct);

        OnReturnedToPool = null; // 리스너 정리
    }

    // ===== External API =====

    public void SetDemand(ProductType product, int count)
    {
        wantProduct = product;
        wantCount   = Mathf.Max(1, count);
    }

    /// <summary>
    /// 손님 이동 플로우 시작. 재사용 시 빈손 보장.
    /// </summary>
    public void Begin(ShelfWaitingArea targetShelf, CheckoutLane targetLane, Vector3 spawnPos)
    {
        shelf = targetShelf;
        lane  = targetLane;

        // 재사용 시작 시 이번에 살 품목은 무조건 빈손으로 시작
        if (wantProduct) WipeCarryOf(wantProduct);

        spawnWorld = spawnPos;
        hasSpawnWorld = true;

        Vector3 enterAnchor = spawnWorld + enterOffset;
        SetGoal(enterAnchor, GoalType.Enter);
        state = State.EnterWalk;

        StartCoroutine(LogicLoop());
    }

    // ===== Internal =====

    public void SetGoal(Vector3 world, GoalType goal)
    {
        currentGoal = goal;
        if (!agent) agent = GetComponent<NavMeshAgent>();
        agent.isStopped = false;
        agent.SetDestination(world);
    }

    private IEnumerator LogicLoop()
    {
        while (true)
        {
            switch (state)
            {
                case State.EnterWalk:
                {
                    if (Arrived())
                    {
                        if (shelf != null && shelf.TryReserveClosest(transform.position, out myShelfSlot))
                        {
                            shelf.Occupy(myShelfSlot, this);
                            SetGoal(myShelfSlot.position, GoalType.ShelfSlot);
                            state = State.ToShelfSlot;
                        }
                        else
                        {
                            GoExit(); // 슬롯이 꽉 찼으면 퇴장
                        }
                    }
                    break;
                }

                case State.ToShelfSlot:
                {
                    if (Arrived())
                    {
                        state = State.WaitingPickup;
                        StartCoroutine(CoPickupActual());
                    }
                    break;
                }

                case State.ToCheckout:
                {
                    if (Arrived())
                    {
                        state = State.InQueue;
                        lane.Join(this);
                    }
                    break;
                }

                case State.InQueue:
                {
                    if (Arrived()) lane.NotifyArrived(this);
                    break;
                }

                case State.ExitToAnchor:
                {
                    if (Arrived())
                    {
                        SetGoal(spawnWorld, GoalType.ExitFinal);
                        state = State.ExitBeyond;
                    }
                    break;
                }

                case State.ExitBeyond:
                {
                    if (Arrived())
                    {
                        OnReturnedToPool?.Invoke(this);
                        PoolManager.Instance.Despawn(gameObject);
                        yield break;
                    }
                    break;
                }
            }
            yield return null;
        }
    }

    private bool Arrived()
    {
        if (!agent || agent.pathPending) return false;
        return agent.remainingDistance <= Mathf.Max(arriveThreshold, agent.stoppingDistance + 0.01f);
    }

    // ===== 실제 픽업(Manual) =====

    private IEnumerator CoPickupActual()
    {
        // 필수 컴포넌트 확인
        if (transfer == null || carrier == null || shelf == null || shelf.Source == null || wantProduct == null)
        {
            Debug.LogWarning("[CustomerAgent] Pickup prerequisites missing. Will not proceed.", this);
            yield break;
        }

        // 1) 내 차례 대기
        yield return new WaitUntil(() => shelf.IsMyTurnNow(this));

        // 2) 서비스 락 획득
        while (!shelf.TryBeginService(this))
            yield return null;

        // 3) 원하는 개수 채울 때까지 같은 자리에서 반복 픽업
        while (carrier.Count(wantProduct) < wantCount)
        {
            int need  = wantCount - carrier.Count(wantProduct);
            int carry = carrier.CapacityLeft;
            if (carry <= 0)
            {
                Debug.LogWarning($"[CustomerAgent] Carrier capacity exhausted before meeting demand ({wantCount}).", this);
                break;
            }

            int avail = shelf.Source.Peek(wantProduct);
            if (avail <= 0)
            {
                yield return new WaitForSeconds(restockCheckInterval);
                continue;
            }

            int steps = Mathf.Min(need, avail, carry);
            for (int s = 0; s < steps; s++)
            {
                transfer.ManualPickup(shelf.Source, wantProduct, 1); // 1개씩
                yield return new WaitUntil(() => transfer.IsBusy == false);
                yield return new WaitForSeconds(restockCheckInterval);
            }
        }

        // 4) 서비스 종료
        shelf.EndService(this);

        // 5) 계산대로 or 퇴장
        if (carrier.Count(wantProduct) >= wantCount)
        {
            if (myShelfSlot) shelf.Release(myShelfSlot);
            myShelfSlot = null;
            ToCheckout();
        }
        else
        {
            if (myShelfSlot) shelf.Release(myShelfSlot);
            myShelfSlot = null;
            GoExit();
        }
    }

    public void SetQueueDestination(Vector3 world)
    {
        if (!agent) agent = GetComponent<NavMeshAgent>();
        agent.stoppingDistance = 0.02f;
        agent.autoBraking = true;
        SetGoal(world, GoalType.CheckoutSlot);
    }

    private void ToCheckout()
    {
        if (lane == null)
        {
            Debug.LogWarning("[CustomerAgent] ToCheckout() called but lane is null. Exiting instead.", this);
            GoExit();
            return;
        }

        // 줄 합류: CheckoutLane이 내 대기 위치를 배정(SetQueueDestination 호출)
        state = State.InQueue;
        lane.Join(this);
    }
    
    public void BeginCheckoutByPlayer(float serviceSeconds)
    {
        if (Servicing) return;
        StartService(serviceSeconds, () => { DoneAndExit(); });
    }

    // ===== 결제/퇴장 =====

    public void StartService(float duration, Action onDone)
    {
        if (!gameObject.activeInHierarchy) return;
        StartCoroutine(CoService(duration, onDone));
    }

    private IEnumerator CoService(float duration, Action onDone)
    {
        Servicing = true;
        yield return new WaitForSeconds(duration);
        Servicing = false;
        onDone?.Invoke();
    }

    public void DoneAndExit() => GoExit();

    private void GoExit()
    {
        if (hasSpawnWorld)
        {
            Vector3 anchor = spawnWorld + enterOffset;
            SetGoal(anchor, GoalType.ExitAnchor);
            state = State.ExitToAnchor;
        }
        else
        {
            Vector3 fallback = transform.position - (enterOffset.normalized * enterOffset.magnitude);
            SetGoal(fallback, GoalType.ExitFinal);
            state = State.ExitBeyond;
        }
    }

    // ===== 정리/보조 =====

    /// <summary>봉투(손님 자식) 모두 정리</summary>
    private void CleanupCarriedBags()
    {
        Transform root = BagHoldParent ? BagHoldParent : transform;
        var bags = root.GetComponentsInChildren<CarriedBag>(includeInactive: true);

        for (int i = 0; i < bags.Length; i++)
        {
            var go = bags[i].gameObject;

            DG.Tweening.DOTween.Kill(go.transform, complete: false);
            go.transform.SetParent(null, true);

            var po = go.GetComponent<PooledObject>();
            if (po != null && PoolManager.Instance != null)
                PoolManager.Instance.Despawn(go);
            else
                Destroy(go);
        }
    }

    /// <summary>현재 수요 품목을 빈손으로 보장(논리+비주얼)</summary>
    private void WipeCarryOf(ProductType type)
    {
        if (type == null || carrier == null) return;

        // 비주얼 스택 비우기
        if (TryGetComponent<StackCarrier>(out var stack))
        {
            while (carrier.Count(type) > 0)
            {
                if (!carrier.TryRemoveOne(type)) break;
                var go = stack.PopTop();
                if (go)
                {
                    var po = go.GetComponent<PooledObject>();
                    if (po != null && PoolManager.Instance != null) PoolManager.Instance.Despawn(go);
                    else Destroy(go);
                }
            }
        }
        else
        {
            while (carrier.Count(type) > 0)
                if (!carrier.TryRemoveOne(type)) break;
        }
    }
}
