using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CustomerSpawner : MonoBehaviour
{
    public enum SpawnKind { Takeout, DineIn }

    [Header("Pool")]
    [SerializeField] private string defaultPoolKey = "Customer";

    [Header("Auto Spawn")]
    [SerializeField, Min(0.1f)] private float spawnInterval = 0.8f;
    [SerializeField, Min(1)]    private int   maxActiveTotal = 12;
    [SerializeField, Min(0)]    private int   maxActiveTakeout = 12;
    [SerializeField, Min(0)]    private int   maxActiveDineIn  = 12;
    [SerializeField] private bool autoStart = true;

    [Header("Scene Refs")]
    [SerializeField] private Transform        spawnTakeout;
    [SerializeField] private Transform        spawnDineIn;
    [SerializeField] private ShelfWaitingArea shelfArea;
    [SerializeField] private CheckoutLane     laneTakeout;   // 왼쪽 줄
    [SerializeField] private CheckoutLane     laneDineIn;    // 오른쪽 줄
    [SerializeField] private TableSeatManager tableSeats;    // 다인인 좌석 관리자

    [Header("Product Table (SO)")]
    [SerializeField] private CustomerProductTableSO productTable;

    [Header("Dine-In Unlock / Mix")]
    [SerializeField] private bool  dineInUnlocked = false;         // 해금 전: false
    [SerializeField, Range(0f,1f)] private float dineInWeight = 0.5f; // 해금 후 섞일 비율

    // runtime
    private readonly HashSet<CustomerAgent> alive = new();
    private float timer;

    private void Start()
    {
        if (autoStart) timer = spawnInterval;
        if (maxActiveTakeout <= 0) maxActiveTakeout = maxActiveTotal;
        if (maxActiveDineIn  <= 0) maxActiveDineIn  = maxActiveTotal;
    }

    private void Update()
    {
        if (!autoStart) return;
        if (!shelfArea || !productTable) return;
        if (alive.Count >= maxActiveTotal) return;

        timer -= Time.deltaTime;
        if (timer > 0f) return;
        timer = spawnInterval;

        // 어떤 종류를 스폰할지 결정
        var kind = DecideNextKind();
        if (kind == SpawnKind.Takeout && (!spawnTakeout || !laneTakeout)) return;
        if (kind == SpawnKind.DineIn  && (!spawnDineIn  || !laneDineIn )) return;

        SpawnOne(kind);
    }

    private SpawnKind DecideNextKind()
    {
        int tCount = CountKind(SpawnKind.Takeout);
        int dCount = CountKind(SpawnKind.DineIn);

        bool canT = tCount < maxActiveTakeout;
        bool canD = dineInUnlocked && dCount < maxActiveDineIn;

        if (canT && !canD) return SpawnKind.Takeout;
        if (!canT && canD) return SpawnKind.DineIn;
        if (!canT && !canD) return SpawnKind.Takeout;

        return (Random.value < (1f - dineInWeight)) ? SpawnKind.Takeout : SpawnKind.DineIn;
    }

    public CustomerAgent SpawnOne(SpawnKind? forceKind = null)
    {
        if (!shelfArea || !productTable) return null;
        if (alive.Count >= maxActiveTotal) return null;

        var kind = forceKind ?? DecideNextKind();
        if (kind == SpawnKind.Takeout && CountKind(kind) >= maxActiveTakeout) return null;
        if (kind == SpawnKind.DineIn  && CountKind(kind) >= maxActiveDineIn)  return null;

        // 상품 규칙
        CustomerProductRule rule = productTable.DrawRule();
        if (rule == null) return null;
        int         count   = productTable.SampleCount(rule);
        ProductType product = rule.product;

        // 위치/레인 선택
        Transform    spawn = (kind == SpawnKind.Takeout) ? spawnTakeout : spawnDineIn;
        CheckoutLane lane  = (kind == SpawnKind.Takeout) ? laneTakeout : laneDineIn;
        if (!spawn || !lane) return null;

        // 스폰
        GameObject go = PoolManager.Instance.Spawn(defaultPoolKey, spawn.position, spawn.rotation);
        if (!go) return null;
        CustomerAgent agent = go.GetComponent<CustomerAgent>();
        if (!agent) return null;

        // 유형/레인/좌석 주입 + 수요 설정`
        agent.SetKind(kind == SpawnKind.DineIn ? CustomerAgent.CustomerKind.DineIn
                                               : CustomerAgent.CustomerKind.Takeout);
        agent.InjectLanes(laneTakeout, laneDineIn);
        agent.InjectTableSeats(tableSeats);
        agent.SetDemand(product, count);

        // 회수 이벤트 연결
        agent.OnReturnedToPool -= HandleReturned;
        agent.OnReturnedToPool += HandleReturned;
        alive.Add(agent);

        // 이동 플로우 시작(여기서 lane 저장됨)
        agent.Begin(shelfArea, lane, spawn.position);

        return agent;
    }

    private void HandleReturned(CustomerAgent agent)
    {
        if (agent == null) return;
        alive.Remove(agent);
    }
    
    public CustomerAgent SpawnOneForce(SpawnKind kind)
    {
        if (!shelfArea || !productTable) return null;

        Transform    spawn = (kind == SpawnKind.DineIn) ? spawnDineIn : spawnTakeout;
        CheckoutLane lane  = (kind == SpawnKind.DineIn) ? laneDineIn  : laneTakeout;
        if (!spawn || !lane) return null;

        var rule = productTable.DrawRule();
        if (rule == null) return null;

        int count = productTable.SampleCount(rule);
        var product = rule.product;

        var go = PoolManager.Instance.Spawn(defaultPoolKey, spawn.position, spawn.rotation);
        if (!go) return null;

        var agent = go.GetComponent<CustomerAgent>();
        if (!agent) return null;

        agent.SetKind(kind == SpawnKind.DineIn ? CustomerAgent.CustomerKind.DineIn
            : CustomerAgent.CustomerKind.Takeout);
        agent.InjectLanes(laneTakeout, laneDineIn);
        agent.InjectTableSeats(tableSeats);
        agent.SetDemand(product, count);

        agent.OnReturnedToPool -= HandleReturned;
        agent.OnReturnedToPool += HandleReturned;
        alive.Add(agent);

        agent.Begin(shelfArea, lane, spawn.position);
        return agent;
    }

    private int CountKind(SpawnKind kind)
    {
        int c = 0;
        foreach (var a in alive)
            if (a && a.IsDineIn == (kind == SpawnKind.DineIn)) c++;
        return c;
    }

    // ── Director에서 호출할 공개 컨트롤 ──
    public void EnableDineIn(bool unlocked)
    {
        dineInUnlocked = unlocked;
        dineInWeight   = Mathf.Clamp01(dineInWeight);
    }
    public void SetMaxTotal(int max)   { maxActiveTotal   = Mathf.Max(1, max); }
    public void SetMaxTakeout(int max) { maxActiveTakeout = Mathf.Max(0, max); }
    public void SetMaxDineIn(int max)  { maxActiveDineIn  = Mathf.Max(0, max); }
}
