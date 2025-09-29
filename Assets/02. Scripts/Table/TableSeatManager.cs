using System.Collections.Generic;
using UnityEngine;

public class TableSeatManager : MonoBehaviour
{
    [System.Serializable]
    public struct Seat
    {
        public Transform seatAnchor;
        public Transform tablePlace;

        public bool occupied;
        public CustomerAgent who;

        // ★ 추가
        public bool dirty;                     // 쓰레기가 올라와 있으면 true
        public TableTrash trash;               // 현재 쓰레기 참조
    }

    [SerializeField] private List<Seat> seats = new();
    
    [System.Serializable]
    public struct PriceEntry { public ProductType product; public int unitPrice; }
    
    [Header("Pricing (Dine-In)")]
    [SerializeField] private List<PriceEntry> priceTable = new();
    
    public bool TryReserve(out Transform seatAnchor, out Transform tablePlace, CustomerAgent who)
    {
        seatAnchor = null; tablePlace = null;
        if (seats == null || who == null) return false;

        for (int i = 0; i < seats.Count; i++)
        {
            var s = seats[i];
            if (!s.occupied && !s.dirty && s.seatAnchor)
            {
                s.occupied = true;
                s.who = who;
                seats[i] = s;

                seatAnchor = s.seatAnchor;
                tablePlace = s.tablePlace ? s.tablePlace : s.seatAnchor;
                return true;
            }
        }
        return false;
    }

    public void ReleaseBy(CustomerAgent who)
    {
        for (int i = 0; i < seats.Count; i++)
        {
            var seat = seats[i];
            if (seat.who == who)
            {
                seat.occupied = false;
                seat.who = null;
            }
        }
    }
    
    public void MarkSeatDirtyBy(CustomerAgent who, TableTrash trash)
    {
        if (seats == null) return;
        for (int i = 0; i < seats.Count; i++)
        {
            var s = seats[i];
            if (s.who == who || (s.occupied && s.who == null && s.tablePlace == who?.transform)) { /* 안전 */ }

            if (s.who == who)
            {
                s.occupied = false;           // 손님은 떠남
                s.who = null;
                s.dirty = true;               // 쓰레기로 막힘
                s.trash = trash;              // 추적
                seats[i] = s;
                return;
            }
        }
    }
    
    public System.Action OnSeatFreed; // (옵션) 카운터에 알려 다시 평가

    public void MarkSeatClean(TableTrash trash)
    {
        if (seats == null) return;
        for (int i = 0; i < seats.Count; i++)
        {
            var s = seats[i];
            if (s.trash == trash)
            {
                s.dirty = false;
                s.trash = null;
                seats[i] = s;

                OnSeatFreed?.Invoke(); // (옵션) 다인인 카운터가 구독하면 즉시 다음 평가
                return;
            }
        }
    }
    
    public bool HasFreeSeat()
    {
        if (seats == null) return false;
        for (int i = 0; i < seats.Count; i++)
            if (!seats[i].occupied && !seats[i].dirty && seats[i].seatAnchor)
                return true;
        return false;
    }
    
    public int GetUnitPrice(ProductType t)
    {
        if (t == null) return 0;
        for (int i = 0; i < priceTable.Count; i++)
            if (priceTable[i].product == t) return Mathf.Max(0, priceTable[i].unitPrice);
        return 0;
    }
}