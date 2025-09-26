using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ProductShelf : ProductHolder,
    IProductSink,              // 넣기
    IAcceptsProductType,       // 선반이 받는 타입 노출
    IProductSource,            // ← 추가: 빼가기
    IProvidesProductType,      // ← 추가: Source/AutoItemTransfer에서 타입 조회용
    ISourceVisualPopper        // ← 추가: 손으로 ‘슥-’ 빨아오기 연출
{
    // ===== 타입/용량 =====
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

    // ====== 배치 보조 ======
    public Vector3 GetNextSlotWorldPos()
    {
        int idx = NextFreeIndex();
        if (idx < 0) idx = _slots.Count - 1; // 꽉 찼다면 맨 마지막 칸으로
        return gridRoot.TransformPoint(LocalPos(idx));
    }

    private int NextFreeIndex()
    {
        for (int i = 0; i < _slots.Count; i++)
            if (_slots[i] == null) return i;
        return -1;
    }

    private int LastFilledIndex()
    {
        for (int i = _slots.Count - 1; i >= 0; i--)
            if (_slots[i] != null) return i;
        return -1;
    }

    private Vector3 LocalPos(int idx)
    {
        int r = idx / cols, c = idx % cols;
        return localOffset + new Vector3(c*cellSize.x, 0f, r*cellSize.y);
    }

    // ====== Sink (채워넣기) ======
    // 손에서 내려놓은 프리팹을 그리드에 고정(연출)
    public bool AcceptFromHand(GameObject go)
    {
        int idx = NextFreeIndex();
        if (idx < 0 || !go) return false;

        if (go.TryGetComponent<Rigidbody>(out var rb))
        {
            rb.isKinematic = true; rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
        }
        if (go.TryGetComponent<Collider>(out var col)) col.enabled = false;

        go.transform.SetParent(gridRoot, false);
        go.transform.localPosition = LocalPos(idx);
        go.transform.localRotation = Quaternion.Euler(itemLocalEuler);
        go.transform.localScale = Vector3.one;

        _slots[idx] = go;
        return true;
    }

    /// <summary>
    /// IProductSink 규약(네 인터페이스): 반환값은 "남은 양(= 저장 못 한 잔여량)".
    /// </summary>
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

    // ====== Source (빼가기) ======
    // 현재 재고 수(해당 타입이 아니면 0)
    public int Peek(ProductType p)
    {
        if (p != product) return 0;
        return Peek(); // ProductHolder에 이미 있는 재고 조회
    }

    /// <summary>
    /// 최대 requested만큼 빼기. 실제로 뺀 개수는 out taken.
    /// </summary>
    public bool TryTake(ProductType p, int requested, out int taken)
    {
        taken = 0;
        if (p != product || requested <= 0) return false;

        int stock = Peek();
        int give = Mathf.Min(requested, stock);
        if (give <= 0) return false;

        // 내부 재고 감소 (※ ProductHolder에 대응되는 감소 메서드 사용)
        // 아래 RemoveInternal(give)는 네 ProductHolder에 존재한다고 가정.
        // 이름이 다르면 네 프로젝트의 감소 메서드로 바꿔줘.
        RemoveInternal(give);

        taken = give;
        return true;
    }

    // ====== Visual Pop (손으로 ‘슥-’ 흡입) ======
    // AutoItemTransfer가 ManualPickup 도중 호출해서, 실제 프리팹 1개를 손으로 빨아가게 함.
    public bool TryPopVisual(out GameObject go)
    {
        go = null;
        int idx = LastFilledIndex();
        if (idx < 0) return false;

        go = _slots[idx];
        _slots[idx] = null;

        if (!go) return false;

        // 손으로 이동하기 쉽게 부모를 끊어줌(콜라이더는 이미 꺼져 있음)
        go.transform.SetParent(null);
        return true;
    }
}
