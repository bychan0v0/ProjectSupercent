using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class StackCarrier : MonoBehaviour
{
    public Transform frontAnchor;
    public Transform stackRoot;
    public float spacing = 0.18f;
    public float suckDuration = 0.15f;
    public float snapDuration = 0.10f;

    private readonly List<GameObject> _stack = new();

    public void AbsorbExisting(GameObject go)
    {
        if (!go) return;

        if (go.TryGetComponent<Rigidbody>(out var rb))
        { rb.isKinematic = true; rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        if (go.TryGetComponent<Collider>(out var col)) col.enabled = false;

        StartCoroutine(CoAbsorb(go));
    }

    private IEnumerator CoAbsorb(GameObject go)
    {
        Vector3 start = go.transform.position;
        Vector3 mid   = frontAnchor ? frontAnchor.position : stackRoot.position;

        float t = 0f;
        while (t < suckDuration) { t += Time.deltaTime;
            float a = Mathf.Clamp01(t/suckDuration); a = a*a*(3f-2f*a);
            go.transform.position = Vector3.Lerp(start, mid, a); yield return null; }

        int idx = _stack.Count;
        go.transform.SetParent(stackRoot, worldPositionStays:false);

        Vector3 from = stackRoot.InverseTransformPoint(mid);
        Vector3 to   = Vector3.up * (spacing * idx);
        t = 0f;
        while (t < snapDuration) { t += Time.deltaTime;
            float a = Mathf.Clamp01(t/snapDuration);
            go.transform.localPosition = Vector3.Lerp(from, to, a); yield return null; }

        go.transform.localPosition = to;
        go.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
        go.transform.localScale = Vector3.one;
        _stack.Add(go);
    }
}
