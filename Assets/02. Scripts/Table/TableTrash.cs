using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class TableTrash : MonoBehaviour
{
    [Header("Player Detect")]
    [SerializeField] private LayerMask playerLayers;
    [SerializeField, Min(0.1f)] private float detectRadius = 1.2f;
    [SerializeField, Min(0f)] private float holdSeconds = 0f; // 0이면 즉시 청소

    [Header("Optional VFX/Sound")]
    [SerializeField] private GameObject cleanVfxPrefab;

    private TableSeatManager seatMgr;
    private bool cleaning;

    public void Init(TableSeatManager mgr) => seatMgr = mgr;

    private void Reset()
    {
        var col = GetComponent<Collider>();
        if (col) col.isTrigger = true;
    }

    private void Update()
    {
        if (cleaning) return;

        // 반경 내 플레이어가 있으면 청소 시작
        Collider[] hits = Physics.OverlapSphere(transform.position, detectRadius, playerLayers);
        if (hits != null && hits.Length > 0)
            StartCoroutine(CoClean());
    }

    private IEnumerator CoClean()
    {
        cleaning = true;
        if (holdSeconds > 0f) yield return new WaitForSeconds(holdSeconds);

        // 좌석 클린 통보
        seatMgr?.MarkSeatClean(this);

        // VFX
        if (cleanVfxPrefab)
            Instantiate(cleanVfxPrefab, transform.position, transform.rotation);

        // 자기 제거(풀 우선)
        var po = GetComponent<PooledObject>();
        if (po && PoolManager.Instance != null) PoolManager.Instance.Despawn(gameObject);
        else Destroy(gameObject);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 1f, 0.6f, 0.25f);
        Gizmos.DrawSphere(transform.position, detectRadius);
    }
#endif
}