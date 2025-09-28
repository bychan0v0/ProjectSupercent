using System.Collections.Generic;
using UnityEngine;

public class TableSeatManager : MonoBehaviour
{
    [System.Serializable]
    public class Seat
    {
        public Transform seatAnchor;     // 손님이 도착할 위치/방향
        public Transform tablePlace;     // (선택) 음식/트레이를 놓을 테이블 표면 앵커
        [HideInInspector] public bool occupied;
        [HideInInspector] public CustomerAgent who;
    }

    [SerializeField] private List<Seat> seats = new();

    public bool TryReserve(out Transform seatAnchor, out Transform tablePlace, CustomerAgent who)
    {
        for (int i = 0; i < seats.Count; i++)
        {
            if (!seats[i].occupied && seats[i].seatAnchor)
            {
                seats[i].occupied = true;
                seats[i].who = who;
                seatAnchor = seats[i].seatAnchor;
                tablePlace = seats[i].tablePlace;
                return true;
            }
        }
        seatAnchor = null; tablePlace = null;
        return false;
    }

    public void ReleaseBy(CustomerAgent who)
    {
        for (int i = 0; i < seats.Count; i++)
        {
            if (seats[i].who == who)
            {
                seats[i].occupied = false;
                seats[i].who = null;
            }
        }
    }
}