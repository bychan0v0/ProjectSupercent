using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 6f;
    private Rigidbody rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    private void FixedUpdate()
    {
        float x = Input.GetAxisRaw("Horizontal"); // A/D or ←/→
        float z = Input.GetAxisRaw("Vertical");   // W/S or ↑/↓

        Vector3 dir = new Vector3(x, 0f, z).normalized;
        rb.MovePosition(rb.position + dir * (moveSpeed * Time.fixedDeltaTime));
    }
}
