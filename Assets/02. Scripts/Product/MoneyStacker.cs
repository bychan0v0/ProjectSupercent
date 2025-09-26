using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

public class MoneyStacker : MonoBehaviour
{
    [Header("Spawn / Parent")]
    [SerializeField] private Transform gridAnchor;      // 지폐가 쌓일 기준(계산대 옆 원하는 위치)
    [SerializeField] private Transform parentForBills;  // 지폐들을 담을 부모(없으면 this)

    [Header("Grid Settings (3x3 per layer)")]
    [SerializeField, Min(1)] private int cols = 3;
    [SerializeField, Min(1)] private int rows = 3;
    [SerializeField] private Vector2 cellSize = new Vector2(0.12f, 0.08f); // x:가로, y:세로(z)
    [SerializeField] private float layerHeight = 0.02f;

    [Header("Value")]
    [SerializeField, Min(1)] private int valuePerBill = 10;      // 1장당 금액

    [Header("Pool / Prefab")]
    [SerializeField] private string cashPoolKey = "CashBill";     // 풀 키(없으면 프리팹 사용)

    [Header("Rise VFX (Below → Target, all simultaneous)")]
    [SerializeField, Min(0f)]   private float riseDistance = 0.25f; // anchor.up 아래에서 시작
    [SerializeField, Min(0.01f)] private float riseDuration = 0.25f;
    [SerializeField]            private Ease riseEase = Ease.OutCubic;
    [SerializeField]            private bool useScaleRise = true;   // 납작→원래 스케일로 살아나는 효과
    [SerializeField] private float yawDeg = 90f; // 살짝 랜덤 회전(선택)

    [Header("Collect to Player (Proximity)")]
    [SerializeField] private LayerMask playerLayers;          // 플레이어 레이어
    [SerializeField, Min(0.01f)] private float perBillStagger = 0.02f; // 지폐 발사 간격(빠르게)
    [SerializeField] private ItemTravelProfile toPlayerProfile; 
    
    private readonly HashSet<Collider> _playerContacts = new();
    private bool _collecting = false;
    
    private int _totalBills; // 지금까지 쌓인 지폐 개수

