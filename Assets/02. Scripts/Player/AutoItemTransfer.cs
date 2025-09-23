using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AutoItemTransfer : MonoBehaviour
{
    [Header("Drip")]
    [Min(0.05f)] public float interval = 0.1f;

    [Header("Filter (Layers)")]
    public LayerMask interactMask = ~0;

    private IProductCarrier _carrier;
    private readonly HashSet<IProductSource> _sources = new();
    private readonly HashSet<IProductSink> _sinks = new();

    private Coroutine _loop;

    private void Awake()
    {
        _carrier = GetComponent<IProductCarrier>();
        if (_carrier == null)
            Debug.LogError("[PlayerAutoItemTransfer] IProductCarrier가 필요합니다(플레이어 인벤).");
    }

    private void OnEnable()
    {
        _loop = StartCoroutine(TransferLoop());
    }

    private void OnDisable()
    {
        if (_loop != null) StopCoroutine(_loop);
        _loop = null;

        _sources.Clear();
        _sinks.Clear();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsInMask(other.gameObject.layer, interactMask)) return;

        if (other.TryGetComponent<IProductSource>(out var src)) _sources.Add(src);
        if (other.TryGetComponent<IProductSink>(out var sink))  _sinks.Add(sink);
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.TryGetComponent<IProductSource>(out var src)) _sources.Remove(src);
        if (other.TryGetComponent<IProductSink>(out var sink))  _sinks.Remove(sink);
    }

    private IEnumerator TransferLoop()
    {
        var wait = new WaitForSeconds(interval);

        while (true)
        {
            bool moved = false;

            // 1) 생성기 -> 플레이어
            if (_carrier.CapacityLeft > 0)
            {
                moved = TryTransferFromSources();
            }

            // 2) 플레이어 -> 진열대
            if (!moved)
            {
                moved = TryTransferToSinks();
            }

            yield return wait;
        }
    }

    private bool TryTransferFromSources()
    {
        foreach (var src in _sources)
        {
            if (src is not IProvidesProductType prov) continue;
            var type = prov.Type;
            if (type == null || src.Peek(type) <= 0) continue;

            if (!src.TryTake(type, 1, out var taken) || taken != 1) continue;
            if (!_carrier.TryAddOne(type)) continue;

            if (src is ISourceVisualPopper pop && pop.TryPopVisual(out var go) && go)
                if (TryGetComponent<StackCarrier>(out var stack)) stack.AbsorbExisting(go);
            
            return true;
        }
        return false;
    }

    private bool TryTransferToSinks()
    {
        foreach (var sink in _sinks)
        {
            if (sink is not IAcceptsProductType acc) continue;
            var type = acc.Type;
            if (type == null || _carrier.Count(type) <= 0 || sink.CapacityLeft(type) <= 0) continue;

            if (!_carrier.TryRemoveOne(type)) continue;
            sink.TryStore(type, 1);

            return true;
        }
        return false;
    }

    private static bool IsInMask(int layer, LayerMask mask)
    {
        return ((1 << layer) & mask.value) != 0;
    }
}
