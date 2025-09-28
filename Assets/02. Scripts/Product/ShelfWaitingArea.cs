using System.Collections.Generic;
using UnityEngine;

public sealed class ShelfWaitingArea : MonoBehaviour
{
    [Header("Product This Shelf Serves")]
    [SerializeField] private ProductType product;
    public ProductType Product => product;

    [Header("Source (고객이 실제로 가져갈 곳)")]
    [SerializeField] private MonoBehaviour sourceObject;
    public IProductSource Source { get; private set; }

    [Header("Slots (Manual Only)")]
    [SerializeField] private Transform[] slots = System.Array.Empty<Transform>();
    public Transform[] Slots => slots;

    // 점유/예약 상태
    private readonly Dictionary<Transform, CustomerAgent> _occupied = new();
    private readonly HashSet<Transform> _reserved = new();

    // 선반 앞 FIFO(한 번에 한 명만 픽업)
    private readonly Queue<CustomerAgent> _queue = new();
    private readonly HashSet<CustomerAgent> _enqueued = new();
    private CustomerAgent _serving = null;

    private void Awake()
    {
        TryBindSource();
        InitOccupiedTable();
    }

    private void TryBindSource()
    {
        if (sourceObject is IProductSource s1) { Source = s1; return; }
        Source = GetComponentInParent<IProductSource>();
        // 없으면 경고만 — 실제 픽업 시엔 반드시 필요
        if (Source == null)
            Debug.LogWarning("[ShelfWaitingArea] IProductSource를 찾지 못했습니다. Source Object를 지정하세요.", this);
    }

    private void InitOccupiedTable()
    {
        _occupied.Clear();
        if (slots == null) return;
        for (int i = 0; i < slots.Length; i++)
        {
            Transform t = slots[i];
            if (t && !_occupied.ContainsKey(t)) _occupied[t] = null;
        }
    }

    public int Capacity => slots?.Length ?? 0;

    // ===== 예약/점유 =====

    public bool TryReserveClosest(Vector3 fromPos, out Transform slot)
    {
        slot = null;
        if (slots == null || slots.Length == 0) return false;

        Transform best = null;
        float bestDist = float.MaxValue;

        for (int i = 0; i < slots.Length; i++)
        {
            Transform t = slots[i];
            if (!t) continue;
            if (_reserved.Contains(t)) continue;
            if (_occupied.TryGetValue(t, out CustomerAgent who) && who != null) continue;

            float d = (t.position - fromPos).sqrMagnitude;
            if (d < bestDist) { bestDist = d; best = t; }
        }

        if (!best) return false;
        _reserved.Add(best);
        slot = best;
        return true;
    }

    public void Occupy(Transform slot, CustomerAgent who)
    {
        if (!slot) return;
        _reserved.Remove(slot);
        if (!_occupied.ContainsKey(slot)) _occupied[slot] = null;
        _occupied[slot] = who;
        Enqueue(who);
    }

    public void Release(Transform slot, CustomerAgent who = null)
    {
        if (!slot) return;
        if (who != null && _occupied.TryGetValue(slot, out var owner) && owner != who)
            return; // 남의 슬롯이면 무시(안전)
        _occupied.Remove(slot);
    }

    public void CancelReserve(Transform slot)
    {
        if (!slot) return;
        _reserved.Remove(slot);
    }

    // ===== FIFO(내 차례 관리) =====

    public bool IsMyTurnNow(CustomerAgent who)
    {
        return _serving == null && _queue.Count > 0 && ReferenceEquals(_queue.Peek(), who);
    }

    public bool TryBeginService(CustomerAgent who)
    {
        if (IsMyTurnNow(who))
        {
            _serving = _queue.Dequeue();
            _enqueued.Remove(who);
            return true;
        }
        return false;
    }

    public void EndService(CustomerAgent who)
    {
        if (_serving == who) _serving = null;
    }

    public void Requeue(CustomerAgent who)
    {
        if (_enqueued.Add(who)) _queue.Enqueue(who);
    }

    private void Enqueue(CustomerAgent who)
    {
        if (_enqueued.Add(who)) _queue.Enqueue(who);
    }

    private void RemoveFromQueue(CustomerAgent who)
    {
        if (ReferenceEquals(_serving, who)) { _serving = null; return; }
        if (!_enqueued.Remove(who)) return;

        int n = _queue.Count;
        for (int i = 0; i < n; i++)
        {
            CustomerAgent c = _queue.Dequeue();
            if (!ReferenceEquals(c, who)) _queue.Enqueue(c);
        }
    }
}
