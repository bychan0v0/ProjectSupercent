using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum RangeDistribution { Uniform, Triangle, NormalApprox }

[System.Serializable]
public class CustomerProductRule
{
    public ProductType product;
    [Min(1)] public int min = 1;
    [Min(1)] public int max = 3;
    [Min(0f)] public float weight = 1f;
    
    public RangeDistribution distribution = RangeDistribution.Triangle;
}

[CreateAssetMenu(menuName = "Spawn/CustomerProductTableSO")]
public class CustomerProductTableSO : ScriptableObject 
{
    public List<CustomerProductRule> rules = new();

    private System.Random _rng;

    private void OnEnable()
    {
        _rng = new System.Random(UnityEngine.Random.Range(int.MinValue, int.MaxValue));
    }

    public CustomerProductRule DrawRule()
    {
        if (rules == null || rules.Count == 0) return null;

        float sum = 0f;
        foreach (var r in rules) sum += Mathf.Max(0f, r.weight);
        if (sum <= 0f) return rules[0];

        float pick = (float)(_rng.NextDouble() * sum);
        float acc = 0f;
        foreach (var r in rules)
        {
            acc += Mathf.Max(0f, r.weight);
            if (pick <= acc) return r;
        }
        return rules[^1];
    }

    public int SampleCount(CustomerProductRule r)
    {
        if (r == null) return 1;

        switch (r.distribution)
        {
            case RangeDistribution.NormalApprox:
            {
                int a = UnityEngine.Random.Range(r.min, r.max + 1);
                int b = UnityEngine.Random.Range(r.min, r.max + 1);
                int c = UnityEngine.Random.Range(r.min, r.max + 1);
                return Mathf.Clamp(Mathf.RoundToInt((a + b + c) / 3f), r.min, r.max);
            }
            case RangeDistribution.Triangle:
            {
                int a = UnityEngine.Random.Range(r.min, r.max + 1);
                int t = UnityEngine.Random.Range(r.min, r.max + 1);
                return Mathf.Clamp(Mathf.RoundToInt((a + t) * 0.5f), r.min, r.max);
            }
            default: // Uniform
            {
                return UnityEngine.Random.Range(r.min, r.max + 1);
            }
        }
    }
}
