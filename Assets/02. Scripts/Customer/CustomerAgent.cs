using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
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
        ExitToAnchor,  // (spawn + enterOffset)까지
        ExitBeyond     // spawn 포인트까지
    }

    public enum GoalType { None, Enter, ShelfSlot, CheckoutSlot, ExitAnchor, ExitFinal }

    [Header("Pooling")]
    [SerializeField] private string poolKey = "Customer";
    public string PoolKey => poolKey;

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

    [Header("Shelf Wait Options")]
    [SerializeField] private bool waitWhenOutOfStock = true; // 재고 0일 때 기다릴지
    [SerializeField, Min(0.05f)] private float restockCheckInterval = 0.4f; // 재고 재확인 주기
    [SerializeField] private float maxShelfWaitSeconds = -1f; // -1이면 무제한 대기, 0이면 즉시 포기
    
    [Header("Components")]
    [SerializeField] private AutoItemTransfer transfer;          // 고객은 Manual 모드 권장
    [SerializeField] private MonoBehaviour carrierBehaviour;     // IProductCarrier
    private IProductCarrier carrier;

    // ===== Runtime =====
    private State state = State.None;
    private GoalType currentGoal = GoalType.None;

    private ShelfWaitingArea shelf;
    private Transform myShelfSlot;
    private CheckoutLane lane;

    // 퇴장 경로 계산을 위해 스폰 위치 저장
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
        OnReturnedToPool = null; // 리스너 정리
    }

    // ===== External API =====

    public void SetDemand(ProductType product, int count)
    {
        wantProduct = product;
        wantCount   = Mathf.Max(1, count);
    }

    /// <summary>
    /// 손님 이동 플로우 시작.
    /// 스폰 포인트와 enterOffset을 기억해두고, 퇴장 시 그 반대로 나간다.
    /// </summary>
    public void Begin(ShelfWaitingArea targetShelf, CheckoutLane targetLane, Vector3 spawnPos)
    {
        shelf = targetShelf;
        lane  = targetLane;

        spawnWorld = spawnPos;
        hasSpawnWorld = true;

        // 1) 스폰 → (spawn + enterOffset)로 입장
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
                            GoExit(); // 슬롯이 꽉 찼으면 바로 퇴장
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
                        // 2) (spawn + enterOffset)에 도착했으니, 이제 spawn 포인트로 직진
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
                        PoolManager.Instance.Despawn(poolKey, gameObject);
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

        // 1) 내 차례가 올 때까지 대기
        yield return new WaitUntil(() => shelf.IsMyTurnNow(this));

        // 2) 서비스 락 획득 (여기서부터는 내가 EndService할 때까지 남들 진입 불가)
        while (!shelf.TryBeginService(this))
            yield return null;

        // 3) 원하는 개수 채울 때까지 "자리를 지키며" 반복 픽업/대기
        while (carrier.Count(wantProduct) < wantCount)
        {
            int need = wantCount - carrier.Count(wantProduct);
            int carry = carrier.CapacityLeft; // 캐리어 남은 용량
            if (carry <= 0)
            {
                Debug.LogWarning($"[CustomerAgent] Carrier capacity exhausted before meeting demand ({wantCount}).",
                    this);
                break; // 더 못 들면 루프 탈출(설계상 캐리어 용량 >= 최대요구 권장)
            }

            int avail = shelf.Source.Peek(wantProduct); // 선반 재고
            if (avail <= 0)
            {
                // 재고 들어올 때까지 "내 순서 유지"한 채로 대기
                yield return new WaitForSeconds(restockCheckInterval);
                continue;
            }

            int steps = Mathf.Min(need, avail, carry);
            for (int s = 0; s < steps; s++)
            {
                transfer.ManualPickup(shelf.Source, wantProduct, 1); // ★ 항상 1개씩
                yield return new WaitUntil(() => transfer.IsBusy == false);
                yield return new WaitForSeconds(restockCheckInterval);
                // 루프 재평가 → need가 남으면 같은 락으로 계속 시도
            }
        }

        // 4) 서비스 종료(락 해제)
        shelf.EndService(this);

        // 5) 다 모았으면 슬롯 해제 후 계산대로
        if (carrier.Count(wantProduct) >= wantCount)
        {
            if (myShelfSlot) shelf.Release(myShelfSlot);
            myShelfSlot = null;
            ToCheckout();
        }
        else
        {
            // 이 경우는 보통 "캐리어 용량이 부족"한 설계 오류.
            // 최소한 자리만 해제하고 퇴장시켜 deadlock 방지(원하면 계산대로 보내도록 바꿔도 됨).
            if (myShelfSlot) shelf.Release(myShelfSlot);
            myShelfSlot = null;
            GoExit();
        }
    }
    
    public void SetQueueDestination(Vector3 world)
    {
        if (!agent) agent = GetComponent<UnityEngine.AI.NavMeshAgent>();

        // 줄 설 땐 최대한 정확히 붙기
        agent.stoppingDistance = 0.02f;   // 거의 0에 가깝게
        agent.autoBraking      = true;    // 목적지 근처에서 속도 줄이기

        SetGoal(world, GoalType.CheckoutSlot);
    }
    
    public void BeginCheckoutByPlayer(float serviceSeconds)
    {
        // 이미 결제 중이면 무시
        if (Servicing) return;

        // 플레이어가 결제 시작을 눌렀을 때만 서비스 진행
        StartService(serviceSeconds, () =>
        {
            // 계산 끝 → 퇴장
            DoneAndExit();
        });
    }
    
    private void ToCheckout()
    {
        // 바로 줄에 합류시키고, CheckoutLane이 목적지를 배정해주도록 한다.
        state = State.InQueue;
        if (lane != null)
        {
            lane.Join(this); // Join()이 RefreshDestinations()를 호출하며 내 목적지도 설정해줌
        }
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

    public void DoneAndExit()
    {
        GoExit();
    }

    private void GoExit()
    {
        if (hasSpawnWorld)
        {
            // 퇴장: (spawn + enterOffset) → spawn
            Vector3 anchor = spawnWorld + enterOffset;
            SetGoal(anchor, GoalType.ExitAnchor);
            state = State.ExitToAnchor;
        }
        else
        {
            // 혹시 스폰 정보가 없다면, 현재 위치에서 enterOffset의 반대 방향으로 탈출
            Vector3 fallback = transform.position - (enterOffset.normalized * enterOffset.magnitude);
            SetGoal(fallback, GoalType.ExitFinal);
            state = State.ExitBeyond;
        }
    }
}
