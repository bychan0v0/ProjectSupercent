using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using DG.Tweening;

public class CustomerAgent : MonoBehaviour
{
    // ===== States =====
    public enum State
    {
        None,
        EnterWalk,
        ToShelfSlot,
        WaitingPickup,
        ToCheckout,
        InQueue,
        Eating_GoToSeat,  // (다인인) 좌석으로 이동
        Eating,           // (다인인) 식사 중
        ExitToAnchor,
        ExitBeyond
    }

    public enum GoalType { None, Enter, ShelfSlot, CheckoutSlot, ExitAnchor, ExitFinal }

    public enum CustomerKind { Takeout, DineIn }
    public bool IsDineIn => kind == CustomerKind.DineIn;

    // ===== Config =====
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
    [SerializeField, Min(0.05f)] private float restockCheckInterval = 0.4f;

    [Header("Components")]
    [SerializeField] private AutoItemTransfer transfer;          // 고객은 Manual 모드 권장
    [SerializeField] private MonoBehaviour carrierBehaviour;     // IProductCarrier 구현체
    private IProductCarrier carrier;

    [Header("Visual Anchors")]
    [SerializeField] private Transform bagHoldParent;            // 손님 자식 중 물건 고정 포인트
    public Transform BagHoldParent => bagHoldParent ? bagHoldParent : transform;

    [Header("Kind / Lanes / Dining")]
    [SerializeField] private CustomerKind kind = CustomerKind.Takeout;
    [SerializeField] private CheckoutLane takeoutLaneRef;        // 왼쪽 줄
    [SerializeField] private CheckoutLane dineInLaneRef;         // 오른쪽 줄
    [SerializeField] private TableSeatManager tableSeats;        // 다인인 좌석 관리자
    [SerializeField, Min(2f)] private float eatSeconds = 8f;     // 식사 시간

    // 테이블에 빵 올릴 때 간단 배치(그리드)
    [Header("Table Placement (Dine-In)")]
    [SerializeField, Min(1)] private int tableCols = 2;
    [SerializeField, Min(1)] private int tableRows = 2;
    [SerializeField] private Vector2 tableCellSize = new Vector2(0.16f, 0.12f);
    [SerializeField] private float tableYOffset = 0.01f;         // 테이블 표면에서 살짝 띄우기

    // ===== Runtime =====
    private State state = State.None;
    private GoalType currentGoal = GoalType.None;

    private ShelfWaitingArea shelf;
    private Transform myShelfSlot;
    private CheckoutLane lane;

    private Vector3 spawnWorld;
    private bool hasSpawnWorld = false;

    public bool Servicing { get; set; }
    public event Action<CustomerAgent> OnReturnedToPool;

    // (다인인) 좌석/테이블 표면
    private Transform mySeatAnchor;
    private Transform myTablePlace;

    // ===== Unity =====
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

