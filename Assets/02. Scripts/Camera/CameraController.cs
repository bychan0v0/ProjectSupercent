using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraController : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private float distance = 8f;

    private void LateUpdate()
    {
        if (!target) return;

        transform.position = target.position - transform.forward * distance;
    }
}
