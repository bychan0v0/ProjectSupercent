using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class AutoItemTransfer : MonoBehaviour
{
    public enum Mode { Auto, Manual }

    [Header("Mode")]
    [SerializeField] private Mode mode = Mode.Auto;

    [Header("Auto Transfer Toggles")]
    [SerializeField] private bool enableAutoPickup = true; // 근처 Source에서 캐리어로
    [SerializeField] private bool enableAutoDrop   = true; // 캐리어에서 근처 Sink로
    [SerializeField] private bool dropFirst        = true; // 드롭을 먼저 시도(바운스 방지)
    [SerializeField] private bool forbidPickupFromSinks = true;
    
    [Header("Masks (Layer Filters)")]
    [SerializeField] private LayerMask pickupMask = ~0; // Source 탐지용
    [SerializeField] private LayerMask dropMask   = ~0; // Sink   탐지용

    [Header("Timings")]
    [SerializeField, Min(0.05f)] private float interval = 0.1f;
    [SerializeField, Min(0f)]    private float antiBounceSeconds = 0.5f; // 방금 내려놓은 선반에서 재픽업 금지 시간

    private IProductCarrier _carrier;

    // 범위 내 대상들
    private readonly HashSet<IProductSource> _sources = new();
    private readonly HashSet<IProductSink>   _sinks   = new();

    // "방금 드롭한 곳"에서 재픽업 금지(같은 GO의 Source를 일정 시간 차단)
    private readonly Dictionary<Object, float> _noPickupUntil = new();

    private Coroutine _loop;

    public bool IsBusy { get; private set; }

    private void Awake()
    {
        _carrier = GetComponent<IProductCarrier>();
        if (_carrier == null)
            Debug.LogError("[AutoItemTransfer] IProductCarrier 필요", this);
    }

    private void OnEnable()
    {
        if (mode == Mode.Auto)
            _loop = StartCoroutine(TransferLoop());
    }

    private void OnDisable()
    {
        if (_loop != null) StopCoroutine(_loop);
        _loop = null;

        _sources.Clear();
        _sinks.Clear();
        _noPickupUntil.Clear();
        IsBusy = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (mode != Mode.Auto) return;

        // Source 감지 (pickupMask로 필터)
        if (IsInMask(other.gameObject.layer, pickupMask) &&
            other.TryGetComponent<IProductSource>(out var src))
        {
            _sources.Add(src);
        }

        // Sink 감지 (dropMask로 필터)
        if (IsInMask(other.gameObject.layer, dropMask) &&
            other.TryGetComponent<IProductSink>(out var sink))
        {
            _sinks.Add(sink);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (mode != Mode.Auto) return;

        if (other.TryGetComponent<IProductSource>(out var src)) _sources.Remove(src);
        if (other.TryGetComponent<IProductSink>(out var sink))  _sinks.Remove(sink);
    }

    private IEnumerator TransferLoop()
    {
        var wait = new WaitForSeconds(interval);

        while (true)
        {
            bool moved = false;

            if (dropFirst)
            {
                if (enableAutoDrop)   moved = TryTransferToSinks();
                if (!moved && enableAutoPickup) moved = TryTransferFromSources();
            }
            else
            {
                if (enableAutoPickup) moved = TryTransferFromSources();
                if (!moved && enableAutoDrop)   moved = TryTransferToSinks();
            }

            yield return wait;
        }
    }

    // ====== AUTO: Source -> Carrier ======
    private bool TryTransferFromSources()
    {
        if (_carrier == null || _carrier.CapacityLeft > 0 == false) return false;

        foreach (IProductSource src in _sources)
        {
            // ★ Sink에서의 픽업 차단: 같은 GameObject에 IProductSink가 있으면 스킵
            if (forbidPickupFromSinks)
            {
                if (src is Component c && c.TryGetComponent<IProductSink>(out _))
                    continue;
            }

            if (src is not IProvidesProductType prov) continue;
            ProductType type = prov.Type;
            if (type == null) continue;

            // (나머지는 기존 그대로)
            int avail = src.Peek(type);
            if (avail <= 0) continue;

            if (!src.TryTake(type, 1, out int taken) || taken != 1) continue;
            if (!_carrier.TryAddOne(type)) continue;

            if (src is ISourceVisualPopper pop && pop.TryPopVisual(out GameObject go) && go)
            {
                if (TryGetComponent<StackCarrier>(out var stack)) stack.AbsorbExisting(go);
            }
            return true;
        }
        return false;
    }

    // ====== AUTO: Carrier -> Sink ======
    private bool TryTransferToSinks()
    {
        foreach (var sink in _sinks)
        {
            if (sink is not IAcceptsProductType acc) continue;
            ProductType type = acc.Type;
            if (type == null) continue;

            if (_carrier.Count(type) <= 0) continue;
            if (sink.CapacityLeft(type) <= 0) continue;

            // 1) 캐리어에서 하나 꺼내기
            if (!_carrier.TryRemoveOne(type)) continue;

            // 2) 손에서 실제 GO 꺼내서 선반에 '연출' 배치
            StackCarrier stack = null;
            TryGetComponent(out stack);

            ProductShelf shelf = (sink as Component)?.GetComponent<ProductShelf>();
            if (stack != null && shelf != null)
            {
                GameObject go = stack.PopTop();
                Vector3 to = shelf.GetNextSlotWorldPos();

                // 짧은 흡착 이동 후 선반에 고정
                StartCoroutine(MoveThenPlace(go, to, 0.12f, () =>
                {
                    shelf.AcceptFromHand(go);
                    // 3) 싱크에 저장(숫자 증가)
                    sink.TryStore(type, 1);
                }));
            }
            else
            {
                // 비주얼 경로가 없으면 그냥 숫자만 증가
                sink.TryStore(type, 1);
            }

            // === 안티-바운스: 같은 GameObject의 Source가 있다면 일정 시간 픽업 금지 ===
            var co = (sink as Component);
            if (co)
            {
                var srcSameGo = co.GetComponent<IProductSource>();
                if (srcSameGo != null)
                    _noPickupUntil[srcSameGo as Object] = Time.time + antiBounceSeconds;
            }

            return true;
        }
        return false;
    }

    private IEnumerator MoveThenPlace(GameObject go, Vector3 to, float dur, System.Action onArrive)
    {
        if (!go) yield break;
        Vector3 from = go.transform.position;
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / dur);
            a = a * a * (3f - 2f * a);
            go.transform.position = Vector3.Lerp(from, to, a);
            yield return null;
        }
        go.transform.position = to;
        onArrive?.Invoke();
    }

    // ====== 고객용 수동 API ======

    public bool ManualPickup(IProductSource src, ProductType type, int count)
    {
        if (mode != Mode.Manual || IsBusy || src == null || type == null || count <= 0) return false;
        StartCoroutine(CoManualPickup(src, type, count));
        return true;
    }

    public bool ManualDrop(IProductSink sink, ProductType type, int count, Vector3? toWorld = null)
    {
        if (mode != Mode.Manual || IsBusy || sink == null || type == null || count <= 0) return false;
        StartCoroutine(CoManualDrop(sink, type, count, toWorld));
        return true;
    }

    public void SetModeManual() => mode = Mode.Manual;
    public void SetModeAuto()   => mode = Mode.Auto;

    private IEnumerator CoManualPickup(IProductSource src, ProductType type, int count)
    {
        IsBusy = true;

        int moved = 0;
        while (moved < count && _carrier.CapacityLeft > 0 && src.Peek(type) > 0)
        {
            if (!src.TryTake(type, 1, out var taken) || taken != 1) break;
            if (!_carrier.TryAddOne(type)) break;

            if (src is ISourceVisualPopper pop && pop.TryPopVisual(out var go) && go)
            {
                if (TryGetComponent<StackCarrier>(out var stack)) stack.AbsorbExisting(go);
            }

            moved++;
            yield return new WaitForSeconds(interval);
        }

        IsBusy = false;
    }

    private IEnumerator CoManualDrop(IProductSink sink, ProductType type, int count, Vector3? toWorld)
    {
        IsBusy = true;

        int moved = 0;
        bool hasStack = TryGetComponent<StackCarrier>(out var stack);
        ProductShelf shelf = (sink as Component)?.GetComponent<ProductShelf>();

        while (moved < count && _carrier.Count(type) > 0 && sink.CapacityLeft(type) > 0)
        {
            if (!_carrier.TryRemoveOne(type)) break;

            if (hasStack && shelf != null)
            {
                GameObject go = stack.PopTop();
                Vector3 to = toWorld ?? shelf.GetNextSlotWorldPos();
                yield return MoveThenPlaceManual(go, to, 0.12f, () => shelf.AcceptFromHand(go));
            }
            sink.TryStore(type, 1);

            // 수동 드롭 후에도 같은 오브젝트에서 즉시 픽업 금지
            var co = (sink as Component);
            if (co)
            {
                var srcSameGo = co.GetComponent<IProductSource>();
                if (srcSameGo != null)
                    _noPickupUntil[srcSameGo as Object] = Time.time + antiBounceSeconds;
            }

            moved++;
            yield return null;
        }

        IsBusy = false;
    }

    private IEnumerator MoveThenPlaceManual(GameObject go, Vector3 to, float dur, System.Action onArrive)
    {
        if (!go) yield break;
        Vector3 from = go.transform.position;
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / dur);
            a = a * a * (3f - 2f * a);
            go.transform.position = Vector3.Lerp(from, to, a);
            yield return null;
        }
        go.transform.position = to;
        onArrive?.Invoke();
    }

    private static bool IsInMask(int layer, LayerMask mask) => ((1 << layer) & mask.value) != 0;
}