    private void Reset()
    {
        // 수거 트리거용 콜라이더가 이 오브젝트에 있다면 트리거화
        var col = GetComponent<Collider>();
        if (col) col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsPlayer(other)) return;
        _playerContacts.Add(other);
        // 플레이어가 들어왔고, 아직 수거 중이 아니면 시작
        if (!_collecting) StartCoroutine(CoCollectToNearestPlayerOnce());
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsPlayer(other)) return;
        _playerContacts.Remove(other);
    }

    private bool IsPlayer(Collider c)
    {
        var root = c.transform.root.gameObject;
        return (playerLayers.value & (1 << root.layer)) != 0;
    }

    private Transform FindNearestPlayer(out PlayerWallet wallet)
    {
        wallet = null;
        float best = float.PositiveInfinity;
        Transform bestT = null;

        foreach (var c in _playerContacts)
        {
            if (!c) continue;
            var w = c.GetComponentInParent<PlayerWallet>();
            var t = c.transform.root;
            if (!t) continue;

            float d = (t.position - transform.position).sqrMagnitude;
            if (d < best) { best = d; bestT = t; wallet = w; }
        }
        return bestT;
    }

    private IEnumerator CoCollectToNearestPlayerOnce()
    {
        _collecting = true;

        // 대상 플레이어 찾기
        PlayerWallet wallet;
        var target = FindNearestPlayer(out wallet);
        if (!target)
        {
            _collecting = false;
            yield break;
        }

        // 현재 쌓여 있는 지폐들 스냅샷
        var parent = parentForBills ? parentForBills : transform;
        List<Transform> bills = new List<Transform>(16);
        foreach (Transform c in parent)
        {
            if (c && c.gameObject.activeInHierarchy)
                bills.Add(c);
        }
        if (bills.Count == 0) { _collecting = false; yield break; }

        // 한 장씩 빠르게 플레이어 쪽으로 '쏙' 이동 → 도착 즉시 Despawn + 지갑 증가
        int launched = 0;
        foreach (var bill in bills)
        {
            if (!bill) continue;

            // 이미 어떤 트윈이 걸려있다면 정리
            bill.DOKill(true);

            // 곡선 이동(짧고 빠르게)
            var seq = ItemTravelTween.ArcMove(bill, target.position + Vector3.up * 3f, toPlayerProfile);

            // 캡쳐
            var captured = bill.gameObject;
            seq.OnComplete(() =>
            {
                // 도착 즉시 풀 회수
                var pooled = captured.GetComponent<PooledObject>();
                if (pooled && PoolManager.Instance != null)
                    PoolManager.Instance.Despawn(captured);
                else
                    Destroy(captured);

                // 그 순간 플레이어 돈 증가
                if (wallet != null) wallet.Add(valuePerBill);

                // 내부 카운트도 정리
                _totalBills = Mathf.Max(0, _totalBills - 1);
            });

            launched++;
            if (perBillStagger > 0f) yield return new WaitForSeconds(perBillStagger);
        }

        // 끝
        _collecting = false;
    }
    
    public void ClearAll()
    {
        var parent = parentForBills ? parentForBills : transform;
        // 풀 오브젝트면 풀로, 아니면 파괴
        foreach (Transform c in parent)
        {
            var pooled = c.GetComponent<PooledObject>();
            if (pooled) PoolManager.Instance.Despawn(c.gameObject);
            else Destroy(c.gameObject);
        }
        _totalBills = 0;
    }

    public void StackAmount(int amount)
    {
        if (amount <= 0) return;
        int bills = Mathf.CeilToInt(amount / (float)valuePerBill);
        StackBills(bills);
    }

    public void StackBills(int bills)
    {
        if (bills <= 0) return;

        var anchor = gridAnchor ? gridAnchor : transform;
        var parent = parentForBills ? parentForBills : transform;

        // 모든 지폐가 동시에 솟아오르도록: 루프에서 각각 바로 트윈 시작
        for (int i = 0; i < bills; i++)
        {
            int idx = _totalBills + i;
            Vector3 target = GetCellWorldPos(idx, anchor);

            // 1) 스폰 (풀 우선)
            GameObject go = null;
            if (!string.IsNullOrEmpty(cashPoolKey) && PoolManager.Instance != null)
                go = PoolManager.Instance.Spawn(cashPoolKey, target, Quaternion.Euler(0f, yawDeg, 0f));
            if (!go) continue;

            // 부모 먼저 지정(정리 및 기준 안정)
            go.transform.SetParent(parent, true);

            // (선택) 물리/충돌 끄기
            if (go.TryGetComponent<Rigidbody>(out var rb))
            { rb.isKinematic = true; rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            if (go.TryGetComponent<Collider>(out var col)) col.enabled = false;

            // 2) 시작 포즈: 아래에서 시작 + 약간 랜덤 회전/스케일
            Vector3 start = target - (anchor.up * Mathf.Abs(riseDistance));
            go.transform.position = start;

            Vector3 originalScale = go.transform.localScale;
            if (useScaleRise)
                go.transform.localScale = new Vector3(originalScale.x, 0.01f, originalScale.z); // 납작하게 시작

            // 3) VFX: 아래→타겟으로 상승 (전부 동시)
            var seq = DOTween.Sequence();
            seq.Join(go.transform.DOMove(target, riseDuration).SetEase(riseEase));

            if (useScaleRise)
                seq.Join(go.transform.DOScaleY(originalScale.y, riseDuration).SetEase(Ease.OutBack));

            seq.OnComplete(() =>
            {
                // 부동오차 정리
                go.transform.position = target;
                if (useScaleRise) go.transform.localScale = originalScale;
            });
        }

        _totalBills += bills;
    }

    private Vector3 GetCellWorldPos(int index, Transform anchor)
    {
        int perLayer = cols * rows;
        int layer = index / perLayer;
        int rem   = index % perLayer;

        int row = rem / cols;
        int col = rem % cols;

        // 로컬 그리드 배치(원하면 중앙정렬로 바꿔도 됨)
        Vector3 local =
            new Vector3(col * cellSize.x, layer * layerHeight, row * cellSize.y);

        return anchor.TransformPoint(local);
    }
}
