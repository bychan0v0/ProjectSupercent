using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

public class ProductGenerator : ProductHolder, IProductSource, IProvidesProductType, ISourceVisualPopper
{
    [Header("Spawn Loop")]
    [Min(0.05f)] public float spawnInterval = 1.2f;
    [Min(1)] public int spawnBatch = 1;
    public bool autoStart = true;
    public float startDelay = 0f;

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
        SyncToStock(Peek());
        if (autoStart && product != null) _loop = StartCoroutine(SpawnLoop());
    }

    private void OnDisable()
    {
        if (_loop != null) StopCoroutine(_loop);
        _loop = null;
        OnAmountChanged -= SyncToStock;
        while (_visuals.Count > 0) Despawn(_visuals.Dequeue());
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
        while (_visuals.Count < targetCount) _visuals.Enqueue(SpawnOnePhys());
    }

    private GameObject SpawnOnePhys()
    {
        Vector3 startPos = spawnPoint ? spawnPoint.position : transform.position;
        var go = PoolManager.Instance.Spawn(PoolKey, startPos, Quaternion.identity, null);

        var rb = go.GetComponent<Rigidbody>();

        Vector3 local = new Vector3(Random.Range(-boxHalfSize.x, boxHalfSize.x), 0f, Random.Range(-boxHalfSize.y, boxHalfSize.y));
        Vector3 target = boxRoot ? boxRoot.TransformPoint(local) : startPos;
        Vector3 dir = (target - startPos).normalized;

        Vector3 force = dir * dropForce
                        + Vector3.up * upForce
                        + (boxRoot ? boxRoot.right : Vector3.right) * Random.Range(-lateralJitter, lateralJitter);

        rb.AddForce(force, ForceMode.Impulse);
        return go;
    }

    private void Despawn(GameObject go)
    {
        if (!go) return;
        if (!string.IsNullOrEmpty(PoolKey)) PoolManager.Instance.Despawn(PoolKey, go);
        else Destroy(go);
    }
}
