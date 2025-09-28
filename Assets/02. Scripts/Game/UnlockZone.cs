using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class UnlockZone : MonoBehaviour
{
    [Header("Payment")]
    [SerializeField, Min(1)] private int totalCost = 30;
    [SerializeField, Min(1)] private int valuePerBill = 10;
    [SerializeField, Min(0f)] private float perBillStagger = 0.02f;

    [Header("Who can pay")]
    [SerializeField] private LayerMask playerLayers;

    [Header("VFX")]
    [SerializeField] private Transform payTarget;             // 바닥 표시
    [SerializeField] private ItemTravelProfile toZoneProfile; // 아크 프로파일
    [SerializeField] private string cashPoolKey = "CashBill";
    [SerializeField] private float spawnYawDeg = 90f;

    [Header("Result")]
    [SerializeField] private UnlockableArea areaToUnlock;

    public System.Action OnPaymentStarted;
    public System.Action OnUnlocked;

    private readonly HashSet<Collider> _players = new();
    private bool _paying = false;
    private int _paid = 0;

    private void Reset() { var c = GetComponent<Collider>(); if (c) c.isTrigger = true; }

    private void OnTriggerEnter(Collider other) { if (IsPlayer(other)) { _players.Add(other); TryPay(); } }
    private void OnTriggerExit(Collider other)  { if (IsPlayer(other)) _players.Remove(other); }

    private bool IsPlayer(Collider c) => (playerLayers.value & (1 << c.transform.root.gameObject.layer)) != 0;

    private Transform FindNearestPlayer(out PlayerWallet wallet)
    {
        wallet = null; float best = float.PositiveInfinity; Transform bestT = null;
        foreach (var c in _players) { if (!c) continue; var t = c.transform.root; float d = (t.position - transform.position).sqrMagnitude;
            if (d < best) { best = d; bestT = t; wallet = t.GetComponentInChildren<PlayerWallet>(); } }
        return bestT;
    }

    private void TryPay()
    {
        if (_paying || _paid >= totalCost || _players.Count == 0) return;
        OnPaymentStarted?.Invoke();
        StartCoroutine(CoPay());
    }

    private IEnumerator CoPay()
    {
        _paying = true;

        while (_paid < totalCost && _players.Count > 0)
        {
            PlayerWallet wallet; var player = FindNearestPlayer(out wallet);
            if (!player || !payTarget || wallet == null) break;

            int remain = totalCost - _paid;
            int pay = Mathf.Min(valuePerBill, remain);

            // 비주얼 지폐 스폰(플레이어 위치)
            Vector3 spawnPos = player.position + Vector3.up * 0.9f;
            Quaternion rot   = Quaternion.AngleAxis(spawnYawDeg, Vector3.up);
            GameObject bill  = SpawnBill(spawnPos, rot);
            if (!bill) break;

            // 아크 이동
            var seq = ItemTravelTween.ArcMove(bill.transform, payTarget.position, toZoneProfile);
            seq.OnComplete(() =>
            {
                wallet.Add(-pay);       // 지갑 차감
                DespawnOrDestroy(bill);
                _paid += pay;

                if (_paid >= totalCost)
                {
                    areaToUnlock?.Unlock();
                    OnUnlocked?.Invoke();
                    // 필요하면 이 존 비활성: GetComponent<Collider>().enabled = false;
                }
            });

            if (perBillStagger > 0) yield return new WaitForSeconds(perBillStagger);
            else yield return null;
        }

        _paying = false;
    }

    private GameObject SpawnBill(Vector3 pos, Quaternion rot)
    {
        GameObject go = null;
        if (!string.IsNullOrEmpty(cashPoolKey) && PoolManager.Instance != null)
            go = PoolManager.Instance.Spawn(cashPoolKey, pos, rot);
        return go;
    }

    private void DespawnOrDestroy(GameObject go)
    {
        if (!go) return;
        var po = go.GetComponent<PooledObject>();
        if (po != null && PoolManager.Instance != null) PoolManager.Instance.Despawn(go);
        else Destroy(go);
    }
}
