using System.Collections.Generic;
using UnityEngine;

public sealed class CheckoutLane : MonoBehaviour
{
    [Header("Queue Layout")]
    [SerializeField] private Transform anchor;
    [SerializeField] private bool useAnchorForward = true;
    [SerializeField] private Vector3 direction = new Vector3(0f, 0f, 1f);
    [SerializeField, Min(0.1f)] private float spacing = 0.7f;

    [Header("Service Time (sec)")]
    [SerializeField] private Vector2 serviceTimeRange = new Vector2(1.2f, 2.0f);

    private readonly List<CustomerAgent> _queue = new();

    /// <summary>줄에 합류시키고 자신의 인덱스를 반환</summary>
    public int Join(CustomerAgent who)
    {
        if (who == null) return -1;
        if (!_queue.Contains(who)) _queue.Add(who);
        RefreshDestinations();
        return _queue.Count - 1;
    }

    /// <summary>목표 지점에 도착했음을 알림(맨 앞이면 결제 시작)</summary>
    public void NotifyArrived(CustomerAgent who)
    {
        if (_queue.Count == 0 || who == null) return;

        // 자동 결제 금지
        // 맨 앞이어도 여기서는 아무 것도 하지 않음

        // 대기열 정렬은 유지 (앞사람이 살짝 움직여도 다시 맞춰줌)
        RefreshDestinations();
    }

    /// <summary>현 대기열의 각 손님 목적지를 재배치</summary>
    public void RefreshDestinations()
    {
        // null 정리
        for (int i = _queue.Count - 1; i >= 0; i--)
            if (_queue[i] == null) _queue.RemoveAt(i);

        // 기준 포즈
        Transform a = anchor ? anchor : transform;
        Vector3 aPos = a.position;

        Vector3 dir = useAnchorForward ? a.forward : direction;
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward; // 폴백
        dir.Normalize();

        // i번째 손님 목적지 = anchor - dir * (spacing * i)
        for (int i = 0; i < _queue.Count; i++)
        {
            CustomerAgent agent = _queue[i];
            if (agent == null) continue;

            Vector3 dst = aPos - dir * (spacing * i);
            agent.SetQueueDestination(dst);
        }
    }
    
    public CustomerAgent PeekFront()
    {
        if (_queue.Count == 0) return null;
        return _queue[0];
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // 에디터에서 줄 미리보기
        Transform a = anchor ? anchor : transform;
        Vector3 aPos = a.position;
        Vector3 dir = useAnchorForward ? a.forward : direction;
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
        dir.Normalize();

        UnityEditor.Handles.color = new Color(0f, 0.8f, 1f, 0.6f);
        for (int i = 0; i < 8; i++)
        {
            Vector3 p = aPos - dir * (spacing * i);
            UnityEditor.Handles.DrawSolidDisc(p, Vector3.up, 0.08f);
        }
    }
#endif
}
