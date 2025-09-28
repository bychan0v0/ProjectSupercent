using System;
using UnityEngine;

public class SalesTracker : MonoBehaviour
{
    public static SalesTracker Instance { get; private set; }

    [Serializable] public struct SaleInfo
    {
        public ProductType product;
        public int count;
        public int revenue;
    }

    public int TotalUnitsSold { get; private set; }
    public int TotalRevenue   { get; private set; }

    public event Action<SaleInfo> OnSale;
    public event Action<int,int>  OnTotalsChanged; // (units, revenue)

    private void Awake()
    {
        Instance = this;
    }

    public static void ReportSale(ProductType product, int count, int revenue)
    {
        Analytics.Log("sale", new {
            product = product ? product.displayName : "null",
            count,
            revenue
        });
        
        if (!Instance) return;
        Instance.TotalUnitsSold += Mathf.Max(0, count);
        Instance.TotalRevenue   += Mathf.Max(0, revenue);
        var info = new SaleInfo { product = product, count = count, revenue = revenue };
        Instance.OnSale?.Invoke(info);
        Instance.OnTotalsChanged?.Invoke(Instance.TotalUnitsSold, Instance.TotalRevenue);
    }
}