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
        SetLockedVisual(false);

        if (tablePrefab && tableSpawnPoints != null)
            foreach (var p in tableSpawnPoints) if (p) Instantiate(tablePrefab, p.position, p.rotation);

        OnUnlocked?.Invoke();
    }
}