using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class PlayerWallet : MonoBehaviour
{
    [SerializeField] private int gold = 0;
    [SerializeField] private TMP_Text goldText;

    public int Gold => gold;
    
    private void RefreshText()
    {
        if (goldText) goldText.text = gold.ToString();
    }

    public void Add(int amount)
    {
        if (amount <= 0) return;
        gold += amount;
        RefreshText();
    }

    public bool TrySpend(int amount)
    {
        if (amount <= 0) return true;
        if (gold < amount) return false;
        gold -= amount;
        RefreshText();
        return true;
    }

    public int SpendUpTo(int amount)
    {
        if (amount <= 0) return 0;
        int spent = Mathf.Min(amount, gold);
        if (spent > 0)
        {
            gold -= spent;
            RefreshText();
        }
        return spent;
    }
}
