using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Bakery/GeneratorConfig")]
public class GeneratorConfig : ScriptableObject
{
    [Header("What to generate")]
    public ProductType productType;

    [Header("Spawn Loop")]
    public float spawnInterval = 1.5f;
    public int spawnBatch = 1;
    public int maxStock = 30;

    [Header("Start")]
    public bool autoStart = true;
    public float startDelay = 0f;
}
