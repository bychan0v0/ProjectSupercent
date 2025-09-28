using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameProgressDirector : MonoBehaviour
{
    [System.Serializable]
    public class Milestone
    {
        [Min(1)] public int unitsRequired = 10;

        [Header("Show / Pay")]
        public Transform cameraLookTarget;  // 우상단 락 위치 등
        [Min(0)] public float cameraHoldSeconds = 1.2f;
        public UnlockZone unlockZone;       // 결제 존(처음에는 Collider 꺼둬도 됨)

        [Header("Area")]
        public UnlockableArea unlockableArea; // (선택) 직접 참조(이벤트 받기)

        [Header("Systems")]
        public MonoBehaviour[] enableOnReveal;   // 결제 '가능' 상태에서 켤 것(표지판, UI 등)
        public MonoBehaviour[] enableOnUnlocked; // 해금 완료 후 켤 것(테이블 손님 스포너, 오른쪽 Lane/Counter 등)
    }

    [Header("Milestones (순서대로)")]
    public List<Milestone> milestones = new();

    [Header("Refs")]
    [SerializeField] private CustomerSpawner spawner;   // 통합 스포너
    [SerializeField] private CheckoutLane    dineInLane; // 다인인 레인 (오른쪽 줄)
    [SerializeField] private CameraPannerSimple cameraPanner;
    [SerializeField] private Transform playerLookTarget;
    
    private int nextIndex = 0;

    private void Awake()
    {
        // 시작 시 결제존/시스템 기본 Off 안전장치
        foreach (var m in milestones)
        {
            if (m == null) continue;
            if (m.unlockZone)
            {
                var col = m.unlockZone.GetComponent<Collider>();
                if (col) col.enabled = false;
            }
            if (m.enableOnReveal != null)
                foreach (var sys in m.enableOnReveal) if (sys) sys.enabled = false;
            if (m.enableOnUnlocked != null)
                foreach (var sys in m.enableOnUnlocked) if (sys) sys.enabled = false;
        }
    }

    private void OnEnable()
    {
        TrySubscribeToTracker();
    }

    private void OnDisable()
    {
        if (SalesTracker.Instance != null)
            SalesTracker.Instance.OnTotalsChanged -= OnTotalsChanged;
    }

    private void TrySubscribeToTracker()
    {
        if (SalesTracker.Instance != null)
        {
            SalesTracker.Instance.OnTotalsChanged += OnTotalsChanged;
            // 현재 누적치로 즉시 한번 평가(이미 임계치 달성한 경우 대비)
            OnTotalsChanged(SalesTracker.Instance.TotalUnitsSold, SalesTracker.Instance.TotalRevenue);
        }
        else
        {
            // 인스턴스가 아직 없으면 한 프레임씩 기다렸다가 구독
            StartCoroutine(CoWaitAndSubscribe());
        }
    }

    private void SafeStart(IEnumerator routine)
    {
        if (routine != null) StartCoroutine(routine);
        else Debug.LogWarning("[Director] Tried to StartCoroutine with null routine");
    }

    private void OnTotalsChanged(int totalUnits, int _)
    {
        // ★ 절대 3항식으로 null을 StartCoroutine에 직접 넘기지 말 것
        while (nextIndex < milestones.Count && totalUnits >= milestones[nextIndex].unitsRequired)
        {
            var m = milestones[nextIndex++];
            var co = RunMilestone(m);      // ← 코루틴을 변수로 받는다
            SafeStart(co);                 // ← null 체크 후 시작
        }
    }
    
    private IEnumerator CoWaitAndSubscribe()
    {
        while (SalesTracker.Instance == null) yield return null;
        SalesTracker.Instance.OnTotalsChanged += OnTotalsChanged;
        OnTotalsChanged(SalesTracker.Instance.TotalUnitsSold, SalesTracker.Instance.TotalRevenue);
    }

    private IEnumerator RunMilestone(Milestone m)
    {
        Debug.Log("마일스톤 시작");
        
        // (안전) 강제 소환이 줄에 설 수 있게, 필요한 시스템은 먼저 켜두자.
        if (m.enableOnReveal != null)
            foreach (var sys in m.enableOnReveal) if (sys) sys.enabled = true;

        // 1) 다인인 1명 ‘강제’ 소환 (정원/타입 캡에 막히지 않도록 Force 권장)
        CustomerAgent forced = null;
        if (spawner)
        {
            // SpawnOneForce가 없다면 SpawnOne(SpawnKind.DineIn)으로 대체 가능
            forced = spawner.SpawnOneForce(CustomerSpawner.SpawnKind.DineIn);
            if (!forced) Debug.LogWarning("[Director] Forced DineIn spawn failed.");
        }

        // 2) 스포너에서 다인인 허용 (이후 자동 스폰 경로에서도 다인인 나오게)
        if (spawner) spawner.EnableDineIn(true);

        // 3) ‘그 강제 손님’이 다인 레인에 ‘도착’하는 순간 카메라 연출 시작
        //    Lane 도착 이벤트 구독 → 해당 손님이면 1회만 팬
        System.Action<CustomerAgent> onArrived = null;
        onArrived = who =>
        {
            if (!forced || who != forced) return;           // 강제 손님만 트리거
            if (dineInLane) dineInLane.OnArrived -= onArrived;

            // 카메라: 락 구역을 보여주고 → 플레이어로 복귀
            if (cameraPanner && m.cameraLookTarget)
                StartCoroutine(cameraPanner.PanToAndBack(m.cameraLookTarget, playerLookTarget, m.cameraHoldSeconds));
        };
        if (dineInLane) dineInLane.OnArrived += onArrived;

        // 4) 결제 존(해금 결제) 활성화
        if (m.unlockZone)
        {
            var col = m.unlockZone.GetComponent<Collider>();
            if (col) col.enabled = true;

            // 해금 완료(돈 완납) 시: 좌석/시스템 켜고, 필요 시 추가 조정
            m.unlockZone.OnUnlocked += () =>
            {
                if (spawner) spawner.EnableDineIn(true); // 재보강(안전)
                if (m.enableOnUnlocked != null)
                    foreach (var sys in m.enableOnUnlocked) if (sys) sys.enabled = true;
            };
        }

        return null;
    }
}
