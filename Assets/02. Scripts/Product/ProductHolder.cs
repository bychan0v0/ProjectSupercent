using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class ProductHolder : MonoBehaviour
{
    [Header("Product & Capacity")]
    public ProductType product;
    public int capacity = 10;

    [Header("Runtime (ReadOnly)")]
    [SerializeField] protected int current = 0;

    public event Action<int> OnAmountChanged;

    protected void AddInternal(int amount)
    {
        int before = current;
        current = Mathf.Min(capacity, current + Mathf.Max(0, amount));
        if (current != before) OnAmountChanged?.Invoke(current);
    }

    protected int RemoveInternal(int amount)
    {
        int take = Mathf.Clamp(amount, 0, current);
        if (take > 0)
        {
            current -= take;
            OnAmountChanged?.Invoke(current);
        }
        return take;
    }

    public int Peek() => current;
    public int SpaceLeft() => Mathf.Max(0, capacity - current);
}
