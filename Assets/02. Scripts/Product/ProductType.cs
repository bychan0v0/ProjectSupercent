using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Bakery/ProductType")]
public class ProductType : ScriptableObject
{
    public int id;
    public string displayName;
    public Sprite icon;
}
