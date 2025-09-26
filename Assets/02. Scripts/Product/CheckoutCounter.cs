using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CheckoutCounter : MonoBehaviour
{
    [SerializeField] private float serviceSeconds = 1.2f; // 결제에 걸리는 시간(임시)

    // Lane 참조: 큐의 맨 앞 손님을 알려주는 API가 있다고 가정
    [SerializeField] private CheckoutLane lane;

    private void OnTriggerStay(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        if (!Input.GetKeyDown(KeyCode.E)) return;

        // _queue의 맨 앞 손님 가져오기
        var front = lane?.PeekFront();
        if (front != null && !front.Servicing)
        {
            front.BeginCheckoutByPlayer(serviceSeconds);
            // lane.NotifyServiceStarted(front); // 필요하면 추가
        }
    }
}
