using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IProductSource 
{
    bool TryTake(ProductType t, int amount, out int taken);
    int Peek(ProductType t);
}

public interface IProductSink 
{
    int CapacityLeft(ProductType t);
    int TryStore(ProductType t, int amount);
}

public interface IProductCarrier 
{
    int CapacityLeft { get; }
    int Count(ProductType t);
    bool TryAddOne(ProductType t);
    bool TryRemoveOne(ProductType t);
}

public interface ISourceVisualPopper
{
    bool TryPopVisual(out GameObject go);
}

public interface IProvidesProductType { ProductType Type { get; } }
public interface IAcceptsProductType { ProductType Type { get; } }
