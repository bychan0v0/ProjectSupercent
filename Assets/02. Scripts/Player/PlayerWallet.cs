using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerWallet : MonoBehaviour
{
    [SerializeField] private int gold = 0;
    public int Gold => gold;

    public void Add(int amount)
    {
        if (amount <= 0) return;
        gold += amount;
        // TODO: UI 갱신 훅 연결 가능 (이벤트/UnityEvent 등)
    }
}
