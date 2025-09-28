using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public sealed class CheckoutLane : MonoBehaviour
{
    [Header("Queue Layout (Auto)")]
    [Tooltip("맨 앞(0번 슬롯) 위치. 비우면 이 오브젝트의 Transform을 사용합니다.")]
    [SerializeField] private Transform anchor;
    [Tooltip("true면 anchor.forward를, false면 아래 direction 벡터를 사용합니다.")]
    [SerializeField] private bool useAnchorForward = true;
    [SerializeField] private Vector3 direction = Vector3.back;
    [SerializeField, Min(0.1f)] private float spacing = 0.7f;

    [Header("NavMesh Snapping (권장)")]
    [SerializeField] private bool snapSlotsToNavMesh = true;
    [SerializeField, Min(0.05f)] private float navSampleMaxDistance = 0.8f;

    [Header("Service Gate")]
    [SerializeField, Min(0f)] private float serviceStartRadius = 0.25f;

    private readonly List<CustomerAgent> _queue = new();
    private readonly HashSet<CustomerAgent> _arrived = new(); // 필요 시 도착 플래그용

    public event System.Action<CustomerAgent> OnArrived;
    
    // ──────────────────────────────────────────────────────────────────────────
    // 슬롯 월드 좌표 계산(0번=앵커, i>0는 뒤로 spacing*i)
    public Vector3 GetSlotWorld(int index)
    {
        Transform a = anchor ? anchor : transform;
        Vector3 dir = useAnchorForward
            ? a.forward
            : (direction.sqrMagnitude > 1e-6f ? direction.normalized : a.forward);

        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
        dir.Normalize();

        Vector3 pos = a.position - dir * (spacing * index);

        if (snapSlotsToNavMesh &&
            NavMesh.SamplePosition(pos, out var hit, navSampleMaxDistance, NavMesh.AllAreas))
            pos = hit.position;

        return pos;
    }

    // 맨 앞 준비 판정(XZ 평면 거리로만)
    public bool IsFrontReadyForService(out CustomerAgent front)
    {
        front = (_queue.Count > 0) ? _queue[0] : null;
        if (front == null || front.Servicing) return false;

        Transform a = anchor ? anchor : transform;
        Vector3 p = front.transform.position; p.y = a.position.y;
        return (p - a.position).sqrMagnitude <= serviceStartRadius * serviceStartRadius;
    }

    // 줄 합류
    public void Join(CustomerAgent who)
    {
        if (!_queue.Contains(who)) _queue.Add(who);
        RefreshDestinations();
    }

    public CustomerAgent PeekFront() => _queue.Count > 0 ? _queue[0] : null;

    public void NotifyArrived(CustomerAgent who)
    {
        if (_queue.Count == 0 || who == null) return;

        RefreshDestinations();      // 기존 포메이션 유지
        OnArrived?.Invoke(who);     // ★ 이 줄 추가: 누가 슬롯에 '도착했는지' 알림
    }

    // 슬롯 재배치(항상 자동 생성 좌표 사용)
    public void RefreshDestinations()
    {
        for (int i = 0; i < _queue.Count; i++)
        {
            var c = _queue[i];
            if (!c) continue;
            c.SetQueueDestination(GetSlotWorld(i));
        }
        _arrived.Clear();
    }

    // 카운터가 호출: 맨 앞 손님 서비스 시작
    public bool TryStartServiceForFront(float duration, System.Action<CustomerAgent> onCompleted)
    {
        if (_queue.Count == 0) return false;
        var who = _queue[0];
        if (!who || who.Servicing) return false;

        // 안전: XZ 평면에서 서비스 반경 안쪽인지 확인
        Transform a = anchor ? anchor : transform;
        Vector3 p = who.transform.position; p.y = a.position.y;
        if ((p - a.position).sqrMagnitude > serviceStartRadius * serviceStartRadius) return false;

        Analytics.Log("checkout_start", new {
            product = who.WantProduct ? who.WantProduct.displayName : "null",
            want = who.WantCount
        });
        
        who.StartService(duration, () =>
        {
            if (_queue.Count > 0 && _queue[0] == who) _queue.RemoveAt(0);
            RefreshDestinations();
            onCompleted?.Invoke(who);
            who.AfterCheckout(); // 테이크아웃/다인인 분기
        });

        Analytics.Log("checkout_done", new {
            product = who.WantProduct ? who.WantProduct.displayName : "null",
            carried = who.WantCount  // 혹은 실제 결제수량으로 교체
        });
        
        return true;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // 에디터 미리보기(최대 8칸)
        int preview = 8;
        Color c = new Color(0f, 0.8f, 1f, 0.6f);
        UnityEditor.Handles.color = c;
        for (int i = 0; i < preview; i++)
            UnityEditor.Handles.DrawSolidDisc(GetSlotWorld(i), Vector3.up, 0.08f);
    }
#endif
}
