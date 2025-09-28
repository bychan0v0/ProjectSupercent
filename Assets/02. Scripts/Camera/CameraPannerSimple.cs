using System.Collections;
using DG.Tweening;
using UnityEngine;

public class CameraPannerSimple : MonoBehaviour
{
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private MonoBehaviour[] controllersToDisable;
    [SerializeField, Min(0.1f)] private float toSeconds = 1.0f;
    [SerializeField, Min(0.1f)] private float backSeconds = 1.0f;

    private void Reset() { if (!cameraTransform && Camera.main) cameraTransform = Camera.main.transform; }

    public IEnumerator PanToAndBack(Transform lookTarget, Transform backTarget, float holdSeconds)
    {
        if (!cameraTransform || !lookTarget) yield break;
        SetControllers(false);
        var startRot = cameraTransform.rotation;

        yield return cameraTransform.DOLookAt(lookTarget.position, toSeconds).WaitForCompletion();
        if (holdSeconds > 0) yield return new WaitForSeconds(holdSeconds);

        if (backTarget)
            yield return cameraTransform.DOLookAt(backTarget.position, backSeconds).WaitForCompletion();
        else
            yield return cameraTransform.DORotateQuaternion(startRot, backSeconds).WaitForCompletion();

        SetControllers(true);
    }

    private void SetControllers(bool on)
    {
        if (controllersToDisable == null) return;
        foreach (var c in controllersToDisable) if (c) c.enabled = on;
    }
}