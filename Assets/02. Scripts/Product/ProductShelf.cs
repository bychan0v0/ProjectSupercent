using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ProductShelf : ProductHolder, IProductSink, IAcceptsProductType
{
    public ProductType Type => product;

    public int CapacityLeft(ProductType t) => (t == product) ? SpaceLeft() : 0;

    public int TryStore(ProductType t, int amount)
    {
        if (t != product || amount <= 0) return amount;
        int put = Mathf.Min(amount, SpaceLeft());
        if (put > 0)
        {
            AddInternal(put);
            Debug.Log($"{{\"event\":\"store_to_shelf\",\"product\":\"{t.displayName}\",\"put\":{put},\"stock\":{Peek()},\"t\":{Time.time:F2}}}");
        }
        return amount - put;
    }
}
