using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PoolManager : MonoBehaviour
{
    [System.Serializable]
    public class Pool
    {
        public string key;
        public GameObject prefab;
        public int initialSize = 8;

        [HideInInspector] public Queue<GameObject> q = new Queue<GameObject>();
    }

    public static PoolManager Instance { get; private set; }
    [SerializeField] private List<Pool> pools;

    private readonly Dictionary<string, Pool> _byKey = new();

    private void Awake()
    {
        Instance = this;
        foreach (var p in pools)
        {
            _byKey[p.key] = p;
            for (int i = 0; i < p.initialSize; i++)
                p.q.Enqueue(New(p));
        }
    }

    GameObject New(Pool p)
    {
        var go = Instantiate(p.prefab, transform);
        go.SetActive(false);
        return go;
    }

    public GameObject Spawn(string key, Vector3 pos, Quaternion rot, Transform parent = null)
    {
        var p = _byKey[key];
        var go = p.q.Count > 0 ? p.q.Dequeue() : New(p);
        go.transform.SetPositionAndRotation(pos, rot);
        if (parent) go.transform.SetParent(parent);
        go.SetActive(true);
        return go;
    }

    public void Despawn(string key, GameObject go)
    {
        var p = _byKey[key];
        go.SetActive(false);
        go.transform.SetParent(transform);
        p.q.Enqueue(go);
    }
}
