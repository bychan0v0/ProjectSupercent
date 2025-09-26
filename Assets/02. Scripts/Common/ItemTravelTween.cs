using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

public class ItemTravelTween : MonoBehaviour
{
    // 월드 좌표로 아크 이동
    public static Sequence ArcMove(Transform tr, Vector3 to, ItemTravelProfile p, Transform endParent = null)
    {
        tr.DOKill(true);
        var s = DOTween.Sequence();
        s.Join(tr.DOJump(to, p.jumpHeight, 1, p.duration).SetEase(p.ease));
        if (p.spinEulerPerSec != Vector3.zero)
            s.Join(tr.DORotate(tr.eulerAngles + p.spinEulerPerSec * p.duration, p.duration, RotateMode.FastBeyond360));

        s.OnComplete(() =>
        {
            if (p.parentToTarget && endParent)
            {
                tr.SetParent(endParent, true);
                tr.localPosition = p.endLocalOffset;
                tr.localRotation = Quaternion.Euler(p.endLocalEuler);
            }
        });
        return s;
    }

    // 로컬 목표를 월드로 변환해 아크 이동
    public static Sequence ArcLocal(Transform tr, Vector3 localTo, ItemTravelProfile p, Transform endParent = null)
    {
        var parent = tr.parent;
        var worldTo = parent ? parent.TransformPoint(localTo) : tr.position + localTo;
        return ArcMove(tr, worldTo, p, endParent);
    }

    // 프리팹을 스폰해서 이동(시각 전용으로 쓸 때)
    public static Sequence SpawnAndArc(GameObject prefab, Vector3 from, Vector3 to,
        ItemTravelProfile p, Transform endParent = null,
        System.Action<GameObject> onSpawned = null,
        System.Action onArrived = null)
    {
        var go = Object.Instantiate(prefab, from, Quaternion.identity);
        onSpawned?.Invoke(go);
        var seq = ArcMove(go.transform, to, p, endParent);
        seq.OnComplete(() =>
        {
            onArrived?.Invoke();
            if (!p.parentToTarget && go) Object.Destroy(go);
        });
        return seq;
    }
}
