using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

public class StackCarrier : MonoBehaviour
{
    [Header("Anchors")]
    [SerializeField] private Transform frontAnchor;   // 흡입 중간 포인트(없으면 stackRoot)
    [SerializeField] private Transform stackRoot;     // 쌓일 부모(없으면 this)

    [Header("Layout")]
    [SerializeField] private float spacing = 0.18f;   // 아이템 간 간격(로컬 Y)
    [SerializeField] private Vector3 stackedLocalEuler = new Vector3(0, 90, 0);

    // 클래스 필드(인스펙터에서 튜닝)
    [Header("Stack – Single Hop")]
    [SerializeField, Min(0.01f)] private float stackDuration = 0.16f; // 전체 한 번에
    [SerializeField, Min(0f)]    private float baseJumpHeight = 0.25f; // DOJump 기본 높이
    [SerializeField]             private Ease  stackEase = Ease.OutCubic;

    // (선택) Bezier 경유점 사용하고 싶을 때
    [SerializeField] private bool  useBezierOneHop = false;
    [SerializeField, Min(0f)] private float bezierHeight = 0.35f;         // 중간 apex 위로 띄우기
    [SerializeField, Range(0f,1f)] private float anchorInfluence = 0.5f;  // frontAnchor 영향도

    private readonly List<GameObject> _stack = new();
    public int Count => _stack.Count;

    // (선택) 트윈 중 정렬/외부 갱신 막고 싶을 때 확인용
    public bool IsAnimating { get; private set; }

    private void Awake()
    {
        if (!stackRoot) stackRoot = transform;
    }

    /// <summary>
    /// 이미 씬에 있는 go를 "손 스택"으로 흡입해 쌓는다.
    /// 슬롯 선점 → 물리 off → 부모 부착(world 유지) → 두 단계 아크 이동.
    /// </summary>
    public bool AbsorbExisting(GameObject go)
{
    if (!go) return false;
    if (!stackRoot) stackRoot = transform;

    // 1) 최종 슬롯 예약
    int slot = _stack.Count;
    _stack.Add(go);

    // 2) 물리/충돌 잠시 OFF
    if (go.TryGetComponent<Rigidbody>(out var rb))
    {
        rb.isKinematic = true;
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }
    if (go.TryGetComponent<Collider>(out var col))
        col.enabled = false;

    // 3) 부모 부착(월드 유지) + 목표 좌표 계산
    Vector3 fromWorld = go.transform.position;
    Vector3 toLocal   = Vector3.up * (spacing * slot);
    Vector3 toWorld   = stackRoot.TransformPoint(toLocal);
    go.transform.SetParent(stackRoot, true);

    // 4) 기존 트윈 종료
    go.transform.DOKill(true);
    IsAnimating = true;

    // 5-A) 한 방에 DOJump(단일 포물선) — 추천
    if (!useBezierOneHop)
    {
        // 스택이 높아질수록 살짝 높게(시각 보정)
        float deltaY     = Mathf.Abs(toWorld.y - fromWorld.y);
        float jumpPower  = Mathf.Max(0.05f, baseJumpHeight + 0.35f * deltaY + 0.02f * slot);

        var seq = DOTween.Sequence();
        seq.Join(go.transform.DOJump(toWorld, jumpPower, 1, stackDuration).SetEase(stackEase));
        seq.Join(go.transform.DOLocalRotate(stackedLocalEuler, stackDuration, RotateMode.Fast));
        seq.OnComplete(FinalizePose);
    }
    // 5-B) 한 방에 DOPath(중간 apex 경유, 멈추지 않음) — frontAnchor 맛 살리고 싶을 때
    else
    {
        // 중간 apex: from~to 중간 + 위로, frontAnchor 방향으로 가볍게 당김
        Vector3 apex = Vector3.Lerp(fromWorld, toWorld, 0.5f) + Vector3.up * bezierHeight;
        if (frontAnchor)
        {
            Vector3 toward = (frontAnchor.position - apex);
            apex += toward * anchorInfluence; // 영향도 0~1
            apex.y = Mathf.Max(apex.y, Mathf.Max(fromWorld.y, toWorld.y) + 0.1f); // 위로 유지
        }

        var path = new Vector3[] { fromWorld, apex, toWorld };
        var seq  = DOTween.Sequence();
        seq.Join(go.transform.DOPath(path, stackDuration, PathType.CatmullRom, PathMode.Full3D)
                              .SetEase(stackEase));
        seq.Join(go.transform.DOLocalRotate(stackedLocalEuler, stackDuration, RotateMode.Fast));
        seq.OnComplete(FinalizePose);
    }

    return true;

    void FinalizePose()
    {
        // 부동오차 정리 + 최종 포즈 고정
        go.transform.localPosition = toLocal;
        go.transform.localRotation = Quaternion.Euler(stackedLocalEuler);
        go.transform.localScale    = Vector3.one;

        // (선택) 여기서 물리/콜라이더를 다시 켤지 여부는 프로젝트 정책에 맞춰
        // if (col) col.enabled = true; if (rb) rb.isKinematic = false;

        IsAnimating = false;
    }
}
    /// <summary>스택의 가장 위 아이템을 떼어낸다(선반으로 옮길 때 사용)</summary>
    public GameObject PopTop()
    {
        if (_stack.Count == 0) return null;

        int top = _stack.Count - 1;
        var go = _stack[top];
        _stack.RemoveAt(top);

        if (go)
        {
            // 현재 트윈 중인 경우 안전하게 중단
            go.transform.DOKill(true);

            // 부모 분리(월드 좌표 유지)
            go.transform.SetParent(null, true);

            // 선반으로 배치될 때까지는 계속 kinematic/콜라이더 off 유지(도착 지점에서 on 권장)
            if (go.TryGetComponent<Rigidbody>(out var rb))
            {
                rb.isKinematic = true;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            if (go.TryGetComponent<Collider>(out var col))
                col.enabled = false;
        }
        return go;
    }

    /// <summary>깨끗하지 않은 참조 정리(선택)</summary>
    public void Compact() => _stack.RemoveAll(g => g == null);
}
