using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ProductShelf : ProductHolder, IProductSink, IAcceptsProductType
{
    public ProductType Type => product;
    public int CapacityLeft(ProductType t) => (t == product) ? SpaceLeft() : 0;

    [Header("Grid")]
    public Transform gridRoot;
    public int rows = 2, cols = 3;
    public Vector2 cellSize = new(0.25f, 0.25f);
    public Vector3 localOffset;
    public Vector3 itemLocalEuler = new(0,0,0);

    private List<GameObject> _slots;
    public int Capacity => rows * cols;

    private void Awake()
    {
        if (!gridRoot) gridRoot = transform;
        _slots = new List<GameObject>(Capacity);
        for (int i = 0; i < Capacity; i++) _slots.Add(null);
    }

    public Vector3 GetNextSlotWorldPos()
    {
        int idx = NextFreeIndex();
        if (idx < 0) idx = _slots.Count - 1;
        return gridRoot.TransformPoint(LocalPos(idx));
    }

    public bool AcceptFromHand(GameObject go)
    {
        int idx = NextFreeIndex();
        if (idx < 0 || !go) return false;

        if (go.TryGetComponent<Rigidbody>(out var rb)) { rb.isKinematic = true; rb.velocity=Vector3.zero; rb.angularVelocity=Vector3.zero; }
        if (go.TryGetComponent<Collider>(out var col)) col.enabled = false;

        go.transform.SetParent(gridRoot, false);
        go.transform.localPosition = LocalPos(idx);
        go.transform.localRotation = Quaternion.Euler(itemLocalEuler);
        go.transform.localScale = Vector3.one;

        _slots[idx] = go;
        return true;
    }

    public int TryStore(ProductType t, int amount)
    {
        if (t != product || amount <= 0) return amount;
        int put = Mathf.Min(amount, SpaceLeft());
        if (put > 0)
        {
            AddInternal(put); // 데이터만 갱신(스폰/풀 없음)
            Debug.Log($"{{\"event\":\"store_to_shelf\",\"product\":\"{t.displayName}\",\"put\":{put},\"stock\":{Peek()},\"t\":{Time.time:F2}}}");
        }
        return amount - put;
    }

    private int NextFreeIndex()
    {
        for (int i = 0; i < _slots.Count; i++)
            if (_slots[i] == null) return i;
        return -1;
    }
    private Vector3 LocalPos(int idx)
    {
        int r = idx / cols, c = idx % cols;
        return localOffset + new Vector3(c*cellSize.x, 0f, r*cellSize.y);
    }
}
