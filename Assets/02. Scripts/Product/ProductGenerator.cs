using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using Random = UnityEngine.Random;

public class ProductGenerator : ProductHolder, IProductSource, IProvidesProductType, ISourceVisualPopper
{
    [Header("Spawn Loop")]
    [Min(0.05f)] public float spawnInterval = 1.2f;
    [Min(1)] public int spawnBatch = 1;
    public bool autoStart = true;
    public float startDelay = 0f;

    [SerializeField] private string explicitPoolKey;
    
    public ProductType Type => product;
    private Coroutine _loop;

    [Header("Physics Drop")]
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private Transform boxRoot;
    [SerializeField] private Vector2 boxHalfSize = new(0.25f, 0.25f);

    [Header("Force")]
    public float dropForce = 3f;
    public float upForce = 1f;
    public float lateralJitter = 0.5f;

    private readonly Queue<GameObject> _visuals = new();
    private string PoolKey => product.displayName;

    private void OnEnable()
    {
        OnAmountChanged += SyncToStock;
    }

    private void OnDisable()
    {
        if (_loop != null) StopCoroutine(_loop);
        _loop = null;
        OnAmountChanged -= SyncToStock;
        while (_visuals.Count > 0) Despawn(_visuals.Dequeue());
    }

    private void Start()
    {
        SyncToStock(Peek());
        if (autoStart && product != null) _loop = StartCoroutine(SpawnLoop());
    }
    
    private IEnumerator SpawnLoop()
    {
        if (startDelay > 0f) yield return new WaitForSeconds(startDelay);
        var wait = new WaitForSeconds(spawnInterval);
        while (true)
        {
            if (SpaceLeft() > 0)
            {
                int add = Mathf.Min(spawnBatch, SpaceLeft());
                AddInternal(add);
            }
            yield return wait;
        }
    }

    public bool TryTake(ProductType t, int amount, out int taken)
    {
        taken = 0;
        if (t == null || t != product || amount <= 0 || Peek() <= 0) return false;
        taken = RemoveInternal(amount);
        return taken > 0;
    }
    public int Peek(ProductType t) => (t == product) ? Peek() : 0;

    public bool TryPopVisual(out GameObject go)
    {
        go = null;
        if (_visuals.Count == 0) return false;
        go = _visuals.Dequeue();
        return go != null;
    }
    
    private void SyncToStock(int targetCount)
    {
        while (_visuals.Count < targetCount)
        {
            _visuals.Enqueue(SpawnOnePhys());
            Analytics.Log("generator_sync", new { type = product.displayName, stock = targetCount });
        }
    }

    private GameObject SpawnOnePhys()
    {
        Vector3 startPos = spawnPoint.position;

        var go = PoolManager.Instance.Spawn(PoolKey, startPos, Quaternion.identity, null);
        if (!go)
        {
            Debug.LogError($"[ProductGenerator] Spawn 실패: key={PoolKey}", this);
            return null;
        }

        // ★ 스폰 직후 상태 리셋(이전 생애의 kinematic/콜라이더 Off/트윈 잔재 제거)
        ResetSpawnState(go);

        // 이제 물리가 살아있으니 AddForce가 정상 동작
        var rb = go.GetComponent<Rigidbody>();
        if (!rb)
        {
            Debug.LogWarning("[ProductGenerator] Rigidbody 없음. 임시로 추가합니다.", go);
            rb = go.AddComponent<Rigidbody>(); // (원래 프리팹에 붙어 있어야 정상)
        }

        Vector3 local  = new Vector3(Random.Range(-boxHalfSize.x, boxHalfSize.x), 0f,
            Random.Range(-boxHalfSize.y, boxHalfSize.y));
        Vector3 target = boxRoot ? boxRoot.TransformPoint(local) : startPos;
        Vector3 dir    = (target - startPos).normalized;

        Vector3 force = dir * dropForce
                        + Vector3.up * upForce
                        + (boxRoot ? boxRoot.right : Vector3.right) * Random.Range(-lateralJitter, lateralJitter);

        rb.AddForce(force, ForceMode.Impulse);
        return go;
    }

    private void Despawn(GameObject go)
    {
        if (!go) return;
        PoolManager.Instance.Despawn(go);
    }
    
    private void ResetSpawnState(GameObject go)
    {
        // 트윈 잔재 제거(부모/자식 전부)
        DOTween.Kill(go.transform, complete: false);
        foreach (var t in go.GetComponentsInChildren<Transform>(true))
            DOTween.Kill(t, complete: false);

        // 부모 영향 제거(혹시 남아있다면)
        go.transform.SetParent(null, true);

        // 콜라이더/리지드바디 원복
        var rbs  = go.GetComponentsInChildren<Rigidbody>(true);
        foreach (var rb in rbs)
        {
            rb.isKinematic     = false;
            rb.useGravity      = true;
            rb.velocity        = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        var cols = go.GetComponentsInChildren<Collider>(true);
        foreach (var c in cols) c.enabled = true;
    }
}
