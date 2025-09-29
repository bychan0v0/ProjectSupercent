using UnityEngine;

public class UnlockableArea : MonoBehaviour
{
    [Header("Locked/Unlocked Sets")]
    [SerializeField] private GameObject[] lockedSet;
    [SerializeField] private GameObject[] unlockedSet;

    [Header("Tables")]
    [SerializeField] private GameObject tablePrefab;
    [SerializeField] private Transform[] tableSpawnPoints;

    public bool IsUnlocked { get; private set; }
    public System.Action OnUnlocked; // 이벤트

    private void Awake() => SetLockedVisual(true);

    public void SetLockedVisual(bool locked)
    {
        foreach (var go in lockedSet)   if (go) go.SetActive(locked);
        foreach (var go in unlockedSet) if (go) go.SetActive(!locked);
    }

    public void Unlock()
    {
        if (IsUnlocked) return;
        IsUnlocked = true;

        // 잠금/해금 세트 전환
        if (lockedSet != null)
            foreach (var go in lockedSet) if (go) go.SetActive(false);
        if (unlockedSet != null)
            foreach (var go in unlockedSet) if (go) go.SetActive(true);

        // 테이블 스폰(필요 시)
        if (tablePrefab && tableSpawnPoints != null)
        {
            foreach (var p in tableSpawnPoints)
                if (p) Instantiate(tablePrefab, p.position, p.rotation, transform);
        }

        OnUnlocked?.Invoke();
        Analytics.Log("area_unlocked", new { area = name });
    }
}