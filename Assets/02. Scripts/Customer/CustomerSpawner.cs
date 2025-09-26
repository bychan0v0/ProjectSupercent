using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CustomerSpawner : MonoBehaviour
{
    [Header("Pool")]
    [SerializeField] private string defaultPoolKey = "Customer";

    [Header("Auto Spawn")]
    [SerializeField, Min(0.1f)] private float spawnInterval = 0.8f;
    [SerializeField, Min(1)]    private int   maxActive = 12;
    [SerializeField] private bool autoStart = true;

    [Header("Scene Refs")]
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private ShelfWaitingArea shelfArea;
    [SerializeField] private CheckoutLane checkoutLane;

    [Header("Product Table (SO)")]
    [SerializeField] private CustomerProductTableSO productTable;

    private readonly HashSet<CustomerAgent> alive = new();
    private float timer;

    private void Start()
    {
        if (autoStart) timer = spawnInterval;
    }

    private void Update()
    {
        if (!autoStart) return;
        if (!spawnPoint || !shelfArea || !checkoutLane || !productTable) return;
        if (alive.Count >= maxActive) return;

        timer -= Time.deltaTime;
        if (timer <= 0f)
        {
            SpawnOne();
            timer = spawnInterval;
        }
    }

    public CustomerAgent SpawnOne()
    {
        if (!spawnPoint || !shelfArea || !checkoutLane || !productTable) return null;
        if (alive.Count >= maxActive) return null;

        // 1) 상품 규칙 뽑기
        CustomerProductRule rule = productTable.DrawRule();
        if (rule == null) return null;

        int         count   = productTable.SampleCount(rule);
        ProductType product = rule.product;

        // 2) 프리팹/풀 키
        string poolKey = defaultPoolKey;

        // 3) 스폰
        GameObject go = PoolManager.Instance.Spawn(poolKey, spawnPoint.position, spawnPoint.rotation);
        CustomerAgent agent = go.GetComponent<CustomerAgent>();
        if (agent == null) return null;

        // 4) 수요 주입
        agent.SetDemand(product, count);

        // 5) 반납 이벤트 연결 + 활성 집합 관리
        agent.OnReturnedToPool -= HandleReturned;
        agent.OnReturnedToPool += HandleReturned;
        alive.Add(agent);

        // 6) 이동 플로우 시작
        agent.Begin(shelfArea, checkoutLane, spawnPoint.position);

        return agent;
    }

    private void HandleReturned(CustomerAgent agent)
    {
        if (agent == null) return;
        alive.Remove(agent);
    }
}
