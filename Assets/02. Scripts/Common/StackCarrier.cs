using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class StackCarrier : MonoBehaviour
{
    [Header("Anchors")]
    [SerializeField] private Transform frontAnchor;   // 빨려올 중간 포인트(없으면 stackRoot)
    [SerializeField] private Transform stackRoot;     // 쌓이는 부모(없으면 this)

    [Header("Layout & Timing")]
    [SerializeField] private float spacing = 0.18f;   // 아이템 간 간격(로컬 Y)
    [SerializeField] private float suckDuration = 0.06f; // 월드→손앞
    [SerializeField] private float snapDuration = 0.06f; // 손앞→슬롯
    [SerializeField] private Vector3 stackedLocalEuler = new Vector3(0, 90, 0);

    private readonly List<GameObject> _stack = new();

    private void Awake()
    {
        if (!stackRoot) stackRoot = transform;
    }

    /// <summary>
    /// 이미 씬에 존재하는 go를 "손 스택"으로 흡입해 쌓는다.
    /// - 슬롯을 즉시 선점(겹침 방지)
    /// - 즉시 parent를 붙여 이동 지연 방지
    /// </summary>
    public bool AbsorbExisting(GameObject go)
    {
        if (!go) return false;
        if (!stackRoot) stackRoot = transform;

        // 1) 슬롯 즉시 선점 (겹침 방지)
        int slot = _stack.Count;
        _stack.Add(go);

        // 2) 물리/충돌 끄기
        if (go.TryGetComponent<Rigidbody>(out var rb))
        {
            rb.isKinematic = true;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        if (go.TryGetComponent<Collider>(out var col))
            col.enabled = false;

        // 3) 부모 붙이기: ★ 세계 좌표 유지(true) → 시작점이 '원래 위치'로 남음
        Vector3 fromWorld = go.transform.position;
        Vector3 midWorld  = frontAnchor ? frontAnchor.position : (stackRoot ? stackRoot.position : transform.position);
        Vector3 toLocal   = Vector3.up * (spacing * slot);

        go.transform.SetParent(stackRoot, /*worldPositionStays:*/ true);

        // 4) 이제 로컬 공간으로 변환해 보간
        Vector3 fromLocal = stackRoot.InverseTransformPoint(fromWorld);
        Vector3 midLocal  = stackRoot.InverseTransformPoint(midWorld);

        StartCoroutine(LerpIn(go.transform, fromLocal, midLocal, toLocal));
        return true;
    }

    private IEnumerator LerpIn(Transform t, Vector3 fromLocal, Vector3 midLocal, Vector3 toLocal)
    {
        float d1 = Mathf.Max(0.0001f, suckDuration);
        float d2 = Mathf.Max(0.0001f, snapDuration);

        // 1) 손앞까지
        float t1 = 0f;
        while (t1 < d1)
        {
            t1 += Time.deltaTime;
            float a = Mathf.Clamp01(t1 / d1);
            a = a * a * (3f - 2f * a);
            t.localPosition = Vector3.Lerp(fromLocal, midLocal, a);
            yield return null;
        }

        // 2) 슬롯까지
        float t2 = 0f;
        while (t2 < d2)
        {
            t2 += Time.deltaTime;
            float a = Mathf.Clamp01(t2 / d2);
            t.localPosition = Vector3.Lerp(midLocal, toLocal, a);
            yield return null;
        }

        // 최종 포즈 고정
        t.localPosition = toLocal;
        t.localRotation = Quaternion.Euler(stackedLocalEuler);
        t.localScale    = Vector3.one;
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
            go.transform.SetParent(null, true);
            if (go.TryGetComponent<Rigidbody>(out var rb))
            {
                rb.isKinematic = true; // 바로 선반으로 쉬프트하므로 여전히 kinematic 유지
                rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
            }
            if (go.TryGetComponent<Collider>(out var col))
                col.enabled = false;
        }
        return go;
    }

    public int Count => _stack.Count;

    /// <summary>깨끗하지 않은 참조 정리용(선택)</summary>
    public void Compact()
    {
        _stack.RemoveAll(g => g == null);
    }
}