        // (원한다면) 에이전트 회피/반경 등은 외부에서 세팅
    }

    private void OnDisable()
    {
        if (shelf && myShelfSlot) shelf.Release(myShelfSlot);
        myShelfSlot = null;

        // 재사용 안전: 봉투/비주얼 정리 + 들고 있던 물건 비우기
        CleanupCarriedBags();
        if (wantProduct) WipeCarryOf(wantProduct);

        // 좌석 점유 중이면 해제
        if (tableSeats) tableSeats.ReleaseBy(this);
        mySeatAnchor = null;
        myTablePlace = null;

        OnReturnedToPool = null;
    }

    // ===== External API =====
    public void SetDemand(ProductType product, int count)
    {
        wantProduct = product;
        wantCount   = Mathf.Max(1, count);
    }

    /// <summary>손님 이동 플로우 시작. 재사용 시 빈손 보장.</summary>
    public void Begin(ShelfWaitingArea targetShelf, CheckoutLane targetLane, Vector3 spawnPos)
    {
        Analytics.Log("customer_state", new { state = "Enter", kind = IsDineIn ? "DineIn" : "Takeout" });
        
        shelf = targetShelf;
        lane  = targetLane; // 초기 lane 힌트(없어도 OK)

        // 재사용 시작 시 이번 품목은 빈손 보장(이전 생애 잔여 제거)
        if (wantProduct) WipeCarryOf(wantProduct);

        spawnWorld = spawnPos;
        hasSpawnWorld = true;

        Vector3 enterAnchor = spawnWorld + enterOffset;
        SetGoal(enterAnchor, GoalType.Enter);
        state = State.EnterWalk;

        StartCoroutine(LogicLoop());
    }

    /// <summary>결제 완료 시 외부에서 호출. 유형별로 다음 행동 분기.</summary>
    public void AfterCheckout()
    {
        if (IsDineIn)
        {
            // 좌석 예약 시도
            if (tableSeats && tableSeats.TryReserve(out mySeatAnchor, out myTablePlace, this) && mySeatAnchor)
            {
                SetGoal(mySeatAnchor.position, GoalType.None);
                state = State.Eating_GoToSeat;
            }
            else
            {
                // 만석이면 테이크아웃처럼 퇴장
                DoneAndExit();
            }
        }
        else
        {
            DoneAndExit();
        }
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

                case State.Eating_GoToSeat:
                {
                    if (Arrived())
                    {
                        PlaceCarryOnTable();               // 들고 온 빵을 테이블 위에 올려놓기
                        state = State.Eating;
                        StartCoroutine(CoEatThenExit());  // 식사 후 퇴장
                    }
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

    // ===== Shelf Pickup =====
    private IEnumerator CoPickupActual()
    {
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
        
        // 3) 같은 자리에서 반복 픽업
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
                Analytics.Log("shelf_wait_restock", new { product = wantProduct.displayName });
                yield return new WaitForSeconds(restockCheckInterval);
                continue;
            }

            int steps = Mathf.Min(need, avail, carry);
            for (int s = 0; s < steps; s++)
            {
                transfer.ManualPickup(shelf.Source, wantProduct, 1); // 1개씩
                Analytics.Log("pickup_one", new { product = wantProduct.displayName, have = carrier.Count(wantProduct) });

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
        Analytics.Log("customer_state", new { state = "Queue_Join", lane = lane ? lane.name : "null" });
        
        // 유형에 맞는 대기열 우선 사용(없으면 Begin에서 준 lane 사용)
        CheckoutLane preferred =
            IsDineIn ? (dineInLaneRef ? dineInLaneRef : lane)
                     : (takeoutLaneRef ? takeoutLaneRef : lane);

        lane = preferred;
        state = State.InQueue;
        lane.Join(this);
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
        Analytics.Log("customer_state", new { state = "Exit" });
        
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

    // ===== Dining Helpers =====
    private void PlaceCarryOnTable()
    {
        if (!myTablePlace || carrier == null) return;

        // 스택 비주얼/실물에서 하나씩 꺼내 테이블에 내려놓는다.
        int moved = 0;
        if (TryGetComponent<StackCarrier>(out var stack))
        {
            // 테이블 표면 그리드 배치
            var targets = new List<Vector3>();
            int perLayer = tableCols * tableRows;
            int countApprox = Mathf.Min(carrier.Count(wantProduct), perLayer);
            for (int i = 0; i < countApprox; i++)
            {
                int row = i / tableCols;
                int col = i % tableCols;
                Vector3 local = new Vector3(
                    (col - (tableCols - 1) * 0.5f) * tableCellSize.x,
                    tableYOffset,
                    (row - (tableRows - 1) * 0.5f) * tableCellSize.y
                );
                targets.Add(myTablePlace.TransformPoint(local));
            }

            int idx = 0;
            while (carrier.Count(wantProduct) > 0 && idx < targets.Count)
            {
                if (!carrier.TryRemoveOne(wantProduct)) break;
                var go = stack.PopTop(); // 실물 빵
                if (go)
                {
                    // 부모/위치 지정(연출 없이 즉시 올림)
                    go.transform.SetParent(myTablePlace, worldPositionStays: false);
                    go.transform.position = targets[idx];
                    go.transform.rotation = myTablePlace.rotation;
                }
                idx++;
                moved++;
            }
        }
        else
        {
            // 비주얼 스택이 없다면 논리만 비움
            while (carrier.Count(wantProduct) > 0)
            {
                if (!carrier.TryRemoveOne(wantProduct)) break;
                moved++;
            }
        }
    }

    private IEnumerator CoEatThenExit()
    {
        yield return new WaitForSeconds(eatSeconds);

        // (선택) 테이블 위 빵 정리
        if (myTablePlace)
        {
            var all = myTablePlace.GetComponentsInChildren<PooledObject>(includeInactive: true);
            foreach (var po in all)
            {
                if (po) PoolManager.Instance.Despawn(po.gameObject);
            }
        }

        if (tableSeats) tableSeats.ReleaseBy(this);
        DoneAndExit();
    }

    // ===== Cleanup Helpers =====
    /// <summary>봉투/트레이 등 손님 자식 오브젝트 정리</summary>
    private void CleanupCarriedBags()
    {
        Transform root = BagHoldParent ? BagHoldParent : transform;
        var bags = root.GetComponentsInChildren<CarriedBag>(includeInactive: true);

        for (int i = 0; i < bags.Length; i++)
        {
            var go = bags[i].gameObject;
            DOTween.Kill(go.transform, complete: false);
            go.transform.SetParent(null, true);

            var po = go.GetComponent<PooledObject>();
            if (po != null && PoolManager.Instance != null) PoolManager.Instance.Despawn(go);
            else Destroy(go);
        }
    }

    /// <summary>현재 수요 품목을 빈손으로 보장(논리+비주얼)</summary>
    private void WipeCarryOf(ProductType type)
    {
        if (type == null || carrier == null) return;

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
    
    // 유형 지정
    public void SetKind(CustomerKind k) { kind = k; }

    // 레인 주입(스포너가 씬 레퍼런스를 전달)
    public void InjectLanes(CheckoutLane takeLane, CheckoutLane dineLane)
    {
        if (takeLane)  this.takeoutLaneRef = takeLane;
        if (dineLane)  this.dineInLaneRef  = dineLane;
    }

    // 다인인 좌석 매니저 주입(옵션)
    public void InjectTableSeats(TableSeatManager seats)
    {
        if (seats) this.tableSeats = seats;
    }
}
