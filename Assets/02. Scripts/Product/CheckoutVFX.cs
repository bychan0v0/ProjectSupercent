using DG.Tweening;
using UnityEngine;

public class CheckoutVFX : MonoBehaviour
{
    [Header("Anchors/Prefabs")]
    public Transform bagSpawnAnchor;
    public GameObject bagPrefab;
    public Vector3 bagMouthOffset = new(0, 0.25f, 0);

    [Header("Profiles")]
    public ItemTravelProfile dropToBagProfile;
    public ItemTravelProfile bagToHandProfile;

    [Header("Per-item timing")]
    [Min(0f)] public float perItemDelay = 0.05f;

    public float EstimateDuration(int itemCount)
    {
        float per = Mathf.Max(dropToBagProfile ? dropToBagProfile.duration : 0.25f, 0.25f) + perItemDelay;
        return Mathf.Max(0f, itemCount * per) + Mathf.Max(bagToHandProfile ? bagToHandProfile.duration : 0.35f, 0.35f);
    }

    public Sequence Play(
    CustomerAgent who,
    int itemCount,
    System.Func<GameObject> popOneVisual,    // StackCarrier.PopTop 등 실물 GO 꺼내오기
    System.Action<GameObject> onConsumeOne,  // 도착 즉시 풀 회수
    System.Action onAllDone)
    {
        var seq = DOTween.Sequence();
    
        // 1) 봉투 스폰
        var bag = Instantiate(bagPrefab, bagSpawnAnchor.position, bagSpawnAnchor.rotation);
        
        // 2) 가방 입구 찾기 (없으면 오프셋)
        Transform mouthTr = bag.GetComponentInChildren<BagMouthAnchor>()?.transform;
        Vector3 bagMouth = mouthTr ? mouthTr.position : (bag.transform.position + bagMouthOffset);
    
        // 3) 아이템 n개 직렬 이동(+도착 시 회수)
        for (int i = 0; i < itemCount; i++)
        {
            GameObject item = popOneVisual != null ? popOneVisual() : null;
            if (!item) break;
    
            if (item.TryGetComponent<Rigidbody>(out var rb))
            { rb.isKinematic = true; rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            if (item.TryGetComponent<Collider>(out var col)) col.enabled = false;
    
            var move = ItemTravelTween.ArcMove(item.transform, bagMouth, dropToBagProfile);
            var captured = item;
            move.OnComplete(() =>
            {
                if (onConsumeOne != null) onConsumeOne(captured);
                else Destroy(captured);
            });
    
            seq.Append(move);
            if (perItemDelay > 0f) seq.AppendInterval(perItemDelay);
        }
    
        // 4) 전부 담긴 뒤: 봉투 → 손님 자식(position)으로 이동 & 자식화
        seq.Append(MoveBagToHoldParent(who, bag));
    
        seq.OnComplete(() => onAllDone?.Invoke());
        return seq;
    }
    
    private Sequence MoveBagToHoldParent(CustomerAgent who, GameObject bag)
    {
        Transform targetParent = (who && who.BagHoldParent) ? who.BagHoldParent : who.transform;
        Vector3 targetPos = targetParent.position;
    
        var s = ItemTravelTween.ArcMove(bag.transform, targetPos, bagToHandProfile,
                                        endParent: bagToHandProfile.parentToTarget ? targetParent : null);
    
        if (!bagToHandProfile.parentToTarget)
        {
            s.OnComplete(() =>
            {
                bag.transform.SetParent(targetParent, worldPositionStays: true);
                bag.transform.localPosition = Vector3.zero; // 필요시 로컬 보정
                // bag.transform.localRotation = Quaternion.identity;
            });
        }
        return s;
    }
}
