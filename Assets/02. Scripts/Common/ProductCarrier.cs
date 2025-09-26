using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ProductCarrier : MonoBehaviour, IProductCarrier
{
    [Min(1)] public int maxCarry = 10;
    private readonly Dictionary<ProductType,int> bag = new();

    private int Total { get { int s=0; foreach(var kv in bag) s+=kv.Value; return s; } }
    public int CapacityLeft => Mathf.Max(0, maxCarry - Total);
    public int Count(ProductType t) => (t != null && bag.TryGetValue(t, out var v)) ? v : 0;

    public bool TryAddOne(ProductType t)
    {
        if (t == null || CapacityLeft <= 0) return false;
        bag[t] = Count(t) + 1;
        Debug.Log($"{{\"event\":\"inv_add\",\"product\":\"{t.displayName}\",\"delta\":1,\"total\":{Total},\"t\":{Time.time:F2}}}");
        return true;
    }
    public bool TryRemoveOne(ProductType t)
    {
        if (t == null || !bag.TryGetValue(t, out var v) || v <= 0) return false;
        v -= 1;
        if (v == 0) bag.Remove(t); else bag[t] = v;
        Debug.Log($"{{\"event\":\"inv_remove\",\"product\":\"{t.displayName}\",\"delta\":-1,\"total\":{Total},\"t\":{Time.time:F2}}}");
        return true;
    }
}
